using EarthOL.BodyThrow;
using EarthOL.Deform;
using System.Collections.Generic;
using UnityEngine;

namespace EarthOL.ARCore
{
    /// <summary>Uses the original camera-warp spring on a padded region around the tracked proxies.</summary>
    [DefaultExecutionOrder(200)]
    public sealed class PersonDeformationSurface : MonoBehaviour
    {
        Camera m_Camera;
        PersonTrackingManager m_PersonTracking;
        PersonDistanceEstimator m_DistanceEstimator;
        PoseDetectionManager m_DetectionManager;
        int m_FocusedPersonId = -1;
        GameObject m_Surface;
        PokeWarpDeformer m_Warp;
        Renderer m_Renderer;
        Rect m_Region;
        bool m_RegionReady;
        bool m_PointerHeld;
        int m_PointerId;
        Vector3 m_SwipeOffset;
        Quaternion m_SwipeTilt = Quaternion.identity;
        float m_SurfaceDepth = 0.45f;
        sealed class ImpactSurface
        {
            public GameObject root;
            public PokeWarpDeformer warp;
            public Renderer renderer;
            public int personId;
            public Vector2 offset;
            public float radiusPixels;
            public float started;
        }
        readonly List<ImpactSurface> m_Impacts = new List<ImpactSurface>(4);
        public int ActiveImpactCount
        {
            get { int count = 0; foreach (var impact in m_Impacts) if (impact.root.activeSelf) count++; return count; }
        }

        /// <summary>Small independent camera regions reuse the original spring for simultaneous person impacts.</summary>
        public bool PlayLocalImpact(int personId, Vector2 viewportPoint)
        {
            var person = m_PersonTracking != null ? m_PersonTracking.Find(personId) : null;
            if (person == null || person.trackingConfidence < .08f || m_Warp == null) return false;
            ImpactSurface effect = null;
            foreach (var candidate in m_Impacts) if (!candidate.root.activeSelf) { effect = candidate; break; }
            if (effect == null && m_Impacts.Count < 4)
            {
                var root = new GameObject("Local person impact");
                root.transform.SetParent(m_Camera.transform, false);
                root.layer = 2;
                root.SetActive(false);
                var warp = root.AddComponent<PokeWarpDeformer>();
                warp.planeInXZ = false; warp.createCollider = false; warp.lockPokesToSurface = true;
                warp.gridX = 24; warp.gridY = 24;
                warp.warpMaterial = m_Warp.warpMaterial;
                warp.stretchAmount = m_Warp.stretchAmount; warp.bulgeAmount = m_Warp.bulgeAmount;
                warp.frequency = m_Warp.frequency; warp.dampingRatio = m_Warp.dampingRatio; warp.logPokes = false;
                warp.Initialize();
                var renderer = root.GetComponent<Renderer>();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                effect = new ImpactSurface { root = root, warp = warp, renderer = renderer };
                m_Impacts.Add(effect);
            }
            if (effect == null)
            {
                effect = m_Impacts[0];
                foreach (var candidate in m_Impacts) if (candidate.started < effect.started) effect = candidate;
            }
            effect.warp.ResetPokes();
            effect.personId = personId;
            effect.offset = new Vector2((viewportPoint.x - person.screenRect.xMin) / person.screenRect.width,
                (viewportPoint.y - person.screenRect.yMin) / person.screenRect.height);
            effect.radiusPixels = Mathf.Clamp(person.screenRect.height * m_Camera.pixelHeight * .085f,
                m_Camera.pixelHeight * .008f, m_Camera.pixelHeight * .042f);
            effect.started = Time.unscaledTime;
            effect.root.SetActive(true);
            PlaceImpact(effect, person);
            const int pointer = int.MinValue + 1;
            effect.warp.BeginAt(pointer, effect.root.transform.position);
            effect.warp.OnPokeEnd(pointer);
            effect.renderer.enabled = true;
            return true;
        }

        void PlaceImpact(ImpactSurface effect, PersonPoseData person)
        {
            Vector2 center = person.screenRect.min + Vector2.Scale(person.screenRect.size, effect.offset);
            float depth = m_DistanceEstimator.Estimate(person, out _);
            Vector3 world = m_Camera.ViewportToWorldPoint(new Vector3(center.x, center.y, depth));
            Vector3 edge = m_Camera.ScreenToWorldPoint(new Vector3(
                m_Camera.pixelRect.xMin + center.x * m_Camera.pixelWidth + effect.radiusPixels,
                m_Camera.pixelRect.yMin + center.y * m_Camera.pixelHeight, depth));
            float radius = Vector3.Distance(world, edge);
            effect.root.transform.SetPositionAndRotation(world, m_Camera.transform.rotation * Quaternion.Euler(0, 180, 0));
            effect.warp.SetPlaneSize(radius * 5f, radius * 5f);
            effect.warp.pokeRadius = radius;
        }

