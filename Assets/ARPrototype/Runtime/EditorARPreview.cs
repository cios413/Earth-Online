using UnityEngine;
using UnityEngine.SpatialTracking;
using UnityEngine.XR.ARFoundation;

namespace ARPrototype
{
    /// <summary>Uses the same AR camera in Editor, with device-only components disabled.</summary>
    [DefaultExecutionOrder(-10000)]
    public sealed class EditorARPreview : MonoBehaviour
    {
        [SerializeField] private ARSession session;
        [SerializeField] private Camera arCamera;

        public void Configure(ARSession arSession, Camera camera)
        {
            session = arSession;
            arCamera = camera;
        }

        private void Awake()
        {
#if UNITY_EDITOR
            if (session != null)
                session.enabled = false;
            if (arCamera == null)
                return;
            arCamera.GetComponent<ARCameraManager>().enabled = false;
            arCamera.GetComponent<ARCameraBackground>().enabled = false;
            arCamera.GetComponent<TrackedPoseDriver>().enabled = false;
            arCamera.clearFlags = CameraClearFlags.SolidColor;
            arCamera.backgroundColor = new Color(0.07f, 0.09f, 0.13f);
#endif
        }
    }
}
