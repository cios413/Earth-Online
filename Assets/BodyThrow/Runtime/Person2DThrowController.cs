using System.Collections.Generic;
using EarthOL.ARCore;
using UnityEngine;
using UnityEngine.UI;

namespace EarthOL.BodyThrow
{
    [DisallowMultipleComponent]
    public sealed class Person2DThrowController : MonoBehaviour
    {
        const float LongPressSeconds = 0.48f;
        const float MaximumTapSeconds = 0.55f;
        const float MaximumTapMotion = 48f;
        const float FlightSeconds = 0.42f;
        const int MaximumProjectiles = 24;

        sealed class ThrownItem
        {
            public RectTransform rect;
            public int personId;
            public Vector2 normalizedOffset;
            public Vector2 startScreen;
            public float flightProgress;
            public float angle;
            public bool attached;
        }

        Camera m_Camera;
        PersonTrackingManager m_Tracking;
        BodyProjectileCatalog m_Catalog;
        Canvas m_Canvas;
        RectTransform m_CanvasRect;
        readonly List<ThrownItem> m_Items = new List<ThrownItem>();
        bool m_InteractionEnabled;
        bool m_PointerHeld;
        bool m_PickerOpen;
        int m_PointerId;
        int m_SelectedProjectile;
        Vector2 m_PressOrigin;
        Vector2 m_PickerCenterGui;
        float m_PressTime;

        public bool IsPickerOpen => m_PickerOpen;
        public int SelectedProjectileIndex => m_SelectedProjectile;
        public int ActiveProjectileCount => m_Items.Count;
        public int AttachedProjectileCount
        {
            get
            {
                int count = 0;
                foreach (var item in m_Items) if (item.attached) count++;
                return count;
            }
        }
        public string SelectedProjectileName => TryGetEntry(m_SelectedProjectile, out var entry) ? entry.displayName : "投掷物";

        public void Configure(Camera camera, PersonTrackingManager tracking, BodyProjectileCatalog catalog)
        {
            m_Camera = camera;
            m_Tracking = tracking;
            m_Catalog = catalog;
            CreateCanvas();
            if (m_Catalog == null || m_Catalog.entries == null || m_Catalog.entries.Length == 0)
                Debug.LogError("[2DThrow] 缺少投掷物缩略图目录 Assets/Resources/Throwables/Catalog.asset");
        }

        public void SetInteractionEnabled(bool enabled)
        {
            m_InteractionEnabled = enabled;
            if (!enabled) CancelGesture();
        }

        public void ClearEffects()
        {
            CancelGesture();
            for (int i = m_Items.Count - 1; i >= 0; i--) DestroyItem(m_Items[i]);
            m_Items.Clear();
            m_SelectedProjectile = 0;
        }

        public bool SelectProjectile(int index)
        {
            if (!TryGetEntry(index, out var entry) || entry.preview == null) return false;
            m_SelectedProjectile = index;
            return true;
        }

        void Update()
        {
            UpdateThrownItems(Time.unscaledDeltaTime);
            if (!m_InteractionEnabled || m_Camera == null || m_Tracking == null) return;
            if (!BodyPointerFrame.TryRead(out var frame))
            {
                if (m_PointerHeld) CancelGesture();
                return;
            }

            if (frame.down) BeginGesture(frame);
            if (!m_PointerHeld || frame.id != m_PointerId) return;
            if (frame.canceled) { CancelGesture(); return; }

            if (!m_PickerOpen && Time.unscaledTime - m_PressTime >= LongPressSeconds)
                OpenPicker(frame.position);

            if (m_PickerOpen)
            {
                UpdatePickerSelection(frame.position);
                if (frame.released) CancelGesture();
                return;
            }

            if (!frame.released) return;
            bool tap = Time.unscaledTime - m_PressTime <= MaximumTapSeconds &&
                Vector2.Distance(frame.position, m_PressOrigin) <= MaximumTapMotion;
            Vector2 position = frame.position;
            CancelGesture();
            if (tap) TryThrowAt(position);
        }

        void BeginGesture(BodyPointerFrame frame)
        {
            if (m_PickerOpen)
            {
                m_PointerHeld = true;
                m_PointerId = frame.id;
                UpdatePickerSelection(frame.position);
                return;
            }
            CancelGesture();
            if (ARCoreModeOverlay.IsPointOnOverlay(frame.position)) return;
            m_PointerHeld = true;
            m_PointerId = frame.id;
            m_PressOrigin = frame.position;
            m_PressTime = Time.unscaledTime;
        }