        public void UpdateLocalImpacts()
        {
            foreach (var effect in m_Impacts)
            {
                if (!effect.root.activeSelf) continue;
                var person = m_PersonTracking.Find(effect.personId);
                if (person == null || !effect.warp.HasActivePokes) { effect.root.SetActive(false); continue; }
                PlaceImpact(effect, person);
                effect.renderer.enabled = person.trackingConfidence >= .08f;
            }
        }

        public Rect Region => m_Region;
        public PokeWarpDeformer Warp => m_Warp;
        public bool ReadPointerInput { get; set; } = true;

        /// <summary>Explicit effect command for input adapters; uses the existing spring unchanged.</summary>
        public bool PulseAt(int personId, Vector2 screenPosition, Vector2 direction)
        {
            var person = m_PersonTracking != null ? m_PersonTracking.Find(personId) : null;
            if (person == null || person.trackingConfidence < .2f || !TryGetRegion(person, out var region)) return false;
            EndPointer();
            m_FocusedPersonId = personId;
            m_Region = region;
            m_RegionReady = true;
            ApplyScreenRegion(m_DistanceEstimator.Estimate(person, out _));
            const int effectPointer = int.MinValue;
            Vector3 start = PointOnSurface(screenPosition);
            m_Warp.BeginAt(effectPointer, start);
            if (direction.sqrMagnitude > .01f)
                m_Warp.DragAt(effectPointer, PointOnSurface(screenPosition + direction.normalized * Mathf.Min(Screen.width, Screen.height) * .035f));
            m_Warp.OnPokeEnd(effectPointer);
            m_Renderer.enabled = true;
            return true;
        }

        /// <summary>让主 warp 面聚焦到指定人物，供挥扫攻击在多人场景下对准被攻击者。</summary>
        public bool FocusPerson(int personId)
        {
            var person = m_PersonTracking != null ? m_PersonTracking.Find(personId) : null;
            if (person == null || person.trackingConfidence < .2f || !TryGetRegion(person, out var region)) return false;
            m_FocusedPersonId = personId;
            m_Region = region;
            m_RegionReady = true;
            ApplyScreenRegion(m_DistanceEstimator != null ? m_DistanceEstimator.Estimate(person, out _) : 0.45f);
            return true;
        }

        /// <summary>挥扫的方向性横向推开 + 倾斜（由 SwipeDeformController 的弹簧驱动）。</summary>
        public void SetSwipeTransform(Vector3 offset, Quaternion tilt)
        {
            m_SwipeOffset = offset;
            m_SwipeTilt = tilt;
        }

        /// <summary>挥扫命中点的局部冲击 poke（与按压的径向凹陷区分）。</summary>
        public void SwipePokeAt(Vector2 screenPosition, float strength)
        {
            if (m_Warp == null || m_Camera == null) return;
            m_Warp.ImpulsePoke(PointOnSurface(screenPosition), strength);
        }

        public IPersonModelDeformation CreateModelDeformation(GameObject model)
        {
            if (m_Warp == null || model == null) return null;
            var filter = model.GetComponent<MeshFilter>();
            var renderer = model.GetComponent<Renderer>();
            if (filter == null || filter.sharedMesh == null || renderer == null) return null;
            var adapter = model.AddComponent<PersonModelDeformation>();
            adapter.Configure(m_Warp, renderer, filter.sharedMesh.bounds);
            return adapter;
        }

        public void ResetEffects()
        {
            GetComponent<SwipeDeformController>()?.ResetEffects();
            foreach (var impact in m_Impacts) impact.root.SetActive(false);
            EndPointer();
            if (m_Warp != null) m_Warp.ResetPokes();
            m_FocusedPersonId = -1;
            m_Region = default;
            m_RegionReady = false;
            if (m_Renderer != null) m_Renderer.enabled = false;
            if (m_Surface != null) m_Surface.SetActive(false);
            if (m_DetectionManager != null) m_DetectionManager.SetInferencePaused(false);
        }

        public void Configure(Camera camera, PersonTrackingManager tracking,
            PersonDistanceEstimator distanceEstimator, PoseDetectionManager detectionManager)
        {
            m_Camera = camera;
            m_PersonTracking = tracking;
            m_DistanceEstimator = distanceEstimator;
            m_DetectionManager = detectionManager;
            CreateSurface();
        }

        // Compatibility overload for older editor scenes; the active runtime uses the tracked 2D person path above.
        public void Configure(Camera camera, BodyProxyRig rig, BodyPoseTracker tracker)
        {
            m_Camera = camera;
            CreateSurface();
        }

