using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace EarthOL.BodyThrow
{
    /// <summary>Monocular pinhole estimate; measured AR projection takes precedence over FOV fallback.</summary>
    [DisallowMultipleComponent]
    public sealed class PersonDistanceEstimator : MonoBehaviour
    {
        [SerializeField, Range(1.1f, 2.2f)] private float assumedPersonHeight = 1.7f;
        [SerializeField, Range(0.3f, 10f)] private float minimumDistance = 0.4f;
        [SerializeField, Range(1f, 15f)] private float maximumDistance = 8f;
        Camera m_Camera;
        ARCameraManager m_CameraManager;
        public float AssumedPersonHeight { get => assumedPersonHeight; set => assumedPersonHeight = Mathf.Clamp(value, 1.1f, 2.2f); }

        public void Configure(Camera camera)
        {
            m_Camera = camera;
            m_CameraManager = camera != null ? camera.GetComponent<ARCameraManager>() : null;
        }

        public float Estimate(PersonPoseData pose, out float confidence)
        {
            confidence = 0f;
            if (pose == null || m_Camera == null) return 0f;
            float pixelHeight = Mathf.Max(1f, pose.screenRect.height * m_Camera.pixelHeight);
            float focalPixels = 0f;
            bool intrinsics = false;
            // AR projection is the intrinsics after display rotation AND viewport
            // crop. Raw sensor focal pixels cannot be scaled by height alone on
            // a portrait fill-cropped camera image.
            if (Mathf.Abs(m_Camera.projectionMatrix.m11) > 0.001f)
            {
                focalPixels = Mathf.Abs(m_Camera.projectionMatrix.m11) * m_Camera.pixelHeight * 0.5f;
                intrinsics = m_CameraManager != null;
            }
            if (focalPixels <= 0f && m_CameraManager != null && m_CameraManager.TryGetIntrinsics(out var values) &&
                values.resolution.y > 0 && values.resolution.x > 0)
            {
                bool rotated = Mathf.Abs((float)values.resolution.y / values.resolution.x - m_Camera.aspect) <
                    Mathf.Abs((float)values.resolution.x / values.resolution.y - m_Camera.aspect);
                float sensorHeight = rotated ? values.resolution.x : values.resolution.y;
                float focal = rotated ? values.focalLength.x : values.focalLength.y;
                focalPixels = focal * m_Camera.pixelHeight / sensorHeight;
                intrinsics = focalPixels > 0f;
            }
            // The AR projection matrix is already adjusted for the displayed orientation/crop.
            if (focalPixels <= 0f && Mathf.Abs(m_Camera.projectionMatrix.m11) > 0.001f)
                focalPixels = Mathf.Abs(m_Camera.projectionMatrix.m11) * m_Camera.pixelHeight * 0.5f;
            if (focalPixels <= 0f)
            {
                float fov = Mathf.Clamp(m_Camera.fieldOfView, 20f, 140f) * Mathf.Deg2Rad;
                focalPixels = m_Camera.pixelHeight / (2f * Mathf.Tan(fov * 0.5f));
            }

            float distance = assumedPersonHeight * focalPixels / pixelHeight;
            var poseView = pose.AsPoseView();
            bool faceVisible = BodyPoseGeometry.HasPoint(poseView, 0) ||
                BodyPoseGeometry.HasPoint(poseView, 1) || BodyPoseGeometry.HasPoint(poseView, 2);
            bool feetVisible = BodyPoseGeometry.HasPoint(poseView, 15) && BodyPoseGeometry.HasPoint(poseView, 16);
            float completeness = 0.55f + (faceVisible ? 0.20f : 0f) + (feetVisible ? 0.25f : 0f);
            if (pose.screenRect.yMin < 0.025f || pose.screenRect.yMax > 0.975f) completeness *= 0.55f;
            confidence = Mathf.Clamp01(pose.detectionConfidence * completeness * (intrinsics ? 1f : 0.85f));
            return Mathf.Clamp(distance, minimumDistance, maximumDistance);
        }
    }
}
