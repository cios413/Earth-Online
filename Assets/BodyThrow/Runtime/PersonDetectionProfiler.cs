using System;
using System.IO;
using UnityEngine;
using UnityEngine.Profiling;

namespace EarthOL.BodyThrow
{
    /// <summary>Development APK: one actual Unity binary profile and timing/memory report after model warm-up.</summary>
    public sealed class PersonDetectionProfiler : MonoBehaviour
    {
        [SerializeField] bool recordDevelopmentProfile = true;
        [SerializeField, Range(5f, 30f)] float sampleSeconds = 8f;
        PoseDetectionManager m_Detection;
        readonly FrameTiming[] m_Timings = new FrameTiming[1];
        float m_Started = -1f;
        bool m_Done;
        int m_ObservedSession = -1;
        int m_Frames, m_TimingFrames, m_GpuFrames;
        int m_StartDetectionCount, m_StartCaptureCount, m_StartTensorInputCount, m_StartTrackingSamples;
        double m_CpuMilliseconds, m_GpuMilliseconds;
        float m_StartInferenceMilliseconds, m_StartTrackingMilliseconds;
        long m_GcBytes, m_MaxMemory;
        string m_ProfilePath, m_DiagnosticSession;

        [Serializable] sealed class Report
        {
            public string unity, device, gpu, graphicsAPI, binaryProfile, diagnosticSession;
            public int detectionSession;
            public float seconds, configuredDetectionHz, actualDetectionHz, inferenceWallMs, trackingCpuMs;
            public int renderedFrames, detectionsInWindow, captureCount, tensorInputCount, nativeFrameTimingSamples, gpuTimingSamples;
            public double cpuFrameMs, gpuFrameMs;
            public long totalFrameGcBytes, peakAllocatedMemoryBytes;
            public string limitations;
        }

        public void Configure(PoseDetectionManager detection) => m_Detection = detection;
        public bool RecordDevelopmentProfile { get => recordDevelopmentProfile; set => recordDevelopmentProfile = value; }

