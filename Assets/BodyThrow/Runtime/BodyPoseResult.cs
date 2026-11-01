using UnityEngine;

namespace EarthOL.BodyThrow
{
    public struct BodyPoseResult
    {
        public Rect screenRect;
        public Vector2[] keypoints;
        public float[] keypointScores;
        public float confidence;

        public bool IsValid => keypoints != null && keypoints.Length >= 17 && confidence > 0f;
    }
}
