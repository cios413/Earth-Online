using System.Collections.Generic;
using EarthOL.ARCore;
using UnityEngine;

namespace EarthOL.BodyThrow
{
    /// <summary>2D pose target with real 3D throwable prefabs in a camera-aligned virtual layer.</summary>
    [DisallowMultipleComponent]
    public sealed class Person3DThrowController : MonoBehaviour
    {
        const float LongPressSeconds = .48f;
        const float MaximumTapSeconds = .55f;
        const float MaximumTapMotion = 48f;
        const float FlightSeconds = .42f;
        const float StartDepth = .45f;
        const float PersonLayerDepth = 1.20f;
        const float ProjectileVisualScale = .25f;
        const int MaximumProjectiles = 24;

        sealed class ThrownItem
        {
            public GameObject gameObject;
            public int personId;
            public Vector2 normalizedOffset;
            public Vector3 startLocal;
            public float flightProgress;
            public bool attached;
            public float impactAge;
            public float halfDepth;
            public Color slimeColor;
            public string actionId;
            public int context;
            public long eventId;
            public bool wheelFlight;
            public Vector2 targetViewport;
            public float missAge;
            public System.Action releaseVisualAssets;
        }

        Camera m_Camera;
        PersonTrackingManager m_Tracking;
        BodyProjectileCatalog m_Catalog;
        GameObject m_ShowcaseSource;
        Texture2D m_ShowcasePreview;
        string m_ShowcaseName = "人物展示模型";
        readonly List<ThrownItem> m_Items = new List<ThrownItem>();
        readonly Dictionary<int, PersonSlimeSurface> m_SlimeSurfaces = new Dictionary<int, PersonSlimeSurface>();
        readonly List<int> m_EmptySurfaces = new List<int>();
        Material m_SlimeTemplate;
        bool m_InteractionEnabled;
        bool m_PointerHeld;
        bool m_PickerOpen;
        int m_PointerId;
        int m_SelectedProjectile;
        Vector2 m_PressOrigin;
        Vector2 m_PickerCenterGui;
        float m_PressTime;

        public bool IsPickerOpen => m_PickerOpen;
        public event System.Action<PersonInteractionHit> InteractionConfirmed;
        public System.Func<int, int> InteractionContext { get; set; }
        public bool ReadPointerInput { get; set; } = true;
        public string SelectedProjectileKey => TryGetEntry(m_SelectedProjectile, out var entry) ? entry.key : string.Empty;
        public Texture2D SelectedProjectilePreview => GetProjectilePreview(m_SelectedProjectile);
        public int SelectedProjectileIndex => m_SelectedProjectile;
        public int ActiveProjectileCount => m_Items.Count;
        public int SlimeSurfaceCount => m_SlimeSurfaces.Count;
        public int AttachedProjectileCount
        {
            get
            {
                int count = 0;
                foreach (var item in m_Items) if (item.attached) count++;
                return count;
            }
        }
        public string SelectedProjectileName => GetProjectileName(m_SelectedProjectile);
        public int ShowcaseProjectileIndex => m_Catalog != null && m_Catalog.entries != null ? m_Catalog.entries.Length : 0;
        public bool HasShowcaseProjectile => m_ShowcaseSource != null;
        public int ProjectileCount => ShowcaseProjectileIndex + (HasShowcaseProjectile ? 1 : 0);

        public void Configure(Camera camera, PersonTrackingManager tracking, BodyProjectileCatalog catalog)
        {
            m_Camera = camera;
            m_Tracking = tracking;
            m_Catalog = catalog;
            m_SlimeTemplate = Resources.Load<Material>("Throwables/PersonSlime");
            if (m_Catalog == null || m_Catalog.entries == null || m_Catalog.entries.Length == 0)
                Debug.LogError("[3DThrow] 缺少投掷物目录 Assets/Resources/Throwables/Catalog.asset");
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
            foreach (var surface in m_SlimeSurfaces.Values)
                if (surface != null) DestroyOwned(surface.gameObject);
            m_SlimeSurfaces.Clear();
            m_SelectedProjectile = 0;
        }

        public bool SelectProjectile(int index)
        {
            if (HasShowcaseProjectile && index == ShowcaseProjectileIndex)
            { m_SelectedProjectile = index; return true; }
            if (!TryGetEntry(index, out var entry) || entry.prefab == null) return false;
            m_SelectedProjectile = index;
            return true;
        }

