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
        int m_Frames, m_TimingFrames, m_GpuFrames, m_StartDetectionCount;
        double m_CpuMilliseconds, m_GpuMilliseconds;
        long m_GcBytes, m_MaxMemory;
        string m_ProfilePath;

        [Serializable] sealed class Report
        {
            public string unity, device, gpu, graphicsAPI, binaryProfile;
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
            if (Application.isEditor || !recordDevelopmentProfile || m_Detection == null || m_Done) return;
            if (m_Started < 0f)
            {
                if (m_Detection.DetectionCount < 3) return;
                m_Started = Time.realtimeSinceStartup;
                m_StartDetectionCount = m_Detection.DetectionCount;
                m_ProfilePath = Path.Combine(Application.persistentDataPath, "person-system-profiler.raw");
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

        void Finish()
        {
            if (m_Started < 0f || m_Done) return;
            m_Done = true;
            Profiler.enabled = false;
            Profiler.enableBinaryLog = false;
            float seconds = Time.realtimeSinceStartup - m_Started;
            int detections = m_Detection.DetectionCount - m_StartDetectionCount;
            var report = new Report
            {
                unity = Application.unityVersion, device = SystemInfo.deviceModel,
                gpu = SystemInfo.graphicsDeviceName, graphicsAPI = SystemInfo.graphicsDeviceType.ToString(),
                binaryProfile = m_ProfilePath, seconds = seconds, configuredDetectionHz = m_Detection.DetectionFramesPerSecond,
                actualDetectionHz = detections / Mathf.Max(.001f, seconds),
                inferenceWallMs = m_Detection.AverageInferenceMilliseconds,
                trackingCpuMs = m_Detection.AverageTrackingMilliseconds,
                renderedFrames = m_Frames, detectionsInWindow = detections, captureCount = m_Detection.CaptureCount,
                tensorInputCount = m_Detection.TensorInputCount, nativeFrameTimingSamples = m_TimingFrames,
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
