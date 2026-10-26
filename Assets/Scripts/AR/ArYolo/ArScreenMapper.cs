using UnityEngine;
using UnityEngine.UI;

namespace YoloSegmentation
{
    /// <summary>
    /// Screen/image mapping for AR Foundation. The AR camera background fills the screen
    /// and crops the excess, so the overlay uses the same "fill" rectangle. This keeps the
    /// mask, click hit-testing and mesh placement aligned with the real camera view.
    /// </summary>
    public static class ArScreenMapper
    {
        public static RawImage OverlayImage;
        public static int FrameWidth;
        public static int FrameHeight;

        /// <summary>Screen-space rectangle that the camera frame fills (crop, not letterbox).</summary>
        public static Rect GetRect()
        {
            var frameW = FrameWidth > 0 ? FrameWidth : Screen.width;
            var frameH = FrameHeight > 0 ? FrameHeight : Screen.height;
            var screenAspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            var frameAspect = (float)frameW / Mathf.Max(1, frameH);

            var scale = frameAspect > screenAspect
                ? (float)Screen.height / frameH
                : (float)Screen.width / frameW;

            var width = frameW * scale;
            var height = frameH * scale;
            return new Rect(
                (Screen.width - width) * 0.5f,
                (Screen.height - height) * 0.5f,
                width,
                height);
        }

        public static void Fit(RawImage image, int width = 0, int height = 0)
        {
            if (image == null)
                return;

            if (width > 0 && height > 0)
            {
                FrameWidth = width;
                FrameHeight = height;
            }

            var fitter = image.GetComponent<AspectRatioFitter>();
            if (fitter != null)
                fitter.aspectMode = AspectRatioFitter.AspectMode.None;

            var rect = GetRect();
            var parent = image.rectTransform.parent as RectTransform;
            if (parent == null)
                return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parent, new Vector2(rect.xMin, rect.yMin), null, out var bottomLeft);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parent, new Vector2(rect.xMax, rect.yMax), null, out var topRight);

            var rectTransform = image.rectTransform;
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = (bottomLeft + topRight) * 0.5f;
            rectTransform.sizeDelta = new Vector2(
                Mathf.Abs(topRight.x - bottomLeft.x),
                Mathf.Abs(topRight.y - bottomLeft.y));
        }
    }
}
