using UnityEngine;

namespace ARPrototype
{
    /// <summary>A single physical flight; local -Z is the face that touches the target.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
    public sealed class Projectile : MonoBehaviour
    {
        public enum FlightState { Ready, Flying, Attached, Finished }

        [SerializeField, Min(0.05f)] private float maxLifetime = 5f;
        [SerializeField, Min(0f)] private float surfaceOffset = 0.001f;

        private Rigidbody body;
        private BoxCollider box;
        private float age;

        public Transform SelectedTarget { get; private set; }
        public FlightState State { get; private set; }
        public Vector3 AttachedLocalPosition { get; private set; }
        public Quaternion AttachedLocalRotation { get; private set; }
        public float MaxLifetime { get => maxLifetime; set => maxLifetime = Mathf.Max(0.05f, value); }
        public float SurfaceOffset => surfaceOffset;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            box = GetComponent<BoxCollider>();
            body.useGravity = false;
        }

        public void Launch(Transform selectedTarget, Vector3 velocity)
        {
            if (State != FlightState.Ready)
                return;

            SelectedTarget = selectedTarget;
            age = 0f;
            box.enabled = true;
            box.isTrigger = false;
            body.isKinematic = false;
            body.useGravity = false;
            body.detectCollisions = true;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.angularVelocity = Vector3.zero;
            // Unity 2022.3 uses velocity, not Unity 6's linearVelocity.
            body.velocity = velocity;
            State = FlightState.Flying;
        }

        private void Update()
        {
            if (State != FlightState.Flying)
                return;

            age += Time.deltaTime;
            if (age >= maxLifetime)
                Finish();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (State != FlightState.Flying)
                return;

            Transform hit = collision.collider.transform;
            if (SelectedTarget == null ||
                (hit != SelectedTarget && !hit.IsChildOf(SelectedTarget)) ||
                collision.contactCount == 0)
            {
                Finish();
                return;
            }

            // Lock the state before changing physics or hierarchy: a compound collider may
            // produce more than one callback in the same simulation step.
            State = FlightState.Attached;
            ContactPoint contact = collision.GetContact(0);
            Attach(contact.point, contact.normal.normalized);
        }

        private void Attach(Vector3 contactPoint, Vector3 normal)
        {
            // +Z points away from the target; the cube's -Z face rests on the surface.
            Vector3 up = Vector3.ProjectOnPlane(transform.up, normal);
            if (up.sqrMagnitude < 0.0001f)
                up = Vector3.ProjectOnPlane(transform.right, normal);
            Quaternion rotation = Quaternion.LookRotation(normal, up.normalized);
            Vector3 scale = transform.lossyScale;
            float halfDepth = box.size.z * Mathf.Abs(scale.z) * 0.5f;
            Vector3 rotatedColliderCenter = rotation * Vector3.Scale(box.center, scale);
            Vector3 position = contactPoint + normal * (halfDepth + surfaceOffset)
                               - rotatedColliderCenter;

            AttachedLocalPosition = SelectedTarget.InverseTransformPoint(position);
            AttachedLocalRotation = Quaternion.Inverse(SelectedTarget.rotation) * rotation;

            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            body.isKinematic = true;
            body.detectCollisions = false;
            body.interpolation = RigidbodyInterpolation.None;
            box.enabled = false;

            transform.SetPositionAndRotation(position, rotation);
            transform.SetParent(SelectedTarget, true);
            transform.localPosition = AttachedLocalPosition;
            transform.localRotation = AttachedLocalRotation;
            // No delayed Destroy was scheduled: attached cubes outlive their flight timer.
        }

        private void Finish()
        {
            State = FlightState.Finished;
            box.enabled = false;
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            body.isKinematic = true;
            body.detectCollisions = false;
            Destroy(gameObject);
        }
    }
}
