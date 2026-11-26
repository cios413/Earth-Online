using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
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
        AsyncGPUReadbackRequest m_Readback;
        byte[] m_PreviousGray, m_CurrentGray;
        int m_Width, m_Height, m_GrayWidth, m_GrayHeight;
        ScreenOrientation m_Orientation;
        float m_NextDetection, m_NextFlow, m_LastFlow, m_LastDetectionStart = -1f;
        float m_DetectionStart, m_TotalInference, m_TotalTracking, m_NextLog;
        float m_FrameDeltaAverage, m_AdaptiveRate = 5f, m_NextAdaptiveCheck;
        float m_LastCaptureWorkMs, m_LastInferenceWorkMs, m_NextFrameSpikeLog;
        float m_MaxFrameMilliseconds, m_MaxScheduleMilliseconds;
        bool m_Enabled, m_HasPreviousGray, m_Paused;
        bool m_ShowcaseBuilding;
        bool m_ReadbackPending, m_ReadbackFlow, m_UseAsyncReadback;
        float m_ReadbackTimestamp, m_ReadbackStarted;
        float m_ReadyFrameTimestamp = -1f, m_ReadyFrameAvailable;
        float m_DetectionCaptureMs, m_DetectionQueueMs, m_DetectionInferenceStarted;
        Quaternion m_ReadyFrameRotation;
        const float MaxInferenceFrameAge = .18f;
        Quaternion m_ReadbackRotation, m_LastFlowRotation;
        Quaternion m_TrackingRotation, m_DetectionRotation;
        bool m_HasTrackingRotation;
        float m_LastFlowCameraDegrees;
        int m_AsyncReadbackFailures;
        IEnumerator m_Inference;
        int m_TrackingSamples;
        ProfilerRecorder m_GcRecorder;
        StreamWriter m_Diagnostics;
        bool m_DiagnosticsUnavailable;
        float m_NextDiagnosticFlush;
        string m_DiagnosticSession;
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
        internal bool InferenceRunning => m_Inference != null;
        internal bool CapturePending => m_ReadbackPending;
        public float AverageInferenceMilliseconds => DetectionCount > 0 ? m_TotalInference / DetectionCount : 0f;
        public float AverageTrackingMilliseconds => m_TrackingSamples > 0 ? m_TotalTracking / m_TrackingSamples : 0f;
        public float AverageReadbackLatencyMilliseconds { get; private set; }
        public float LastTrackingMilliseconds { get; private set; }
        public float MeasuredDetectionFramesPerSecond { get; private set; }
        public long ManagedMemoryBytes => Profiler.GetMonoUsedSizeLong();
        public long FrameGcBytes => m_GcRecorder.Valid ? m_GcRecorder.LastValue : -1;
        public int TrackedPersonCount => m_Tracking != null ? m_Tracking.Count : 0;
        public PersonTrackingManager Tracking => m_Tracking;
        public event Action DetectionStopping;

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
            m_HasTrackingRotation = false;
            m_AdaptiveRate = detectionFramesPerSecond;
            m_NextDetection = m_NextFlow = 0f;
            m_Tracking?.ResumeTracks(Time.realtimeSinceStartup);
            m_Detector = new BodyPoseDetector();
            State = PersonDetectionState.LoadingModel;
            if (!m_GcRecorder.Valid) m_GcRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
            var load = m_Detector.Load();
            while (load.MoveNext()) { }
            State = m_Detector.Ready ? PersonDetectionState.WaitingForCamera : PersonDetectionState.Error;
            if (!m_Detector.Ready) m_Enabled = false;
            RecordDiagnostic(m_Enabled ? "start" : "model_error");
            Debug.Log($"[PersonDetection] Start: YOLO cap={detectionFramesPerSecond:F1}Hz flow={flowFramesPerSecond:F0}Hz");
        }

        public void StopDetection()
        {
            m_Enabled = false;
            DetectionStopping?.Invoke();
            DisposeInference();
            m_Detector?.Dispose(); m_Detector = null;
            m_Tracking?.PauseTracks(Time.realtimeSinceStartup);
            ReleaseCapture();
            State = PersonDetectionState.Stopped;
            if (m_Diagnostics != null) RecordDiagnostic("stop", flush: true);
            Debug.Log("[PersonDetection] Stop: inference stopped and live tracks suspended.");
        }

        public void SetInferencePaused(bool paused)
        {
            // Legacy deformation gestures keep 2D recognition active.
            m_Paused = false;
        }

        internal void SetShowcaseBuilding(bool building)
        {
            if (m_ShowcaseBuilding == building) return;
            m_ShowcaseBuilding = building;
            m_ReadyFrameTimestamp = -1f;
            if (!building)
            {
                m_NextDetection = Time.realtimeSinceStartup + .10f;
                m_NextFlow = m_NextDetection;
            }
        }

        public bool ShouldStartInference(float now) => m_Enabled && m_Inference == null &&
            !m_Paused && !m_ShowcaseBuilding && now >= m_NextDetection;

        void Update()
        {
            if (!m_Enabled || State == PersonDetectionState.Error) return;
            float now = Time.realtimeSinceStartup;
            float frameDelta = Time.unscaledDeltaTime;
            if (frameDelta > 0f) m_FrameDeltaAverage = m_FrameDeltaAverage <= 0f ? frameDelta : Mathf.Lerp(m_FrameDeltaAverage, frameDelta, 0.08f);
            m_MaxFrameMilliseconds = Mathf.Max(m_MaxFrameMilliseconds, frameDelta * 1000f);
            if (frameDelta >= .06f && now >= m_NextFrameSpikeLog)
            {
                m_NextFrameSpikeLog = now + .5f;
                RecordDiagnostic(m_ShowcaseBuilding ? "showcase_frame_spike" : "frame_spike",
                    elapsedMs: frameDelta * 1000f, captureMs: m_LastCaptureWorkMs, inferenceMs: m_LastInferenceWorkMs);
            }
            m_LastCaptureWorkMs = m_LastInferenceWorkMs = 0f;
            long started = Stopwatch.GetTimestamp();
            if (m_Camera != null)
            {
                Quaternion rotation = m_Camera.transform.rotation;
                if (m_HasTrackingRotation && Quaternion.Angle(m_TrackingRotation, rotation) > .02f)
                    m_Tracking?.Reproject(RotationProjection(m_Camera.projectionMatrix, m_TrackingRotation, rotation));
                m_TrackingRotation = rotation;
                m_HasTrackingRotation = true;
            }
            using (TrackMarker.Auto()) m_Tracking?.Tick(now);
            LastTrackingMilliseconds = Elapsed(started);
            m_TotalTracking += LastTrackingMilliseconds; m_TrackingSamples++;
            if (m_Camera == null || m_Background == null || !m_Background.backgroundRenderingEnabled || m_Background.material == null)
            { State = PersonDetectionState.WaitingForCamera; return; }
            UpdateMobileBudget(now);

            try
            {
                bool captureCompleted = m_ReadbackPending && m_Readback.done;
                if (captureCompleted)
                {
                    long captureStart = Stopwatch.GetTimestamp();
                    CompleteCapture();
                    m_LastCaptureWorkMs = Elapsed(captureStart);
                }
                now = Time.realtimeSinceStartup;
                if (ShouldStartInference(now) && m_ReadyFrameTimestamp > m_LastDetectionStart &&
                    (now - m_ReadyFrameTimestamp <= MaxInferenceFrameAge || now - m_ReadyFrameAvailable <= .03f))
                    StartReadyInference(now);
                if (m_Inference != null)
                {
                    bool pending;
                    long scheduleStart = Stopwatch.GetTimestamp();
                    using (InferMarker.Auto()) pending = m_Inference.MoveNext();
                    m_LastInferenceWorkMs = Elapsed(scheduleStart);
                    m_MaxScheduleMilliseconds = Mathf.Max(m_MaxScheduleMilliseconds, m_LastInferenceWorkMs);
                    if (!pending) DisposeInference();
                }
                now = Time.realtimeSinceStartup;
                bool underPressure = adaptiveMobileBudget && m_FrameDeltaAverage > .040f;
                bool flowDue = now >= m_NextFlow && TrackedPersonCount > 0;
                bool prefetch = !underPressure && m_UseAsyncReadback && now >= m_NextDetection - .10f &&
                    (m_ReadyFrameTimestamp <= m_LastDetectionStart || now - m_ReadyFrameTimestamp >= .10f);
                bool captureBudgetAvailable = !underPressure ||
                    (m_Inference == null && !captureCompleted && m_LastInferenceWorkMs == 0f);
                if (captureBudgetAvailable && !m_ShowcaseBuilding && !m_ReadbackPending &&
                    (ShouldStartInference(now) || flowDue || prefetch) &&
                    EnsureCaptureTarget())
                {
                    m_ReadbackTimestamp = now;
                    m_ReadbackRotation = m_Camera.transform.rotation;
                    m_ReadbackFlow = flowDue;
                    m_ReadbackStarted = now;
                    float flowRate = m_UseAsyncReadback && !underPressure
                        ? flowFramesPerSecond : Mathf.Min(flowFramesPerSecond, 8f);
                    if (flowDue) m_NextFlow = now + 1f / flowRate;
                    long captureStart = Stopwatch.GetTimestamp();
                    RequestCapture();
                    m_LastCaptureWorkMs += Elapsed(captureStart);
                }
                State = PersonDetectionState.Running;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                RecordDiagnostic("detection_error", flush: true);
                StopDetection(); State = PersonDetectionState.Error;
            }
            if (now >= m_NextLog)
            {
                m_NextLog = now + 5f;
                Debug.Log($"[PersonPerf] detection={DetectionCount} capture={CaptureCount} tensor={TensorInputCount} " +
                    $"actualHz={MeasuredDetectionFramesPerSecond:F2} inferWallMs={AverageInferenceMilliseconds:F1} " +
                    $"readbackLatencyMs={AverageReadbackLatencyMilliseconds:F2} asyncReadback={m_UseAsyncReadback} " +
                    $"frameMaxMs={m_MaxFrameMilliseconds:F1} scheduleMaxMs={m_MaxScheduleMilliseconds:F1} " +
                    $"trackingMs={AverageTrackingMilliseconds:F3} flow={m_Tracking.FlowUpdateCount} " +
                    $"tracks={TrackedPersonCount} monoMB={ManagedMemoryBytes / 1048576f:F1} frameGC={FrameGcBytes}");
                m_MaxFrameMilliseconds = m_MaxScheduleMilliseconds = 0f;
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
            if (m_Camera != null && Quaternion.Angle(m_DetectionRotation, m_TrackingRotation) > .02f)
            {
                var projection = RotationProjection(m_Camera.projectionMatrix, m_DetectionRotation, m_TrackingRotation);
                for (int i = 0; i < result.Length; i++)
                {
                    var detection = result[i];
                    if (!detection.IsValid) continue;
                    detection.screenRect = PersonTrackingManager.ReprojectRect(projection, detection.screenRect);
                    for (int k = 0; k < 17; k++)
                        detection.keypoints[k] = PersonTrackingManager.ReprojectPoint(projection, detection.keypoints[k]);
                    result[i] = detection;
                }
            }
            // Compensate the bounded capture-to-result latency with each track's learned velocity.
            using (TrackMarker.Auto()) m_Tracking.UpdateDetections(result, now, now - m_DetectionStart);
            RecordDiagnostic("pose", detections: result.Length, elapsedMs: (now - m_DetectionStart) * 1000f,
                captureMs: m_DetectionCaptureMs, queueMs: m_DetectionQueueMs,
                inferenceMs: Mathf.Max(0f, (now - m_DetectionInferenceStarted) * 1000f - m_Detector.LastDecodeMilliseconds),
                decodeMs: m_Detector.LastDecodeMilliseconds);
        }

        static Matrix4x4 RotationProjection(Matrix4x4 projection, Quaternion from, Quaternion to)
        {
            var cameraSpace = Matrix4x4.Scale(new Vector3(1, 1, -1));
            return projection * cameraSpace * Matrix4x4.Rotate(Quaternion.Inverse(to) * from) * cameraSpace * projection.inverse;
        }

        [Serializable]
        sealed class RecognitionDiagnostic
        {
            public string utc, stage, session, poseModel;
            public int sequence, detections, personId, width, height, vertices, foregroundPixels, flowRejections, flowTracking, flowMatches, singleRecoveries;
            public int[] visibleIds;
            public bool asyncReadback;
            public float elapsedMs, captureMs, queueMs, inferenceMs, decodeMs, segmentationMs, meshMs, depth, frameMaxMs, scheduleMaxMs, readbackLatencyMs, flowCameraDegrees;
        }

        internal void RecordDiagnostic(string stage, int detections = -1, float elapsedMs = 0f,
            int personId = -1, int width = 0, int height = 0, int vertices = 0, float depth = 0f,
            int foregroundPixels = 0, float captureMs = 0f, float segmentationMs = 0f, float meshMs = 0f, bool flush = false,
            float queueMs = 0f, float inferenceMs = 0f, float decodeMs = 0f)
        {
            if (!Application.isMobilePlatform || m_DiagnosticsUnavailable) return;
            try
            {
                if (m_Diagnostics == null)
                {
                    string path = Path.Combine(Application.persistentDataPath, "person-recognition.jsonl");
                    m_DiagnosticSession ??= Guid.NewGuid().ToString("N");
                    m_Diagnostics = new StreamWriter(path, true);
                }
                var people = m_Tracking?.Persons;
                var ids = new int[people?.Count ?? 0];
                for (int i = 0; i < ids.Length; i++) ids[i] = people[i].personId;
                m_Diagnostics.WriteLine(JsonUtility.ToJson(new RecognitionDiagnostic
                {
                    utc = DateTime.UtcNow.ToString("O"), stage = stage, session = m_DiagnosticSession,
                    poseModel = m_Detector?.ModelName, sequence = DetectionCount,
                    detections = detections, visibleIds = ids, personId = personId, width = width, height = height,
                    vertices = vertices, depth = depth, foregroundPixels = foregroundPixels, elapsedMs = elapsedMs,
                    captureMs = captureMs, queueMs = queueMs, inferenceMs = inferenceMs, decodeMs = decodeMs,
                    segmentationMs = segmentationMs, meshMs = meshMs,
                    asyncReadback = m_UseAsyncReadback, frameMaxMs = m_MaxFrameMilliseconds,
                    scheduleMaxMs = m_MaxScheduleMilliseconds, readbackLatencyMs = AverageReadbackLatencyMilliseconds,
                    flowRejections = m_Tracking?.FlowRejectionCount ?? 0,
                    flowTracking = m_Tracking?.FlowTrackingCount ?? 0, flowMatches = m_Tracking?.FlowMatchCount ?? 0,
                    singleRecoveries = m_Tracking?.RecentSingleRecoveryCount ?? 0,
                    flowCameraDegrees = m_LastFlowCameraDegrees
                }));
                float now = Time.realtimeSinceStartup;
                if (flush || now >= m_NextDiagnosticFlush)
                {
                    m_Diagnostics.Flush();
                    m_NextDiagnosticFlush = now + 2f;
                    if (m_Diagnostics.BaseStream.Length >= 4 * 1024 * 1024)
                    {
                        string path = Path.Combine(Application.persistentDataPath, "person-recognition.jsonl");
                        m_Diagnostics.Dispose();
                        m_Diagnostics = null;
                        File.Copy(path, path + ".previous", true);
                        m_Diagnostics = new StreamWriter(path, false);
                    }
                }
            }
            catch (IOException exception)
            {
                m_DiagnosticsUnavailable = true;
                Debug.LogWarning("[PersonDiagnostics] " + exception.Message);
            }
            catch (UnauthorizedAccessException exception)
            {
                m_DiagnosticsUnavailable = true;
                Debug.LogWarning("[PersonDiagnostics] " + exception.Message);
            }
        }

        bool EnsureCaptureTarget()
        {
            using (CaptureMarker.Auto())
            {
                int w = m_Camera.pixelWidth, h = m_Camera.pixelHeight;
                if (w <= 0 || h <= 0) return false;
                if (m_Target == null || w != m_Width || h != m_Height || m_Orientation != Screen.orientation)
                {
                    if (m_ReadbackPending) return false;
                    if (m_Inference != null) return false;
                    bool changed = m_Target != null;
                    ReleaseCapture();
                    if (changed) m_Tracking.ClearPeople();
                    m_Width = w; m_Height = h; m_Orientation = Screen.orientation;
                    float scale = captureMaxSide / (float)Mathf.Max(w, h);
                    int cw = Mathf.Max(1, Mathf.RoundToInt(w * scale)), ch = Mathf.Max(1, Mathf.RoundToInt(h * scale));
                    m_Target = new RenderTexture(cw, ch, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "PersonCameraCapture" };
                    m_Target.Create();
                    m_Frame = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
                    m_Pixels = new Color32[cw * ch];
                    m_UseAsyncReadback = SystemInfo.supportsAsyncGPUReadback;
                    float grayScale = 192f / Mathf.Max(cw, ch);
                    m_GrayWidth = Mathf.Max(16, Mathf.RoundToInt(cw * grayScale));
                    m_GrayHeight = Mathf.Max(16, Mathf.RoundToInt(ch * grayScale));
                    m_PreviousGray = new byte[m_GrayWidth * m_GrayHeight]; m_CurrentGray = new byte[m_PreviousGray.Length];
                }
                return m_Target != null;
            }
        }

        void RequestCapture()
        {
            bool srgb = GL.sRGBWrite;
            var previous = RenderTexture.active;
            try
            {
                GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                Graphics.Blit(null, m_Target, m_Background.material);
            }
            finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; }

            if (m_UseAsyncReadback)
            {
                try
                {
                    m_Readback = AsyncGPUReadback.Request(m_Target, 0, TextureFormat.RGBA32);
                    m_ReadbackPending = true;
                    return;
                }
                catch (Exception exception)
                {
                    m_AsyncReadbackFailures++;
                    Debug.LogWarning("[PersonCapture] Async readback request failed: " + exception.Message);
                    m_UseAsyncReadback = false;
                }
            }

            CaptureSynchronously();
            ConsumeCapture();
        }

        void CompleteCapture()
        {
            using (CaptureMarker.Auto())
            {
                if (m_Readback.hasError)
                {
                    m_AsyncReadbackFailures++;
                    Debug.LogWarning("[PersonCapture] Async GPU readback returned an error.");
                    m_ReadbackPending = false;
                    if (m_AsyncReadbackFailures >= 3) m_UseAsyncReadback = false;
                    return;
                }

                var data = m_Readback.GetData<Color32>();
                if (data.Length != m_Pixels.Length)
                {
                    m_AsyncReadbackFailures++;
                    m_ReadbackPending = false;
                    if (m_AsyncReadbackFailures >= 3) m_UseAsyncReadback = false;
                    Debug.LogWarning("[PersonCapture] Async GPU readback size did not match the camera frame.");
                    return;
                }
                data.CopyTo(m_Pixels);
                m_ReadbackPending = false;
                m_AsyncReadbackFailures = 0;
                CaptureCount++;
                float latency = (Time.realtimeSinceStartup - m_ReadbackStarted) * 1000f;
                AverageReadbackLatencyMilliseconds = AverageReadbackLatencyMilliseconds <= 0f
                    ? latency
                    : Mathf.Lerp(AverageReadbackLatencyMilliseconds, latency, 0.2f);
                ConsumeCapture();
            }
        }

        void CaptureSynchronously()
        {
            using (CaptureMarker.Auto())
            {
                var previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = m_Target;
                    m_Frame.ReadPixels(new Rect(0, 0, m_Target.width, m_Target.height), 0, 0, false);
                    m_Frame.GetRawTextureData<Color32>().CopyTo(m_Pixels);
                    CaptureCount++;
                }
                finally { RenderTexture.active = previous; }
            }
        }

        void ConsumeCapture()
        {
            m_ReadyFrameTimestamp = m_ReadbackTimestamp;
            m_ReadyFrameRotation = m_ReadbackRotation;
            m_ReadyFrameAvailable = Time.realtimeSinceStartup;
            if (m_ReadbackFlow) UpdateFlow(m_ReadbackTimestamp);
        }

        void StartReadyInference(float now)
        {
            float timestamp = m_ReadyFrameTimestamp;
            if (m_LastDetectionStart >= 0f)
            {
                float fps = 1f / Mathf.Max(0.001f, timestamp - m_LastDetectionStart);
                MeasuredDetectionFramesPerSecond = MeasuredDetectionFramesPerSecond > 0f
                    ? Mathf.Lerp(MeasuredDetectionFramesPerSecond, fps, 0.2f) : fps;
            }
            m_LastDetectionStart = m_DetectionStart = timestamp;
            m_DetectionRotation = m_ReadyFrameRotation;
            m_DetectionCaptureMs = (m_ReadyFrameAvailable - timestamp) * 1000f;
            m_DetectionQueueMs = (now - m_ReadyFrameAvailable) * 1000f;
            m_DetectionInferenceStarted = now;
            m_NextDetection = timestamp + 1f / Mathf.Max(1f, m_AdaptiveRate);
            m_Inference = m_Detector.DetectScheduled(m_Pixels, m_Target.width, m_Target.height, maxPeople, OnDetection);
            TensorInputCount++;
        }

        public bool CapturePersonPhoto(Rect viewportBounds, out Texture2D photo, out Rect photoViewportBounds)
        {
            photo = null;
            photoViewportBounds = default;
            if (m_Camera == null || m_Background == null || m_Background.material == null ||
                !m_Background.backgroundRenderingEnabled || viewportBounds.width <= 0f || viewportBounds.height <= 0f)
                return false;

            int sourceWidth = m_Camera.pixelWidth;
            int sourceHeight = m_Camera.pixelHeight;
            if (sourceWidth <= 0 || sourceHeight <= 0) return false;
            bool lowMemory = SystemInfo.graphicsDeviceName.IndexOf("Mali", StringComparison.OrdinalIgnoreCase) >= 0 ||
                SystemInfo.systemMemorySize <= 6000;
            int maxSide = lowMemory ? 768 : 1024;
            float scale = Mathf.Min(1f, maxSide / (float)Mathf.Max(sourceWidth, sourceHeight));
            int captureWidth = Mathf.Max(1, Mathf.RoundToInt(sourceWidth * scale));
            int captureHeight = Mathf.Max(1, Mathf.RoundToInt(sourceHeight * scale));
            var target = new RenderTexture(captureWidth, captureHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            { name = "ShowcasePersonPhotoCapture" };
            var fullFrame = new Texture2D(captureWidth, captureHeight, TextureFormat.RGBA32, false, false)
            { name = "ShowcasePersonPhotoSource" };
            try
            {
                RenderTexture previous = RenderTexture.active;
                bool previousSrgb = GL.sRGBWrite;
                try
                {
                    GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                    Graphics.Blit(null, target, m_Background.material);
                    RenderTexture.active = target;
                    fullFrame.ReadPixels(new Rect(0, 0, captureWidth, captureHeight), 0, 0, false);
                    fullFrame.Apply(false, false);
                }
                finally
                {
                    RenderTexture.active = previous;
                    GL.sRGBWrite = previousSrgb;
                }

                float padX = viewportBounds.width * 0.15f;
                float padY = viewportBounds.height * 0.10f;
                photoViewportBounds = Rect.MinMaxRect(
                    Mathf.Clamp01(viewportBounds.xMin - padX), Mathf.Clamp01(viewportBounds.yMin - padY),
                    Mathf.Clamp01(viewportBounds.xMax + padX), Mathf.Clamp01(viewportBounds.yMax + padY));
                int x = Mathf.Clamp(Mathf.FloorToInt(photoViewportBounds.xMin * captureWidth), 0, captureWidth - 1);
                int y = Mathf.Clamp(Mathf.FloorToInt(photoViewportBounds.yMin * captureHeight), 0, captureHeight - 1);
                int width = Mathf.Clamp(Mathf.CeilToInt(photoViewportBounds.xMax * captureWidth) - x, 1, captureWidth - x);
                int height = Mathf.Clamp(Mathf.CeilToInt(photoViewportBounds.yMax * captureHeight) - y, 1, captureHeight - y);
                photoViewportBounds = new Rect(x / (float)captureWidth, y / (float)captureHeight,
                    width / (float)captureWidth, height / (float)captureHeight);
                var sourcePixels = fullFrame.GetPixels32();
                var croppedPixels = new Color32[width * height];
                for (int row = 0; row < height; row++)
                    System.Array.Copy(sourcePixels, (y + row) * captureWidth + x, croppedPixels, row * width, width);

                photo = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
                { name = "ShowcasePersonPhoto", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                photo.SetPixels32(croppedPixels);
                photo.Apply(false, false);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[PersonShowcase] 人物照片捕获失败：" + exception.Message);
                if (photo != null) Destroy(photo);
                photo = null;
                return false;
            }
            finally
            {
                target.Release();
                Destroy(target);
                Destroy(fullFrame);
            }
        }

        internal IEnumerator CapturePersonPhotoScheduled(Rect viewportBounds, Action<Texture2D, Rect> completed)
        {
            if (m_Camera == null || m_Background == null || m_Background.material == null ||
                !m_Background.backgroundRenderingEnabled || viewportBounds.width <= 0f || viewportBounds.height <= 0f)
            {
                completed?.Invoke(null, default);
                yield break;
            }

            int sourceWidth = m_Camera.pixelWidth, sourceHeight = m_Camera.pixelHeight;
            if (sourceWidth <= 0 || sourceHeight <= 0)
            {
                completed?.Invoke(null, default);
                yield break;
            }
            bool lowMemory = SystemInfo.graphicsDeviceName.IndexOf("Mali", StringComparison.OrdinalIgnoreCase) >= 0 ||
                SystemInfo.systemMemorySize <= 6000;
            int maxSide = lowMemory ? 768 : 1024;
            float scale = Mathf.Min(1f, maxSide / (float)Mathf.Max(sourceWidth, sourceHeight));
            int captureWidth = Mathf.Max(1, Mathf.RoundToInt(sourceWidth * scale));
            int captureHeight = Mathf.Max(1, Mathf.RoundToInt(sourceHeight * scale));
            var target = new RenderTexture(captureWidth, captureHeight, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "ShowcasePersonPhotoCapture" };
            Texture2D photo = null;
            Rect photoBounds = default;
            AsyncGPUReadbackRequest request = default;
            bool requested = false;
            try
            {
                target.Create();
                bool srgb = GL.sRGBWrite;
                RenderTexture previous = RenderTexture.active;
                try
                {
                    GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                    Graphics.Blit(null, target, m_Background.material);
                }
                finally
                {
                    RenderTexture.active = previous;
                    GL.sRGBWrite = srgb;
                }

                if (!SystemInfo.supportsAsyncGPUReadback)
                {
                    var previousPixels = RenderTexture.active;
                    var frame = new Texture2D(captureWidth, captureHeight, TextureFormat.RGBA32, false, false);
                    try
                    {
                        RenderTexture.active = target;
                        frame.ReadPixels(new Rect(0, 0, captureWidth, captureHeight), 0, 0, false);
                        frame.Apply(false, false);
                        var crop = CropPersonPhoto(frame.GetPixels32(), captureWidth, captureHeight,
                            viewportBounds, out photoBounds);
                        photo = CreatePhotoTexture(crop, Mathf.RoundToInt(photoBounds.width * captureWidth),
                            Mathf.RoundToInt(photoBounds.height * captureHeight));
                    }
                    finally
                    {
                        RenderTexture.active = previousPixels;
                        DestroyOwned(frame);
                    }
                }
                else
                {
                    request = AsyncGPUReadback.Request(target, 0, TextureFormat.RGBA32);
                    requested = true;
                    while (!request.done) yield return null;
                    if (request.hasError)
                    {
                        completed?.Invoke(null, default);
                        yield break;
                    }
                    var cropped = CropPersonPhoto(request.GetData<Color32>(), captureWidth, captureHeight,
                        viewportBounds, out photoBounds);
                    int width = Mathf.Max(1, Mathf.RoundToInt(photoBounds.width * captureWidth));
                    int height = Mathf.Max(1, Mathf.RoundToInt(photoBounds.height * captureHeight));
                    photo = CreatePhotoTexture(cropped, width, height);
                }
            }
            finally
            {
                if (requested && !request.done) request.WaitForCompletion();
                target.Release();
                DestroyOwned(target);
            }
            completed?.Invoke(photo, photoBounds);
        }

        static Color32[] CropPersonPhoto(Color32[] source, int sourceWidth, int sourceHeight,
            Rect viewportBounds, out Rect photoViewportBounds)
        {
            GetPhotoCrop(viewportBounds, sourceWidth, sourceHeight, out photoViewportBounds,
                out int x, out int y, out int width, out int height);
            var cropped = new Color32[width * height];
            for (int row = 0; row < height; row++)
                Array.Copy(source, (y + row) * sourceWidth + x, cropped, row * width, width);
            return cropped;
        }

        static Color32[] CropPersonPhoto(Unity.Collections.NativeArray<Color32> source, int sourceWidth,
            int sourceHeight, Rect viewportBounds, out Rect photoViewportBounds)
        {
            GetPhotoCrop(viewportBounds, sourceWidth, sourceHeight, out photoViewportBounds,
                out int x, out int y, out int width, out int height);
            var cropped = new Color32[width * height];
            for (int row = 0; row < height; row++)
            for (int column = 0; column < width; column++)
                cropped[row * width + column] = source[(y + row) * sourceWidth + x + column];
            return cropped;
        }

        static void GetPhotoCrop(Rect viewportBounds, int sourceWidth, int sourceHeight,
            out Rect photoViewportBounds, out int x, out int y, out int width, out int height)
        {
            float padX = viewportBounds.width * .15f, padY = viewportBounds.height * .10f;
            photoViewportBounds = Rect.MinMaxRect(
                Mathf.Clamp01(viewportBounds.xMin - padX), Mathf.Clamp01(viewportBounds.yMin - padY),
                Mathf.Clamp01(viewportBounds.xMax + padX), Mathf.Clamp01(viewportBounds.yMax + padY));
            x = Mathf.Clamp(Mathf.FloorToInt(photoViewportBounds.xMin * sourceWidth), 0, sourceWidth - 1);
            y = Mathf.Clamp(Mathf.FloorToInt(photoViewportBounds.yMin * sourceHeight), 0, sourceHeight - 1);
            width = Mathf.Clamp(Mathf.CeilToInt(photoViewportBounds.xMax * sourceWidth) - x, 1, sourceWidth - x);
            height = Mathf.Clamp(Mathf.CeilToInt(photoViewportBounds.yMax * sourceHeight) - y, 1, sourceHeight - y);
            photoViewportBounds = new Rect(x / (float)sourceWidth, y / (float)sourceHeight,
                width / (float)sourceWidth, height / (float)sourceHeight);
        }

        static Texture2D CreatePhotoTexture(Color32[] pixels, int width, int height)
        {
            var photo = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
            {
                name = "ShowcasePersonPhoto",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            photo.SetPixels32(pixels);
            photo.Apply(false, false);
            return photo;
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
                if (m_HasPreviousGray && dt > 0f)
                {
                    m_LastFlowCameraDegrees = Quaternion.Angle(m_LastFlowRotation, m_ReadbackRotation);
                    if (dt >= .3f)
                        m_Tracking.RejectImageFlow();
                    else if (m_LastFlowCameraDegrees < 45f)
                        m_Tracking.ApplyImageFlow(m_PreviousGray, m_CurrentGray, m_GrayWidth, m_GrayHeight, dt,
                            RotationProjection(m_Camera.projectionMatrix, m_TrackingRotation, m_LastFlowRotation),
                            RotationProjection(m_Camera.projectionMatrix, m_TrackingRotation, m_ReadbackRotation));
                }
                var swap = m_PreviousGray; m_PreviousGray = m_CurrentGray; m_CurrentGray = swap;
                m_LastFlow = now; m_HasPreviousGray = true;
                m_LastFlowRotation = m_ReadbackRotation;
            }
        }

        void DisposeInference()
        {
            (m_Inference as IDisposable)?.Dispose(); m_Inference = null;
        }
        void ReleaseCapture()
        {
            m_ReadyFrameTimestamp = -1f;
            if (m_ReadbackPending)
            {
                m_Readback.WaitForCompletion();
                m_ReadbackPending = false;
            }
            if (m_Target != null) { m_Target.Release(); DestroyOwned(m_Target); m_Target = null; }
            if (m_Frame != null) { DestroyOwned(m_Frame); m_Frame = null; }
            m_Pixels = null; m_PreviousGray = m_CurrentGray = null; m_HasPreviousGray = false;
        }
        static float Elapsed(long start) => (float)((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
        static void DestroyOwned(UnityEngine.Object obj) { if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj); }
        void OnApplicationPause(bool paused)
        {
            if (paused && m_Diagnostics != null) RecordDiagnostic("background", flush: true);
            if (!m_Enabled) return;
            if (paused)
            {
                DisposeInference();
                if (m_ReadbackPending)
                {
                    m_Readback.WaitForCompletion();
                    m_ReadbackPending = false;
                }
                m_Tracking?.PauseTracks(Time.realtimeSinceStartup);
                m_HasPreviousGray = false;
                m_ReadyFrameTimestamp = -1f;
            }
            else
            {
                m_Tracking?.ResumeTracks(Time.realtimeSinceStartup);
                m_NextDetection = m_NextFlow = 0f;
            }
        }
        void OnDisable() => StopDetection();
        void OnDestroy()
        {
            StopDetection();
            m_GcRecorder.Dispose();
            try { m_Diagnostics?.Dispose(); }
            catch (IOException exception) { Debug.LogWarning("[PersonDiagnostics] " + exception.Message); }
        }
    }
}
