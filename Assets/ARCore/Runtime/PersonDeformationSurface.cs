using EarthOL.BodyThrow;
using EarthOL.Deform;
using UnityEngine;

namespace EarthOL.ARCore
{
    /// <summary>Uses the original camera-warp spring on a padded region around the tracked proxies.</summary>
    [DefaultExecutionOrder(200)]
    public sealed class PersonDeformationSurface : MonoBehaviour
    {
        Camera m_Camera;
        BodyProxyRig m_Rig;
        BodyPoseTracker m_Tracker;
        GameObject m_Surface;
        PokeWarpDeformer m_Warp;
        Renderer m_Renderer;
        Rect m_Region;
        bool m_RegionReady;
        bool m_PointerHeld;
        int m_PointerId;

        [Tooltip("按压攻击对应的攻击 ID，按压真正触发形变后上报。")]
        [SerializeField] private string pressAttackId = "press";

        public Rect Region => m_Region;
        public PokeWarpDeformer Warp => m_Warp;
        public string PressAttackId { get => pressAttackId; set => pressAttackId = value; }

        public void Configure(Camera camera, BodyProxyRig rig, BodyPoseTracker tracker)
        {
            m_Camera = camera;
            m_Rig = rig;
            m_Tracker = tracker;
            m_Surface = new GameObject("Person camera deformation region");
            m_Surface.transform.SetParent(camera.transform, false);
            m_Surface.SetActive(false);
            m_Surface.layer = 2; // A visual overlay, excluded from ordinary pointer raycasts.
            m_Warp = m_Surface.AddComponent<PokeWarpDeformer>();
            m_Warp.planeWidth = m_Warp.planeHeight = 1f;
            m_Warp.planeInXZ = false;
            m_Warp.gridX = 48;
            m_Warp.gridY = 64;
            m_Warp.createCollider = false;
            m_Warp.lockPokesToSurface = true;
            m_Warp.warpMaterial = Resources.Load<Material>("PersonWarp");
            m_Warp.stretchAmount = 0.45f;
            m_Warp.bulgeAmount = 0.02f;
            m_Warp.frequency = 2.6f;
            m_Warp.dampingRatio = 0.30f;
            m_Warp.logPokes = false;
            m_Warp.Initialize();
            m_Renderer = m_Surface.GetComponent<Renderer>();
            m_Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_Renderer.receiveShadows = false;
            m_Renderer.enabled = false;
        }

        private void LateUpdate()
        {
            if (m_Warp == null || m_Camera == null) return;
            RefreshRegion(Time.unscaledDeltaTime);
            bool hasFrame = BodyPointerFrame.TryRead(out var frame);
            if (ARCoreModeOverlay.BlocksWorldInput)
            {
                EndPointer();
            }
            else if (hasFrame)
            {
                if (frame.down) TryBegin(frame.id, frame.position);
                if (m_PointerHeld && frame.id == m_PointerId)
                {
                    if (frame.released || frame.canceled) EndPointer();
                    else if (frame.held) m_Warp.DragAt(frame.id, PointOnSurface(frame.position));
                }
            }
            else if (m_PointerHeld) EndPointer();
            m_Renderer.enabled = m_Surface.activeInHierarchy && m_Warp.HasActivePokes;
            if (m_Tracker != null) m_Tracker.DeformationGestureActive = m_PointerHeld || m_Warp.HasActivePokes;
        }

        public bool RefreshRegion(float deltaTime)
        {
            Rect desired = default;
            bool valid = m_Tracker != null && m_Tracker.HasPose && TryGetRegion(m_Camera, m_Rig, out desired);
            if (valid)
            {
                if (!m_Warp.HasActivePokes)
                {
                    if (!m_RegionReady) m_Region = desired;
                    else
                    {
                        float blend = 1f - Mathf.Exp(-Mathf.Max(0f, deltaTime) / 0.16f);
                        m_Region = Rect.MinMaxRect(Mathf.Lerp(m_Region.xMin, desired.xMin, blend), Mathf.Lerp(m_Region.yMin, desired.yMin, blend),
                            Mathf.Lerp(m_Region.xMax, desired.xMax, blend), Mathf.Lerp(m_Region.yMax, desired.yMax, blend));
                    }
                    m_RegionReady = true;
                }
            }
            else EndPointer();

            if (!m_RegionReady || !valid && !m_Warp.HasActivePokes)
            {
                m_Surface.SetActive(false);
                m_RegionReady = false;
                return false;
            }
            float depth = Mathf.Max(0.45f, m_Camera.nearClipPlane + 0.05f);
            Vector2 center = m_Region.center;
            Vector3 world = m_Camera.ViewportToWorldPoint(new Vector3(center.x, center.y, depth));
            float width = Vector3.Distance(m_Camera.ViewportToWorldPoint(new Vector3(m_Region.xMin, center.y, depth)),
                m_Camera.ViewportToWorldPoint(new Vector3(m_Region.xMax, center.y, depth)));
            float height = Vector3.Distance(m_Camera.ViewportToWorldPoint(new Vector3(center.x, m_Region.yMin, depth)),
                m_Camera.ViewportToWorldPoint(new Vector3(center.x, m_Region.yMax, depth)));
            m_Surface.transform.SetPositionAndRotation(world, m_Camera.transform.rotation * Quaternion.Euler(0f, 180f, 0f));
            m_Surface.transform.localScale = Vector3.one;
            m_Warp.SetPlaneSize(width, height);
            m_Warp.pokeRadius = Mathf.Min(width, height) * 0.40f;
            m_Surface.SetActive(true);
            return valid;
        }

