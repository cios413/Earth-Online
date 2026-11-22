using UnityEngine;
using EarthOL.Combat;

namespace EarthOL
{
    [DefaultExecutionOrder(-275)]
    [DisallowMultipleComponent]
    public sealed class RatFigure : MonoBehaviour
    {
        public static RatFigure Instance { get; private set; }
        const float kLongPress = 0.7f;
        const float kMoveThreshold = 24f;
        GameObject m_Rat;
        GameObject m_Preview;
        bool m_PointerHeld;
        bool m_PressAccepted;
        bool m_PressMoved;
        bool m_DraggingRat;
        bool m_Consumed;
        bool m_Modal;
        Vector2 m_PressStart;
        Vector3 m_DragOrigin;
        float m_PressTime;
        float m_PokeTime = -10f;
        public bool HasRat => m_Rat != null;
        public bool OwnsGesture => m_Consumed || m_Modal || m_Preview != null;
        public bool ReplacementPending => m_Modal;
        Camera Cam => Camera.main;

        void Awake() => Instance = this;
        void OnDestroy()
        {
            RemoveRat();
            if (Instance == this) Instance = null;
        }

        static bool Active => ModeOverlay.Instance != null && ModeOverlay.Instance.IsPlaying &&
            ModeOverlay.Current == GameMode.Free;

        public static bool ContainsControls(Vector2 position)
        {
            if (Instance == null || !Active) return false;
            return Instance.m_Modal || Instance.HasRat &&
                new Rect(Screen.width - 180f, 120f, 168f, 44f)
                    .Contains(new Vector2(position.x, Screen.height - position.y));
        }

        public void SyncVisibility()
        {
            if (m_Rat != null) m_Rat.SetActive(Active);
            if (!Active)
            {
                CancelPreview();
                m_PressAccepted = m_PointerHeld = m_DraggingRat = m_Consumed = false;
            }
        }

        void Update()
        {
            SyncVisibility();
            if (!Active || m_Modal) return;
            if (m_Rat != null)
            {
                float elapsed = Time.unscaledTime - m_PokeTime;
                float squeeze = elapsed < 0.5f ? Mathf.Sin(elapsed * 24f) * Mathf.Exp(-elapsed * 8f) * 0.15f : 0f;
                m_Rat.transform.localScale = new Vector3(1f + squeeze, 1f - squeeze, 1f);
            }
            if (!PollPointer(out var pos, out bool down, out bool up))
            {
                if (m_PressAccepted) CancelPreview();
                m_PressAccepted = m_DraggingRat = false;
                return;
            }
            ProcessPointer(pos, down, up, m_PointerHeld);
        }

        public void ProcessPointer(Vector2 pos, bool down, bool up, bool held)
        {
            if (!Active || m_Modal) return;
            m_PointerHeld = held;
            if (down)
            {
                m_Consumed = false;
                m_PressStart = pos;
                m_PressTime = Time.unscaledTime;
                m_PressMoved = false;
                m_DraggingRat = !ModeOverlay.IsPointOnOverlay(pos) && TryHitRat(pos);
                m_PressAccepted = !ModeOverlay.IsPointOnOverlay(pos) &&
                    (m_DraggingRat || CombatManager.Instance != null && CombatManager.Instance.Tracker.Pick(pos) != null);
                if (m_DraggingRat)
                {
                    m_Consumed = true;
                    m_DragOrigin = m_Rat.transform.position;
                }
            }
            if (m_PointerHeld && m_PressAccepted)
            {
                if (Vector2.Distance(pos, m_PressStart) >= kMoveThreshold) m_PressMoved = true;
                if (m_DraggingRat && m_PressMoved) MoveFigure(m_Rat, pos);
                else if (m_Preview != null) MoveFigure(m_Preview, pos);
                else if (!m_DraggingRat && !m_PressMoved && !m_Consumed &&
                    Time.unscaledTime - m_PressTime >= kLongPress)
                {
                    m_Consumed = true;
                    var frozen = CombatManager.Instance.BuildFrozenPersonAt(m_PressStart);
                    if (frozen != null)
                    {
                        m_Preview = frozen.gameObject;
                        MoveFigure(m_Preview, pos);
                    }
                }
            }
            if (!up) return;
            if (m_DraggingRat && ModeOverlay.IsPointOnOverlay(pos)) m_Rat.transform.position = m_DragOrigin;
            if (m_DraggingRat && !m_PressMoved) m_PokeTime = Time.unscaledTime;
            if (m_Preview != null)
            {
                if (ModeOverlay.IsPointOnOverlay(pos)) CancelPreview();
                else if (m_Rat != null) m_Modal = true;
                else CommitPreview();
            }
            m_DraggingRat = m_PressAccepted = m_PointerHeld = false;
        }

        void MoveFigure(GameObject figure, Vector2 position)
        {
            if (figure == null || Cam == null) return;
            var safe = Screen.safeArea;
            float margin = Mathf.Min(safe.width, safe.height) * 0.08f;
            position.x = Mathf.Clamp(position.x, safe.xMin + margin, safe.xMax - margin);
            position.y = Mathf.Clamp(position.y, safe.yMin + margin, safe.yMax - margin);
            var ray = Cam.ScreenPointToRay(position);
            var plane = new Plane(-Cam.transform.forward, figure.transform.position);
            if (plane.Raycast(ray, out float distance)) figure.transform.position = ray.GetPoint(distance);
        }

