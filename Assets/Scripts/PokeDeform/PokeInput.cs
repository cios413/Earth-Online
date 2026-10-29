using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
// UnityEngine.Touch 和 UnityEngine.InputSystem.EnhancedTouch.Touch 同名，必须用别名区分
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using Finger = UnityEngine.InputSystem.EnhancedTouch.Finger;
#endif

namespace EarthOL.Deform
{
    /// <summary>
    /// 手机触摸 / 编辑器鼠标 → 射线 → 命中被识别的物体 → 触发形变。
    ///
    /// 关键点：Vuforia 的 DefaultObserverEventHandler 在丢失跟踪时会把
    /// 目标的 Renderer / Collider 全部禁用，所以"射线能打到" == "目标正被识别"，
    /// 不需要额外判断跟踪状态。
    ///
    /// 本工程 Player Settings 里 Active Input Handling = Input System Package (New)，
    /// 因此使用新输入系统（EnhancedTouch + Mouse）。同时保留了旧输入系统的分支，
    /// 方便把脚本搬到别的工程。
    /// </summary>
    [AddComponentMenu("Earth OL/Poke Input (触摸戳击)")]
    [DisallowMultipleComponent]
    public class PokeInput : MonoBehaviour
    {
        const int kMousePointerId = -1;

        [Header("射线")]
        [Tooltip("留空则自动使用 Camera.main")]
        [SerializeField] Camera m_Camera;

        [Tooltip("射线最远距离（米）")]
        [SerializeField] float m_MaxRayDistance = 5f;

        [Tooltip("可被戳的层。默认全部，脚本会自行跳过没有 IPokeTarget 的物体")]
        [SerializeField] LayerMask m_RaycastLayers = ~0;

        [Header("手感")]
        [Tooltip("手指戳偏了一点也能戳中：宽容半径（米），0 = 只用精确射线")]
        [SerializeField] float m_ForgivenessRadius = 0.008f;

        [Tooltip("按住后滑动手指时，凹陷是否跟着手指在物体表面移动")]
        [SerializeField] bool m_EnableDrag = true;

        [Tooltip("点在 UI 上时不要穿透到 3D 物体")]
        [SerializeField] bool m_IgnoreUI = true;

        [SerializeField] bool m_LogPokes = false;

        readonly Dictionary<int, IPokeTarget> m_ActivePointers = new Dictionary<int, IPokeTarget>(4);
        readonly RaycastHit[] m_Hits = new RaycastHit[8];

        Camera Cam
        {
            get
            {
                if (m_Camera == null) m_Camera = Camera.main;
                return m_Camera;
            }
        }

#if ENABLE_INPUT_SYSTEM
        void OnEnable()
        {
            EnhancedTouchSupport.Enable();
            Touch.onFingerDown += HandleFingerDown;
            Touch.onFingerMove += HandleFingerMove;
            Touch.onFingerUp += HandleFingerUp;
        }

        void OnDisable()
        {
            Touch.onFingerDown -= HandleFingerDown;
            Touch.onFingerMove -= HandleFingerMove;
            Touch.onFingerUp -= HandleFingerUp;
            EnhancedTouchSupport.Disable();
            ReleaseAllPointers();
        }

        void HandleFingerDown(Finger finger) => HandleDown(finger.index, finger.screenPosition);
        void HandleFingerMove(Finger finger) => HandleMove(finger.index, finger.screenPosition);
        void HandleFingerUp(Finger finger) => HandleUp(finger.index);

        // 触摸走 EnhancedTouch 事件；鼠标（编辑器 / 模拟器）在 Update 里轮询
        void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            if (mouse.leftButton.wasPressedThisFrame)
                HandleDown(kMousePointerId, mouse.position.ReadValue());
            else if (mouse.leftButton.isPressed)
                HandleMove(kMousePointerId, mouse.position.ReadValue());
            else if (mouse.leftButton.wasReleasedThisFrame)
                HandleUp(kMousePointerId);
        }
#else
        void Update()
        {
            for (int i = 0; i < Input.touchCount; i++)
            {
                var touch = Input.GetTouch(i);
                switch (touch.phase)
                {
                    case TouchPhase.Began:
                        HandleDown(touch.fingerId, touch.position);
                        break;
                    case TouchPhase.Moved:
                    case TouchPhase.Stationary:
                        HandleMove(touch.fingerId, touch.position);
                        break;
                    case TouchPhase.Ended:
                    case TouchPhase.Canceled:
                        HandleUp(touch.fingerId);
                        break;
                }
            }

            if (Input.GetMouseButtonDown(0)) HandleDown(kMousePointerId, Input.mousePosition);
            else if (Input.GetMouseButton(0)) HandleMove(kMousePointerId, Input.mousePosition);
            else if (Input.GetMouseButtonUp(0)) HandleUp(kMousePointerId);
        }

