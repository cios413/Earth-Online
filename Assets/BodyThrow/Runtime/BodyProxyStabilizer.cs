using UnityEngine;

namespace EarthOL.BodyThrow
{
    /// <summary>
    /// Filters a shared world frame, then body shape within that frame. Common
    /// camera/pose jitter therefore moves the head and torso together.
    /// </summary>
    public sealed class BodyProxyStabilizer
    {
        const float PositionDeadZone = 0.008f;
        const float AngleDeadZone = 1.2f;
        const float ShapeDeadZone = 0.003f;
        const float RadiusDeadZone = 0.0015f;

        bool m_Initialized;
        bool m_TorsoAnchor;
        Vector3 m_Position;
        Quaternion m_Rotation = Quaternion.identity;
        Vector3 m_TargetPosition;
        Quaternion m_TargetRotation = Quaternion.identity;
        Vector3 m_Head;
        Vector3 m_Shoulders;
        Vector3 m_Hips;
        Vector3 m_TargetHead;
        Vector3 m_TargetShoulders;
        Vector3 m_TargetHips;
        float m_HeadRadius;
        float m_TorsoRadius;
        float m_TargetHeadRadius;
        float m_TargetTorsoRadius;

        public bool HasHead { get; private set; }
        public bool HasTorso { get; private set; }
        public Vector3 Head => m_Position + m_Rotation * m_Head;
        public Vector3 Shoulders => m_Position + m_Rotation * m_Shoulders;
        public Vector3 Hips => m_Position + m_Rotation * m_Hips;
        public float HeadRadius => m_HeadRadius;
        public float TorsoRadius => m_TorsoRadius;

        public void SetTarget(bool hasHead, Vector3 head, float headRadius,
            bool hasTorso, Vector3 shoulders, Vector3 hips, float torsoRadius)
        {
            if (!hasHead && !hasTorso) return;

            Vector3 anchor = hasTorso ? (shoulders + hips) * 0.5f : head;
            Vector3 axis = shoulders - hips;
            Quaternion rotation = hasTorso && axis.sqrMagnitude > 0.000001f
                ? Quaternion.FromToRotation(Vector3.up, axis.normalized) : m_TargetRotation;

            if (!m_Initialized)
            {
                m_Position = anchor;
                m_Rotation = rotation;
                m_Initialized = true;
            }
            else if (m_TorsoAnchor != hasTorso)
            {
                // Rebase when a torso appears/disappears. Preserve all visible
                // world geometry so changing the anchor cannot cause a jump.
                Vector3 oldHead = Head;
                Vector3 oldShoulders = Shoulders;
                Vector3 oldHips = Hips;
                m_Position = anchor;
                m_Rotation = rotation;
                Quaternion inverse = Quaternion.Inverse(rotation);
                m_Head = m_TargetHead = inverse * (oldHead - anchor);
                m_Shoulders = m_TargetShoulders = inverse * (oldShoulders - anchor);
                m_Hips = m_TargetHips = inverse * (oldHips - anchor);
            }

            m_TorsoAnchor = hasTorso;
            m_TargetPosition = anchor;
            m_TargetRotation = rotation;
            Quaternion toLocal = Quaternion.Inverse(rotation);
            if (hasHead)
            {
                m_TargetHead = toLocal * (head - anchor);
                m_TargetHeadRadius = headRadius;
                if (!HasHead)
                {
                    m_Head = m_TargetHead;
                    m_HeadRadius = headRadius;
                }
                HasHead = true;
            }
            if (hasTorso)
            {
                m_TargetShoulders = toLocal * (shoulders - anchor);
                m_TargetHips = toLocal * (hips - anchor);
                m_TargetTorsoRadius = torsoRadius;
                if (!HasTorso)
                {
                    m_Shoulders = m_TargetShoulders;
                    m_Hips = m_TargetHips;
                    m_TorsoRadius = torsoRadius;
                }
                HasTorso = true;
            }
        }

        public void Step(float deltaTime)
        {
            if (!m_Initialized || deltaTime <= 0f) return;
            // A stalled inference/render frame must not turn into one large jump.
            deltaTime = Mathf.Min(deltaTime, 0.05f);
            float error = Vector3.Distance(m_Position, m_TargetPosition);
            float positionTime = Mathf.Lerp(0.14f, 0.045f, Mathf.InverseLerp(0.025f, 0.15f, error));
            m_Position = Filter(m_Position, m_TargetPosition, PositionDeadZone, positionTime, deltaTime);

            float angle = Quaternion.Angle(m_Rotation, m_TargetRotation);
            if (angle > AngleDeadZone)
            {
                float rotationTime = Mathf.Lerp(0.16f, 0.055f, Mathf.InverseLerp(3f, 20f, angle));
                float fraction = (1f - Mathf.Exp(-deltaTime / rotationTime)) * (1f - AngleDeadZone / angle);
                m_Rotation = Quaternion.Slerp(m_Rotation, m_TargetRotation, fraction);
            }

            if (HasHead)
            {
                m_Head = Filter(m_Head, m_TargetHead, ShapeDeadZone, 0.10f, deltaTime);
                m_HeadRadius = Filter(m_HeadRadius, m_TargetHeadRadius, RadiusDeadZone, deltaTime);
            }
            if (HasTorso)
            {
                // Filter center/half-axis as a pair to keep the capsule symmetric.
                Vector3 center = (m_Shoulders + m_Hips) * 0.5f;
                Vector3 halfAxis = (m_Shoulders - m_Hips) * 0.5f;
                center = Filter(center, (m_TargetShoulders + m_TargetHips) * 0.5f, ShapeDeadZone, 0.12f, deltaTime);
                halfAxis = Filter(halfAxis, (m_TargetShoulders - m_TargetHips) * 0.5f, ShapeDeadZone, 0.16f, deltaTime);
                m_Shoulders = center + halfAxis;
                m_Hips = center - halfAxis;
                m_TorsoRadius = Filter(m_TorsoRadius, m_TargetTorsoRadius, RadiusDeadZone, deltaTime);
            }
        }

        public void ForgetHead() => HasHead = false;
        public void ForgetTorso() => HasTorso = false;

        public void Reset()
        {
            m_Initialized = HasHead = HasTorso = false;
            m_TargetRotation = m_Rotation = Quaternion.identity;
        }

        static Vector3 Filter(Vector3 current, Vector3 target, float deadZone, float responseTime, float dt)
        {
            Vector3 delta = target - current;
            float distance = delta.magnitude;
            if (distance <= deadZone) return current;
            return current + delta * ((1f - Mathf.Exp(-dt / responseTime)) * (1f - deadZone / distance));
        }

        static float Filter(float current, float target, float deadZone, float dt)
        {
            float delta = target - current;
            if (Mathf.Abs(delta) <= deadZone) return current;
            return current + (delta - Mathf.Sign(delta) * deadZone) * (1f - Mathf.Exp(-dt / 0.18f));
        }
    }
}
