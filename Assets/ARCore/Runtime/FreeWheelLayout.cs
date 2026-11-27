using UnityEngine;

namespace EarthOL.ARCore
{
    /// <summary>Screen geometry; all vertical dimensions are fractions of the 2732px reference height.</summary>
    public readonly struct FreeWheelLayout
    {
        public readonly float width, height;
        public readonly float reveal;
        public FreeWheelLayout(float width, float height, float reveal = 1f)
        { this.width = width; this.height = height; this.reveal = Mathf.Clamp01(reveal); }
        public float Scale => Mathf.Min(width / 1260f, height / 2732f);
        public float PeakHeight => height * 720f / 2732f;
        public float EdgeHeight => height * 517f / 2732f;
        public float PeekHeight => height * 56f / 2732f;
        public float Travel => PeakHeight - PeekHeight;
        public float OffsetY => Travel * (1f - reveal);
        public Rect PanelRect => new Rect(0, height - PeakHeight + OffsetY, width, PeakHeight);
        public Rect HandleRect => new Rect(width * .5f - Mathf.Max(32f, 72f * Scale),
            PanelRect.y - Mathf.Max(32f, 76f * Scale), Mathf.Max(64f, 144f * Scale), Mathf.Max(40f, 92f * Scale));
        public float Step => width * .22f;

        public float ArcHeight(float x)
        {
            const float radius = (630f * 630f + 203f * 203f) / (2f * 203f);
            float dx = (x / width - .5f) * 1260f;
            return (720f - radius + Mathf.Sqrt(Mathf.Max(0, radius * radius - dx * dx))) * height / 2732f;
        }

        public Vector2 SlotCenter(float offset)
        {
            float x = width * .5f + Step * offset;
            return new Vector2(x, height - ArcHeight(x) + height * 220f / 2732f + OffsetY);
        }

        public float SlotRadius(float offset)
        {
            float distance = Mathf.Abs(offset);
            return (distance <= 1f ? Mathf.Lerp(144f, 112f, distance) : Mathf.Lerp(112f, 82f, distance - 1f)) * Scale;
        }

        public float SlotOpacity(float offset) => Mathf.Clamp01(1f - Mathf.Abs(offset) * .32f);
        public Rect SlotRect(float offset)
        {
            Vector2 center = SlotCenter(offset);
            float radius = SlotRadius(offset);
            return new Rect(center.x - radius, center.y - radius, radius * 2, radius * 2);
        }

        public bool Contains(Vector2 guiPoint) => guiPoint.x >= 0 && guiPoint.x <= width &&
            guiPoint.y >= height - ArcHeight(guiPoint.x) + OffsetY && guiPoint.y <= height;
        public bool InCenter(Vector2 guiPoint, float padding = 1f)
            => Vector2.Distance(guiPoint, SlotCenter(0)) <= SlotRadius(0) * padding;
    }
}
