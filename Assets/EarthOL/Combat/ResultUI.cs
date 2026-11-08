using System.Collections.Generic;
using UnityEngine;

namespace EarthOL.Combat
{
    /// <summary>
    /// 结算对话框：根据本轮攻击历史生成自然语言总结，并提供「重新选择」按钮。
    /// 只负责呈现与发起重新选择请求，具体清理逻辑由外部协调器处理。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ResultUI : MonoBehaviour
    {
        public static ResultUI Instance { get; private set; }

        [Tooltip("结算文案中用来称呼被击败物体的代词。")]
        [SerializeField] string targetName = "它";

        public event System.Action ReselectRequested;

        bool showing;
        string summary = string.Empty;

        public static bool IsShowing => Instance != null && Instance.showing;

        public static bool IsPointOnOverlay(Vector2 screenPosition)
        {
            // 对话框打开时覆盖全屏，任何点击都应视为落在 UI 上。
            return IsShowing;
        }

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Show(IReadOnlyList<MergedAttack> merged)
        {
            summary = BuildSummary(merged);
            showing = true;
        }

        public void Hide()
        {
            showing = false;
        }

        void OnGUI()
        {
            if (!showing) return;

            var previousColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.62f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = previousColor;

            var width = Mathf.Min(Screen.width * 0.86f, 680f);
            var height = Mathf.Min(Screen.height * 0.52f, 460f);
            var box = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            GUI.Box(box, GUIContent.none);

            const float pad = 24f;
            var textStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.height * 0.024f, 20f, 38f)),
                alignment = TextAnchor.UpperLeft,
                wordWrap = true
            };
            GUI.Label(new Rect(box.x + pad, box.y + pad, box.width - pad * 2f, box.height * 0.62f),
                summary, textStyle);

            var buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.height * 0.026f, 22f, 40f))
            };
            if (GUI.Button(new Rect(box.x + box.width * 0.5f - 130f, box.yMax - 96f, 260f, 68f),
                "重新选择", buttonStyle))
            {
                Hide();
                ReselectRequested?.Invoke();
            }
        }

        static string BuildSummary(IReadOnlyList<MergedAttack> merged)
        {
            if (merged == null || merged.Count == 0)
                return "本轮没有成功攻击。";

            var parts = new List<string>(merged.Count);
            foreach (var entry in merged)
            {
                var definition = entry.Definition;
                var action = definition != null ? definition.DisplayName : "攻击";
                var item = definition != null ? definition.ItemName : string.Empty;
                var unit = definition != null ? definition.CountUnit : "个";

                var segment = !string.IsNullOrEmpty(item)
                    ? action + "了 " + entry.Count + " " + unit + item
                    : action + "了 " + entry.Count + " " + unit;
                parts.Add(segment);
            }

            var target = Instance != null ? Instance.targetName : "它";
            var sb = new System.Text.StringBuilder();
            sb.Append("你先对").Append(target);

            for (var i = 0; i < parts.Count; i++)
            {
                if (i == 0) sb.Append(parts[i]);
                else if (i == parts.Count - 1) sb.Append("，最后").Append(parts[i]);
                else sb.Append("，接着").Append(parts[i]);
            }

            sb.Append("，终结了").Append(target).Append("。");
            return sb.ToString();
        }
    }
}
