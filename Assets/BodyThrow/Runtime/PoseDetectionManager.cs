using System;
using System.Collections;
using System.Diagnostics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.XR.ARFoundation;
using Debug = UnityEngine.Debug;

namespace EarthOL.BodyThrow
{
    /// <summary>One camera stream, low-frequency scheduled YOLO and higher-frequency grayscale patch tracking.</summary>
    [DisallowMultipleComponent]
    public sealed class PoseDetectionManager : MonoBehaviour
    {
        [SerializeField, Range(1f, 15f)] float detectionFramesPerSecond = 5f;
        [SerializeField, Range(5f, 30f)] float flowFramesPerSecond = 15f;
        [SerializeField, Range(128, 640)] int captureMaxSide = 384;
        [SerializeField, Range(1, 10)] int maxPeople = 6;
        [SerializeField] bool startOnAwake = true;
        [SerializeField] bool adaptiveMobileBudget = true;
        Camera m_Camera;
        ARCameraBackground m_Background;
        PersonTrackingManager m_Tracking;
        BodyPoseDetector m_Detector;
        RenderTexture m_Target;
        Texture2D m_Frame;
        Color32[] m_Pixels;
        byte[] m_PreviousGray, m_CurrentGray;
        int m_Width, m_Height, m_GrayWidth, m_GrayHeight;
        ScreenOrientation m_Orientation;
        float m_NextDetection, m_NextFlow, m_LastFlow, m_LastDetectionStart = -1f;
        float m_DetectionStart, m_TotalInference, m_TotalTracking, m_NextLog;
        float m_FrameDeltaAverage, m_AdaptiveRate = 5f, m_NextAdaptiveCheck;
        bool m_Enabled, m_HasPreviousGray, m_Paused;
        IEnumerator m_Inference;
        int m_TrackingSamples;
        ProfilerRecorder m_GcRecorder;
        static readonly ProfilerMarker CaptureMarker = new ProfilerMarker("Person.CameraCapture");
        static readonly ProfilerMarker InferMarker = new ProfilerMarker("Person.YOLO.ScheduleStep");
        static readonly ProfilerMarker TrackMarker = new ProfilerMarker("Person.Tracking");
        static readonly ProfilerMarker FlowMarker = new ProfilerMarker("Person.PatchFlow");

        public PersonDetectionState State { get; private set; } = PersonDetectionState.Stopped;
        public bool DetectionEnabled => m_Enabled;
        public float DetectionFramesPerSecond { get => detectionFramesPerSecond; set => detectionFramesPerSecond = Mathf.Clamp(value, 1f, 15f); }
        public float FlowFramesPerSecond { get => flowFramesPerSecond; set => flowFramesPerSecond = Mathf.Clamp(value, 5f, 30f); }
        public int CaptureMaxSide { get => captureMaxSide; set => captureMaxSide = Mathf.Clamp(value, 128, 640); }
        public bool AdaptiveMobileBudget { get => adaptiveMobileBudget; set => adaptiveMobileBudget = value; }
        public float EffectiveDetectionFramesPerSecond => m_AdaptiveRate;
        public bool AutoStart { get => startOnAwake; set => startOnAwake = value; }
        public int DetectionCount { get; private set; }
        public int CaptureCount { get; private set; }
        public int TensorInputCount { get; private set; }
        public float AverageInferenceMilliseconds => DetectionCount > 0 ? m_TotalInference / DetectionCount : 0f;
        public float AverageTrackingMilliseconds => m_TrackingSamples > 0 ? m_TotalTracking / m_TrackingSamples : 0f;
        public float LastTrackingMilliseconds { get; private set; }
        public float MeasuredDetectionFramesPerSecond { get; private set; }
        public long ManagedMemoryBytes => Profiler.GetMonoUsedSizeLong();
        public long FrameGcBytes => m_GcRecorder.Valid ? m_GcRecorder.LastValue : -1;
        public int TrackedPersonCount => m_Tracking != null ? m_Tracking.Count : 0;
        public PersonTrackingManager Tracking => m_Tracking;

        public void Configure(Camera camera, PersonTrackingManager tracking)
        {
            m_Camera = camera;
            m_Background = camera != null ? camera.GetComponent<ARCameraBackground>() : null;
            m_Tracking = tracking;
        }

        void Start() { if (startOnAwake) StartDetection(); }

