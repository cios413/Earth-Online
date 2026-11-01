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
        public Camera ARCamera { get; private set; }
        public BodyProxyRig BodyRig { get; private set; }
        public BodyProjectileSpawner Spawner { get; private set; }
        public BodyPoseTracker PoseTracker { get; private set; }
        public PersonDeformationSurface PersonDeformer { get; private set; }
        ARSession m_Session;
        XRGeneralSettings m_XRSettings;
        bool m_StartedXR;

        private IEnumerator Start()
        {
            BuildARCamera();
            BuildBodyRig();
            BuildProjectileSpawner();
            PersonDeformer = gameObject.AddComponent<PersonDeformationSurface>();
            PersonDeformer.Configure(ARCamera, BodyRig, PoseTracker);
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

        private void BuildBodyRig()
        {
            var rigObject = new GameObject("BodyProxyRig");
            rigObject.SetActive(false);
            BodyRig = rigObject.AddComponent<BodyProxyRig>();
            rigObject.SetActive(true);
            BodyRig.BuildDefaultRig();
            PoseTracker = gameObject.AddComponent<BodyPoseTracker>();
            PoseTracker.Configure(ARCamera, BodyRig);
        }

        private void BuildProjectileSpawner()
        {
            Spawner = new GameObject("Body Projectile Spawner").AddComponent<BodyProjectileSpawner>();
            Spawner.ARCamera = ARCamera;
            Spawner.SelectedTarget = BodyRig.TargetRoot;
            Spawner.ConfigureCatalog(Resources.Load<BodyProjectileCatalog>("Throwables/Catalog"));
            Spawner.PoseTracker = PoseTracker;
            Spawner.ThrowSpeed = 6f;
            Spawner.SpawnDistance = 0.2f;
            int layer = LayerMask.NameToLayer("Projectile");
            if (layer >= 0) Physics.IgnoreLayerCollision(layer, layer, true);
        }

        public void SetMode(GameMode mode)
        {
            bool deform = mode == GameMode.Deform;
            if (BodyRig != null) BodyRig.gameObject.SetActive(true);
            if (Spawner != null) Spawner.enabled = !deform;
            if (PoseTracker != null) PoseTracker.SetDeformationMode(deform);
            if (PersonDeformer != null) PersonDeformer.enabled = deform;
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
            if (m_StartedXR && m_XRSettings != null && m_XRSettings.Manager != null)
            {
                m_XRSettings.Manager.StopSubsystems();
                m_XRSettings.Manager.DeinitializeLoader();
                m_StartedXR = false;
            }
        }
    }
}