        public void SetShowcaseProjectile(GameObject source, Texture2D preview)
        {
            m_ShowcaseSource = source;
            m_ShowcasePreview = preview;
            if (!HasShowcaseProjectile && m_SelectedProjectile >= ShowcaseProjectileIndex) m_SelectedProjectile = 0;
        }

        public void ClearShowcaseProjectile()
        {
            m_ShowcaseSource = null;
            m_ShowcasePreview = null;
            if (m_SelectedProjectile >= ShowcaseProjectileIndex) m_SelectedProjectile = 0;
        }

        public Texture2D GetProjectilePreview(int index)
        {
            if (HasShowcaseProjectile && index == ShowcaseProjectileIndex) return m_ShowcasePreview;
            return TryGetEntry(index, out var entry) ? entry.preview : null;
        }

        public string GetProjectileName(int index)
        {
            if (HasShowcaseProjectile && index == ShowcaseProjectileIndex) return m_ShowcaseName;
            return TryGetEntry(index, out var entry) ? entry.displayName : "投掷物";
        }

        void Update()
        {
            UpdateThrownItems(Time.unscaledDeltaTime);
            if (!m_InteractionEnabled || !ReadPointerInput || m_Camera == null || m_Tracking == null) return;
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
                if (PickerItemRect(i).Contains(point)) { SelectProjectile(i); return; }
        }

        public void DrawPicker()
        {
            if (!m_PickerOpen || m_Catalog == null || m_Catalog.entries == null) return;
            Rect panel = PickerPanelRect();
            Color previous = GUI.color;
            GUI.color = new Color(.08f, .10f, .12f, .94f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = Color.white;
            var labelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter, wordWrap = true,
                fontSize = Mathf.RoundToInt(Mathf.Clamp(panel.height * .11f, 13f, 20f)),
                normal = { textColor = Color.white }
            };
            for (int i = 0; i < m_Catalog.entries.Length; i++)
            {
                Rect card = PickerItemRect(i);
                GUI.color = i == m_SelectedProjectile ? new Color(.26f, .74f, .56f) : new Color(.22f, .25f, .28f);
                GUI.DrawTexture(card, Texture2D.whiteTexture);
                GUI.color = Color.white;
                var entry = m_Catalog.entries[i];
                Rect imageRect = new Rect(card.x + card.width * .08f, card.y + card.height * .04f,
                    card.width * .84f, card.height * .70f);
                if (entry.preview != null) GUI.DrawTexture(imageRect, entry.preview, ScaleMode.ScaleToFit, true);
                GUI.Label(new Rect(card.x + 2f, imageRect.yMax, card.width - 4f, card.height * .24f), entry.displayName, labelStyle);
            }
            GUI.color = previous;
        }

        Rect PickerPanelRect()
        {
            float pad = Mathf.Clamp(Screen.width * .012f, 8f, 16f);
            float tile = PickerTileSize;
            float gap = pad * .6f;
            int count = m_Catalog != null && m_Catalog.entries != null ? m_Catalog.entries.Length : 0;
            float width = pad * 2f + count * tile + Mathf.Max(0, count - 1) * gap;
            float height = pad * 2f + tile;
            float x = Mathf.Clamp(m_PickerCenterGui.x - width * .5f, pad, Mathf.Max(pad, Screen.width - width - pad));
            float y = Mathf.Clamp(m_PickerCenterGui.y - height * .5f, pad, Mathf.Max(pad, Screen.height - height - pad));
            return new Rect(x, y, width, height);
        }

        Rect PickerItemRect(int index)
        {
            Rect panel = PickerPanelRect();
            float pad = Mathf.Clamp(Screen.width * .012f, 8f, 16f);
            float tile = PickerTileSize;
            return new Rect(panel.x + pad + index * (tile + pad * .6f), panel.y + pad, tile, tile);
        }

        float PickerTileSize
        {
            get
            {
                int count = Mathf.Max(1, m_Catalog != null && m_Catalog.entries != null ? m_Catalog.entries.Length : 1);
                float pad = Mathf.Clamp(Screen.width * .012f, 8f, 16f);
                return Mathf.Min(Mathf.Clamp(Screen.width * .095f, 52f, 112f),
                    (Screen.width - pad * 4f - pad * .6f * (count - 1)) / count);
            }
        }