        public void StartDetection()
        {
            if (m_Enabled) return;
            m_Enabled = true;
            m_Paused = false;
            m_AdaptiveRate = detectionFramesPerSecond;
            m_NextDetection = m_NextFlow = 0f;
            m_Tracking?.ResumeCandidates(Time.realtimeSinceStartup);
            m_Detector = new BodyPoseDetector();
            State = PersonDetectionState.LoadingModel;
            if (!m_GcRecorder.Valid) m_GcRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
            var load = m_Detector.Load();
            while (load.MoveNext()) { }
            State = m_Detector.Ready ? PersonDetectionState.WaitingForCamera : PersonDetectionState.Error;
            if (!m_Detector.Ready) m_Enabled = false;
            Debug.Log($"[PersonDetection] Start: YOLO cap={detectionFramesPerSecond:F1}Hz flow={flowFramesPerSecond:F0}Hz");
        }

        public void StopDetection()
        {
            bool wasRunning = m_Enabled;
            m_Enabled = false;
            DisposeInference();
            m_Detector?.Dispose(); m_Detector = null;
            ReleaseCapture();
            if (wasRunning) m_Tracking?.SuspendPeople();
            State = PersonDetectionState.Stopped;
            Debug.Log("[PersonDetection] Stop: capture, tensor creation, inference, decode, flow and tracking stopped; anchors retained.");
        }

        public void SetInferencePaused(bool paused)
        {
            // Compatibility with Legacy deformation. New 2D tracking must continue during gestures.
            m_Paused = false;
        }

        public bool ShouldStartInference(float now) => m_Enabled && m_Inference == null && !m_Paused && now >= m_NextDetection;

        void Update()
        {
            if (!m_Enabled || State == PersonDetectionState.Error) return;
            float now = Time.realtimeSinceStartup;
            float frameDelta = Time.unscaledDeltaTime;
            if (frameDelta > 0f) m_FrameDeltaAverage = m_FrameDeltaAverage <= 0f ? frameDelta : Mathf.Lerp(m_FrameDeltaAverage, frameDelta, 0.08f);
            long started = Stopwatch.GetTimestamp();
            using (TrackMarker.Auto()) m_Tracking?.Tick(now);
            LastTrackingMilliseconds = Elapsed(started);
            m_TotalTracking += LastTrackingMilliseconds; m_TrackingSamples++;
            if (m_Camera == null || m_Background == null || !m_Background.backgroundRenderingEnabled || m_Background.material == null)
            { State = PersonDetectionState.WaitingForCamera; return; }
            UpdateMobileBudget(now);

            try
            {
                bool detectionDue = ShouldStartInference(now);
                bool flowDue = now >= m_NextFlow && TrackedPersonCount > 0;
                if ((detectionDue || flowDue) && Capture())
                {
                    if (flowDue)
                    {
                        UpdateFlow(now);
                        m_NextFlow = now + 1f / Mathf.Max(5f, flowFramesPerSecond);
                    }
                    if (detectionDue)
                    {
                        m_NextDetection = now + 1f / Mathf.Max(1f, m_AdaptiveRate);
                        if (m_LastDetectionStart >= 0f)
                        {
                            float fps = 1f / Mathf.Max(0.001f, now - m_LastDetectionStart);
                            MeasuredDetectionFramesPerSecond = MeasuredDetectionFramesPerSecond > 0f ?
                                Mathf.Lerp(MeasuredDetectionFramesPerSecond, fps, 0.2f) : fps;
                        }
                        m_LastDetectionStart = m_DetectionStart = now;
                        m_Inference = m_Detector.DetectScheduled(m_Pixels, m_Frame.width, m_Frame.height, maxPeople, OnDetection);
                        TensorInputCount++;
                    }
                }
                if (m_Inference != null)
                {
                    bool pending;
                    using (InferMarker.Auto()) pending = m_Inference.MoveNext();
                    if (!pending) DisposeInference();
                }
                State = PersonDetectionState.Running;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                StopDetection(); State = PersonDetectionState.Error;
            }
            if (now >= m_NextLog)
            {
                m_NextLog = now + 5f;
                Debug.Log($"[PersonPerf] detection={DetectionCount} capture={CaptureCount} tensor={TensorInputCount} " +
                    $"actualHz={MeasuredDetectionFramesPerSecond:F2} inferWallMs={AverageInferenceMilliseconds:F1} " +
                    $"trackingMs={AverageTrackingMilliseconds:F3} flow={m_Tracking.FlowUpdateCount} " +
                    $"tracks={TrackedPersonCount} monoMB={ManagedMemoryBytes / 1048576f:F1} frameGC={FrameGcBytes}");
            }
        }

