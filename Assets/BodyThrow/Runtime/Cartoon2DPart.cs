using UnityEngine;
using UnityEngine.UI;

namespace EarthOL.BodyThrow
{
    [DisallowMultipleComponent]
    public sealed class Cartoon2DPart : MonoBehaviour
    {
        Image m_Image;
        RectTransform m_Rect;
        bool m_Placed;
        public bool IsVisible => m_Image != null && m_Image.enabled;
        public Vector2 DisplayCenter => m_Rect.anchoredPosition;
        public Vector2 DisplaySize => m_Rect.sizeDelta;
        public Vector2 DisplayStart => DisplayCenter - (Vector2)(m_Rect.localRotation * Vector3.right) * m_Rect.sizeDelta.x * .5f;
        public Vector2 DisplayEnd => DisplayCenter + (Vector2)(m_Rect.localRotation * Vector3.right) * m_Rect.sizeDelta.x * .5f;
        public void Configure(Transform parent, Sprite sprite, Color color, bool face = false)
        {
            var part = new GameObject("Artwork", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            part.transform.SetParent(parent, false);
            m_Image = part.GetComponent<Image>();
            m_Rect = part.GetComponent<RectTransform>();
            m_Rect.anchorMin = m_Rect.anchorMax = new Vector2(0.5f, 0.5f);
            m_Rect.pivot = new Vector2(0.5f, 0.5f);
            m_Image.sprite = sprite;
            m_Image.color = color;
            m_Image.raycastTarget = false;
            m_Image.maskable = false;
            m_Image.enabled = false;
        }

        public void SetSegment(Vector2 a, Vector2 b, Vector2 localOrigin, float width, float alpha)
        {
            Vector2 delta = b - a;
            float length = delta.magnitude;
            if (length < 2f || width < 1f) { SetVisible(false); return; }
            Place((a + b) * 0.5f - localOrigin, new Vector2(length, width), Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            var color = m_Image.color;
            color.a = alpha;
            m_Image.color = color;
            m_Image.enabled = true;
        }

        public void SetHead(Vector2 center, Vector2 localOrigin, float diameter, float alpha, bool mirror)
        {
            if (diameter < 2f) { SetVisible(false); return; }
            Place(center - localOrigin, Vector2.one * diameter, 0f);
            m_Rect.localScale = new Vector3(mirror ? -1f : 1f, 1f, 1f);
            var color = m_Image.color;
            color.a = alpha;
            m_Image.color = color;
            m_Image.enabled = true;
        }

        public void SetVisible(bool visible)
        {
            if (m_Image != null) m_Image.enabled = visible;
            if (!visible) m_Placed = false;
        }

        void Place(Vector2 position, Vector2 size, float angle)
        {
            float blend = m_Placed ? 1f - Mathf.Exp(-Mathf.Max(.001f, Time.unscaledDeltaTime) / .045f) : 1f;
            m_Rect.anchoredPosition = Vector2.Lerp(m_Rect.anchoredPosition, position, blend);
            m_Rect.sizeDelta = Vector2.Lerp(m_Rect.sizeDelta, size, blend);
            float rotation = Mathf.LerpAngle(m_Rect.localEulerAngles.z, angle, blend);
            m_Rect.localRotation = Quaternion.Euler(0f, 0f, rotation);
            m_Placed = true;
        }
    }
}