        bool TryHitRat(Vector2 position)
        {
            if (Cam == null || m_Rat == null) return false;
            foreach (var hit in Physics.RaycastAll(Cam.ScreenPointToRay(position), 30f))
                if (hit.collider.transform == m_Rat.transform || hit.collider.transform.IsChildOf(m_Rat.transform)) return true;
            return false;
        }

        void CommitPreview()
        {
            if (m_Preview == null) return;
            if (m_Rat != null) Destroy(m_Rat);
            m_Rat = m_Preview;
            m_Preview = null;
            m_Modal = false;
            AddDragCollider(m_Rat);
        }

        public void ConfirmReplacement(bool replace)
        {
            if (!m_Modal) return;
            if (replace) CommitPreview();
            else CancelPreview();
        }

        void CancelPreview()
        {
            if (m_Preview != null) Destroy(m_Preview);
            m_Preview = null;
            m_Modal = false;
        }

        public void RemoveRat()
        {
            CancelPreview();
            if (m_Rat != null) Destroy(m_Rat);
            m_Rat = null;
            m_PressAccepted = m_DraggingRat = false;
        }

        void AddDragCollider(GameObject root)
        {
            var collider = root.GetComponent<BoxCollider>();
            if (collider == null) collider = root.AddComponent<BoxCollider>();

            var bounds = new Bounds(root.transform.position, Vector3.zero);
            bool hasBounds = false;
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (r == null) continue;
                if (!hasBounds) { bounds = r.bounds; hasBounds = true; }
                else bounds.Encapsulate(r.bounds);
            }

            if (!hasBounds)
            {
                collider.center = new Vector3(0f, 0.85f, 0f);
                collider.size = new Vector3(0.6f, 1.7f, 0.4f);
                return;
            }

            collider.center = root.transform.InverseTransformPoint(bounds.center);
            collider.size = root.transform.InverseTransformVector(bounds.size);
        }

        void OnGUI()
        {
            if (!Active) return;

            if (m_Rat != null)
            {
                var removeRect = new Rect(Screen.width - 180f, 120f, 168f, 44f);
                var style = UIChrome.Button(UIPalette.Orange);
                style.fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.height * 0.02f, 15f, 26f));
                UIChrome.DrawShadow(removeRect);
                if (GUI.Button(removeRect, "✕ 移除老鼠干", style)) RemoveRat();
            }

            if (!m_Modal) return;

            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.70f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = prev;

            float w = Screen.width, h = Screen.height;
            var box = new Rect(w * 0.16f, h * 0.30f, w * 0.68f, h * 0.40f);
            GUI.Box(box, GUIContent.none);

            var textStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(Mathf.Clamp(h * 0.028f, 18f, 34f)),
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true
            };
            GUI.Label(new Rect(box.x + 20f, box.y + h * 0.04f, box.width - 40f, h * 0.16f),
                "已经有一只老鼠干，要换掉吗？\n背包只能放 1 个，换掉后旧的会消失。", textStyle);

            var btn = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(Mathf.Clamp(h * 0.026f, 18f, 32f)) };
            if (GUI.Button(new Rect(box.x + box.width * 0.10f, box.y + h * 0.28f, box.width * 0.36f, h * 0.09f), "替换", btn))
            {
                ConfirmReplacement(true);
            }
            if (GUI.Button(new Rect(box.x + box.width * 0.54f, box.y + h * 0.28f, box.width * 0.36f, h * 0.09f), "取消", btn))
            {
                ConfirmReplacement(false);
            }
        }

        bool PollPointer(out Vector2 pos, out bool down, out bool up)
        {
            pos = Vector2.zero;
            down = up = false;

#if ENABLE_INPUT_SYSTEM
            var touchscreen = UnityEngine.InputSystem.Touchscreen.current;
            if (touchscreen != null)
            {
                var primary = touchscreen.primaryTouch;
                if (primary.phase.ReadValue() == UnityEngine.InputSystem.TouchPhase.Canceled)
                {
                    m_PointerHeld = false;
                    return false;
                }
                bool active = primary.press.isPressed || primary.press.wasPressedThisFrame || primary.press.wasReleasedThisFrame;
                if (active)
                {
                    pos = primary.position.ReadValue();
                    down = primary.press.wasPressedThisFrame;
                    up = primary.press.wasReleasedThisFrame;
                    m_PointerHeld = primary.press.isPressed;
                    return true;
                }
                if (Application.isMobilePlatform) { m_PointerHeld = false; return false; }
            }

            if (!Application.isMobilePlatform)
            {
                var mouse = UnityEngine.InputSystem.Mouse.current;
                if (mouse != null)
                {
                    pos = mouse.position.ReadValue();
                    down = mouse.leftButton.wasPressedThisFrame;
                    up = mouse.leftButton.wasReleasedThisFrame;
                    m_PointerHeld = mouse.leftButton.isPressed;
                    return true;
                }
            }
#endif
            m_PointerHeld = false;
            return false;
        }
    }
}