        void UpdateMobileBudget(float now)
        {
            if (!adaptiveMobileBudget || now < m_NextAdaptiveCheck) return;
            m_NextAdaptiveCheck = now + 3f;
            float frameMs = m_FrameDeltaAverage * 1000f;
            if (frameMs > 34f || AverageInferenceMilliseconds > 230f) m_AdaptiveRate = Mathf.Max(1.5f, m_AdaptiveRate - 0.5f);
            else if (frameMs < 29f && AverageInferenceMilliseconds > 0f && AverageInferenceMilliseconds < 150f)
                m_AdaptiveRate = Mathf.Min(detectionFramesPerSecond, m_AdaptiveRate + 0.25f);
        }

        void OnDetection(BodyPoseResult[] result)
        {
            float now = Time.realtimeSinceStartup;
            DetectionCount++;
            m_TotalInference += (now - m_DetectionStart) * 1000f;
            // Compensate the bounded capture-to-result latency with each track's learned velocity.
            using (TrackMarker.Auto()) m_Tracking.UpdateDetections(result, now, now - m_DetectionStart);
        }

        bool Capture()
        {
            using (CaptureMarker.Auto())
            {
                int w = m_Camera.pixelWidth, h = m_Camera.pixelHeight;
                if (w <= 0 || h <= 0) return false;
                if (m_Target == null || w != m_Width || h != m_Height || m_Orientation != Screen.orientation)
                {
                    if (m_Inference != null) return false;
                    bool changed = m_Target != null;
                    ReleaseCapture();
                    if (changed) m_Tracking.ClearPeople();
                    m_Width = w; m_Height = h; m_Orientation = Screen.orientation;
                    float scale = captureMaxSide / (float)Mathf.Max(w, h);
                    int cw = Mathf.Max(1, Mathf.RoundToInt(w * scale)), ch = Mathf.Max(1, Mathf.RoundToInt(h * scale));
                    m_Target = new RenderTexture(cw, ch, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "PersonCameraCapture" };
                    m_Target.Create(); m_Frame = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
                    m_Pixels = new Color32[cw * ch];
                    float grayScale = 192f / Mathf.Max(cw, ch);
                    m_GrayWidth = Mathf.Max(16, Mathf.RoundToInt(cw * grayScale));
                    m_GrayHeight = Mathf.Max(16, Mathf.RoundToInt(ch * grayScale));
                    m_PreviousGray = new byte[m_GrayWidth * m_GrayHeight]; m_CurrentGray = new byte[m_PreviousGray.Length];
                }
                var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
                try
                {
                    GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                    Graphics.Blit(null, m_Target, m_Background.material);
                    RenderTexture.active = m_Target;
                    m_Frame.ReadPixels(new Rect(0, 0, m_Target.width, m_Target.height), 0, 0, false);
                    m_Frame.GetRawTextureData<Color32>().CopyTo(m_Pixels);
                    CaptureCount++;
                    return true;
                }
                finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; }
            }
        }

        void UpdateFlow(float now)
        {
            using (FlowMarker.Auto())
            {
                for (int y = 0; y < m_GrayHeight; y++) for (int x = 0; x < m_GrayWidth; x++)
                {
                    var pixel = m_Pixels[(y * m_Frame.height / m_GrayHeight) * m_Frame.width + x * m_Frame.width / m_GrayWidth];
                    m_CurrentGray[y * m_GrayWidth + x] = (byte)((pixel.r * 77 + pixel.g * 150 + pixel.b * 29) >> 8);
                }
                float dt = now - m_LastFlow;
                if (m_HasPreviousGray && dt > 0f && dt < 0.3f)
                    m_Tracking.ApplyImageFlow(m_PreviousGray, m_CurrentGray, m_GrayWidth, m_GrayHeight, dt);
                var swap = m_PreviousGray; m_PreviousGray = m_CurrentGray; m_CurrentGray = swap;
                m_LastFlow = now; m_HasPreviousGray = true;
                // Periodic YOLO always corrects flow; failure requests the next allowed detection (never bypassing the cap).
            }
        }

        void DisposeInference()
        {
            (m_Inference as IDisposable)?.Dispose(); m_Inference = null;
        }
        void ReleaseCapture()
        {
            if (m_Target != null) { m_Target.Release(); DestroyOwned(m_Target); m_Target = null; }
            if (m_Frame != null) { DestroyOwned(m_Frame); m_Frame = null; }
            m_Pixels = null; m_PreviousGray = m_CurrentGray = null; m_HasPreviousGray = false;
        }
        static float Elapsed(long start) => (float)((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
        static void DestroyOwned(UnityEngine.Object obj) { if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj); }
        void OnApplicationPause(bool paused) { if (paused) { DisposeInference(); m_HasPreviousGray = false; } }
        void OnDisable() => StopDetection();
        void OnDestroy() { StopDetection(); m_GcRecorder.Dispose(); }
    }
}
