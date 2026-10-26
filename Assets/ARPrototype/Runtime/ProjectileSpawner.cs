using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.ARFoundation;

namespace ARPrototype
{
    [DisallowMultipleComponent]
    public sealed class ProjectileSpawner : MonoBehaviour
    {
        [SerializeField] private Projectile projectilePrefab;
        [SerializeField] private Camera arCamera;
        [SerializeField] private Transform selectedTarget;
        [SerializeField, Min(0.01f)] private float throwSpeed = 6f;
        [SerializeField, Min(0.05f)] private float spawnDistance = 0.2f;
        [SerializeField] private bool requireTrackingOnDevice = true;

        public Projectile ProjectilePrefab { get => projectilePrefab; set => projectilePrefab = value; }
        public Camera ARCamera { get => arCamera; set => arCamera = value; }
        public Transform SelectedTarget { get => selectedTarget; set => selectedTarget = value; }
        public float ThrowSpeed { get => throwSpeed; set => throwSpeed = Mathf.Max(0.01f, value); }
        public float SpawnDistance { get => spawnDistance; set => spawnDistance = Mathf.Max(0.05f, value); }

        private void Update()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.touchCount > 0)
            {
                for (int i = 0; i < Input.touchCount; i++)
                {
                    Touch touch = Input.GetTouch(i);
                    if (touch.phase == TouchPhase.Began && !IsOverUI(touch.fingerId))
                        ThrowAtScreenPoint(touch.position);
                }
                return;
            }

            // Mobile platforms use only Touch. This also excludes synthetic mouse events
            // on the final touch frame, including Active Input Handling = Both.
            if (!Application.isMobilePlatform && Input.GetMouseButtonDown(0) && !IsOverUI(-1))
                ThrowAtScreenPoint(Input.mousePosition);
#endif
        }

        private static bool IsOverUI(int pointerId)
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(pointerId);
        }

        public Projectile ThrowAtScreenPoint(Vector2 screenPosition)
        {
            if (arCamera == null)
                return null;
            Ray ray = arCamera.ScreenPointToRay(screenPosition);
            return Spawn(ray.origin, ray.direction);
        }

        /// <summary>Future gestures can pass a direction in Unity world coordinates.</summary>
        public Projectile Throw(Vector3 direction)
        {
            return arCamera == null ? null : Spawn(arCamera.transform.position, direction);
        }

        private Projectile Spawn(Vector3 origin, Vector3 direction)
        {
            if (projectilePrefab == null || arCamera == null || selectedTarget == null ||
                !selectedTarget.gameObject.activeInHierarchy || !IsFinite(direction) ||
                direction.sqrMagnitude < 0.000001f)
                return null;

            if (!Application.isEditor && Application.isMobilePlatform && requireTrackingOnDevice &&
                ARSession.state != ARSessionState.SessionTracking)
                return null;

            direction.Normalize();
            Projectile projectile = Instantiate(projectilePrefab, origin + direction * spawnDistance,
                Quaternion.LookRotation(direction));
            projectile.Launch(selectedTarget, direction * throwSpeed);
            return projectile;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }
    }
}