        public bool TryBegin(int pointerId, Vector2 screenPosition)
        {
            if (!m_RegionReady || m_Tracker == null || !m_Tracker.HasPose || !m_Rig.HasTrackedPart ||
                ARCoreModeOverlay.IsPointOnOverlay(screenPosition)) return false;
            Vector3 viewport = m_Camera.ScreenToViewportPoint(screenPosition);
            if (!m_Region.Contains(new Vector2(viewport.x, viewport.y))) return false;
            EndPointer();
            m_PointerHeld = true;
            m_Tracker.DeformationGestureActive = true;
            m_PointerId = pointerId;
            m_Warp.BeginAt(pointerId, PointOnSurface(screenPosition));
            m_Renderer.enabled = true;

            // 按压真正触发有效形变后，才算一次成功攻击。
            EarthOL.Combat.AttackDispatcher.Report(pressAttackId);

            Debug.Log("[PersonWarp] 人物周围区域开始形变，区域=" + m_Region);
            return true;
        }

        public void EndPointer()
        {
            if (!m_PointerHeld) return;
            m_PointerHeld = false;
            if (m_Warp != null) m_Warp.OnPokeEnd(m_PointerId);
            Debug.Log("[PersonWarp] 松手，开始弹簧回弹。");
        }

        private Vector3 PointOnSurface(Vector2 screenPosition)
        {
            Vector3 viewport = m_Camera.ScreenToViewportPoint(screenPosition);
            viewport.x = Mathf.Clamp(viewport.x, m_Region.xMin, m_Region.xMax);
            viewport.y = Mathf.Clamp(viewport.y, m_Region.yMin, m_Region.yMax);
            var ray = m_Camera.ViewportPointToRay(viewport);
            var plane = new Plane(m_Surface.transform.forward, m_Surface.transform.position);
            return plane.Raycast(ray, out float distance) ? ray.GetPoint(distance) : m_Surface.transform.position;
        }

        public static bool TryGetRegion(Camera camera, BodyProxyRig rig, out Rect region)
        {
            region = default;
            if (camera == null || rig == null || !rig.HasTrackedPart) return false;
            Vector2 min = Vector2.one * float.MaxValue, max = Vector2.one * float.MinValue;
            bool any = false;
            foreach (var part in rig.Parts)
            {
                if (!part.gameObject.activeInHierarchy) continue;
                var collider = part.GetComponent<Collider>();
                if (collider == null || !collider.enabled) continue;
                var bounds = collider.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var point = bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 viewport = camera.WorldToViewportPoint(point);
                    if (viewport.z <= camera.nearClipPlane) continue;
                    min = Vector2.Min(min, viewport);
                    max = Vector2.Max(max, viewport);
                    any = true;
                }
            }
            if (!any) return false;
            Vector2 padding = new Vector2(Mathf.Max(0.06f, (max.x - min.x) * 0.35f), Mathf.Max(0.06f, (max.y - min.y) * 0.22f));
            region = Rect.MinMaxRect(Mathf.Clamp01(min.x - padding.x), Mathf.Clamp01(min.y - padding.y),
                Mathf.Clamp01(max.x + padding.x), Mathf.Clamp01(max.y + padding.y));
            return region.width > 0.01f && region.height > 0.01f;
        }

        private void OnDisable()
        {
            EndPointer();
            if (m_Tracker != null) m_Tracker.DeformationGestureActive = false;
            if (m_Surface != null) m_Surface.SetActive(false);
            m_RegionReady = false;
        }

        private void OnDestroy()
        {
            if (m_Surface == null) return;
            if (Application.isPlaying) Destroy(m_Surface);
            else DestroyImmediate(m_Surface);
        }
    }
}
