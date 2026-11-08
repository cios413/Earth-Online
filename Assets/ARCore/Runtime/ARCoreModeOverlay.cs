using UnityEngine;
using EarthOL.BodyThrow;

namespace EarthOL.ARCore
{
    /// <summary>Compact controls for live 2D tracking, fixed 3D snapshots and detection lifetime.</summary>
    public sealed class ARCoreModeOverlay : MonoBehaviour
    {
        public static ARCoreModeOverlay Instance { get; private set; }
        static readonly Rect[] s_NoRects = new Rect[0];
        ARCoreMainSceneBootstrap m_Bootstrap;
        Rect[] m_ButtonRects = s_NoRects;
        GameMode m_Mode = GameMode.Deform;
        public static bool BlocksWorldInput => false;
        public static bool IsRadialHoldOpen => false;
        public static bool IsProjectilePickerOpen => false;
        // Legacy BodyPoseTracker remains compilable but is never activated by the new bootstrap.
        public void BeginRadialSelection(Vector2 point) { }
        public void UpdateRadialSelection(Vector2 point) { }
        public void EndRadialSelection(bool canceled) { }
        public static bool IsPointOnOverlay(Vector2 screenPosition)
        {
            if (Instance == null) return false;
            var guiPoint = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
            foreach (var rect in Instance.m_ButtonRects) if (rect.Contains(guiPoint)) return true;
            return false;
        }

        public void Configure(ARCoreMainSceneBootstrap bootstrap)
        {
            Instance = this;
            m_Bootstrap = bootstrap;
            Apply(GameMode.Throw);
        }

        void OnDestroy() { if (Instance == this) Instance = null; }
        void Apply(GameMode mode)
        {
            m_Mode = mode;
            m_Bootstrap?.SetMode(mode);
        }

        void Update()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.digit1Key.wasPressedThisFrame) Apply(GameMode.Deform);
                else if (keyboard.digit2Key.wasPressedThisFrame) Apply(GameMode.Throw);
            }
#endif
        }

        void OnGUI()
        {
            float pad = Mathf.Clamp(Screen.width * 0.012f, 8f, 18f);
            float height = Mathf.Clamp(Screen.height * 0.045f, 62f, 112f);
            float width = (Screen.width - pad * 4f) / 3f;
            var deform = new Rect(pad, pad, width, height);
            var frozen = new Rect(pad * 2f + width, pad, width, height);
            var detection = new Rect(pad * 3f + width * 2f, pad, width, height);
            var stats = new Rect(pad, pad + height + pad * 0.45f, Screen.width - pad * 2f, height * 0.72f);
            var diagnostics = new Rect(Screen.width - width - pad, Screen.height - height - pad, width, height);
            m_ButtonRects = new[] { deform, frozen, detection, diagnostics };

            var style = new GUIStyle(GUI.skin.button)
            {
                fontSize = Mathf.RoundToInt(Mathf.Clamp(height * 0.25f, 20f, 32f)),
                wordWrap = true,
                alignment = TextAnchor.MiddleCenter
            };
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = m_Mode == GameMode.Deform ? new Color(0.24f, 0.69f, 0.52f) : Color.white;
            if (GUI.Button(deform, "人物形变", style)) Apply(GameMode.Deform);
            GUI.backgroundColor = m_Mode == GameMode.Throw ? new Color(0.24f, 0.69f, 0.52f) : Color.white;
            if (GUI.Button(frozen, "点击人物", style)) Apply(GameMode.Throw);
            GUI.backgroundColor = m_Bootstrap != null && m_Bootstrap.DetectionManager != null &&
                                  m_Bootstrap.DetectionManager.DetectionEnabled ? new Color(0.83f, 0.55f, 0.23f) : Color.white;
            if (m_Bootstrap != null && m_Bootstrap.DetectionManager != null &&
                GUI.Button(detection, m_Bootstrap.DetectionManager.DetectionEnabled ? "停止识别" : "启动识别", style))
            {
                var manager = m_Bootstrap.DetectionManager;
                if (manager.DetectionEnabled) manager.StopDetection();
                else manager.StartDetection();
            }
            GUI.backgroundColor = previous;

            if (m_Bootstrap != null && m_Bootstrap.DetectionManager != null)
            {
                var manager = m_Bootstrap.DetectionManager;
                string text = $"{manager.State} YOLO {manager.MeasuredDetectionFramesPerSecond:F1}/{manager.EffectiveDetectionFramesPerSecond:F1}Hz (cap {manager.DetectionFramesPerSecond:F1}) " +
                    $"wall {manager.AverageInferenceMilliseconds:F0}ms track {manager.AverageTrackingMilliseconds:F2}ms\n" +
                    $"目标 {manager.TrackedPersonCount} Mono {manager.ManagedMemoryBytes / 1048576f:F0}MB GC {manager.FrameGcBytes}B";
                var label = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Mathf.Clamp(height * 0.18f, 15f, 24f)) };
                GUI.Label(stats, text, label);
            }

            if (m_Bootstrap != null && m_Bootstrap.ClickController != null)
            {
                var label = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Mathf.Clamp(height * 0.20f, 16f, 26f)) };
                GUI.Label(diagnostics, "固定模型 " + m_Bootstrap.ClickController.FrozenPersonCount, label);
            }
        }
    }
}
