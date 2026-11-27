using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;

namespace EarthOL.BodyThrow
{
    [DisallowMultipleComponent]
    public sealed class Frozen3DPersonBuilder : MonoBehaviour
    {
        [SerializeField, Range(0.05f, 0.35f)] float torsoRadiusRatio = 0.22f;
        [SerializeField, Range(0.02f, 0.20f)] float limbRadiusRatio = 0.075f;
        [SerializeField, Range(4, 16)] int maximumFrozenPersons = 10;
        Material m_Material;
        int m_CapsuleCount;
        readonly List<Frozen3DPersonController> m_Models = new List<Frozen3DPersonController>();
        public int CapsuleCount => m_CapsuleCount;
        public int ModelCount
        {
            get
            {
                for (int i = m_Models.Count - 1; i >= 0; i--) if (m_Models[i] == null) m_Models.RemoveAt(i);
                return m_Models.Count;
            }
        }

        public Frozen3DPersonController Build(FrozenPersonData snapshot, Camera camera, ARAnchorManager anchorManager)
        {
            if (snapshot == null || camera == null) return null;
            if (!Application.isEditor && (ARSession.state != ARSessionState.SessionTracking || anchorManager == null ||
                anchorManager.subsystem == null || !anchorManager.subsystem.running))
            {
                Debug.LogWarning("[FrozenPerson] 等待ARCore空间跟踪后再点击人物。");
                return null;
            }
            var pose = snapshot.ToPose();
            if (!PersonWorldPositionCalculator.TryCalculate(camera, snapshot.ViewportBounds.center,
                snapshot.EstimatedDistance, out var anchorPose)) return null;

            int existing = ModelCount;
            if (existing >= maximumFrozenPersons)
            {
                Debug.LogWarning("[FrozenPerson] 已达到固定人体数量上限 " + maximumFrozenPersons);
                return null;
            }

            var root = new GameObject("Frozen Person " + snapshot.PersonId + " #" + (existing + 1));
            root.transform.SetPositionAndRotation(anchorPose.position, anchorPose.rotation);
            root.transform.localScale = Vector3.one;
            if (m_Material == null)
            {
                var template = Resources.Load<Material>("FrozenPerson");
                if (template == null) { Destroy(root); return null; }
                m_Material = new Material(template) { name = "Frozen person capsules", hideFlags = HideFlags.DontSave };
            }
            var anchor = root.AddComponent<ARAnchor>();
            var controller = root.AddComponent<Frozen3DPersonController>();
            controller.Initialize(snapshot, anchor);
            m_CapsuleCount = 0;

            float aspect = (float)camera.pixelWidth / Mathf.Max(1, camera.pixelHeight);
            if (BodyPoseGeometry.TryFitHead(pose, aspect, out var headCenter, out var headRadius))
            {
                Vector3 headWorld = PersonWorldPositionCalculator.ViewportPointToWorld(camera, headCenter, snapshot.EstimatedDistance);
                float worldRadius = WorldRadius(camera, headCenter, headRadius, snapshot.EstimatedDistance);
                AddSegment(root.transform, "Head Capsule", headWorld + camera.transform.up * worldRadius * 0.35f,
                    headWorld - camera.transform.up * worldRadius * 0.35f, worldRadius * 0.72f, new Color(0.91f, 0.66f, 0.49f));
            }

            if (Valid(pose, 5, 6) && Valid(pose, 11, 12))
            {
                Vector2 shoulders2D = (pose.keypoints[5] + pose.keypoints[6]) * 0.5f;
                Vector2 hips2D = (pose.keypoints[11] + pose.keypoints[12]) * 0.5f;
                Vector3 shoulders = PersonWorldPositionCalculator.ViewportPointToWorld(camera, shoulders2D, snapshot.EstimatedDistance);
                Vector3 hips = PersonWorldPositionCalculator.ViewportPointToWorld(camera, hips2D, snapshot.EstimatedDistance);
                float width = Vector3.Distance(
                    PersonWorldPositionCalculator.ViewportPointToWorld(camera, pose.keypoints[5], snapshot.EstimatedDistance),
                    PersonWorldPositionCalculator.ViewportPointToWorld(camera, pose.keypoints[6], snapshot.EstimatedDistance));
                AddSegment(root.transform, "Torso Capsule", shoulders, hips, Mathf.Clamp(width * torsoRadiusRatio, 0.08f, 0.24f),
                    new Color(0.13f, 0.57f, 0.55f));
            }

            AddLimb(root.transform, pose, camera, snapshot.EstimatedDistance, 5, 7, new Color(0.91f, 0.35f, 0.26f));
            AddLimb(root.transform, pose, camera, snapshot.EstimatedDistance, 7, 9, new Color(0.96f, 0.56f, 0.31f));
            AddLimb(root.transform, pose, camera, snapshot.EstimatedDistance, 6, 8, new Color(0.92f, 0.67f, 0.23f));
            AddLimb(root.transform, pose, camera, snapshot.EstimatedDistance, 8, 10, new Color(0.95f, 0.76f, 0.30f));
            AddLimb(root.transform, pose, camera, snapshot.EstimatedDistance, 11, 13, new Color(0.23f, 0.47f, 0.70f));
            AddLimb(root.transform, pose, camera, snapshot.EstimatedDistance, 13, 15, new Color(0.29f, 0.56f, 0.77f));
            AddLimb(root.transform, pose, camera, snapshot.EstimatedDistance, 12, 14, new Color(0.39f, 0.44f, 0.72f));
            AddLimb(root.transform, pose, camera, snapshot.EstimatedDistance, 14, 16, new Color(0.47f, 0.51f, 0.78f));

            if (m_CapsuleCount == 0)
            {
                Destroy(root);
                Debug.LogWarning("[FrozenPerson] 点击快照中没有足够置信度的头部、躯干或肢体关键点。");
                return null;
            }
            Debug.Log($"[FrozenPerson] 固定姿态快照：PersonID={snapshot.PersonId} capsules={m_CapsuleCount}, " +
                $"depth={snapshot.EstimatedDistance:F2}m confidence={snapshot.DistanceConfidence:F2} anchorPending={anchor.pending}");
            m_Models.Add(controller);
            return controller;
        }

