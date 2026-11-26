using UnityEngine;
using UnityEngine.UI;

namespace EarthOL.Account
{
    /// <summary>
    /// 运行时构建 uGUI 账户界面的静态工具。沿用现有 AR 界面的深色底 + 白字 + 青绿按钮风格。
    /// </summary>
    public static class AccountUIBuilder
    {
        static Font s_Font;

        public static Font DefaultFont
        {
            get
            {
                if (s_Font == null)
                {
                    s_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    if (s_Font == null) s_Font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                }
                return s_Font;
            }
        }

        public static readonly Color Background = new Color(0.07f, 0.09f, 0.13f, 1f);
        public static readonly Color Panel = new Color(0.12f, 0.15f, 0.20f, 0.98f);
        public static readonly Color Field = new Color(0.05f, 0.07f, 0.10f, 1f);
        public static readonly Color Accent = new Color(0.20f, 0.78f, 0.72f, 1f);
        public static readonly Color AccentDark = new Color(0.14f, 0.58f, 0.54f, 1f);
        public static readonly Color AccentDisabled = new Color(0.35f, 0.38f, 0.42f, 1f);
        public static readonly Color White = Color.white;
        public static readonly Color Error = new Color(1f, 0.42f, 0.36f, 1f);
        public static readonly Color Hint = new Color(0.75f, 0.82f, 0.89f, 1f);
        public static readonly Color Placeholder = new Color(0.55f, 0.60f, 0.66f, 1f);

        public static Canvas CreateCanvas(string name, int sortingOrder)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        public static Image CreateBackground(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = true;
            return image;
        }

        /// <summary>创建铺满父节点的 RectTransform 根节点。</summary>
        public static RectTransform CreateStretchedRoot(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        public static Text CreateText(Transform parent, string name, string content, int fontSize, Color color, TextAnchor alignment)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.font = DefaultFont;
            text.text = content;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        public static Button CreateButton(Transform parent, string name, string label, int fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = Accent;
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;

            var colors = button.colors;
            colors.normalColor = Accent;
            colors.highlightedColor = AccentDark;
            colors.pressedColor = AccentDark;
            colors.disabledColor = AccentDisabled;
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            var text = labelGo.AddComponent<Text>();
            text.font = DefaultFont;
            text.text = label;
            text.fontSize = fontSize;
            text.color = White;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;

            return button;
        }

        public static Button CreateLinkButton(Transform parent, string name, string label, int fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0f);
            image.raycastTarget = true;
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;

            var colors = button.colors;
            colors.normalColor = new Color(1f, 1f, 1f, 0f);
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.08f);
            colors.pressedColor = new Color(1f, 1f, 1f, 0.16f);
            colors.disabledColor = new Color(1f, 1f, 1f, 0f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            var text = labelGo.AddComponent<Text>();
            text.font = DefaultFont;
            text.text = label;
            text.fontSize = fontSize;
            text.color = Accent;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;

            return button;
        }

        public static InputField CreateInputField(Transform parent, string name, string placeholderText, InputField.ContentType contentType)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = Field;

            var input = go.AddComponent<InputField>();
            input.contentType = contentType;
            input.lineType = InputField.LineType.SingleLine;
            input.characterLimit = 64;

            var text = CreateFieldChildText(go.transform, "Text", White);
            var placeholder = CreateFieldChildText(go.transform, "Placeholder", Placeholder);
            placeholder.fontStyle = FontStyle.Italic;
            placeholder.text = placeholderText;

            input.textComponent = text;
            input.placeholder = placeholder;
            return input;
        }

        static Text CreateFieldChildText(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(18f, 0f);
            rect.offsetMax = new Vector2(-18f, 0f);

            var text = go.AddComponent<Text>();
            text.font = DefaultFont;
            text.fontSize = 34;
            text.color = color;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = false;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>将元素水平居中锚定到画布顶部，y 为距顶部的负偏移（像素）。</summary>
        public static void PlaceTopCenter(RectTransform rect, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, y);
            rect.sizeDelta = new Vector2(width, height);
        }
    }
}