        void CreateSurface()
        {
            m_Surface = new GameObject("Person camera deformation region");
            m_Surface.transform.SetParent(m_Camera.transform, false);
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
            UpdateLocalImpacts();
            RefreshRegion(Time.unscaledDeltaTime);
            UpdateBodyMask();
            BodyPointerFrame frame = default;
            bool hasFrame = ReadPointerInput && !ARCoreModeOverlay.WheelTapToThrowEnabled && BodyPointerFrame.TryRead(out frame);
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
            m_Renderer.enabled = m_Surface.activeInHierarchy && (m_Warp.HasActivePokes || m_Warp.HasActiveSwipeShear);
            if (m_DetectionManager != null) m_DetectionManager.SetInferencePaused(m_PointerHeld || m_Warp.HasActivePokes);
        }

        public bool RefreshRegion(float deltaTime)
        {
            return m_PersonTracking != null && RefreshTrackedRegion(deltaTime);
        }

        bool RefreshTrackedRegion(float deltaTime)
        {
            PersonPoseData person = m_FocusedPersonId >= 0 ? m_PersonTracking.Find(m_FocusedPersonId) : BestPerson();
            Rect desired = default;
            bool valid = person != null && person.trackingConfidence > 0.1f && TryGetRegion(person, out desired);
            if (valid && !m_Warp.HasActivePokes)
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
            else if (!valid) EndPointer();

            if (!m_RegionReady || !valid && !m_Warp.HasActivePokes)
            {
                m_Surface.SetActive(false);
                m_RegionReady = false;
                m_FocusedPersonId = -1;
                return false;
            }
            float depth = m_DistanceEstimator.Estimate(person, out _);
            ApplyScreenRegion(depth);
            return valid;
        }

        PersonPoseData BestPerson()
        {
            PersonPoseData best = null;
            var persons = m_PersonTracking.Persons;
            for (int i = 0; i < persons.Count; i++)
                if (persons[i].trackingConfidence > .2f && (best == null || persons[i].trackingConfidence > best.trackingConfidence))
                    best = persons[i];
            return best;
        }

        PersonPoseData CurrentPerson()
        {
            if (m_PersonTracking == null) return null;
            return m_FocusedPersonId >= 0 ? m_PersonTracking.Find(m_FocusedPersonId) : BestPerson();
        }

        void UpdateBodyMask()
        {
            var segments = new List<PokeWarpDeformer.BodySegment>();
            var person = CurrentPerson();
            if (person != null && m_Camera != null && m_Surface != null && m_Warp != null)
            {
                var pose = person.AsPoseView();
                float aspect = (float)m_Camera.pixelWidth / Mathf.Max(1f, m_Camera.pixelHeight);

                if (BodyPoseGeometry.TryFitHead(pose, aspect, out var headCenter, out var headRadius))
                {
                    Vector2 h = KeypointToLocal(headCenter);
                    float hr = RadiusToLocal(headCenter, headRadius);
                    segments.Add(new PokeWarpDeformer.BodySegment(h, h, hr * 0.9f));
                }

                if (BodyPoseGeometry.TryFitTorso(pose, aspect, out var shoulders, out var hips, out var torsoRadius))
                {
                    Vector2 s = KeypointToLocal(shoulders);
                    Vector2 h = KeypointToLocal(hips);
                    float tr = RadiusToLocal(shoulders, torsoRadius);
                    segments.Add(new PokeWarpDeformer.BodySegment(s, h, tr * 0.85f));
                    float limbR = tr * 0.5f;
                    AddLimb(pose, segments, 5, 7, limbR);
                    AddLimb(pose, segments, 7, 9, limbR * 0.8f);
                    AddLimb(pose, segments, 6, 8, limbR);
                    AddLimb(pose, segments, 8, 10, limbR * 0.8f);
                    AddLimb(pose, segments, 11, 13, limbR * 1.1f);
                    AddLimb(pose, segments, 13, 15, limbR * 0.9f);
                    AddLimb(pose, segments, 12, 14, limbR * 1.1f);
                    AddLimb(pose, segments, 14, 16, limbR * 0.9f);
                }
            }

            float gridSpacing = Mathf.Max(m_Warp.planeWidth / Mathf.Max(1, m_Warp.gridX),
                m_Warp.planeHeight / Mathf.Max(1, m_Warp.gridY));
            float feather = Mathf.Max(gridSpacing * 3f, Mathf.Min(m_Warp.planeWidth, m_Warp.planeHeight) * .06f);
            // Upload an empty mask too, so a lost/missing pose cannot reuse old limbs.
            m_Warp.SetBodyMask(segments.ToArray(), feather);
        }

        Vector2 KeypointToLocal(Vector2 viewportPoint)
        {
            Vector3 world = m_Camera.ViewportToWorldPoint(new Vector3(viewportPoint.x, viewportPoint.y, m_SurfaceDepth));
            Vector3 local = m_Surface.transform.InverseTransformPoint(world);
            return new Vector2(local.x, local.y);
        }