        public bool TryThrowAt(Vector2 screenPosition)
            => TryLaunch(new Vector2(.5f, .08f), screenPosition, false);

        /// <summary>The free wheel uses the same authored models, attachments and slime as ordinary throwing.</summary>
        public bool TryThrowFromWheel(Vector2 startScreen, Vector2 targetScreen)
        {
            if (m_Camera == null) return false;
            return TryLaunch(m_Camera.ScreenToViewportPoint(startScreen), targetScreen, true);
        }

        public Vector3 PreviewWheelPoint(Vector2 startScreen, Vector2 targetScreen, float progress)
        {
            Vector3 local = FlightPoint(LocalPoint(m_Camera.ScreenToViewportPoint(startScreen), StartDepth),
                LocalPoint(m_Camera.ScreenToViewportPoint(targetScreen), PersonLayerDepth), progress, true);
            return m_Camera.WorldToScreenPoint(m_Camera.transform.TransformPoint(local));
        }

        static Vector3 FlightPoint(Vector3 start, Vector3 target, float t, bool parabolic)
            => Vector3.Lerp(start, target, t) + (parabolic
                ? Vector3.up * (.18f * 4f * t * (1f - t))
                : Vector3.up * Mathf.Sin(t * Mathf.PI) * .18f);

        bool TryLaunch(Vector2 startViewport, Vector2 screenPosition, bool wheelFlight)
        {
            if (!m_InteractionEnabled || m_Camera == null || m_Tracking == null ||
                !TryGetLaunchVisual(m_SelectedProjectile, out var prefab, out var source, out var actionId, out var slimeColor)) return false;
            Vector3 viewport3 = m_Camera.ScreenToViewportPoint(screenPosition);
            Vector2 viewport = new Vector2(viewport3.x, viewport3.y);
            PersonPoseData person = m_Tracking.Pick(viewport);
            if (person == null && !wheelFlight) return false;
            Rect bounds = person != null ? person.screenRect : new Rect(0, 0, 1, 1);
            var item = source != null ? CreateThrownItem(source, GetProjectileName(m_SelectedProjectile)) : CreateThrownItem(prefab);
            if (item == null) return false;
            item.personId = person != null ? person.personId : -1;
            item.wheelFlight = wheelFlight;
            item.targetViewport = viewport;
            item.actionId = actionId;
            item.context = person != null && InteractionContext != null ? InteractionContext(person.personId) : 0;
            item.eventId = PersonInteractionHit.NextId();
            item.slimeColor = slimeColor;
            item.normalizedOffset = new Vector2(
                Mathf.Clamp01((viewport.x - bounds.xMin) / Mathf.Max(.001f, bounds.width)),
                Mathf.Clamp01((viewport.y - bounds.yMin) / Mathf.Max(.001f, bounds.height)));
            item.startLocal = LocalPoint(startViewport, StartDepth);
            item.gameObject.transform.localPosition = item.startLocal;
            Vector3 direction = LocalPoint(viewport, PersonLayerDepth) - item.startLocal;
            item.gameObject.transform.GetChild(0).localRotation = Quaternion.LookRotation(direction, Vector3.up);
            var renderers = item.gameObject.GetComponentsInChildren<Renderer>();
            Bounds modelBounds = renderers[0].bounds;
            foreach (var renderer in renderers) modelBounds.Encapsulate(renderer.bounds);
            float halfDepth = 0f;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 world = modelBounds.center + Vector3.Scale(modelBounds.extents,
                    new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                halfDepth = Mathf.Max(halfDepth, Mathf.Abs(item.gameObject.transform.InverseTransformPoint(world).z));
            }
            item.halfDepth = halfDepth * ProjectileVisualScale;
            m_Items.Add(item);
            while (m_Items.Count > MaximumProjectiles)
            {
                DestroyItem(m_Items[0]);
                m_Items.RemoveAt(0);
            }
            return true;
        }

