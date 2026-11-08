using UnityEngine;

namespace EarthOL.BodyThrow
{
    /// <summary>
    /// Physical projectile adapted from shashasha's tested projectile.
    /// It validates against the body rig, then attaches to the exact body part hit.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
    public sealed class BodyProjectile : MonoBehaviour
    {
        public enum FlightState { Ready, Flying, Attached, Finished }

        [SerializeField, Min(0.05f)] private float maxLifetime = 5f;
        [SerializeField, Min(0f)] private float surfaceOffset = 0.001f;

        private Rigidbody m_Body;
        private BoxCollider m_Box;
        private float m_Age;

        public Transform SelectedTarget { get; private set; }
        public BodyPartProxy AttachedPart { get; private set; }
        public FlightState State { get; private set; }
        public Vector3 AttachedLocalPosition { get; private set; }
        public Quaternion AttachedLocalRotation { get; private set; }
        public float MaxLifetime { get => maxLifetime; set => maxLifetime = Mathf.Max(0.05f, value); }

        /// <summary>本次投掷对应的攻击 ID（由投掷器在生成时写入），命中后用于上报攻击事件。</summary>
        public string AttackId { get; set; }

        private void Awake()
        {
            m_Body = GetComponent<Rigidbody>();
            m_Box = GetComponent<BoxCollider>();
            m_Body.useGravity = false;
        }

        public void Launch(Transform targetRoot, Vector3 velocity)
        {
            if (State != FlightState.Ready) return;
            if (m_Body == null || m_Box == null) Awake();

            SelectedTarget = targetRoot;
            AttachedPart = null;
            m_Age = 0f;
            m_Box.enabled = true;
            m_Box.isTrigger = false;
            m_Body.isKinematic = false;
            m_Body.useGravity = false;
            m_Body.detectCollisions = true;
            m_Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            m_Body.interpolation = RigidbodyInterpolation.Interpolate;
            m_Body.angularVelocity = Vector3.zero;
            m_Body.velocity = velocity;
            State = FlightState.Flying;
        }

        private void Update()
        {
            if (State != FlightState.Flying) return;
            m_Age += Time.deltaTime;
            if (m_Age >= maxLifetime) Finish();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (State != FlightState.Flying) return;

            var part = collision.collider.GetComponentInParent<BodyPartProxy>();
            if (SelectedTarget == null || part == null ||
                (part.transform != SelectedTarget && !part.transform.IsChildOf(SelectedTarget)) ||
                collision.contactCount == 0)
            {
                Finish();
                return;
            }

            State = FlightState.Attached;
            var contact = collision.GetContact(0);
            Attach(part, contact.point, contact.normal.normalized);
        }

        private void Attach(BodyPartProxy part, Vector3 contactPoint, Vector3 normal)
        {
            Vector3 up = Vector3.ProjectOnPlane(transform.up, normal);
            if (up.sqrMagnitude < 0.0001f)
                up = Vector3.ProjectOnPlane(transform.right, normal);

            // Local +Z is the flight/tip direction. Keep it pointing into the
            // body surface when attached, rather than reversing arrows on impact.
            Quaternion rotation = Quaternion.LookRotation(-normal, up.normalized);
            Vector3 scale = transform.lossyScale;
            float halfDepth = m_Box.size.z * Mathf.Abs(scale.z) * 0.5f;
            Vector3 rotatedColliderCenter = rotation * Vector3.Scale(m_Box.center, scale);
            Vector3 position = contactPoint + normal * (halfDepth + surfaceOffset) - rotatedColliderCenter;
            Transform attachment = part.AttachmentTransform;

            AttachedPart = part;
            AttachedLocalPosition = attachment.InverseTransformPoint(position);
            AttachedLocalRotation = Quaternion.Inverse(attachment.rotation) * rotation;

            m_Body.velocity = Vector3.zero;
            m_Body.angularVelocity = Vector3.zero;
            m_Body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            m_Body.isKinematic = true;
            m_Body.detectCollisions = false;
            m_Body.interpolation = RigidbodyInterpolation.None;
            m_Box.enabled = false;

            transform.SetPositionAndRotation(position, rotation);
            transform.SetParent(attachment, true);
            transform.localPosition = AttachedLocalPosition;
            transform.localRotation = AttachedLocalRotation;

            var bounce = GetComponent<ProjectileImpactBounce>();
            if (bounce == null) bounce = gameObject.AddComponent<ProjectileImpactBounce>();
            bounce.Play(m_Box.center.z + m_Box.size.z * 0.5f);

            // 投掷物成功命中并贴附后，才算一次成功攻击。
            EarthOL.Combat.AttackDispatcher.Report(AttackId);

            Debug.Log($"[BodyThrow] 命中身体部位：{part.PartType}，投掷物已贴附");
        }

        private void Finish()
        {
            State = FlightState.Finished;
            if (m_Box != null) m_Box.enabled = false;
            if (m_Body != null)
            {
                m_Body.velocity = Vector3.zero;
                m_Body.angularVelocity = Vector3.zero;
                m_Body.collisionDetectionMode = CollisionDetectionMode.Discrete;
                m_Body.isKinematic = true;
                m_Body.detectCollisions = false;
            }
            Destroy(gameObject);
        }
    }
}
