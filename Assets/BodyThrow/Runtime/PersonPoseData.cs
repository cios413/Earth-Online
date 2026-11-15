using UnityEngine;

namespace EarthOL.BodyThrow
{
    public enum PersonFacing { Unknown, Front, Back, Left, Right }

    /// <summary>One person's pose, always expressed in bottom-left-origin camera viewport coordinates.</summary>
    public sealed class PersonPoseData
    {
        public int personId;
        public Rect screenRect;
        public Vector2[] keypoints;
        public float[] keypointConfidence;
        public float detectionConfidence;
        public float trackingConfidence;
        public float timestamp;
        public PersonFacing facing;
        public bool IsValid => keypoints != null && keypoints.Length >= 17 && screenRect.width > 0f && screenRect.height > 0f;

        public static PersonPoseData FromDetection(BodyPoseResult result, float timestamp)
        {
            if (!result.IsValid) return null;
            var data = new PersonPoseData
            {
                personId = -1,
                screenRect = result.screenRect,
                keypoints = (Vector2[])result.keypoints.Clone(),
                keypointConfidence = (float[])result.keypointScores.Clone(),
                detectionConfidence = result.confidence,
                trackingConfidence = result.confidence,
                timestamp = timestamp,
                facing = PersonFacing.Unknown
            };
            return data;
        }

        public BodyPoseResult ToBodyPoseResult()
        {
            return new BodyPoseResult
            {
                screenRect = screenRect,
                keypoints = (Vector2[])keypoints.Clone(),
                keypointScores = (float[])keypointConfidence.Clone(),
                confidence = Mathf.Max(0.001f, trackingConfidence)
            };
        }

        public BodyPoseResult AsPoseView() => new BodyPoseResult
        {
            screenRect = screenRect,
            keypoints = keypoints,
            keypointScores = keypointConfidence,
            confidence = Mathf.Max(0.001f, trackingConfidence)
        };
    }
}
