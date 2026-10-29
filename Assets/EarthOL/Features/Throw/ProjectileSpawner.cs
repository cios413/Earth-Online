using UnityEngine;
using UnityEngine.EventSystems;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EarthOL.Throw
{
    /// <summary>
    /// 点击屏幕 → 从相机射出屏幕射线 → 生成投掷物朝指定目标飞过去。
    /// 逻辑移植自 cios413/Earth-Online 的 ProjectileSpawner.cs，改动只有两处：
    ///   1) 输入同时兼容新/旧输入系统（本工程是「仅新输入系统」）；
    ///   2) 原来的 AR Foundation 追踪判断（ARSession.state）换成不需要 AR Foundation 的检查。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProjectileSpawner : MonoBehaviour
    {
        [SerializeField] private Projectile projectilePrefab;
        [SerializeField] private Camera arCamera;
        [SerializeField] private Transform selectedTarget;
        [SerializeField, Min(0.01f)] private float throwSpeed = 6f;
        [SerializeField, Min(0.05f)] private float spawnDistance = 0.2f;
        [SerializeField] private bool requireTrackingOnDevice = true;
        [SerializeField] private Vector3 projectileSize = new Vector3(0.04f, 0.04f, 0.04f);
        [SerializeField] private Material projectileMaterial;

        public Projectile ProjectilePrefab { get => projectilePrefab; set => projectilePrefab = value; }
        public Camera ARCamera { get => arCamera; set => arCamera = value; }
        public Transform SelectedTarget { get => selectedTarget; set => selectedTarget = value; }
        public float ThrowSpeed { get => throwSpeed; set => throwSpeed = Mathf.Max(0.01f, value); }
        public float SpawnDistance { get => spawnDistance; set => spawnDistance = Mathf.Max(0.05f, value); }
        public Material ProjectileMaterial { get => projectileMaterial; set => projectileMaterial = value; }

        private void Update()
        {
            HandleInput();
        }

        private void HandleInput()
        {
            bool enabledMode = EarthOL.ModeOverlay.IsActive && EarthOL.ModeOverlay.Current == EarthOL.GameMode.Throw;
            if (!enabledMode) return;

            bool clicked = false;
            Vector2 position = Vector2.zero;

#if ENABLE_INPUT_SYSTEM
            var touchscreen = Touchscreen.current;
            if (touchscreen != null)
            {
                foreach (var touch in touchscreen.touches)
                {
                    if (!touch.press.wasPressedThisFrame) continue;
                    position = touch.position.ReadValue();
                    clicked = true;
                    break;
                }
            }

            if (!clicked && !Application.isMobilePlatform)
            {
                var mouse = Mouse.current;
                if (mouse != null && mouse.leftButton.wasPressedThisFrame)
                {
                    position = mouse.position.ReadValue();
                    clicked = true;
                }
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            if (!clicked && Input.touchCount > 0)
            {
                for (int i = 0; i < Input.touchCount; i++)
                {
                    var touch = Input.GetTouch(i);
                    if (touch.phase != TouchPhase.Began) continue;
                    position = touch.position;
                    clicked = true;
                    break;
                }
            }

            if (!clicked && !Application.isMobilePlatform && Input.GetMouseButtonDown(0))
            {
                position = Input.mousePosition;
                clicked = true;
            }
#endif

            if (!clicked) return;
            if (EarthOL.ModeOverlay.IsPointOnOverlay(position)) return;
            if (IsOverUI(position)) return;

            ThrowAtScreenPoint(position);
        }

        private static bool IsOverUI(Vector2 screenPosition)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null) return false;

            return eventSystem.IsPointerOverGameObject();
        }

        public Projectile ThrowAtScreenPoint(Vector2 screenPosition)
        {
            if (arCamera == null) return null;

            Ray ray = arCamera.ScreenPointToRay(screenPosition);
            return Spawn(ray.origin, ray.direction);
        }

        /// <summary>给未来的手势模块用：传世界坐标方向即可。</summary>
        public Projectile Throw(Vector3 direction)
        {
            return arCamera == null ? null : Spawn(arCamera.transform.position, direction);
        }

        private Projectile Spawn(Vector3 origin, Vector3 direction)
        {
            if (arCamera == null || selectedTarget == null ||
                !selectedTarget.gameObject.activeInHierarchy ||
                !IsFinite(direction) || direction.sqrMagnitude < 0.000001f)
                return null;

            // 手机上没有有效位姿时就先不发射（原来是判断 ARSession.state）
            if (!Application.isEditor && Application.isMobilePlatform && requireTrackingOnDevice && !HasCameraPose())
                return null;

            direction.Normalize();

            Projectile projectile = projectilePrefab != null
                ? Instantiate(projectilePrefab)
                : BuildProjectile();

            projectile.transform.SetPositionAndRotation(
                origin + direction * spawnDistance,
                Quaternion.LookRotation(direction));

            projectile.Launch(selectedTarget, direction * throwSpeed);
            Debug.Log($"[Throw] 发射：{projectile.name} 速度 {throwSpeed} m/s 目标 {selectedTarget.name}");
            return projectile;
        }

        /// <summary>没有 Prefab 时用基本体现建一个（这样不需要往工程里加 Prefab 资源）。</summary>
        private Projectile BuildProjectile()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Projectile (runtime)";
            go.transform.localScale = projectileSize;

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null && projectileMaterial != null) renderer.sharedMaterial = projectileMaterial;

            var body = go.AddComponent<Rigidbody>();
            body.mass = 0.02f;
            body.useGravity = false;

            return go.AddComponent<Projectile>();
        }

        /// <summary>相机位姿是否可用（不需要 AR Foundation）。</summary>
        private bool HasCameraPose()
        {
            if (arCamera == null) return false;

            // 位姿退化（NaN / 单位矩阵）说明还没建立追踪
            var position = arCamera.transform.position;
            return IsFinite(position) && position.sqrMagnitude < 1e6f && arCamera.enabled;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }
    }
}
