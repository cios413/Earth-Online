using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.ARFoundation;
using EarthOL.ARCore;
using System.Collections.Generic;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EarthOL.BodyThrow
{
    /// <summary>AR Foundation throw input and projectile creation for the body proxy scene.</summary>
    [DisallowMultipleComponent]
    public sealed class BodyProjectileSpawner : MonoBehaviour
    {
        [SerializeField] private BodyProjectile projectilePrefab;
        [SerializeField] private Camera arCamera;
        [SerializeField] private Transform selectedTarget;
        [SerializeField, Min(0.01f)] private float throwSpeed = 6f;
        [SerializeField, Min(0.05f)] private float spawnDistance = 0.2f;
        [SerializeField] private bool requireTrackingOnDevice = true;
        [SerializeField] private BodyPoseTracker poseTracker;
        [SerializeField] private BodyProjectileCatalog catalog;
        private int m_SelectedProjectileIndex;
        private readonly List<BodyProjectile> m_ActiveProjectiles = new List<BodyProjectile>();

        public BodyProjectile ProjectilePrefab
        {
            get => catalog != null && catalog.entries.Length > 0 ? catalog.entries[m_SelectedProjectileIndex].prefab : projectilePrefab;
            set => projectilePrefab = value;
        }
        public BodyProjectileCatalog Catalog => catalog;
        public int SelectedProjectileIndex => m_SelectedProjectileIndex;
        public string SelectedProjectileName => catalog != null && catalog.entries.Length > 0
            ? catalog.entries[m_SelectedProjectileIndex].displayName : "投掷物";
        public int ActiveProjectileCount => m_ActiveProjectiles.Count;

        public void ClearProjectiles()
        {
            int cleared = 0;
            for (int i = m_ActiveProjectiles.Count - 1; i >= 0; i--)
            {
                var projectile = m_ActiveProjectiles[i];
                if (projectile == null) { m_ActiveProjectiles.RemoveAt(i); continue; }
                if (Application.isPlaying) Destroy(projectile.gameObject);
                else DestroyImmediate(projectile.gameObject);
                cleared++;
            }
            m_ActiveProjectiles.Clear();
            Debug.Log("[BodyThrow] 已清空投掷物：" + cleared);
        }
        public Camera ARCamera { get => arCamera; set => arCamera = value; }
        public Transform SelectedTarget { get => selectedTarget; set => selectedTarget = value; }
        public float ThrowSpeed { get => throwSpeed; set => throwSpeed = Mathf.Max(0.01f, value); }
        public float SpawnDistance { get => spawnDistance; set => spawnDistance = Mathf.Max(0.05f, value); }
        public BodyPoseTracker PoseTracker { get => poseTracker; set => poseTracker = value; }

        public void ConfigureCatalog(BodyProjectileCatalog choices)
        {
            catalog = choices;
            m_SelectedProjectileIndex = 0;
            if (catalog == null || catalog.entries.Length != 4)
                Debug.LogError("[BodyThrow] 缺少四种投掷物目录，请先运行 ThrowableAssetBuilder。");
            else
                Debug.Log("[BodyThrow] 四种投掷物就绪，默认：" + SelectedProjectileName);
        }

        public bool SelectProjectile(int index)
        {
            if (catalog == null || index < 0 || index >= catalog.entries.Length || catalog.entries[index].prefab == null)
                return false;
            m_SelectedProjectileIndex = index;
            if (poseTracker != null) poseTracker.ClearPendingInput();
            Debug.Log("[BodyThrow] 已更改投掷物：" + SelectedProjectileName);
            return true;
        }

        private void Update()
        {
            if (ARCoreModeOverlay.BlocksWorldInput)
            {
                if (poseTracker != null) poseTracker.ClearPendingInput();
                return;
            }
            if (poseTracker != null)
            {
                if (!poseTracker.TryConsumeProjectileTap(out var trackedTap)) return;
                if (!poseTracker.CanThrow) return;
                ThrowAtScreenPoint(trackedTap);
                return;
            }

            if (!TryGetInput(out var screenPosition)) return;
            if (ARCoreModeOverlay.IsPointOnOverlay(screenPosition)) return;
            if (IsOverUI()) return;
            ThrowAtScreenPoint(screenPosition);
        }

        public BodyProjectile ThrowAtScreenPoint(Vector2 screenPosition)
        {
            if (arCamera == null) return null;
            Ray ray = arCamera.ScreenPointToRay(screenPosition);
            return Spawn(ray.origin, ray.direction);
        }

        public BodyProjectile Throw(Vector3 worldDirection)
        {
            return arCamera == null ? null : Spawn(arCamera.transform.position, worldDirection);
        }

        private BodyProjectile Spawn(Vector3 origin, Vector3 direction)
        {
            var prefab = ProjectilePrefab;
            if (ARCoreModeOverlay.BlocksWorldInput || arCamera == null || prefab == null || selectedTarget == null ||
                !selectedTarget.gameObject.activeInHierarchy || !IsFinite(direction) ||
                direction.sqrMagnitude < 0.000001f)
                return null;

            if (!Application.isEditor && Application.isMobilePlatform && requireTrackingOnDevice &&
                ARSession.state != ARSessionState.SessionTracking)
                return null;

            direction.Normalize();
            var box = prefab.GetComponent<BoxCollider>();
            float safeDistance = spawnDistance;
            if (box != null)
            {
                float halfDepth = (box.size.z * 0.5f + Mathf.Abs(box.center.z)) * Mathf.Abs(prefab.transform.lossyScale.z);
                safeDistance = Mathf.Max(safeDistance, halfDepth + arCamera.nearClipPlane + 0.03f);
            }
            var projectile = Instantiate(prefab,
                origin + direction * safeDistance,
                Quaternion.LookRotation(direction));
            m_ActiveProjectiles.Add(projectile);
            projectile.AttackId = CurrentAttackId;
            projectile.Launch(selectedTarget, direction * throwSpeed);
            return projectile;
        }

        string CurrentAttackId =>
            catalog != null && catalog.entries.Length > 0 && m_SelectedProjectileIndex < catalog.entries.Length
                ? catalog.entries[m_SelectedProjectileIndex].key
                : null;

        private static bool TryGetInput(out Vector2 position)
        {
            position = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
            var touchscreen = Touchscreen.current;
            if (touchscreen != null)
            {
                foreach (var touch in touchscreen.touches)
                {
                    if (!touch.press.wasPressedThisFrame) continue;
                    position = touch.position.ReadValue();
                    return true;
                }
            }

            if (!Application.isMobilePlatform)
            {
                var mouse = Mouse.current;
                if (mouse != null && mouse.leftButton.wasPressedThisFrame)
                {
                    position = mouse.position.ReadValue();
                    return true;
                }
            }
#endif
            return false;
        }

        private static bool IsOverUI()
        {
            var eventSystem = EventSystem.current;
            return eventSystem != null && eventSystem.IsPointerOverGameObject();
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }
    }
}