        ThrownItem CreateThrownItem(BodyProjectile prefab)
        {
            if (prefab == null || m_Camera == null) return null;
            var root = new GameObject("3D Thrown " + SelectedProjectileName);
            root.transform.SetParent(m_Camera.transform, false);
            root.transform.localScale = Vector3.one * ProjectileVisualScale;
            var instance = Object.Instantiate(prefab.gameObject, root.transform);
            PrepareThrownVisual(instance);
            return new ThrownItem { gameObject = root };
        }

        ThrownItem CreateThrownItem(GameObject source, string displayName)
        {
            if (source == null || m_Camera == null) return null;
            var root = new GameObject("3D Thrown " + displayName);
            root.transform.SetParent(m_Camera.transform, false);
            root.transform.localScale = Vector3.one * ProjectileVisualScale;
            var instance = Object.Instantiate(source, root.transform);
            PrepareThrownVisual(instance);
            var showcase = source.GetComponentInParent<PersonShowcaseController>();
            return new ThrownItem
            {
                gameObject = root,
                releaseVisualAssets = showcase != null ? showcase.RetainProjectileAssets(source) : null
            };
        }

        void PrepareThrownVisual(GameObject instance)
        {
            instance.SetActive(true);
            var bodyProjectile = instance.GetComponent<BodyProjectile>();
            if (bodyProjectile != null) bodyProjectile.enabled = false;
            var oldBounce = instance.GetComponent<ProjectileImpactBounce>();
            if (oldBounce != null) oldBounce.enabled = false;
            foreach (var body in instance.GetComponentsInChildren<Rigidbody>(true))
            {
                body.isKinematic = true;
                body.useGravity = false;
                body.detectCollisions = false;
            }
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            instance.transform.localScale = Vector3.one;
            instance.transform.localRotation = Quaternion.identity;
        }

        bool TryGetLaunchVisual(int index, out BodyProjectile prefab, out GameObject source,
            out string actionId, out Color slimeColor)
        {
            prefab = null; source = null; actionId = string.Empty; slimeColor = Color.white;
            if (HasShowcaseProjectile && index == ShowcaseProjectileIndex)
            { source = m_ShowcaseSource; actionId = "ShowcaseModel"; slimeColor = new Color(.45f, .72f, 1f); return true; }
            if (!TryGetEntry(index, out var entry) || entry.prefab == null) return false;
            prefab = entry.prefab; actionId = entry.key; slimeColor = entry.slimeColor; return true;
        }

        void UpdateThrownItems(float deltaTime)
        {
            foreach (var surface in m_SlimeSurfaces.Values) if (surface != null) surface.BeginFrame();
            for (int i = m_Items.Count - 1; i >= 0; i--)
            {
                var item = m_Items[i];
                PersonPoseData person = m_Tracking != null ? m_Tracking.Find(item.personId) : null;
                if (item.gameObject == null || person == null && !item.wheelFlight)
                {
                    DestroyItem(item); m_Items.RemoveAt(i); continue;
                }
                bool validPerson = person != null && person.trackingConfidence >= .08f;
                if (item.wheelFlight && item.attached && person == null)
                { DestroyItem(item); m_Items.RemoveAt(i); continue; }
                if (item.attached && !validPerson) { item.gameObject.SetActive(false); continue; }
                item.gameObject.SetActive(item.wheelFlight || validPerson);
                if (!item.wheelFlight && !validPerson) continue;
                if (item.wheelFlight && !validPerson) item.personId = -1;
                Vector2 viewport = validPerson ? TargetViewport(person, item.normalizedOffset) : item.targetViewport;
                item.targetViewport = viewport;
                Vector3 target = LocalPoint(viewport, PersonLayerDepth);
                if (item.wheelFlight && item.flightProgress >= 1f && !item.attached)
                {
                    item.missAge += Mathf.Max(0f, deltaTime);
                    float age = Mathf.Clamp01(item.missAge / .60f);
                    item.gameObject.transform.localPosition = target + (target - item.startLocal).normalized * age * 2.4f;
                    item.gameObject.transform.localScale = Vector3.one * ProjectileVisualScale * (1f - age) * (1f - age);
                    if (age >= 1f) { DestroyItem(item); m_Items.RemoveAt(i); }
                    continue;
                }
                if (!item.attached)
                {
                    float remainingFlight = (1f - item.flightProgress) * FlightSeconds;
                    item.flightProgress = Mathf.Min(1f, item.flightProgress + deltaTime / FlightSeconds);
                    float t = item.flightProgress;
                    item.gameObject.transform.localPosition = FlightPoint(item.startLocal, target, t, item.wheelFlight);
                    if (t >= 1f && validPerson)
                    {
                        item.attached = true;
                        item.impactAge = Mathf.Max(0f, deltaTime - remainingFlight);
                        InteractionConfirmed?.Invoke(new PersonInteractionHit(item.personId, item.actionId,
                            viewport, item.eventId, item.context));
                    }
                }
                else item.impactAge += Mathf.Max(0f, deltaTime);
                if (item.attached)
                {
                    float age = Mathf.Min(item.impactAge, ProjectileSlimeMotion.SettleSeconds);
                    Vector3 scale = ProjectileSlimeMotion.ModelScale(age);
                    item.gameObject.transform.localScale = Vector3.Scale(Vector3.one * ProjectileVisualScale, scale);
                    item.gameObject.transform.localPosition = target - Vector3.forward *
                        (item.halfDepth * scale.z + ProjectileSlimeMotion.Rebound(age));
                    AddSlime(item, person, target, age);
                }
            }
            m_EmptySurfaces.Clear();
            foreach (var pair in m_SlimeSurfaces)
            {
                if (pair.Value == null) { m_EmptySurfaces.Add(pair.Key); continue; }
                pair.Value.FinishFrame(PersonLayerDepth + .002f);
                if (m_Tracking.Find(pair.Key) == null) m_EmptySurfaces.Add(pair.Key);
            }
            foreach (int id in m_EmptySurfaces)
            {
                if (m_SlimeSurfaces[id] != null) DestroyOwned(m_SlimeSurfaces[id].gameObject);
                m_SlimeSurfaces.Remove(id);
            }
        }

