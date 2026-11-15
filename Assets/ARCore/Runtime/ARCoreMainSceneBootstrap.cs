using System.Collections;
using EarthOL.BodyThrow;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SpatialTracking;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Management;

namespace EarthOL.ARCore
{
    /// <summary>Person tracking, invisible body depth, throwing and camera deformation share one AR session.</summary>
    [DisallowMultipleComponent]
    public sealed class ARCoreMainSceneBootstrap : MonoBehaviour
    {
        public const string SceneName = "ARCoreMainScene";
        public const string DeformSceneName = SceneName;
        [Header("Person detection / tracking")]
        [SerializeField, Range(1f, 15f)] float personDetectionFps = 5f;
        [SerializeField, Range(5f, 30f)] float personFlowFps = 15f;
        [SerializeField, Range(.3f, 3f)] float personLostSeconds = 1f;
        [SerializeField] bool startPersonDetection = true;
        public Camera ARCamera { get; private set; }
        public PersonDeformationSurface PersonDeformer { get; private set; }
        public PersonTrackingManager PersonTracking { get; private set; }
        public PoseDetectionManager DetectionManager { get; private set; }
        public Cartoon2DCanvas CartoonCanvas { get; private set; }
        public PersonDistanceEstimator DistanceEstimator { get; private set; }
        public PersonClickController ClickController { get; private set; }
        public PersonShowcaseController ShowcaseController { get; private set; }
        public Person3DThrowController ThrowController { get; private set; }
        // Legacy editor-only API surface; the four active modes use ThrowController and PersonTracking.
        public BodyPoseTracker PoseTracker { get; private set; }
        public BodyProjectileSpawner Spawner { get; private set; }
        public GameMode CurrentMode { get; private set; }
        ARSession m_Session;
        XRGeneralSettings m_XRSettings;
        bool m_StartedXR;

