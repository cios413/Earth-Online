using UnityEngine;
using EarthOL.Deform;
using EarthOL.Throw;

namespace EarthOL
{
    /// <summary>
    /// 自由模式皮肤切换：4 种颜色，右上角按钮 + 左右滑屏切换。
    /// 颜色应用到自由模式的投掷材质与形变物体（PokeDeformer）颜色。
    /// 由 ARCameraBootstrap 创建，仅自由模式 + 游玩中生效。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FreeModeSkin : MonoBehaviour
    {
        public static FreeModeSkin Instance { get; private set; }

        public static readonly Color[] Colors =
        {
            new Color(1f, 0.24f, 0.60f),    // 荧光粉
            new Color(0.55f, 0.95f, 0.30f), // 电光绿
            new Color(1f, 0.54f, 0.12f),    // 橘子橙
            new Color(1f, 0.89f, 0.22f)     // 柠檬黄
        };

        int m_Index;
        public int Index => m_Index;
        public Color CurrentColor => Colors[m_Index];

        // 滑屏判定
        Vector2 m_PressStart;
        bool m_Held;

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        static bool Active =>
            ModeOverlay.Instance != null && ModeOverlay.Instance.IsPlaying &&
            ModeOverlay.Current == GameMode.Free &&
            (GameFlow.Instance == null || GameFlow.Instance.CurrentScreen == FlowScreen.Playing);

        public void SetSkin(int index)
        {
            m_Index = ((index % Colors.Length) + Colors.Length) % Colors.Length;
            ApplyColor();
            Debug.Log("[Skin] 自由模式皮肤 → " + (m_Index + 1) + "/" + Colors.Length + " " + CurrentColor);
        }

        public void Cycle(int direction) => SetSkin(m_Index + direction);

        void ApplyColor()
        {
            var color = CurrentColor;
            EarthOL.Combat.CombatManager.Instance?.SetSkin(color);

            // 形变物体（如老鼠干）材质着色
            foreach (var deformer in FindObjectsOfType<PokeDeformer>(true))
            {
                if (deformer == null) continue;
                var renderer = deformer.GetComponent<Renderer>();
                if (renderer != null) renderer.material.color = color;
            }

            // 自由模式投掷物材质着色
            var throwRuntime = FindObjectOfType<ThrowModeRuntime>(true);
            if (throwRuntime != null && throwRuntime.Spawner != null)
            {
                var material = throwRuntime.Spawner.ProjectileMaterial;
                if (material != null) material.color = color;
            }
        }

        void Update()
        {
            if (!Active) { m_Held = false; return; }
            PollSwipe();
        }

        void PollSwipe()
        {
#if ENABLE_INPUT_SYSTEM
            var touchscreen = UnityEngine.InputSystem.Touchscreen.current;
            if (touchscreen != null)
            {
                var primary = touchscreen.primaryTouch;
                if (primary.phase.ReadValue() == UnityEngine.InputSystem.TouchPhase.Canceled)
                {
                    m_Held = false;
                    return;
                }
                if (primary.press.wasPressedThisFrame)
                {
                    BeginSwipe(primary.position.ReadValue());
                }
                else if (primary.press.wasReleasedThisFrame && m_Held)
                {
                    EndSwipe(primary.position.ReadValue());
                }
                if (Application.isMobilePlatform) return;
            }

            if (!Application.isMobilePlatform)
            {
                var mouse = UnityEngine.InputSystem.Mouse.current;
                if (mouse != null)
                {
                    if (mouse.leftButton.wasPressedThisFrame)
                    {
                        BeginSwipe(mouse.position.ReadValue());
                    }
                    else if (mouse.leftButton.wasReleasedThisFrame && m_Held)
                    {
                        EndSwipe(mouse.position.ReadValue());
                    }
                }
            }
#endif
        }

        public void BeginSwipe(Vector2 position)
        {
            m_PressStart = position;
            m_Held = Active && !ModeOverlay.IsPointOnOverlay(position) &&
                (RatFigure.Instance == null || !RatFigure.Instance.OwnsGesture);
        }

        public void EndSwipe(Vector2 position)
        {
            bool accepted = m_Held;
            m_Held = false;
            if (accepted && Active) TrySwipe(position);
        }

        void TrySwipe(Vector2 end)
        {
            if (ModeOverlay.IsPointOnOverlay(end) || RatFigure.Instance != null && RatFigure.Instance.OwnsGesture) return;
            float dx = end.x - m_PressStart.x;
            float dy = end.y - m_PressStart.y;

            if (Mathf.Abs(dx) > 44f && Mathf.Abs(dx) > Mathf.Abs(dy) + 8f)
                Cycle(dx < 0f ? 1 : -1);
        }

        public static bool ContainsPoint(Vector2 position)
        {
            return Active && new Rect(Screen.width - 180f, 12f, 168f, 80f)
                .Contains(new Vector2(position.x, Screen.height - position.y));
        }

        void OnGUI()
        {
            if (!Active) return;

            var btnRect = new Rect(Screen.width - 180f, 12f, 168f, 52f);
            var style = UIChrome.Button(CurrentColor);
            style.fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.height * 0.022f, 16f, 28f));
            UIChrome.DrawShadow(btnRect);

            if (GUI.Button(btnRect, "皮肤 ▸ " + (m_Index + 1) + "/" + Colors.Length, style)) Cycle(1);

            GUI.Label(new Rect(Screen.width - 180f, 68f, 168f, 24f), "左右滑屏换色",
                new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleRight });
        }
    }
}