        float RadiusToLocal(Vector2 center, float viewportRadius)
        {
            Vector2 c = KeypointToLocal(center);
            Vector2 r = KeypointToLocal(center + Vector2.up * viewportRadius);
            return Vector2.Distance(c, r);
        }

        void AddLimb(BodyPoseResult pose, List<PokeWarpDeformer.BodySegment> segments, int first, int second, float radius)
        {
            if (!BodyPoseGeometry.HasPoint(pose, first) || !BodyPoseGeometry.HasPoint(pose, second)) return;
            Vector2 a = KeypointToLocal(pose.keypoints[first]);
            Vector2 b = KeypointToLocal(pose.keypoints[second]);
            segments.Add(new PokeWarpDeformer.BodySegment(a, b, radius));
        }

        static bool TryGetRegion(PersonPoseData person, out Rect region)
        {
            region = default;
            if (person == null) return false;
            Rect rect = person.screenRect;
            Vector2 padding = new Vector2(Mathf.Max(0.06f, rect.width * 0.30f), Mathf.Max(0.06f, rect.height * 0.18f));
            region = Rect.MinMaxRect(Mathf.Clamp01(rect.xMin - padding.x), Mathf.Clamp01(rect.yMin - padding.y),
                Mathf.Clamp01(rect.xMax + padding.x), Mathf.Clamp01(rect.yMax + padding.y));
            return region.width > 0.01f && region.height > 0.01f;
        }

        void ApplyScreenRegion(float depth)
        {
            depth = Mathf.Max(m_Camera.nearClipPlane + 0.02f, depth);
            m_SurfaceDepth = depth;
            Vector2 center = m_Region.center;
            Vector3 world = m_Camera.ViewportToWorldPoint(new Vector3(center.x, center.y, depth));
            float width = Vector3.Distance(m_Camera.ViewportToWorldPoint(new Vector3(m_Region.xMin, center.y, depth)),
                m_Camera.ViewportToWorldPoint(new Vector3(m_Region.xMax, center.y, depth)));
            float height = Vector3.Distance(m_Camera.ViewportToWorldPoint(new Vector3(center.x, m_Region.yMin, depth)),
                m_Camera.ViewportToWorldPoint(new Vector3(center.x, m_Region.yMax, depth)));
            var baseRotation = m_Camera.transform.rotation * Quaternion.Euler(0f, 180f, 0f);
            m_Surface.transform.SetPositionAndRotation(world + m_SwipeOffset, baseRotation * m_SwipeTilt);
            m_Surface.transform.localScale = Vector3.one;
            m_Warp.SetPlaneSize(width, height);
            m_Warp.pokeRadius = Mathf.Min(width, height) * 0.40f;
            m_Surface.SetActive(true);
        }

        public bool TryBegin(int pointerId, Vector2 screenPosition)
        {
            if (!m_RegionReady || ARCoreModeOverlay.IsPointOnOverlay(screenPosition)) return false;
            Vector3 viewport = m_Camera.ScreenToViewportPoint(screenPosition);
            if (m_PersonTracking != null)
            {
                var person = m_PersonTracking.Pick(new Vector2(viewport.x, viewport.y), 0.30f, 0.18f);
                if (person == null || !TryGetRegion(person, out m_Region)) return false;
                m_FocusedPersonId = person.personId;
                m_RegionReady = true;
                float depth = m_DistanceEstimator.Estimate(person, out _);
                ApplyScreenRegion(depth);
            }
            else return false;
            if (!m_Region.Contains(new Vector2(viewport.x, viewport.y))) return false;
            EndPointer();
            m_PointerHeld = true;
            m_PointerId = pointerId;
            m_Warp.BeginAt(pointerId, PointOnSurface(screenPosition));
            m_Renderer.enabled = true;
            if (m_DetectionManager != null) m_DetectionManager.SetInferencePaused(true);
            Debug.Log("[PersonWarp] 人物周围区域开始形变，区域=" + m_Region);
            return true;
        }

        public void EndPointer()
        {
            if (!m_PointerHeld) return;
            m_PointerHeld = false;
            if (m_Warp != null) m_Warp.OnPokeEnd(m_PointerId);
            if (m_DetectionManager != null) m_DetectionManager.SetInferencePaused(m_Warp != null && m_Warp.HasActivePokes);
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

        private void OnDisable()
        {
            ResetEffects();
        }

        private void OnDestroy()
        {
            foreach (var impact in m_Impacts)
                if (impact.root != null) { if (Application.isPlaying) Destroy(impact.root); else DestroyImmediate(impact.root); }
            if (m_Surface == null) return;
            if (Application.isPlaying) Destroy(m_Surface);
            else DestroyImmediate(m_Surface);
        }
    }
}