        void AddSlime(ThrownItem item, PersonPoseData person, Vector3 target, float age)
        {
            if (m_SlimeTemplate == null) return;
            if (!m_SlimeSurfaces.TryGetValue(item.personId, out var surface) || surface == null)
            {
                surface = new GameObject("Merged Slime Person " + item.personId).AddComponent<PersonSlimeSurface>();
                surface.Configure(m_Camera, m_SlimeTemplate);
                m_SlimeSurfaces[item.personId] = surface;
                surface.BeginFrame();
            }
            Rect bounds = person.screenRect;
            Vector3 lower = LocalPoint(new Vector2(bounds.center.x, bounds.yMin), PersonLayerDepth);
            Vector3 upper = LocalPoint(new Vector2(bounds.center.x, bounds.yMax), PersonLayerDepth);
            float height = Mathf.Abs(upper.y - lower.y);
            float radius = Mathf.Clamp(height * .085f, .025f, .14f) * ProjectileSlimeMotion.Spread(age);
            float travel = Mathf.Min(height * .14f, Mathf.Max(0f, target.y - lower.y - radius * .4f)) * ProjectileSlimeMotion.Flow(age);
            surface.Add(new Vector2(target.x, target.y), radius, travel, item.slimeColor);
        }

        Vector2 TargetViewport(PersonPoseData person, Vector2 offset)
        {
            Rect bounds = person.screenRect;
            return new Vector2(bounds.xMin + bounds.width * offset.x, bounds.yMin + bounds.height * offset.y);
        }

        Vector3 LocalPoint(Vector2 viewport, float depth)
        {
            Vector3 world = m_Camera.ViewportToWorldPoint(new Vector3(viewport.x, viewport.y, depth));
            return m_Camera.transform.InverseTransformPoint(world);
        }

        bool TryGetEntry(int index, out BodyProjectileCatalog.Entry entry)
        {
            entry = null;
            if (m_Catalog == null || m_Catalog.entries == null || index < 0 || index >= m_Catalog.entries.Length) return false;
            entry = m_Catalog.entries[index];
            return entry != null;
        }

        public void CancelGesture() { m_PointerHeld = false; m_PickerOpen = false; }

        void DestroyItem(ThrownItem item)
        {
            if (item == null) return;
            if (item.gameObject != null) DestroyOwned(item.gameObject);
            item.releaseVisualAssets?.Invoke();
            item.releaseVisualAssets = null;
        }

        static void DestroyOwned(Object value)
        {
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }

        void OnDestroy() => ClearEffects();
    }
}
