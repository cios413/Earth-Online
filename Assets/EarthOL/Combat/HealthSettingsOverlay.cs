using UnityEngine;

namespace EarthOL.Combat
{
    /// <summary>
    /// IMGUI 风格的「血条模式」按钮与设置面板（开启/关闭 + 低/中/高）。
    /// 与项目现有 OnGUI 界面保持一致，不引入新的 Canvas/EventSystem。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HealthSettingsOverlay : MonoBehaviour
    {
        public static HealthSettingsOverlay Instance { get; private set; }

        HealthSettings settings;
        bool panelOpen;
        Rect modeButtonRect;
        Rect[] panelRects = new Rect[0];

        public event System.Action<bool> ModeChanged;
        public event System.Action<HealthTier> TierChanged;

        public static bool IsPanelOpen => Instance != null && Instance.panelOpen;

        public static bool IsPointOnOverlay(Vector2 screenPosition)
        {
            if (Instance == null) return false;

            var guiPoint = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
            if (Instance.modeButtonRect.width > 0f && Instance.modeButtonRect.Contains(guiPoint)) return true;

            foreach (var rect in Instance.panelRects)
                if (rect.width > 0f && rect.Contains(guiPoint)) return true;

            return false;
        }

        public void Configure(HealthSettings healthSettings)
        {
            settings = healthSettings;
        }

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void OnGUI()
        {
            var buttonWidth = Mathf.Min(Screen.width * 0.32f, 340f);
            var buttonHeight = Mathf.Max(60f, Screen.height * 0.05f);
            const float pad = 12f;
            modeButtonRect = new Rect(Screen.width - buttonWidth - pad, pad, buttonWidth, buttonHeight);

            var style = new GUIStyle(GUI.skin.button)
            {
                fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.height * 0.022f, 20f, 34f))
            };

            var enabled = settings != null && settings.HealthBarModeEnabled;
            var previous = GUI.backgroundColor;
            GUI.backgroundColor = enabled ? new Color(0.2f, 0.9f, 0.5f) : new Color(0.45f, 0.45f, 0.48f);
            if (GUI.Button(modeButtonRect, enabled ? "血条模式：开" : "血条模式：关", style))
                panelOpen = !panelOpen;
            GUI.backgroundColor = previous;

            if (!panelOpen)
            {
                panelRects = new[] { modeButtonRect };
                return;
            }

            DrawPanel(style);
        }

        void DrawPanel(GUIStyle buttonStyle)
        {
            var width = Mathf.Min(Screen.width * 0.88f, 560f);
            var height = Mathf.Min(Screen.height * 0.5f, 440f);
            var box = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            GUI.Box(box, GUIContent.none);

            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.height * 0.028f, 24f, 42f)),
                alignment = TextAnchor.MiddleCenter
            };
            GUI.Label(new Rect(box.x, box.y + 16f, box.width, 44f), "血条设置", titleStyle);

            var rowWidth = box.width - 48f;
            var rowHeight = Mathf.Max(56f, buttonStyle.fontSize + 26f);
            var y = box.y + 86f;

            var enabled = settings != null && settings.HealthBarModeEnabled;
            if (GUI.Button(new Rect(box.x + 24f, y, rowWidth, rowHeight),
                enabled ? "血条模式：开启中（点击关闭）" : "血条模式：已关闭（点击开启）", buttonStyle))
            {
                if (settings != null)
                {
                    settings.HealthBarModeEnabled = !settings.HealthBarModeEnabled;
                    ModeChanged?.Invoke(settings.HealthBarModeEnabled);
                }
            }
            y += rowHeight + 16f;

            GUI.Label(new Rect(box.x + 24f, y, rowWidth, 36f), "血量档位", titleStyle);
            y += 48f;

            var tiers = new[] { HealthTier.Low, HealthTier.Medium, HealthTier.High };
            var tierNames = new[] { "低", "中", "高" };
            var tierWidth = rowWidth / 3f;
            for (var i = 0; i < tiers.Length; i++)
            {
                var tierRect = new Rect(box.x + 24f + tierWidth * i, y, tierWidth - 12f, rowHeight);
                var selected = settings != null && settings.Tier == tiers[i];
                var previous = GUI.backgroundColor;
                GUI.backgroundColor = selected ? new Color(0.2f, 0.9f, 0.5f) : Color.white;
                if (GUI.Button(tierRect, tierNames[i], buttonStyle))
                {
                    if (settings != null)
                    {
                        settings.Tier = tiers[i];
                        TierChanged?.Invoke(tiers[i]);
                    }
                }
                GUI.backgroundColor = previous;
            }
            y += rowHeight + 24f;

            if (GUI.Button(new Rect(box.x + 24f, y, rowWidth, rowHeight), "完成", buttonStyle))
                panelOpen = false;

            var rects = new System.Collections.Generic.List<Rect> { modeButtonRect, box };
            panelRects = rects.ToArray();
        }
    }
}
