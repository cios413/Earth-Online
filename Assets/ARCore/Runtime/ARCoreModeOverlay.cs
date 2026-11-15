using System.Collections.Generic;
using EarthOL.BodyThrow;
using UnityEngine;

namespace EarthOL.ARCore
{
    [DefaultExecutionOrder(10000)]
    public sealed class ARCoreModeOverlay : MonoBehaviour
    {
        public static ARCoreModeOverlay Instance { get; private set; }
        static readonly Rect[] s_NoRects = new Rect[0];

        ARCoreMainSceneBootstrap m_Bootstrap;
        Rect[] m_ButtonRects = s_NoRects;
        Rect m_ShowcaseExitRect;
        GameMode m_Mode = GameMode.Deform;

        public static GameMode CurrentMode => Instance != null ? Instance.m_Mode : GameMode.Deform;
        public static bool IsStickerMode => CurrentMode == GameMode.Sticker;
        public static bool IsShowcaseMode => CurrentMode == GameMode.Showcase;
        public static bool IsThrowMode => CurrentMode == GameMode.Throw;
        public static bool IsProjectilePickerOpen => Instance != null && Instance.m_Bootstrap != null &&
            Instance.m_Bootstrap.ThrowController != null && Instance.m_Bootstrap.ThrowController.IsPickerOpen;
        public static bool IsRadialHoldOpen => IsProjectilePickerOpen;
        public static bool BlocksWorldInput => IsProjectilePickerOpen;
        public void BeginRadialSelection(Vector2 point) { }
        public void UpdateRadialSelection(Vector2 point) { }
        public void EndRadialSelection(bool canceled) { }

        public static bool IsPointOnOverlay(Vector2 screenPosition)
        {
            if (Instance == null) return false;
            if (BlocksWorldInput) return true;
            var guiPoint = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
            foreach (var rect in Instance.m_ButtonRects) if (rect.Contains(guiPoint)) return true;
            return false;
        }

        public static bool IsShowcaseExitPoint(Vector2 screenPosition)
        {
            if (Instance == null || !Instance.IsShowingShowcase) return false;
            var guiPoint = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
            return Instance.m_ShowcaseExitRect.Contains(guiPoint);
        }

        bool IsShowingShowcase => m_Bootstrap != null && m_Bootstrap.ShowcaseController != null &&
            m_Bootstrap.ShowcaseController.IsPresenting;

        public void Configure(ARCoreMainSceneBootstrap bootstrap)
        {
            Instance = this;
            m_Bootstrap = bootstrap;
            Apply(GameMode.Deform);
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
            if (keyboard == null) return;
            if (keyboard.digit1Key.wasPressedThisFrame) Apply(GameMode.Deform);
            else if (keyboard.digit2Key.wasPressedThisFrame) Apply(GameMode.Sticker);
            else if (keyboard.digit3Key.wasPressedThisFrame) Apply(GameMode.Showcase);
            else if (keyboard.digit4Key.wasPressedThisFrame) Apply(GameMode.Throw);
#endif
        }

        void OnGUI()
        {
            bool presenting = IsShowingShowcase;
            if (presenting) DrawShowcaseBackground();
            DrawModeBar(presenting);
            if (presenting) DrawShowcaseModel();
            else DrawDetectionStatus();
            if (m_Bootstrap != null && m_Bootstrap.ThrowController != null &&
                m_Bootstrap.ThrowController.IsPickerOpen)
                m_Bootstrap.ThrowController.DrawPicker();
        }

