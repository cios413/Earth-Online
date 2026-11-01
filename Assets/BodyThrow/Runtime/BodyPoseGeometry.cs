using UnityEngine;

namespace EarthOL.BodyThrow
{
    /// <summary>Fits proxy outlines in viewport coordinates, accounting for the display aspect ratio.</summary>
    public static class BodyPoseGeometry
    {
        public const float MinimumScore = 0.45f;

        public static bool HasPoint(BodyPoseResult pose, int index)
        {
            if (!pose.IsValid || pose.keypointScores == null || pose.keypointScores.Length <= index ||
                pose.keypointScores[index] < MinimumScore) return false;
            Vector2 point = pose.keypoints[index];
            return !float.IsNaN(point.x) && !float.IsNaN(point.y) &&
                point.x >= 0f && point.x <= 1f && point.y >= 0f && point.y <= 1f;
        }

        public static bool TryFitTorso(BodyPoseResult pose, float aspect,
            out Vector2 shoulders, out Vector2 hips, out float radius)
        {
            shoulders = hips = Vector2.zero;
            radius = 0f;
            if (!HasPoint(pose, 5) || !HasPoint(pose, 6) || !HasPoint(pose, 11) || !HasPoint(pose, 12))
                return false;

            shoulders = (pose.keypoints[5] + pose.keypoints[6]) * 0.5f;
            hips = (pose.keypoints[11] + pose.keypoints[12]) * 0.5f;
            float shoulderWidth = Metric(pose.keypoints[5] - pose.keypoints[6], aspect).magnitude;
            float hipWidth = Metric(pose.keypoints[11] - pose.keypoints[12], aspect).magnitude;
            float height = Metric(shoulders - hips, aspect).magnitude;
            if (shoulderWidth < 0.015f || height < 0.025f || height < shoulderWidth * 0.3f ||
                height > shoulderWidth * 4f || hipWidth < shoulderWidth * 0.15f || hipWidth > shoulderWidth * 2f)
                return false;

            radius = Mathf.Min(Mathf.Max(shoulderWidth, hipWidth) * 0.46f, height * 0.45f);
            return true;
        }

        public static bool TryFitHead(BodyPoseResult pose, float aspect, out Vector2 center, out float radius)
        {
            center = Vector2.zero;
            radius = 0f;
            if (!pose.IsValid) return false;

            int faceCount = 0;
            Vector2 faceCenter = Vector2.zero;
            for (int i = 0; i <= 4; i++)
            {
                if (!HasPoint(pose, i)) continue;
                faceCenter += Metric(pose.keypoints[i], aspect);
                faceCount++;
            }
            if (faceCount < 2) return false;
            faceCenter /= faceCount;

            bool shouldersValid = HasPoint(pose, 5) && HasPoint(pose, 6);
            float shoulderWidth = shouldersValid
                ? Metric(pose.keypoints[5] - pose.keypoints[6], aspect).magnitude : 0f;
            Vector2 up = Vector2.up;
            if (shouldersValid)
            {
                Vector2 shoulderCenter = Metric((pose.keypoints[5] + pose.keypoints[6]) * 0.5f, aspect);
                Vector2 delta = faceCenter - shoulderCenter;
                if (delta.magnitude < shoulderWidth * 0.15f || delta.magnitude > shoulderWidth * 1.8f)
                    return false;
                up = delta.normalized;
            }

            bool ears = HasPoint(pose, 3) && HasPoint(pose, 4);
            bool eyes = HasPoint(pose, 1) && HasPoint(pose, 2);
            if (ears)
                radius = Metric(pose.keypoints[3] - pose.keypoints[4], aspect).magnitude * 0.60f;
            else if (eyes)
                radius = Metric(pose.keypoints[1] - pose.keypoints[2], aspect).magnitude * 1.35f;
            else if (shouldersValid && HasPoint(pose, 0))
                radius = shoulderWidth * 0.25f;
            else
                return false;

            if (radius < 0.006f) return false;
            if (shouldersValid && shoulderWidth > 0.015f)
                radius = Mathf.Clamp(radius, shoulderWidth * 0.16f, shoulderWidth * 0.38f);

            // Ears lie near the head center; eyes sit above it and the nose below it.
            Vector2 estimate = Vector2.zero;
            float weight = 0f;
            if (ears)
            {
                estimate += Metric((pose.keypoints[3] + pose.keypoints[4]) * 0.5f, aspect) * 2f;
                weight += 2f;
            }
            if (eyes)
            {
                estimate += Metric((pose.keypoints[1] + pose.keypoints[2]) * 0.5f, aspect) - up * radius * 0.15f;
                weight += 1f;
            }
            if (HasPoint(pose, 0))
            {
                estimate += Metric(pose.keypoints[0], aspect) + up * radius * 0.22f;
                weight += 1f;
            }
            if (weight <= 0f) return false;
            Vector2 metricCenter = estimate / weight;
            center = new Vector2(metricCenter.x / aspect, metricCenter.y);
            return center.x >= 0f && center.x <= 1f && center.y >= 0f && center.y <= 1f;
        }

        public static Vector2 Metric(Vector2 point, float aspect) => new Vector2(point.x * aspect, point.y);
    }
}