        private IEnumerator Start()
        {
            BuildARCamera();
            BuildPersonSystems();
            PersonDeformer = gameObject.AddComponent<PersonDeformationSurface>();
            PersonDeformer.Configure(ARCamera, PersonTracking, DistanceEstimator, DetectionManager);
            PersonDeformer.enabled = false;
            var overlay = new GameObject("ARCore Mode Overlay").AddComponent<ARCoreModeOverlay>();
            overlay.Configure(this);
            var light = new GameObject("ARCore Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            yield return InitializeXR();
        }

        private void BuildARCamera()
        {
            var sessionObject = new GameObject("AR Session");
            m_Session = sessionObject.AddComponent<ARSession>();
            sessionObject.AddComponent<ARInputManager>();
            var originObject = new GameObject("XR Origin (ARCore)");
            var origin = originObject.AddComponent<XROrigin>();
            var offset = new GameObject("Camera Offset");
            offset.transform.SetParent(originObject.transform, false);
            var cameraObject = new GameObject("AR Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(offset.transform, false);
            ARCamera = cameraObject.AddComponent<Camera>();
            ARCamera.nearClipPlane = 0.01f;
            ARCamera.farClipPlane = 30f;
            ARCamera.clearFlags = CameraClearFlags.SolidColor;
            ARCamera.backgroundColor = new Color(0.07f, 0.09f, 0.13f);
            // Avoid allocating HDR/MSAA camera buffers on low-memory Android GPUs.
            ARCamera.allowHDR = false;
            ARCamera.allowMSAA = false;
            Application.targetFrameRate = 30;
            Debug.Log("[ARCore] Graphics API=" + SystemInfo.graphicsDeviceType +
                      ", GPU=" + SystemInfo.graphicsDeviceName);
            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<ARCameraManager>();
            cameraObject.AddComponent<ARCameraBackground>();
            var poseDriver = cameraObject.AddComponent<TrackedPoseDriver>();
            poseDriver.SetPoseSource(TrackedPoseDriver.DeviceType.GenericXRDevice, TrackedPoseDriver.TrackedPose.Center);
            poseDriver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            poseDriver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            origin.Camera = ARCamera;
            origin.CameraFloorOffsetObject = offset;
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;
            origin.CameraYOffset = 0f;
#if UNITY_EDITOR
            if (Application.isEditor)
            {
                m_Session.enabled = false;
                cameraObject.GetComponent<ARCameraManager>().enabled = false;
                cameraObject.GetComponent<ARCameraBackground>().enabled = false;
                poseDriver.enabled = false;
            }
#endif
        }

        private void BuildPersonSystems()
        {
            PersonTracking = gameObject.AddComponent<PersonTrackingManager>();
            PersonTracking.LostPersonSeconds = personLostSeconds;
            DetectionManager = gameObject.AddComponent<PoseDetectionManager>();
            DetectionManager.Configure(ARCamera, PersonTracking);
            bool lowMemoryMobile = Application.isMobilePlatform &&
                (SystemInfo.processorType.IndexOf("mt6893", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                 SystemInfo.graphicsDeviceName.IndexOf("Mali", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                 SystemInfo.systemMemorySize <= 6000);
            DetectionManager.DetectionFramesPerSecond = lowMemoryMobile ? Mathf.Min(personDetectionFps, 3.5f) : personDetectionFps;
            DetectionManager.FlowFramesPerSecond = lowMemoryMobile ? Mathf.Min(personFlowFps, 10f) : personFlowFps;
            DetectionManager.CaptureMaxSide = lowMemoryMobile ? 256 : 384;
            DetectionManager.AdaptiveMobileBudget = true;
            DetectionManager.AutoStart = startPersonDetection;
            var profiler = gameObject.AddComponent<PersonDetectionProfiler>();
            profiler.Configure(DetectionManager);
            if (lowMemoryMobile)
            {
                Application.targetFrameRate = 24;
                DetectionManager.DetectionFramesPerSecond = Mathf.Min(personDetectionFps, 2.5f);
                DetectionManager.FlowFramesPerSecond = Mathf.Min(personFlowFps, 8f);
                profiler.RecordDevelopmentProfile = false;
                Debug.Log("[PersonPerf] Mali/low-memory profile: 24fps, capture=256px, detection<=2.5Hz, flow<=8Hz, profiler off.");
            }
            CartoonCanvas = gameObject.AddComponent<Cartoon2DCanvas>();
            CartoonCanvas.Configure(ARCamera, PersonTracking);
            DistanceEstimator = gameObject.AddComponent<PersonDistanceEstimator>();
            DistanceEstimator.Configure(ARCamera);
            ClickController = gameObject.AddComponent<PersonClickController>();
            ClickController.Configure(ARCamera, PersonTracking, CartoonCanvas);
            ShowcaseController = gameObject.AddComponent<PersonShowcaseController>();
            ShowcaseController.Configure(ARCamera, PersonTracking, DetectionManager);
            ThrowController = gameObject.AddComponent<Person3DThrowController>();
            ThrowController.Configure(ARCamera, PersonTracking, Resources.Load<BodyProjectileCatalog>("Throwables/Catalog"));
            DetectionManager.DetectionStopping += ResetFeatureEffects;
        }

        public void SetMode(GameMode mode)
        {
            bool deform = mode == GameMode.Deform;
            CurrentMode = mode;
            if (ClickController != null) ClickController.enabled = mode == GameMode.Sticker;
            if (ShowcaseController != null) ShowcaseController.SetInteractionEnabled(mode == GameMode.Showcase);
            if (ThrowController != null) ThrowController.SetInteractionEnabled(mode == GameMode.Throw);
            if (PersonDeformer != null) PersonDeformer.enabled = deform;
        }

        void ResetFeatureEffects()
        {
            PersonDeformer?.ResetEffects();
            ClickController?.ResetInput();
            CartoonCanvas?.ClearSelection();
            ShowcaseController?.ResetForDetectionStop();
            ThrowController?.ClearEffects();
        }

        private IEnumerator InitializeXR()
        {
#if UNITY_EDITOR
            if (Application.isEditor) yield break;
#endif
            m_XRSettings = XRGeneralSettings.Instance;
            if (m_XRSettings == null || m_XRSettings.Manager == null) yield break;
            if (!m_XRSettings.Manager.isInitializationComplete) yield return m_XRSettings.Manager.InitializeLoader();
            if (m_XRSettings.Manager.activeLoader == null)
            {
                Debug.LogWarning("[ARCore] ARCore Loader 初始化失败。");
                yield break;
            }
            m_XRSettings.Manager.StartSubsystems();
            m_StartedXR = true;
        }

        private void OnDestroy()
        {
            if (DetectionManager != null) DetectionManager.DetectionStopping -= ResetFeatureEffects;
            if (m_StartedXR && m_XRSettings != null && m_XRSettings.Manager != null)
            {
                m_XRSettings.Manager.StopSubsystems();
                m_XRSettings.Manager.DeinitializeLoader();
                m_StartedXR = false;
            }
        }
    }
}
