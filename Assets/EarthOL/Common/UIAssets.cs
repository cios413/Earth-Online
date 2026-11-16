using UnityEngine;

namespace EarthOL
{
    /// <summary>UI 调色板：inkplay.html 的 9 色平涂涂鸦配色。</summary>
    public static class UIPalette
    {
        public static readonly Color Ink = Hex("14101C");
        public static readonly Color Ink2 = Hex("2A2140");
        public static readonly Color Chalk = Hex("FBF6E9");
        public static readonly Color Green = Hex("B6FF3C");
        public static readonly Color Pink = Hex("FF3D9A");
        public static readonly Color Orange = Hex("FF8A1F");
        public static readonly Color Purple = Hex("9B4DFF");
        public static readonly Color Yellow = Hex("FFE23D");
        public static readonly Color Lime = Hex("D8F56B");

        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }
    }

    /// <summary>UI 美术贴图：Resources/UI 下从 inkplay.html 导出的平涂涂鸦 PNG。</summary>
    public static class UITextures
    {
        static Texture2D s_AlienFierce, s_AlienFriendly, s_EarthBall, s_Splat;
        static Texture2D s_IcBrain, s_IcJelly, s_IcHand, s_IcArrow, s_IcBomb, s_IcStar;

        static Texture2D Load(string name)
        {
            var t = Resources.Load<Texture2D>("UI/" + name);
            if (t == null) Debug.LogWarning("[UI] 缺少美术贴图：UI/" + name);
            return t;
        }

        public static Texture2D AlienFierce => s_AlienFierce ?? (s_AlienFierce = Load("alien-fierce"));
        public static Texture2D AlienFriendly => s_AlienFriendly ?? (s_AlienFriendly = Load("alien-friendly"));
        public static Texture2D EarthBall => s_EarthBall ?? (s_EarthBall = Load("earth-ball"));
        public static Texture2D Splat => s_Splat ?? (s_Splat = Load("splat"));
        public static Texture2D Brain => s_IcBrain ?? (s_IcBrain = Load("ic-brain"));
        public static Texture2D Jelly => s_IcJelly ?? (s_IcJelly = Load("ic-jelly"));
        public static Texture2D Hand => s_IcHand ?? (s_IcHand = Load("ic-hand"));
        public static Texture2D Arrow => s_IcArrow ?? (s_IcArrow = Load("ic-arrow"));
        public static Texture2D Bomb => s_IcBomb ?? (s_IcBomb = Load("ic-bomb"));
        public static Texture2D Star => s_IcStar ?? (s_IcStar = Load("ic-star"));

        /// <summary>按宽度等比缩放后居中放置，避免拉伸变形。</summary>
        public static Rect FitRect(float centerX, float centerY, float width, Texture2D tex)
        {
            float h = width;
            if (tex != null && tex.width > 0) h = width * tex.height / tex.width;
            return new Rect(centerX - width * 0.5f, centerY - h * 0.5f, width, h);
        }
    }
}