        void OpenPicker(Vector2 screenPosition)
        {
            if (m_Catalog == null || m_Catalog.entries == null || m_Catalog.entries.Length == 0)
            {
                CancelGesture();
                return;
            }
            m_PickerOpen = true;
            m_PickerCenterGui = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
        }

        void UpdatePickerSelection(Vector2 screenPosition)
        {
            Vector2 point = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
            for (int i = 0; i < m_Catalog.entries.Length; i++)
            {
                if (!PickerItemRect(i).Contains(point)) continue;
                SelectProjectile(i);
                return;
            }
        }

        public void DrawPicker()
        {
            if (!m_PickerOpen || m_Catalog == null || m_Catalog.entries == null) return;
            Rect panel = PickerPanelRect();
            Color previous = GUI.color;
            GUI.color = new Color(0.08f, 0.10f, 0.12f, 0.94f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = Color.white;
            var labelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                fontSize = Mathf.RoundToInt(Mathf.Clamp(panel.height * 0.11f, 13f, 20f)),
                normal = { textColor = Color.white }
            };

            for (int i = 0; i < m_Catalog.entries.Length; i++)
            {
                Rect card = PickerItemRect(i);
                GUI.color = i == m_SelectedProjectile ? new Color(0.26f, 0.74f, 0.56f, 1f) : new Color(0.22f, 0.25f, 0.28f, 1f);
                GUI.DrawTexture(card, Texture2D.whiteTexture);
                GUI.color = Color.white;
                var entry = m_Catalog.entries[i];
                Rect imageRect = new Rect(card.x + card.width * 0.08f, card.y + card.height * 0.04f,
                    card.width * 0.84f, card.height * 0.70f);
                if (entry.preview != null) GUI.DrawTexture(imageRect, entry.preview, ScaleMode.ScaleToFit, true);
                GUI.Label(new Rect(card.x + 2f, imageRect.yMax, card.width - 4f, card.height * 0.24f),
                    entry.displayName, labelStyle);
            }
            GUI.color = previous;
        }

        Rect PickerPanelRect()
        {
            float pad = Mathf.Clamp(Screen.width * 0.012f, 8f, 16f);
            float tile = Mathf.Clamp(Screen.width * 0.095f, 68f, 112f);
            float gap = pad * 0.6f;
            int count = Mathf.Min(4, m_Catalog != null && m_Catalog.entries != null ? m_Catalog.entries.Length : 0);
            float width = pad * 2f + count * tile + Mathf.Max(0, count - 1) * gap;
            float height = pad * 2f + tile;
            float x = Mathf.Clamp(m_PickerCenterGui.x - width * 0.5f, pad, Mathf.Max(pad, Screen.width - width - pad));
            float y = Mathf.Clamp(m_PickerCenterGui.y - height * 0.5f, pad, Mathf.Max(pad, Screen.height - height - pad));
            return new Rect(x, y, width, height);
        }

        Rect PickerItemRect(int index)
        {
            Rect panel = PickerPanelRect();
            float pad = Mathf.Clamp(Screen.width * 0.012f, 8f, 16f);
            float tile = Mathf.Clamp(Screen.width * 0.095f, 68f, 112f);
            float gap = pad * 0.6f;
            return new Rect(panel.x + pad + index * (tile + gap), panel.y + pad, tile, tile);
        }

        public bool TryThrowAt(Vector2 screenPosition)
        {
            if (!m_InteractionEnabled || m_Camera == null || m_Tracking == null ||
                !TryGetEntry(m_SelectedProjectile, out var entry) || entry.preview == null) return false;
            Vector3 viewport3 = m_Camera.ScreenToViewportPoint(screenPosition);
            Vector2 viewport = new Vector2(viewport3.x, viewport3.y);
            PersonPoseData person = m_Tracking.Pick(viewport);
            if (person == null) return false;

            Rect bounds = person.screenRect;
            var item = CreateThrownItem(entry.preview);
            if (item == null) return false;
            item.personId = person.personId;
            item.normalizedOffset = new Vector2(
                Mathf.Clamp01((viewport.x - bounds.xMin) / Mathf.Max(.001f, bounds.width)),
                Mathf.Clamp01((viewport.y - bounds.yMin) / Mathf.Max(.001f, bounds.height)));
            item.startScreen = new Vector2(Screen.width * .5f, Screen.height * .08f);
            Vector2 target = ScreenPointFor(person, item.normalizedOffset);
            item.angle = Mathf.Atan2(target.y - item.startScreen.y, target.x - item.startScreen.x) * Mathf.Rad2Deg;
            item.rect.localScale = Vector3.one * .35f;
            SetScreenPosition(item.rect, item.startScreen);

            m_Items.Add(item);
            while (m_Items.Count > MaximumProjectiles)
            {
                DestroyItem(m_Items[0]);
                m_Items.RemoveAt(0);
            }
            return true;
        }

