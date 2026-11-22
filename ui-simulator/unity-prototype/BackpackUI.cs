using UnityEngine;
using EarthOL.Combat;

namespace EarthOL
{
    /// <summary>
    /// 底部背包 dock（对齐 inkplay.html）：荧光粉 dock + 把手「物品 ▾」，点开向上展开
    /// 4 个投掷物卡（墨黑圆角块 + 各自荧光描边，选中填荧光实心）+ 老鼠干槽。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BackpackUI : MonoBehaviour
    {
        public static BackpackUI Instance { get; private set; }

        bool m_Open;

        static readonly Color[] s_ItemColors =
        {
            UIPalette.Yellow,   // 1 粘粘手
            UIPalette.Green,    // 2 大脑
            UIPalette.Pink,     // 3 方块
            UIPalette.Orange    // 4 箭头
        };

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        static bool Active =>
            ModeOverlay.Instance != null && ModeOverlay.Instance.IsPlaying &&
            (GameFlow.Instance == null || GameFlow.Instance.CurrentScreen == FlowScreen.Playing);

        public static bool ContainsPoint(Vector2 position)
        {
            if (Instance == null || !Active) return false;
            float width = Mathf.Min(Screen.width * 0.96f, 640f);
            float height = Mathf.Max(50f, Screen.height * 0.05f);
            if (Instance.m_Open) height += Mathf.Max(120f, Screen.height * 0.16f) + 10f;
            return new Rect((Screen.width - width) * 0.5f, 10f, width, height).Contains(position);
        }

        void OnGUI()
        {
            if (!Active) return;

            float w = Screen.width, h = Screen.height;
            float dockW = Mathf.Min(w * 0.96f, 640f);
            float handleH = Mathf.Max(50f, h * 0.05f);
            float trayH = Mathf.Max(120f, h * 0.16f);
            float dockH = m_Open ? handleH + trayH + 10f : handleH;

            var dockRect = new Rect((w - dockW) * 0.5f, h - dockH - 10f, dockW, dockH);

            // 荧光粉 dock：墨黑外描边 + 粉内层
            GUI.color = UIPalette.Ink;
            GUI.DrawTexture(dockRect, Texture2D.whiteTexture);
            GUI.color = UIPalette.Pink;
            GUI.DrawTexture(new Rect(dockRect.x + 4f, dockRect.y + 4f, dockRect.width - 8f, dockRect.height - 8f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            // 把手「物品」
            var handleRect = new Rect(dockRect.x, dockRect.y, dockRect.width, handleH);
            var handleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(Mathf.Clamp(h * 0.024f, 16f, 28f)),
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                normal = { textColor = UIPalette.Ink }
            };
            if (GUI.Button(handleRect, m_Open ? "物品 ▲ 收起" : "物品 ▾ 展开", handleStyle)) m_Open = !m_Open;

            if (!m_Open) return;

            var combat = CombatManager.Instance;
            var rat = RatFigure.Instance;
            var catalog = combat != null ? combat.Catalog : null;

            var trayRect = new Rect(dockRect.x + 6f, dockRect.y + handleH + 4f, dockRect.width - 12f, trayH);
            float cellW = trayRect.width / 5f;

            var nameStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(Mathf.Clamp(h * 0.015f, 12f, 20f)),
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                normal = { textColor = UIPalette.Chalk }
            };

            // 4 个投掷物
            int itemCount = catalog != null ? Mathf.Min(catalog.entries.Length, 4) : 0;
            for (int i = 0; i < itemCount; i++)
            {
                var entry = catalog.entries[i];
                if (entry == null) continue;
                var cell = new Rect(trayRect.x + i * cellW + 3f, trayRect.y, cellW - 6f, trayRect.height);
                bool selected = combat != null && combat.SelectedProjectileIndex == i;
                var edge = s_ItemColors[i % s_ItemColors.Length];

                // 墨黑圆角块 + 荧光描边；选中填荧光实心
                var cardStyle = new GUIStyle();
                cardStyle.normal.background = selected ? UIChrome.ButtonTexture(edge) : UIChrome.Card(edge);
                cardStyle.border = new RectOffset(28, 28, 28, 28);
                GUI.Box(cell, GUIContent.none, cardStyle);

                // 荧光图标
                var icon = IconFor(entry.key);
                if (icon != null)
                {
                    float iconSize = Mathf.Min(cell.width * 0.62f, cell.height * 0.46f);
                    GUI.color = edge;
                    GUI.DrawTexture(new Rect(cell.center.x - iconSize * 0.5f, cell.y + 6f, iconSize, iconSize), icon, ScaleMode.ScaleToFit);
                    GUI.color = Color.white;
                }

                GUI.Label(new Rect(cell.x, cell.y + cell.height * 0.58f, cell.width, cell.height * 0.40f), entry.displayName, nameStyle);

                if (GUI.Button(cell, GUIContent.none, GUIStyle.none))
                {
                    if (combat != null) combat.SelectProjectile(i);
                }
            }

            // 老鼠干槽（第 5 槽）
            var ratCell = new Rect(trayRect.x + 4 * cellW + 3f, trayRect.y, cellW - 6f, trayRect.height);
            bool free = ModeOverlay.Current == GameMode.Free;
            bool hasRat = rat != null && rat.HasRat;
            var ratStyle = new GUIStyle();
            ratStyle.normal.background = hasRat ? UIChrome.ButtonTexture(UIPalette.Lime) : UIChrome.Card(UIPalette.Purple);
            ratStyle.border = new RectOffset(28, 28, 28, 28);
            GUI.Box(ratCell, GUIContent.none, ratStyle);

            string ratLabel = !free
                ? "老鼠干\n仅自由\n0/1"
                : (hasRat ? "老鼠干\n1/1\n点此移除" : "老鼠干\n0/1\n长按拖出");
            GUI.Label(ratCell, ratLabel, nameStyle);
            if (GUI.Button(ratCell, GUIContent.none, GUIStyle.none))
            {
                if (free && hasRat && rat != null) rat.RemoveRat();
            }
        }

        static Texture2D IconFor(string key)
        {
            switch (key)
            {
                case "StickyHand": return UITextures.Hand;
                case "Brain": return UITextures.Brain;
                case "JellyCube": return UITextures.Jelly;
                case "Arrow": return UITextures.Arrow;
                default: return null;
            }
        }
    }
}