        void OnDestroy()
        {
            if (m_Material == null) return;
            if (Application.isPlaying) Destroy(m_Material); else DestroyImmediate(m_Material);
        }

        static bool Valid(BodyPoseResult pose, int a, int b) => BodyPoseGeometry.HasPoint(pose, a) && BodyPoseGeometry.HasPoint(pose, b);

        static float WorldRadius(Camera camera, Vector2 center, float radius, float depth) => Vector3.Distance(
            PersonWorldPositionCalculator.ViewportPointToWorld(camera, center, depth),
            PersonWorldPositionCalculator.ViewportPointToWorld(camera, center + Vector2.up * radius, depth));

        void AddLimb(Transform root, BodyPoseResult pose, Camera camera, float depth, int first, int second, Color color)
        {
            if (!Valid(pose, first, second)) return;
            Vector3 start = PersonWorldPositionCalculator.ViewportPointToWorld(camera, pose.keypoints[first], depth);
            Vector3 end = PersonWorldPositionCalculator.ViewportPointToWorld(camera, pose.keypoints[second], depth);
            float width;
            if (BodyPoseGeometry.HasPoint(pose, 5) && BodyPoseGeometry.HasPoint(pose, 6))
                width = Vector3.Distance(
                    PersonWorldPositionCalculator.ViewportPointToWorld(camera, pose.keypoints[5], depth),
                    PersonWorldPositionCalculator.ViewportPointToWorld(camera, pose.keypoints[6], depth));
            else
                width = Vector3.Distance(
                    PersonWorldPositionCalculator.ViewportPointToWorld(camera, pose.screenRect.min, depth),
                    PersonWorldPositionCalculator.ViewportPointToWorld(camera, pose.screenRect.max, depth)) * 0.38f;
            width = Mathf.Clamp(width, 0.08f, 0.6f);
            float radius = Mathf.Clamp(width * limbRadiusRatio, 0.025f, 0.11f);
            if (Vector3.Distance(start, end) > 0.015f) AddSegment(root, $"Capsule {first}-{second}", start, end, radius, color);
        }

        void AddSegment(Transform root, string partName, Vector3 startWorld, Vector3 endWorld, float radius, Color color)
        {
            Vector3 start = root.InverseTransformPoint(startWorld);
            Vector3 end = root.InverseTransformPoint(endWorld);
            Vector3 axis = end - start;
            float length = axis.magnitude;
            if (length < 0.005f) return;
            var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsule.name = partName;
            capsule.transform.SetParent(root, false);
            capsule.transform.localPosition = (start + end) * 0.5f;
            capsule.transform.localRotation = Quaternion.FromToRotation(Vector3.up, axis / length);
            capsule.transform.localScale = new Vector3(radius * 2f, Mathf.Max(length, radius * 2f) * 0.5f, radius * 2f);
            var collider = capsule.GetComponent<Collider>();
            if (collider != null) collider.enabled = false;
            var renderer = capsule.GetComponent<Renderer>();
            renderer.sharedMaterial = m_Material;
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor("_Color", color);
            renderer.SetPropertyBlock(block);
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            m_CapsuleCount++;
        }
    }
}