        void Update()
        {
#if DEVELOPMENT_BUILD
            if (Application.isEditor || !recordDevelopmentProfile || m_Detection == null) return;
            if (m_ObservedSession != m_Detection.DetectionSessionCount)
            {
                if (m_Started >= 0f && !m_Done) Finish();
                ResetSample();
                m_ObservedSession = m_Detection.DetectionSessionCount;
            }

            if (!m_Detection.DetectionEnabled)
            {
                if (m_Started >= 0f && !m_Done) Finish();
                return;
            }
            if (m_Done) return;

            if (m_Started < 0f)
            {
                if (m_Detection.DetectionCount < 3) return;
                m_Started = Time.realtimeSinceStartup;
                m_StartDetectionCount = m_Detection.DetectionCount;
                m_StartCaptureCount = m_Detection.CaptureCount;
                m_StartTensorInputCount = m_Detection.TensorInputCount;
                m_StartTrackingSamples = m_Detection.TrackingSampleCount;
                m_StartInferenceMilliseconds = m_Detection.TotalInferenceMilliseconds;
                m_StartTrackingMilliseconds = m_Detection.TotalTrackingMilliseconds;
                m_DiagnosticSession = m_Detection.DiagnosticSessionId;
                m_ProfilePath = Path.Combine(Application.persistentDataPath,
                    "person-system-profiler-" + m_Detection.DetectionSessionCount + ".raw");
                Profiler.logFile = m_ProfilePath;
                Profiler.enableBinaryLog = true;
                Profiler.enabled = true;
                Debug.Log("[PersonProfile] Recording Unity Profiler for " + sampleSeconds + "s: " + m_ProfilePath);
            }
            m_Frames++;
            m_GcBytes += Math.Max(0L, m_Detection.FrameGcBytes);
            m_MaxMemory = Math.Max(m_MaxMemory, Profiler.GetTotalAllocatedMemoryLong());
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, m_Timings) > 0)
            {
                m_CpuMilliseconds += m_Timings[0].cpuFrameTime;
                m_TimingFrames++;
                if (m_Timings[0].gpuFrameTime > 0)
                {
                    m_GpuMilliseconds += m_Timings[0].gpuFrameTime;
                    m_GpuFrames++;
                }
            }
            if (Time.realtimeSinceStartup - m_Started >= sampleSeconds) Finish();
#endif
        }

        void ResetSample()
        {
            m_Started = -1f;
            m_Done = false;
            m_Frames = m_TimingFrames = m_GpuFrames = 0;
            m_StartDetectionCount = m_StartCaptureCount = m_StartTensorInputCount = m_StartTrackingSamples = 0;
            m_StartInferenceMilliseconds = m_StartTrackingMilliseconds = 0f;
            m_CpuMilliseconds = m_GpuMilliseconds = 0;
            m_GcBytes = m_MaxMemory = 0;
            m_ProfilePath = m_DiagnosticSession = null;
        }

        void Finish()
        {
            if (m_Started < 0f || m_Done) return;
            m_Done = true;
            Profiler.enabled = false;
            Profiler.enableBinaryLog = false;
            float seconds = Time.realtimeSinceStartup - m_Started;
            int detections = m_Detection.DetectionCount - m_StartDetectionCount;
            int trackingSamples = m_Detection.TrackingSampleCount - m_StartTrackingSamples;
            float inferenceMilliseconds = m_Detection.TotalInferenceMilliseconds - m_StartInferenceMilliseconds;
            float trackingMilliseconds = m_Detection.TotalTrackingMilliseconds - m_StartTrackingMilliseconds;
            var report = new Report
            {
                unity = Application.unityVersion, device = SystemInfo.deviceModel,
                gpu = SystemInfo.graphicsDeviceName, graphicsAPI = SystemInfo.graphicsDeviceType.ToString(),
                binaryProfile = m_ProfilePath, diagnosticSession = m_DiagnosticSession,
                detectionSession = m_ObservedSession,
                seconds = seconds, configuredDetectionHz = m_Detection.DetectionFramesPerSecond,
                actualDetectionHz = detections / Mathf.Max(.001f, seconds),
                inferenceWallMs = detections > 0 ? inferenceMilliseconds / detections : 0f,
                trackingCpuMs = trackingSamples > 0 ? trackingMilliseconds / trackingSamples : 0f,
                renderedFrames = m_Frames, detectionsInWindow = detections,
                captureCount = m_Detection.CaptureCount - m_StartCaptureCount,
                tensorInputCount = m_Detection.TensorInputCount - m_StartTensorInputCount, nativeFrameTimingSamples = m_TimingFrames,
                gpuTimingSamples = m_GpuFrames, cpuFrameMs = m_TimingFrames > 0 ? m_CpuMilliseconds / m_TimingFrames : -1,
                gpuFrameMs = m_GpuFrames > 0 ? m_GpuMilliseconds / m_GpuFrames : -1,
                totalFrameGcBytes = m_GcBytes, peakAllocatedMemoryBytes = m_MaxMemory,
                limitations = "Development build with profiling overhead; inferenceWall includes yielded frames; GC includes the whole frame. " +
                    "CPU/GPU frame timing -1 means the device/API did not expose it. Scene/person count must be considered when comparing runs."
            };
            string path = Path.Combine(Application.persistentDataPath, "person-system-performance.json");
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            Debug.Log($"[PersonProfile] Complete actualHz={report.actualDetectionHz:F2} inferWall={report.inferenceWallMs:F1}ms " +
                $"track={report.trackingCpuMs:F3}ms cpuFrame={report.cpuFrameMs:F2}ms gpuFrame={report.gpuFrameMs:F2}ms report={path}");
        }

        void OnDestroy() { if (m_Started >= 0f && !m_Done) Finish(); }
    }
}