        ThrownItem CreateThrownItem(Texture2D texture)
        {
            if (m_CanvasRect == null || texture == null) return null;
            var go = new GameObject("2D Thrown " + SelectedProjectileName,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            go.transform.SetParent(m_CanvasRect, false);
            var image = go.GetComponent<RawImage>();
            image.texture = texture;
            image.raycastTarget = false;
            image.maskable = false;
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
            float size = Mathf.Clamp(Screen.width * .075f, 54f, 112f) / Mathf.Max(.01f, m_Canvas.scaleFactor);
            rect.sizeDelta = new Vector2(size, size);
            return new ThrownItem { rect = rect };
        }

        void UpdateThrownItems(float deltaTime)
        {
            for (int i = m_Items.Count - 1; i >= 0; i--)
            {
                var item = m_Items[i];
                PersonPoseData person = m_Tracking != null ? m_Tracking.Find(item.personId) : null;
                if (item.rect == null || person == null || person.trackingConfidence < .08f)
                {
                    DestroyItem(item);
                    m_Items.RemoveAt(i);
                    continue;
                }

                Vector2 target = ScreenPointFor(person, item.normalizedOffset);
                if (!item.attached)
                {
                    item.flightProgress = Mathf.Min(1f, item.flightProgress + deltaTime / FlightSeconds);
                    float t = item.flightProgress;
                    Vector2 position = Vector2.Lerp(item.startScreen, target, t) +
                        Vector2.up * Mathf.Sin(t * Mathf.PI) * Screen.height * .11f;
                    SetScreenPosition(item.rect, position);
                    item.rect.localScale = Vector3.one * Mathf.Lerp(.35f, 1f, t);
                    item.rect.localRotation = Quaternion.Euler(0f, 0f, item.angle * (1f - t) - 360f * t);
                    if (t >= 1f) item.attached = true;
                }
                else
                {
                    SetScreenPosition(item.rect, target);
                    item.rect.localRotation = Quaternion.Euler(0f, 0f, item.angle);
                }
            }
        }

        Vector2 ScreenPointFor(PersonPoseData person, Vector2 normalizedOffset)
        {
            Rect bounds = person.screenRect;
            var viewport = new Vector3(bounds.xMin + bounds.width * normalizedOffset.x,
                bounds.yMin + bounds.height * normalizedOffset.y, 0f);
            Vector3 point = m_Camera.ViewportToScreenPoint(viewport);
            return new Vector2(point.x, point.y);
        }

        void SetScreenPosition(RectTransform rect, Vector2 screenPosition)
        {
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(m_CanvasRect, screenPosition, null, out var local))
                rect.anchoredPosition = local;
        }

        bool TryGetEntry(int index, out BodyProjectileCatalog.Entry entry)
        {
            entry = null;
            if (m_Catalog == null || m_Catalog.entries == null || index < 0 || index >= m_Catalog.entries.Length) return false;
            entry = m_Catalog.entries[index];
            return entry != null;
        }

        void CreateCanvas()
        {
            if (m_Canvas != null) return;
            var canvasObject = new GameObject("2D Throw Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            m_Canvas = canvasObject.GetComponent<Canvas>();
            m_Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            m_Canvas.overrideSorting = true;
            m_Canvas.sortingOrder = 125;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = .5f;
            m_CanvasRect = (RectTransform)canvasObject.transform;
        }

        void CancelGesture()
        {
            m_PointerHeld = false;
            m_PickerOpen = false;
        }

        void DestroyItem(ThrownItem item)
        {
            if (item?.rect == null) return;
            if (Application.isPlaying) Destroy(item.rect.gameObject);
            else DestroyImmediate(item.rect.gameObject);
        }

        void OnDestroy()
        {
            ClearEffects();
            if (m_Canvas != null)
            {
                if (Application.isPlaying) Destroy(m_Canvas.gameObject);
                else DestroyImmediate(m_Canvas.gameObject);
            }
        }
    }
}
