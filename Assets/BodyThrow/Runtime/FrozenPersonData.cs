using UnityEngine;

namespace EarthOL.BodyThrow
{
    /// <summary>Immutable-by-convention pose captured exactly when the user selects a tracked person.</summary>
    public sealed class FrozenPersonData
    {
        public readonly int PersonId;
        public readonly Rect ViewportBounds;
        readonly Vector2[] m_Keypoints;
        readonly float[] m_KeypointConfidence;
        public Vector2[] Keypoints => (Vector2[])m_Keypoints.Clone();
        public float[] KeypointConfidence => (float[])m_KeypointConfidence.Clone();
        public readonly float DetectionConfidence;
        public readonly PersonFacing Facing;
        public readonly float Timestamp;
        public readonly Vector2 ClickViewport;
        public readonly float EstimatedDistance;
        public readonly float DistanceConfidence;

        public FrozenPersonData(PersonPoseData pose, Vector2 clickViewport, float distance, float distanceConfidence)
        {
            PersonId = pose.personId;
            ViewportBounds = pose.screenRect;
            m_Keypoints = (Vector2[])pose.keypoints.Clone();
            m_KeypointConfidence = (float[])pose.keypointConfidence.Clone();
            DetectionConfidence = pose.detectionConfidence;
            Facing = pose.facing;
            Timestamp = pose.timestamp;
            ClickViewport = clickViewport;
            EstimatedDistance = distance;
            DistanceConfidence = distanceConfidence;
        }

        public BodyPoseResult ToPose() => new BodyPoseResult
        {
            screenRect = ViewportBounds,
            keypoints = (Vector2[])m_Keypoints.Clone(),
            keypointScores = (float[])m_KeypointConfidence.Clone(),
            confidence = DetectionConfidence
        };
    }
}
