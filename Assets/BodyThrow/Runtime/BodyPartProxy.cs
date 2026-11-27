using UnityEngine;
using UnityEngine.Rendering;

namespace EarthOL.BodyThrow
{
    public enum BodyPartType
    {
        Head,
        Chest,
        LeftUpperArm,
        LeftForearm,
        RightUpperArm,
        RightForearm,
        LeftUpperLeg,
        LeftLowerLeg,
        RightUpperLeg,
        RightLowerLeg
    }

    /// <summary>
    /// A single physical body-part proxy. The pose tracker in the next stage will update
    /// this transform; the projectile system already treats it as an attachment surface.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BodyPartProxy : MonoBehaviour
    {
        [SerializeField] private BodyPartType partType;
        [SerializeField] private bool debugVisible = true;

        private Renderer m_Renderer;
        private CapsuleCollider m_Capsule;
        private SphereCollider m_Sphere;
        private Transform m_Visual;

        public BodyPartType PartType => partType;
        public Transform AttachmentTransform => transform;
        public bool DebugVisible => debugVisible;

        public void Configure(BodyPartType type, Material material, bool visible)
        {
            partType = type;
            debugVisible = visible;
            m_Renderer = GetComponentInChildren<Renderer>();
            m_Visual = m_Renderer != null ? m_Renderer.transform : null;

            if (m_Renderer != null)
            {
                if (material != null) m_Renderer.sharedMaterial = material;
                // The invisible material still needs an enabled renderer to write depth.
                m_Renderer.enabled = visible;
                m_Renderer.shadowCastingMode = ShadowCastingMode.Off;
                m_Renderer.receiveShadows = false;
            }

            m_Capsule = GetComponent<CapsuleCollider>();
            m_Sphere = GetComponent<SphereCollider>();
        }

        public void SetLocalSphere(Vector3 localPosition, float radius)
        {
            if (m_Sphere == null) m_Sphere = GetComponent<SphereCollider>();
            if (m_Sphere == null) return;

            transform.localPosition = localPosition;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
            m_Sphere.radius = Mathf.Max(0.001f, radius);
            if (m_Visual != null)
                m_Visual.localScale = Vector3.one * Mathf.Max(0.001f, radius * 2f);
        }

        public void SetWorldSphere(Vector3 worldPosition, float radius)
        {
            Transform parent = transform.parent;
            SetLocalSphere(parent != null ? parent.InverseTransformPoint(worldPosition) : worldPosition, radius);
        }

        /// <summary>Sets a capsule whose long axis runs from start to end in rig-local space.</summary>
        public void SetLocalSegment(Vector3 start, Vector3 end, float radius)
        {
            if (m_Capsule == null) m_Capsule = GetComponent<CapsuleCollider>();
            if (m_Capsule == null) return;

            Vector3 delta = end - start;
            float length = delta.magnitude;
            if (length < 0.0001f) length = radius * 2f;

            transform.localPosition = (start + end) * 0.5f;
            transform.localRotation = Quaternion.FromToRotation(Vector3.up, delta.normalized);
            transform.localScale = Vector3.one;
            m_Capsule.radius = Mathf.Max(0.001f, radius);
            m_Capsule.height = Mathf.Max(radius * 2f, length + radius * 2f);
            if (m_Visual != null)
            {
                // The visual child is a unit primitive capsule: diameter 1, height 2.
                m_Visual.localScale = new Vector3(
                    Mathf.Max(0.001f, radius * 2f),
                    Mathf.Max(0.001f, m_Capsule.height * 0.5f),
                    Mathf.Max(0.001f, radius * 2f));
            }
        }

        public void SetWorldSegment(Vector3 start, Vector3 end, float radius)
        {
            Transform parent = transform.parent;
            if (parent != null)
            {
                SetLocalSegment(parent.InverseTransformPoint(start), parent.InverseTransformPoint(end), radius);
            }
            else
            {
                SetLocalSegment(start, end, radius);
            }
        }

        public void SetDebugVisible(bool visible)
        {
            debugVisible = visible;
            if (m_Renderer == null) m_Renderer = GetComponentInChildren<Renderer>();
            if (m_Renderer != null) m_Renderer.enabled = visible;
        }
    }
}
