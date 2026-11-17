using UnityEngine;

namespace EarthOL.BodyThrow
{
    /// <summary>Conservative yaw estimate from COCO face visibility and nose offset.</summary>
    public static class PersonFacingDetector
    {
        public static PersonFacing Estimate(PersonPoseData pose)
        {
            if (pose == null || pose.keypoints == null || pose.keypointConfidence == null) return PersonFacing.Unknown;
            bool nose = Visible(pose, 0), leftEye = Visible(pose, 1), rightEye = Visible(pose, 2);
            bool leftEar = Visible(pose, 3), rightEar = Visible(pose, 4);
            bool shoulders = Visible(pose, 5) && Visible(pose, 6);
            if (nose && shoulders)
            {
                float shoulderX = (pose.keypoints[5].x + pose.keypoints[6].x) * 0.5f;
                float shoulderWidth = Mathf.Abs(pose.keypoints[5].x - pose.keypoints[6].x);
                float offset = pose.keypoints[0].x - shoulderX;
                if (shoulderWidth > 0.01f && Mathf.Abs(offset) > shoulderWidth * 0.20f)
                    return offset < 0f ? PersonFacing.Left : PersonFacing.Right;
                if (leftEye && rightEye) return PersonFacing.Front;
            }
            if (!nose && !leftEye && !rightEye && shoulders &&
                (leftEar || rightEar || Visible(pose, 11) && Visible(pose, 12))) return PersonFacing.Back;
            return PersonFacing.Unknown;
        }

        static bool Visible(PersonPoseData pose, int index) => index < pose.keypointConfidence.Length &&
            pose.keypointConfidence[index] >= BodyPoseGeometry.MinimumScore;
    }
}
