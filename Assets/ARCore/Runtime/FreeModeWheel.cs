using EarthOL.BodyThrow;
using UnityEngine;

namespace EarthOL.ARCore
{
    /// <summary>Retractable item selector; an expanded wheel enables taps on people to throw.</summary>
    [DefaultExecutionOrder(50)]
    public sealed class FreeModeWheel : MonoBehaviour
    {
        ARCoreMainSceneBootstrap m_Bootstrap;
        Texture2D m_Panel, m_Disc, m_Ring;
        Texture2D[] m_Thumbnails;
        int m_ThumbnailCount = -1;
        int m_TextureWidth, m_TextureHeight;
        float m_Scroll, m_StartScroll;
        bool m_Pressed, m_Scrolling, m_Dragging;
        bool m_Expanded, m_WorldPressed, m_WorldMoved;
        float m_Reveal, m_StartReveal, m_WorldPressTime;
        int m_ConsumedFrame = -1, m_WorldPointer, m_WorldPerson;
        Vector2 m_WorldOrigin;
        int m_Pointer;
        Vector2 m_Origin;
        float m_LastWidth, m_LastHeight;
        public bool Expanded => m_Expanded;
        public float Reveal => m_Reveal;
        public bool OwnsPointer => m_Pressed || m_WorldPressed || m_ConsumedFrame == Time.frameCount;
        public bool ShowUpArrow => !m_Expanded;
        public bool ShowDownArrow => m_Pressed && (m_Expanded || m_Dragging);
        public FreeWheelLayout Layout => new FreeWheelLayout(Screen.width, Screen.height, m_Reveal);
        public bool Visible => m_Bootstrap != null && m_Bootstrap.SessionActive &&
            !m_Bootstrap.ShowcaseController.IsPresenting;
        public bool CanInteract => Visible && m_Bootstrap.DetectionManager.DetectionEnabled &&
            !m_Bootstrap.CombatController.BlocksInput;
        public bool ControlsPersonInput => CanInteract &&
            (m_Bootstrap.CurrentMode == GameMode.Deform || m_Bootstrap.CurrentMode == GameMode.Throw);
        public bool TapToThrowEnabled => ControlsPersonInput && m_Expanded;
        public int SelectedIndex => m_Bootstrap.ThrowController.SelectedProjectileIndex;

        public void Configure(ARCoreMainSceneBootstrap bootstrap)
        {
            m_Bootstrap = bootstrap;
        }

        public static int Wrap(int value, int count) => count > 0 ? (value % count + count) % count : 0;

        public void ResetInteraction()
        {
            m_Pressed = m_Scrolling = m_Dragging = m_WorldPressed = m_WorldMoved = false;
            m_Reveal = m_Expanded ? 1f : 0f;
            if (m_Bootstrap != null && m_Bootstrap.ThrowController != null)
                m_Scroll = m_Bootstrap.ThrowController.SelectedProjectileIndex;
        }

        public void SetExpanded(bool expanded)
        {
            m_Expanded = expanded;
            ResetInteraction();
            m_Bootstrap?.PersonDeformer?.EndPointer();
        }

        void Update()
        {
            if (!Visible || m_Bootstrap.CombatController.BlocksInput) { ResetInteraction(); return; }
            if (Screen.width != m_LastWidth || Screen.height != m_LastHeight)
            {
                ResetInteraction(); m_LastWidth = Screen.width; m_LastHeight = Screen.height;
            }
            if (BodyPointerFrame.TryRead(out var frame)) ProcessPointer(frame);
            else if (m_Pressed || m_WorldPressed) ResetInteraction();
            if (!m_Pressed)
            {
                float target = Mathf.Round(m_Scroll);
                m_Scroll = Mathf.Lerp(m_Scroll, target, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 18f));
                if (Mathf.Abs(m_Scroll - target) < .001f) m_Scroll = Wrap(Mathf.RoundToInt(target), ItemCount);
            }
        }

        int ItemCount => m_Bootstrap != null ? m_Bootstrap.ThrowController.ProjectileCount : 0;

