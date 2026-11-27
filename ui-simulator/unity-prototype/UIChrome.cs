using System.Collections.Generic;
using UnityEngine;

namespace EarthOL
{
    /// <summary>
    /// UI 控件皮肤生成（对应 inkplay.html 的果冻按钮 / 长条贴纸风格）：
    /// 圆角 + 厚黑描边 + 荧光填充 + 顶部阶梯高光。运行时程序化生成贴图并缓存。
    /// </summary>
    public static class UIChrome
    {
        const int kSize = 96;
        const int kRadius = 20;
        const int kOutline = 4;
        const int kBorder = 28;   // 9-slice 边框（圆角 + 描边，缩放不糊）

        static readonly Dictionary<Color, Texture2D> s_ButtonTex = new Dictionary<Color, Texture2D>();
        static Texture2D s_ShadowTex;

        /// <summary>果冻按钮样式（荧光填充 + 墨黑描边 + 圆角）。</summary>
        public static GUIStyle Button(Color fill)
        {
            var style = new GUIStyle(GUI.skin.button);
            var bg = ButtonTexture(fill);
            style.normal.background = bg;
            style.hover.background = bg;
            style.active.background = bg;
            style.border = new RectOffset(kBorder, kBorder, kBorder, kBorder);
            style.padding = new RectOffset(10, 10, 10, 10);
            style.normal.textColor = UIPalette.Ink;
            style.hover.textColor = UIPalette.Ink;
            style.active.textColor = UIPalette.Ink;
            style.alignment = TextAnchor.MiddleCenter;
            style.fontStyle = FontStyle.Bold;
            style.wordWrap = true;
            return style;
        }

        /// <summary>长条贴纸面板样式（平涂填充 + 墨黑描边，用于 HUD/信息条）。</summary>
        public static GUIStyle Panel(Color fill)
        {
            var style = new GUIStyle(GUI.skin.box);
            var bg = ButtonTexture(fill);
            style.normal.background = bg;
            style.border = new RectOffset(kBorder, kBorder, kBorder, kBorder);
            style.padding = new RectOffset(12, 12, 10, 10);
            style.normal.textColor = UIPalette.Ink;
            style.alignment = TextAnchor.MiddleLeft;
            return style;
        }

        /// <summary>果冻按钮的硬投影（墨黑圆角块，向右下偏移）。</summary>
        public static void DrawShadow(Rect rect, int offset = 6)
        {
            if (s_ShadowTex == null) s_ShadowTex = MakeRoundedRect(Color.white, kSize, kRadius, 0, false);
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 1f);
            GUI.DrawTexture(new Rect(rect.x + offset, rect.y + offset, rect.width, rect.height), s_ShadowTex);
            GUI.color = prev;
        }

        public static Texture2D ButtonTexture(Color fill)
        {
            if (!s_ButtonTex.TryGetValue(fill, out var tex) || tex == null)
            {
                tex = MakeRoundedRect(fill, kSize, kRadius, kOutline, true);
                tex.name = "UIChrome " + ColorUtility.ToHtmlStringRGB(fill);
                s_ButtonTex[fill] = tex;
            }
            return tex;
        }

        static readonly Dictionary<Color, Texture2D> s_CardTex = new Dictionary<Color, Texture2D>();
        /// <summary>物品卡皮肤：墨黑圆角块 + 荧光描边。</summary>
        public static Texture2D Card(Color outline)
        {
            if (!s_CardTex.TryGetValue(outline, out var tex) || tex == null)
            {
                tex = MakeRoundedRect2(UIPalette.Ink, outline, kSize, kRadius, kOutline, false);
                tex.name = "UIChrome Card " + ColorUtility.ToHtmlStringRGB(outline);
                s_CardTex[outline] = tex;
            }
            return tex;
        }

        static Texture2D MakeRoundedRect2(Color fill, Color outline, int size, int radius, int outlineThickness, bool highlight)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color32[size * size];
            float half = size * 0.5f;
            float inner = half - radius;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(Mathf.Abs(x + 0.5f - half) - inner, 0f);
                    float dy = Mathf.Max(Mathf.Abs(y + 0.5f - half) - inner, 0f);
                    float dist = Mathf.Sqrt(dx * dx + dy * dy) - radius;
                    int idx = y * size + x;
                    if (dist < -outlineThickness) px[idx] = fill;
                    else if (dist < 0f) px[idx] = outline;
                    else px[idx] = new Color32(0, 0, 0, 0);
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }

        static Texture2D s_Circle;
        /// <summary>实心圆贴图（圆形放大转场用）。</summary>
        public static Texture2D Circle()
        {
            if (s_Circle == null)
            {
                int s = 128;
                s_Circle = new Texture2D(s, s, TextureFormat.RGBA32, false);
                var px = new Color32[s * s];
                float half = s * 0.5f;
                for (int y = 0; y < s; y++)
                    for (int x = 0; x < s; x++)
                    {
                        float dx = x + 0.5f - half, dy = y + 0.5f - half;
                        px[y * s + x] = (dx * dx + dy * dy <= half * half) ? (Color32)Color.white : new Color32(0, 0, 0, 0);
                    }
                s_Circle.SetPixels32(px);
                s_Circle.Apply(false, true);
                s_Circle.name = "UIChrome Circle";
            }
            return s_Circle;
        }

        static Texture2D MakeRoundedRect(Color fill, int size, int radius, int outline, bool highlight)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color32[size * size];
            float half = size * 0.5f;
            float inner = half - radius;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(Mathf.Abs(x + 0.5f - half) - inner, 0f);
                    float dy = Mathf.Max(Mathf.Abs(y + 0.5f - half) - inner, 0f);
                    float dist = Mathf.Sqrt(dx * dx + dy * dy) - radius;   // <0 在圆角矩形内
                    int idx = y * size + x;

                    if (dist < -outline)
                    {
                        var c = fill;
                        if (highlight && y < size * 0.30f) c = Color.Lerp(fill, Color.white, 0.30f);
                        px[idx] = c;
                    }
                    else if (dist < 0f)
                    {
                        px[idx] = UIPalette.Ink;
                    }
                    else
                    {
                        px[idx] = new Color32(0, 0, 0, 0);
                    }
                }
            }

            tex.SetPixels32(px);
            tex.Apply(false, true);
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }
    }
}