        void DrawModeBar(bool presenting)
        {
            float pad = Mathf.Clamp(Screen.width * .012f, 8f, 18f);
            float height = Mathf.Clamp(Screen.height * .045f, 60f, 98f);
            float gap = Mathf.Clamp(pad * .55f, 5f, 10f);
            float groupWidth = Screen.width * .72f;
            float buttonWidth = (groupWidth - gap * 3f) * .25f;
            float detectionWidth = Mathf.Clamp(Screen.width * .18f, 128f, 230f);
            var modes = new[] { GameMode.Deform, GameMode.Sticker, GameMode.Showcase, GameMode.Throw };
            var labels = new[] { "人物形变", "人物贴图", "人物展示", "3D投掷" };
            var rects = new List<Rect>(6);
            var style = new GUIStyle(GUI.skin.button)
            {
                fontSize = Mathf.RoundToInt(Mathf.Clamp(height * .28f, 17f, 27f)),
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true
            };
            Color previous = GUI.backgroundColor;
            for (int i = 0; i < modes.Length; i++)
            {
                var rect = new Rect(pad + i * (buttonWidth + gap), pad, buttonWidth, height);
                rects.Add(rect);
                GUI.backgroundColor = m_Mode == modes[i] ? new Color(.22f, .68f, .52f, 1f) : new Color(.93f, .95f, .96f, 1f);
                if (GUI.Button(rect, labels[i], style)) Apply(modes[i]);
            }

            var detection = new Rect(Screen.width - pad - detectionWidth, pad, detectionWidth, height);
            rects.Add(detection);
            bool running = m_Bootstrap != null && m_Bootstrap.DetectionManager != null &&
                m_Bootstrap.DetectionManager.DetectionEnabled;
            GUI.backgroundColor = running ? new Color(.83f, .55f, .23f, 1f) : new Color(.48f, .51f, .54f, 1f);
            if (m_Bootstrap != null && m_Bootstrap.DetectionManager != null &&
                GUI.Button(detection, running ? "停止识别" : "开始识别", style))
            {
                if (running) m_Bootstrap.DetectionManager.StopDetection();
                else m_Bootstrap.DetectionManager.StartDetection();
            }

            m_ShowcaseExitRect = default;
            if (presenting)
            {
                float exitWidth = detectionWidth;
                m_ShowcaseExitRect = new Rect(Screen.width - pad - exitWidth,
                    Screen.height - pad - height, exitWidth, height);
                rects.Add(m_ShowcaseExitRect);
                GUI.backgroundColor = new Color(.93f, .95f, .96f, 1f);
                if (GUI.Button(m_ShowcaseExitRect, "退出展示", style))
                    m_Bootstrap.ShowcaseController.ExitPresentation();
            }
            GUI.backgroundColor = previous;
            m_ButtonRects = rects.ToArray();
        }

        void DrawDetectionStatus()
        {
            var manager = m_Bootstrap != null ? m_Bootstrap.DetectionManager : null;
            if (manager == null) return;
            float pad = Mathf.Clamp(Screen.width * .012f, 8f, 18f);
            float top = Mathf.Clamp(Screen.height * .045f, 60f, 98f) + pad * 1.45f;
            string text = $"{manager.State}   人物 {manager.TrackedPersonCount}   " +
                $"YOLO {manager.MeasuredDetectionFramesPerSecond:F1}/{manager.EffectiveDetectionFramesPerSecond:F1} Hz";
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.height * .014f, 15f, 22f)),
                normal = { textColor = Color.white }
            };
            GUI.Label(new Rect(pad, top, Screen.width - pad * 2f, 32f), text, style);
        }

        static void DrawShowcaseBackground()
        {
            Color previous = GUI.color;
            GUI.color = new Color(.20f, .22f, .24f, .70f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = previous;
        }

        void DrawShowcaseModel()
        {
            Texture preview = m_Bootstrap.ShowcaseController.PreviewTexture;
            if (preview == null) return;
            float maxWidth = Screen.width * .88f;
            float maxHeight = Screen.height * .70f;
            float aspect = preview.width / (float)preview.height;
            float width = Mathf.Min(maxWidth, maxHeight * aspect);
            float height = width / aspect;
            var rect = new Rect((Screen.width - width) * .5f, (Screen.height - height) * .5f, width, height);
            GUI.DrawTexture(rect, preview, ScaleMode.ScaleToFit, true);
        }
    }
}