        void OnDisable()
        {
            ReleaseAllPointers();
        }
#endif

        // 手机切后台 / 失焦时，手指抬起事件可能收不到，这里兜底松开所有凹陷
        void OnApplicationPause(bool paused)
        {
            if (paused) ReleaseAllPointers();
        }

        void OnApplicationFocus(bool focused)
        {
            if (!focused) ReleaseAllPointers();
        }

        void HandleDown(int pointerId, Vector2 screenPosition)
        {
            if (m_ActivePointers.ContainsKey(pointerId)) return;
            if (m_IgnoreUI && IsPointerOverUI(pointerId)) return;

            // 点在左上角的模式切换按钮上时不要穿透到 3D
            if (EarthOL.ModeOverlay.IsPointOnOverlay(screenPosition)) return;

            if (!TryFindTarget(screenPosition, out var target, out var hit)) return;

            m_ActivePointers[pointerId] = target;
            target.OnPokeBegin(pointerId, hit);

            if (m_LogPokes)
                Debug.Log($"[PokeInput] down id={pointerId} -> {target} @ {hit.point}");
        }

        void HandleMove(int pointerId, Vector2 screenPosition)
        {
            if (!m_ActivePointers.TryGetValue(pointerId, out var target) || !IsAlive(target)) return;
            if (!m_EnableDrag) return;

            if (TryFindTarget(screenPosition, out var hitTarget, out var hit) && ReferenceEquals(hitTarget, target))
                target.OnPokeDrag(pointerId, hit);
        }

        void HandleUp(int pointerId)
        {
            if (!m_ActivePointers.TryGetValue(pointerId, out var target)) return;
            m_ActivePointers.Remove(pointerId);
            if (IsAlive(target)) target.OnPokeEnd(pointerId);
        }

        void ReleaseAllPointers()
        {
            if (m_ActivePointers.Count == 0) return;

            foreach (var pair in m_ActivePointers)
                if (IsAlive(pair.Value)) pair.Value.OnPokeEnd(pair.Key);

            m_ActivePointers.Clear();
        }

        /// <summary>
        /// 接口类型变量上的 == null 不会走 UnityEngine.Object 的重载，
        /// 物体被销毁后会误判成"还活着"，这里显式转成 Object 判断。
        /// </summary>
        static bool IsAlive(IPokeTarget target)
        {
            if (ReferenceEquals(target, null)) return false;
            return !(target is UnityEngine.Object unityObject) || unityObject != null;
        }

        /// <summary>先用精确射线，打不到再放宽成一个小球，模拟"手指肚"的宽容度。</summary>
        bool TryFindTarget(Vector2 screenPosition, out IPokeTarget target, out RaycastHit bestHit)
        {
            target = null;
            bestHit = default;

            var cam = Cam;
            if (cam == null) return false;

            var ray = cam.ScreenPointToRay(screenPosition);

            if (CastAndPickNearest(ray, 0f, ref target, ref bestHit)) return true;
            if (m_ForgivenessRadius > 0f && CastAndPickNearest(ray, m_ForgivenessRadius, ref target, ref bestHit))
                return true;

            return false;
        }

        bool CastAndPickNearest(Ray ray, float radius, ref IPokeTarget target, ref RaycastHit bestHit)
        {
            int count;
            if (radius <= 0f)
            {
                count = Physics.RaycastNonAlloc(ray, m_Hits, m_MaxRayDistance, m_RaycastLayers,
                    QueryTriggerInteraction.Ignore);
            }
            else
            {
                // QueryTriggerInteraction.Ignore + 起始点已在 Collider 内时，SphereCast 不会报重叠，正合需要
                count = Physics.SphereCastNonAlloc(ray, radius, m_Hits, m_MaxRayDistance, m_RaycastLayers,
                    QueryTriggerInteraction.Ignore);
            }

            if (count <= 0) return false;

            // RaycastNonAlloc 的结果不保证有序，自己找出最近的、并且带 IPokeTarget 的那个
            float nearest = float.MaxValue;
            bool found = false;

            for (int i = 0; i < count; i++)
            {
                var hit = m_Hits[i];
                if (hit.collider == null) continue;

                var candidate = hit.collider.GetComponentInParent<IPokeTarget>();
                if (candidate == null) continue;

                // hit.distance 为 0 表示射线起点就在 Collider 内部，也算命中
                if (hit.distance < nearest)
                {
                    nearest = hit.distance;
                    bestHit = hit;
                    target = candidate;
                    found = true;
                }
            }

            return found;
        }

        bool IsPointerOverUI(int pointerId)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null) return false;
            return eventSystem.IsPointerOverGameObject(pointerId);
        }
    }
}
