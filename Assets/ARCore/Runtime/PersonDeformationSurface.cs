using System.Collections.Generic;
using EarthOL.BodyThrow;
using EarthOL.Deform;
using UnityEngine;

namespace EarthOL.ARCore
{
    /// <summary>基于 2D 姿态的相机 warp 形变（按压/挥扫），不再做连续深度估计。</summary>
    [DefaultExecutionOrder(200)]
    public sealed class PersonDeformationSurface : MonoBehaviour
    {
        Camera m_Camera;
        PersonTrackingManager m_PersonTracking;
        GameObject m_Surface;
        PokeWarpDeformer m_Warp;
        Renderer m_Renderer;
        Rect m_Region;
        bool m_RegionReady;
        bool m_PointerHeld;
        int m_PointerId;
        public bool PressEnabled = true;
        Vector3 m_SwipeOffset;
        Quaternion m_SwipeTilt = Quaternion.identity;
        float m_SurfaceDepth = 0.45f;

        public Rect Region => m_Region;
        public PokeWarpDeformer Warp => m_Warp;

        public void Configure(Camera camera, PersonTrackingManager tracking)
        {
            m_Camera = camera;
            m_PersonTracking = tracking;
            m_Surface = new GameObject("Person camera deformation region");
            m_Surface.transform.SetParent(camera.transform, false);
            m_Surface.SetActive(false);
            m_Surface.layer = 2;
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

        PersonPoseData CurrentPerson() => m_PersonTracking != null ? m_PersonTracking.BestPerson() : null;

        private void LateUpdate()
        {
            if (m_Warp == null || m_Camera == null) return;
            RefreshRegion(Time.unscaledDeltaTime);
            UpdateBodyMask();

            // Li 方式：形变输入由本组件自己读取触摸。
            if (PressEnabled && BodyPointerFrame.TryRead(out var frame))
            {
                if (frame.down) TryBegin(frame.id, frame.position);
                if (m_PointerHeld && frame.id == m_PointerId)
                {
                    if (frame.released || frame.canceled) EndPointer();
                    else if (frame.held) Drag(frame.id, frame.position);
                }
            }
            else if (m_PointerHeld) EndPointer();

            m_Renderer.enabled = m_Surface.activeInHierarchy && (m_Warp.HasActivePokes || m_Warp.HasActiveSwipeShear);
        }

        public bool RefreshRegion(float deltaTime)
        {
            Rect desired = default;
            var person = CurrentPerson();
            bool valid = person != null && TryGetPoseRegion(person.AsPoseView(), out desired);

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
            return valid;
        }

        public bool TryBegin(int pointerId, Vector2 screenPosition)
        {
            if (!m_RegionReady || CurrentPerson() == null) return false;
            Vector3 viewport = m_Camera.ScreenToViewportPoint(screenPosition);
            if (!m_Region.Contains(new Vector2(viewport.x, viewport.y))) return false;
            EndPointer();
            m_PointerHeld = true;
            m_PointerId = pointerId;
            m_Warp.BeginAt(pointerId, PointOnSurface(screenPosition));
            m_Renderer.enabled = true;
            Debug.Log("[PersonWarp] 人物周围区域开始形变，区域=" + m_Region);
            return true;
        }

        public void Drag(int pointerId, Vector2 screenPosition)
        {
            if (m_PointerHeld && pointerId == m_PointerId)
                m_Warp.DragAt(pointerId, PointOnSurface(screenPosition));
        }

        public void EndPointer()
        {
            if (!m_PointerHeld) return;
            m_PointerHeld = false;
            if (m_Warp != null) m_Warp.OnPokeEnd(m_PointerId);
            Debug.Log("[PersonWarp] 松手，开始弹簧回弹。");
        }

        public void SetSwipeTransform(Vector3 offset, Quaternion tilt)
        {
            m_SwipeOffset = offset;
            m_SwipeTilt = tilt;
        }

        public void SwipePokeAt(Vector2 screenPosition, float strength)
        {
            if (m_Warp == null || m_Camera == null) return;
            m_Warp.ImpulsePoke(PointOnSurface(screenPosition), strength);
        }

        void UpdateBodyMask()
        {
            var segments = new List<PokeWarpDeformer.BodySegment>();
            var person = CurrentPerson();
            if (person != null && m_Camera != null && m_Surface != null)
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

            if (segments.Count > 0)
                m_Warp.SetBodyMask(segments.ToArray(), 0.05f);
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

        static bool TryGetPoseRegion(BodyPoseResult pose, out Rect region)
        {
            region = default;
            if (pose.keypoints == null || pose.keypoints.Length == 0) return false;
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            bool any = false;
            for (int i = 0; i < pose.keypoints.Length; i++)
            {
                if (pose.keypointScores[i] < 0.1f) continue;
                Vector2 p = pose.keypoints[i];
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
                any = true;
            }
            if (!any) return false;
            float padX = Mathf.Max(0.08f, (maxX - minX) * 0.25f);
            float padY = Mathf.Max(0.06f, (maxY - minY) * 0.12f);
            region = Rect.MinMaxRect(Mathf.Clamp01(minX - padX), Mathf.Clamp01(minY - padY),
                Mathf.Clamp01(maxX + padX), Mathf.Clamp01(maxY + padY));
            return region.width > 0.02f && region.height > 0.02f;
        }

        Vector3 PointOnSurface(Vector2 screenPosition)
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
            EndPointer();
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