        public void ProcessPointer(BodyPointerFrame frame)
        {
            if (!Visible || m_Bootstrap.CombatController.BlocksInput || ItemCount == 0) { ResetInteraction(); return; }
            Vector2 point = new Vector2(frame.position.x, Screen.height - frame.position.y);
            var layout = Layout;
            if (frame.down)
            {
                if (!layout.Contains(point) && !layout.HandleRect.Contains(point))
                {
                    ProcessPersonPointer(frame);
                    return;
                }
                m_WorldPressed = false;
                m_Pressed = true; m_Scrolling = m_Dragging = false; m_Pointer = frame.id;
                m_Origin = point;
                m_StartReveal = m_Reveal;
                // A new press always begins on a settled, known selected item.
                m_Scroll = m_StartScroll = SelectedIndex;
                m_Bootstrap.PersonDeformer.EndPointer();
            }
            if (!m_Pressed) { ProcessPersonPointer(frame); return; }
            if (frame.id != m_Pointer) return;
            m_ConsumedFrame = Time.frameCount;
            if (frame.canceled) { ResetInteraction(); return; }
            float dx = point.x - m_Origin.x;
            float dy = point.y - m_Origin.y;
            if (!m_Scrolling && !m_Dragging && Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) > Screen.width * .022f)
            {
                m_Dragging = Mathf.Abs(dy) >= Mathf.Abs(dx) || !m_Expanded;
                m_Scrolling = !m_Dragging;
            }
            if (m_Dragging) m_Reveal = Mathf.Clamp01(m_StartReveal - dy / layout.Travel);
            if (m_Scrolling)
            {
                m_Scroll = m_StartScroll - dx / layout.Step;
                m_Bootstrap.ThrowController.SelectProjectile(Wrap(Mathf.RoundToInt(m_Scroll), ItemCount));
            }
            if (!frame.released) return;
            if (m_Dragging) m_Expanded = m_Reveal >= .5f;
            m_Reveal = m_Expanded ? 1f : 0f;
            m_Pressed = false;
            m_Scrolling = m_Dragging = false;
            m_Bootstrap.ThrowController.SelectProjectile(Wrap(Mathf.RoundToInt(m_Scroll), ItemCount));
        }

        void ProcessPersonPointer(BodyPointerFrame frame)
        {
            if (!TapToThrowEnabled) { m_WorldPressed = false; return; }
            if (frame.down)
            {
                m_WorldPressed = false;
                if (ARCoreModeOverlay.IsPointOnOverlay(frame.position)) return;
                var person = m_Bootstrap.PersonTracking.Pick(m_Bootstrap.ARCamera.ScreenToViewportPoint(frame.position));
                if (person == null) return;
                m_WorldPressed = true; m_WorldMoved = false;
                m_WorldPointer = frame.id; m_WorldPerson = person.personId;
                m_WorldOrigin = frame.position; m_WorldPressTime = Time.unscaledTime;
                m_Bootstrap.PersonDeformer.EndPointer();
            }
            if (!m_WorldPressed || frame.id != m_WorldPointer) return;
            m_ConsumedFrame = Time.frameCount;
            m_WorldMoved |= Vector2.Distance(frame.position, m_WorldOrigin) > Mathf.Max(20f, Screen.width * .035f);
            m_WorldMoved |= ARCoreModeOverlay.IsPointOnControls(frame.position);
            if (frame.canceled) { m_WorldPressed = false; return; }
            if (!frame.released) return;
            m_WorldPressed = false;
            if (m_WorldMoved || Time.unscaledTime - m_WorldPressTime > .55f) return;
            var target = m_Bootstrap.PersonTracking.Pick(m_Bootstrap.ARCamera.ScreenToViewportPoint(frame.position));
            Vector2 guiPoint = new Vector2(frame.position.x, Screen.height - frame.position.y);
            if (target == null || target.personId != m_WorldPerson || Layout.Contains(guiPoint) || Layout.HandleRect.Contains(guiPoint)) return;
            // Restore the original flight from the bottom of the screen to the tapped person position.
            m_Bootstrap.ThrowController.TryThrowAt(frame.position);
        }

        public bool BlocksPoint(Vector2 guiPoint) => Visible && (OwnsPointer || Layout.Contains(guiPoint) || Layout.HandleRect.Contains(guiPoint));

        public void Draw()
        {
            if (!Visible) return;
            EnsureTextures();
            var layout = Layout;
            Color previous = GUI.color;
            int nearest = Mathf.RoundToInt(m_Scroll);
            // Item sprites move behind five stationary, genuinely transparent circular apertures.
            for (int i = nearest - 3; i <= nearest + 3; i++)
            {
                float offset = i - m_Scroll;
                if (Mathf.Abs(offset) > 2.65f) continue;
                int index = Wrap(i, ItemCount);
                var rect = layout.SlotRect(offset);
                GUI.color = new Color(.12f, .20f, .28f, layout.SlotOpacity(offset) * .7f);
                GUI.DrawTexture(rect, m_Disc);
                GUI.color = new Color(1, 1, 1, layout.SlotOpacity(offset) * (CanInteract ? 1f : .45f));
                if (m_Thumbnails != null && index < m_Thumbnails.Length && m_Thumbnails[index] != null)
                {
                    float inset = rect.width * .07f;
                    GUI.DrawTexture(new Rect(rect.x + inset, rect.y + inset, rect.width - inset * 2, rect.height - inset * 2), m_Thumbnails[index]);
                }
            }
            GUI.color = Color.white;
            GUI.DrawTexture(layout.PanelRect, m_Panel, ScaleMode.StretchToFill, true);
            for (int i = -2; i <= 2; i++)
            {
                GUI.color = i == 0 ? new Color(.73f, .96f, 1f, .86f) : new Color(.71f, .81f, .88f, layout.SlotOpacity(i) * .6f);
                GUI.DrawTexture(layout.SlotRect(i), m_Ring);
            }
            GUI.color = previous;
            float scale = layout.Scale;
            var title = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.Max(16, Mathf.RoundToInt(34f * scale)), normal = { textColor = new Color(.91f, .96f, 1f) } };
            GUI.Label(new Rect(0, Screen.height - layout.height * 235f / 2732f + layout.OffsetY, Screen.width, 54f * scale),
                m_Bootstrap.ThrowController.SelectedProjectileName, title);
            title.fontSize = Mathf.Max(12, Mathf.RoundToInt(24f * scale));
            title.normal.textColor = new Color(.60f, .72f, .81f);
            GUI.Label(new Rect(0, Screen.height - layout.height * 150f / 2732f + layout.OffsetY, Screen.width, 60f * scale),
                !m_Bootstrap.DetectionManager.DetectionEnabled ? "开始识别后可投掷" : "左右滑动选物 · 点击人物投掷 · 下推收起", title);
            DrawHandle();
        }

        void DrawHandle()
        {
            if (!ShowUpArrow && !ShowDownArrow) return;
            Color previous = GUI.color;
            GUI.color = new Color(.79f, .97f, 1f, .95f);
            Rect handle = Layout.HandleRect;
            float halfWidth = Mathf.Max(12f, 28f * Layout.Scale);
            float halfHeight = halfWidth * .5f;
            Vector2 center = handle.center;
            bool down = ShowDownArrow;
            Vector2 tip = center + Vector2.up * (down ? halfHeight : -halfHeight);
            float endY = center.y + (down ? -halfHeight : halfHeight);
            float thickness = Mathf.Max(2.5f, 5f * Layout.Scale);
            DrawLine(new Vector2(center.x - halfWidth, endY), tip, thickness);
            DrawLine(tip, new Vector2(center.x + halfWidth, endY), thickness);
            GUI.color = previous;
        }

        static void DrawLine(Vector2 from, Vector2 to, float thickness)
        {
            Matrix4x4 previous = GUI.matrix;
            Vector2 delta = to - from;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, from);
            GUI.DrawTexture(new Rect(from.x, from.y - thickness * .5f, delta.magnitude, thickness), Texture2D.whiteTexture);
            GUI.matrix = previous;
        }

        void EnsureTextures()
        {
            if (m_Disc == null) { m_Disc = CircleTexture(false); m_Ring = CircleTexture(true); }
            if (m_Panel == null || m_TextureWidth != Screen.width || m_TextureHeight != Screen.height)
            {
                DestroyTexture(m_Panel);
                m_TextureWidth = Screen.width; m_TextureHeight = Screen.height;
                m_Panel = CreatePanelTexture(new FreeWheelLayout(Screen.width, Screen.height));
            }
            if (m_Thumbnails == null || m_ThumbnailCount != ItemCount)
            {
                if (m_Thumbnails != null) foreach (var old in m_Thumbnails) DestroyTexture(old);
                m_Thumbnails = new Texture2D[ItemCount];
                m_ThumbnailCount = ItemCount;
                for (int i = 0; i < ItemCount; i++)
                {
                    var preview = m_Bootstrap.ThrowController.GetProjectilePreview(i);
                    if (preview != null) m_Thumbnails[i] = CircularThumbnail(preview);
                }
            }
        }

        public static Texture2D CreatePanelTexture(FreeWheelLayout layout)
        {
            const int width = 630;
            int height = Mathf.Clamp(Mathf.RoundToInt(width * layout.PeakHeight / layout.width), 180, 640);
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                Vector2 point = new Vector2((x + .5f) / width * layout.width,
                    layout.height - (y + .5f) / height * layout.PeakHeight);
                float edge = Mathf.Clamp01((point.y - (layout.height - layout.ArcHeight(point.x))) * height / layout.PeakHeight);
                float hole = 1f;
                for (int slot = -2; slot <= 2; slot++)
                    hole = Mathf.Min(hole, Mathf.Clamp01((Vector2.Distance(point, layout.SlotCenter(slot)) - layout.SlotRadius(slot)) * width / layout.width));
                float highlight = Mathf.Exp(-Mathf.Max(0, point.y - layout.height + layout.ArcHeight(point.x)) / (layout.height * .003f));
                Color color = Color.Lerp(new Color(.045f, .075f, .11f), new Color(.26f, .39f, .46f), highlight * .85f);
                color.a = edge * hole;
                pixels[y * width + x] = color;
            }
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = "Free wheel aperture mask", wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(pixels); texture.Apply(false, true);
            return texture;
        }

        static Texture2D CircleTexture(bool ring)
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x + .5f, y + .5f), Vector2.one * size * .5f);
                float alpha = Mathf.Clamp01(size * .5f - distance);
                if (ring) alpha *= Mathf.Clamp01(distance - (size * .5f - 2.5f));
                pixels[y * size + x] = new Color(1, 1, 1, alpha);
            }
            texture.SetPixels32(pixels); texture.Apply(false, true);
            return texture;
        }

        static Texture2D CircularThumbnail(Texture source)
        {
            const int size = 256;
            var target = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            Texture2D result = null;
            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                result = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                result.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                var pixels = result.GetPixels32();
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float alpha = Mathf.Clamp01(size * .5f - Vector2.Distance(new Vector2(x + .5f, y + .5f), Vector2.one * size * .5f));
                    pixels[y * size + x].a = (byte)(pixels[y * size + x].a * alpha);
                }
                result.SetPixels32(pixels); result.Apply(false, true);
                return result;
            }
            catch { DestroyTexture(result); throw; }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); }
        }

        void OnApplicationFocus(bool focused) { if (!focused) ResetInteraction(); }
        void OnApplicationPause(bool paused) { if (paused) ResetInteraction(); }
        static void DestroyTexture(Texture texture)
        { if (texture != null) { if (Application.isPlaying) Destroy(texture); else DestroyImmediate(texture); } }
        void OnDestroy()
        {
            DestroyTexture(m_Panel); DestroyTexture(m_Disc); DestroyTexture(m_Ring);
            if (m_Thumbnails != null) foreach (var texture in m_Thumbnails) DestroyTexture(texture);
        }
    }
}
