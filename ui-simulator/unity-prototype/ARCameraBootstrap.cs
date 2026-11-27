using System.Collections;
using UnityEngine;
using UnityEngine.SpatialTracking;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Management;
using Unity.XR.CoreUtils;

namespace EarthOL
{
    /// <summary>
    /// ARCore 相机引导：运行时构建 AR Session + XR Origin + AR Camera（设为 MainCamera），
    /// 初始化 ARCore Loader，并创建 PokeInput 与 ModeOverlay（战斗/自由模式路由）。
    ///
    /// 取代 Vuforia：不再依赖 ImageTarget / VuforiaBehaviour；抓帧仍是把 AR 相机
    /// Render 到 RenderTexture（与 wang 分支的 BodyPoseTracker 同一条链路）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ARCameraBootstrap : MonoBehaviour
    {
        public Camera ARCamera { get; private set; }

        ARSession m_Session;
        XRGeneralSettings m_XRSettings;
        bool m_StartedXR;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void EnsureExists()
        {
            if (FindObjectOfType<ARCameraBootstrap>() != null) return;

            var go = new GameObject("EarthOL AR Camera Bootstrap");
            go.AddComponent<ARCameraBootstrap>();
        }

        void Awake()
        {
            BuildARCamera();
        }

        void Start()
        {
            EnsurePokeInput();
            EnsureModeOverlay();
            EnsureGameFlow();
            EnsureFreeModeSkin();
            EnsureRatFigure();
            EnsureBackpackUI();
            StartCoroutine(InitializeXR());
        }

        void BuildARCamera()
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
            // 编辑器里没有 ARCore 子系统：关掉追踪驱动，保留相机用于鼠标预览。
            if (Application.isEditor)
            {
                m_Session.enabled = false;
                cameraObject.GetComponent<ARCameraManager>().enabled = false;
                cameraObject.GetComponent<ARCameraBackground>().enabled = false;
                poseDriver.enabled = false;
            }
#endif
        }

        IEnumerator InitializeXR()
        {
#if UNITY_EDITOR
            if (Application.isEditor) yield break;
#endif
            m_XRSettings = XRGeneralSettings.Instance;
            if (m_XRSettings == null || m_XRSettings.Manager == null) yield break;

            if (!m_XRSettings.Manager.isInitializationComplete)
                yield return m_XRSettings.Manager.InitializeLoader();

            if (m_XRSettings.Manager.activeLoader == null)
            {
                Debug.LogWarning("[ARCore] ARCore Loader 初始化失败，请确认 Project Settings > XR Plug-in Management > Android 已勾选 ARCore。");
                yield break;
            }

            m_XRSettings.Manager.StartSubsystems();
            m_StartedXR = true;
            Debug.Log("[ARCore] ARCore 子系统已启动");
        }

        void EnsurePokeInput()
        {
            if (ARCamera == null) return;

            if (ARCamera.GetComponent<EarthOL.Deform.PokeInput>() == null)
            {
                ARCamera.gameObject.AddComponent<EarthOL.Deform.PokeInput>();
                Debug.Log("[ARCore] 已给 AR 相机补上 PokeInput");
            }
        }

        void EnsureModeOverlay()
        {
            if (ModeOverlay.Instance != null || FindObjectOfType<ModeOverlay>(true) != null) return;

            var go = new GameObject("EarthOL ModeOverlay");
            go.AddComponent<ModeOverlay>();
            Debug.Log("[ARCore] 已创建模式切换器（战斗/自由）");
        }

        void EnsureGameFlow()
        {
            if (GameFlow.Instance != null || FindObjectOfType<GameFlow>(true) != null) return;

            var go = new GameObject("EarthOL GameFlow");
            go.AddComponent<GameFlow>();
            Debug.Log("[ARCore] 已创建游戏流程状态机（开场/游玩/结算）");
        }

        void EnsureFreeModeSkin()
        {
            if (FreeModeSkin.Instance != null || FindObjectOfType<FreeModeSkin>(true) != null) return;

            var go = new GameObject("EarthOL FreeModeSkin");
            go.AddComponent<FreeModeSkin>();
            Debug.Log("[ARCore] 已创建自由模式皮肤切换器");
        }

        void EnsureRatFigure()
        {
            if (RatFigure.Instance != null || FindObjectOfType<RatFigure>(true) != null) return;

            var go = new GameObject("EarthOL RatFigure");
            go.AddComponent<RatFigure>();
            Debug.Log("[ARCore] 已创建老鼠干（常驻捏捏乐）控制器");
        }

        void EnsureBackpackUI()
        {
            if (BackpackUI.Instance != null || FindObjectOfType<BackpackUI>(true) != null) return;

            var go = new GameObject("EarthOL BackpackUI");
            go.AddComponent<BackpackUI>();
            Debug.Log("[ARCore] 已创建背包界面");
        }

        void OnDestroy()
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
