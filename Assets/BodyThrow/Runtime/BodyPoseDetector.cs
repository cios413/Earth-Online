using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Sentis;
using UnityEngine;

namespace EarthOL.BodyThrow
{
    /// <summary>YOLO pose inference and decoding for person tracking.</summary>
    public sealed class BodyPoseDetector : System.IDisposable
    {
        const int InputSize = 416;
        const int KeypointCount = 17;
        const int KeypointDimensions = 3;
        const int AnchorCount = 3549;
        const int Yolo26BoxChannels = 4;
        const int LegacyBoxChannels = 64;
        const float ConfidenceThreshold = 0.25f;
        static readonly float k_MinScoreLogit = Mathf.Log(ConfidenceThreshold / (1f - ConfidenceThreshold));
        const float NmsThreshold = 0.45f;
        const int MaxDecodedAnchors = 64;

        struct ScoredAnchor
        {
            public int anchor;
            public float score;
        }

        Model m_Model;
        readonly bool m_UseYolo26;
        Worker m_Worker;
        Tensor<float> m_Input;
        readonly List<BodyPoseResult> m_Candidates = new List<BodyPoseResult>(16);
        readonly List<ScoredAnchor> m_ScoredAnchors = new List<ScoredAnchor>(512);
        Rect m_InputRect = new Rect(0, 0, InputSize, InputSize);
        bool m_LoggedShapes;
        NativeArray<float> m_InputData;
        Color32[] m_SourcePixels;
        int m_SourceWidth, m_SourceHeight;

        public bool Ready => m_Worker != null;
        public string Error { get; private set; }
        public float LastDecodeMilliseconds { get; private set; }
        public string ModelName => m_UseYolo26 ? "yolo26n-pose-raw" : "yolov8n-pose-raw";
        public string BackendName => m_Worker != null ? m_Worker.backendType.ToString() : "Unavailable";

        public BodyPoseDetector(bool useYolo26 = true) => m_UseYolo26 = useYolo26;

        public IEnumerator Load() => Load(false);

        internal IEnumerator Load(bool scheduled)
        {
            string path = "Models/" + ModelName;
            ModelAsset modelAsset;
            if (scheduled)
            {
                var request = Resources.LoadAsync<ModelAsset>(path);
                yield return request;
                modelAsset = request.asset as ModelAsset;
            }
            else modelAsset = Resources.Load<ModelAsset>(path);
            if (modelAsset == null)
            {
                Error = "找不到姿态模型 " + ModelName;
                Debug.LogWarning("[Pose] " + Error);
                yield break;
            }

            try
            {
                m_Model = ModelLoader.Load(modelAsset);
                m_Worker = CreateInferenceWorker(m_Model);
                var backend = m_Worker.backendType;
                Debug.Log("[Pose] " + ModelName + " 模型就绪：" + backend +
                    (m_UseYolo26 ? "，YOLO26 reg_max=1 box=LTRB4 class=1 keypoints=17x3" : string.Empty));
            }
            catch (System.Exception exception)
            {
                Error = exception.Message;
                Debug.LogException(exception);
            }

            yield return null;
        }

        internal static Worker CreateInferenceWorker(Model model)
        {
            bool mali = SystemInfo.graphicsDeviceName.IndexOf("Mali", System.StringComparison.OrdinalIgnoreCase) >= 0;
            if (!mali && SystemInfo.supportsComputeShaders && SystemInfo.supportsAsyncGPUReadback &&
                Resources.Load<ComputeShader>("Sentis/TextureConversion/TextureToTensor") != null)
            {
                try
                {
                    return new Worker(model, BackendType.GPUCompute);
                }
                catch (System.Exception exception)
                {
                    Debug.LogWarning("[Sentis] GPU worker unavailable, using CPU: " + exception.Message);
                }
            }
            return new Worker(model, BackendType.CPU);
        }

        public BodyPoseResult[] Detect(Color32[] pixels, int width, int height, int maxResults)
        {
            if (m_Worker == null || pixels == null || width <= 0 || height <= 0 || maxResults <= 0)
                return System.Array.Empty<BodyPoseResult>();

            SnapshotAppearanceFrame(pixels, width, height);
            using (var input = BuildInput(pixels, width, height))
            {
                m_Worker.Schedule(input);
                var boxes = m_Worker.PeekOutput("boxes_raw") as Tensor<float>;
                var scores = m_Worker.PeekOutput("scores_raw") as Tensor<float>;
                var pose = m_Worker.PeekOutput("pose_raw") as Tensor<float>;
                if (boxes == null || scores == null || pose == null)
                    return System.Array.Empty<BodyPoseResult>();
                DecodeOutputsWithCpuFallback(input, boxes, scores, pose);
            }

            return SelectResults(maxResults);
        }

        public IEnumerator DetectScheduled(Color32[] pixels, int width, int height, int maxResults,
            System.Action<BodyPoseResult[]> completed)
        {
            if (!Ready) yield break;
            SnapshotAppearanceFrame(pixels, width, height);
            var input = BuildInput(pixels, width, height);
            {
                yield return null;
                foreach (var step in ScheduleInference(m_Worker, input)) yield return step;
                var boxes = m_Worker.PeekOutput("boxes_raw") as Tensor<float>;
                var scores = m_Worker.PeekOutput("scores_raw") as Tensor<float>;
                var pose = m_Worker.PeekOutput("pose_raw") as Tensor<float>;
                if (boxes == null || scores == null || pose == null)
                    throw new System.InvalidOperationException("YOLO pose model did not return all expected outputs.");
                var outputs = new[] { boxes, scores, pose };
                var copies = new Tensor<float>[outputs.Length];
                try
                {
                    System.InvalidOperationException readbackFailure = null;
                    foreach (var step in ReadOutputs(outputs, copies, error => readbackFailure = error)) yield return step;
                    if (readbackFailure != null)
                    {
                        if (!ShouldFallbackToCpu(m_Worker.backendType, readbackFailure)) throw readbackFailure;
                        LogCpuFallback(readbackFailure);
                        DisposeOutputs(copies);
                        SwitchToCpuBackend();
                        foreach (var step in ScheduleInference(m_Worker, input)) yield return step;
                        outputs[0] = m_Worker.PeekOutput("boxes_raw") as Tensor<float>;
                        outputs[1] = m_Worker.PeekOutput("scores_raw") as Tensor<float>;
                        outputs[2] = m_Worker.PeekOutput("pose_raw") as Tensor<float>;
                        foreach (var step in ReadOutputs(outputs, copies, error => readbackFailure = error)) yield return step;
                        if (readbackFailure != null) throw readbackFailure;
                    }
                    long decodeStart = System.Diagnostics.Stopwatch.GetTimestamp();
                    Decode(copies[0], copies[1], copies[2]);
                    var results = SelectResults(maxResults);
                    LastDecodeMilliseconds = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - decodeStart) *
                        1000.0 / System.Diagnostics.Stopwatch.Frequency);
                    completed(results);
                }
                finally
                {
                    DisposeOutputs(copies);
                }
            }
        }

        internal static IEnumerable ScheduleInference(Worker worker, Tensor<float> input)
        {
            var schedule = worker.ScheduleIterable(input);
            var slice = System.Diagnostics.Stopwatch.StartNew();
            while (schedule.MoveNext())
            {
                if (slice.ElapsedMilliseconds < 4) continue;
                yield return null;
                slice.Restart();
            }
        }

        internal static IEnumerable ReadOutputs(Tensor<float>[] outputs, Tensor<float>[] copies,
            System.Action<System.InvalidOperationException> failed)
        {
            failed(null);
            foreach (var output in outputs)
                if (output == null) throw new System.InvalidOperationException("Missing model output tensor.");

            int remaining = outputs.Length;
            bool requested = false;
            while (remaining > 0)
            {
                System.InvalidOperationException failure = null;
                try
                {
                    // Read each completed request in the frame it becomes available.
                    if (!requested)
                        foreach (var output in outputs) output.ReadbackRequest();
                    requested = true;
                    for (int i = 0; i < outputs.Length; i++)
                    {
                        if (copies[i] != null || !outputs[i].IsReadbackRequestDone()) continue;
                        copies[i] = outputs[i].ReadbackAndClone();
                        remaining--;
                    }
                }
                catch (System.InvalidOperationException exception) { failure = exception; }
                if (failure != null)
                {
                    failed(failure);
                    yield break;
                }
                if (remaining > 0) yield return null;
            }
        }

        internal static void DisposeOutputs(Tensor<float>[] outputs)
        {
            for (int i = 0; i < outputs.Length; i++)
            {
                outputs[i]?.Dispose();
                outputs[i] = null;
            }
        }

        internal static bool ShouldFallbackToCpu(BackendType backend, System.Exception exception) =>
            backend == BackendType.GPUCompute && exception is System.InvalidOperationException;

        void DecodeOutputsWithCpuFallback(Tensor<float> input, Tensor<float> boxes,
            Tensor<float> scores, Tensor<float> pose)
        {
            Tensor<float> cpuBoxes = null;
            Tensor<float> cpuScores = null;
            Tensor<float> cpuPose = null;
            try
            {
                System.InvalidOperationException readbackFailure = null;
                try
                {
                    cpuBoxes = boxes.ReadbackAndClone();
                    cpuScores = scores.ReadbackAndClone();
                    cpuPose = pose.ReadbackAndClone();
                }
                catch (System.InvalidOperationException exception)
                {
                    DisposeTensor(cpuBoxes);
                    DisposeTensor(cpuScores);
                    DisposeTensor(cpuPose);
                    cpuBoxes = cpuScores = cpuPose = null;
                    readbackFailure = exception;
                }

                if (readbackFailure != null)
                {
                    if (!ShouldFallbackToCpu(m_Worker.backendType, readbackFailure)) throw readbackFailure;
                    LogCpuFallback(readbackFailure);
                    SwitchToCpuBackend();
                    m_Worker.Schedule(input);
                    boxes = m_Worker.PeekOutput("boxes_raw") as Tensor<float>;
                    scores = m_Worker.PeekOutput("scores_raw") as Tensor<float>;
                    pose = m_Worker.PeekOutput("pose_raw") as Tensor<float>;
                    if (boxes == null || scores == null || pose == null)
                        throw new System.InvalidOperationException("CPU retry did not return all expected YOLO pose outputs.");
                    cpuBoxes = boxes.ReadbackAndClone();
                    cpuScores = scores.ReadbackAndClone();
                    cpuPose = pose.ReadbackAndClone();
                }

                Decode(cpuBoxes, cpuScores, cpuPose);
            }
            finally
            {
                DisposeTensor(cpuBoxes);
                DisposeTensor(cpuScores);
                DisposeTensor(cpuPose);
            }
        }

        static void LogCpuFallback(System.InvalidOperationException exception)
        {
            Debug.LogWarning("[Pose] GPU output readback failed. Switching to CPU and retrying this frame once. " +
                exception.Message);
        }

        void SwitchToCpuBackend()
        {
            var previous = m_Worker;
            m_Worker = new Worker(m_Model, BackendType.CPU);
            previous.Dispose();
        }

        static void DisposeTensor(Tensor<float> tensor)
        {
            tensor?.Dispose();
        }

        BodyPoseResult[] SelectResults(int maxResults)
        {
            m_Candidates.Sort((a, b) => b.confidence.CompareTo(a.confidence));
            var result = new List<BodyPoseResult>(Mathf.Min(maxResults, m_Candidates.Count));

            foreach (var candidate in m_Candidates)
            {
                if (result.Count >= maxResults) break;
                bool overlaps = false;
                foreach (var kept in result)
                {
                    if (IoU(candidate.screenRect, kept.screenRect) > NmsThreshold)
                    {
                        overlaps = true;
                        break;
                    }
                }
                if (overlaps) continue;
                var selected = candidate;
                selected.appearance = BuildAppearance(candidate.keypoints, candidate.keypointScores);
                result.Add(selected);
            }

            return result.ToArray();
        }

        Tensor<float> BuildInput(Color32[] pixels, int width, int height)
        {
            if (!m_InputData.IsCreated)
                m_InputData = new NativeArray<float>(InputSize * InputSize * 3, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            if (m_Input == null)
                m_Input = new Tensor<float>(new TensorShape(1, 3, InputSize, InputSize), clearOnInit: false);
            else
                m_Input.CompleteAllPendingOperations();
            var data = m_InputData;
            float resize = Mathf.Min((float)InputSize / width, (float)InputSize / height);
            int resizedWidth = Mathf.Max(1, Mathf.RoundToInt(width * resize));
            int resizedHeight = Mathf.Max(1, Mathf.RoundToInt(height * resize));
            int padX = (InputSize - resizedWidth) / 2;
            int padY = (InputSize - resizedHeight) / 2;
            m_InputRect = new Rect(padX, padY, resizedWidth, resizedHeight);

            for (int y = 0; y < InputSize; y++)
            {
                for (int x = 0; x < InputSize; x++)
                {
                    int pixelIndex = y * InputSize + x;
                    if (x < padX || x >= padX + resizedWidth || y < padY || y >= padY + resizedHeight)
                    {
                        float padding = 114f / 255f;
                        data[pixelIndex] = data[InputSize * InputSize + pixelIndex] =
                            data[InputSize * InputSize * 2 + pixelIndex] = padding;
                        continue;
                    }

                    // ReadPixels is bottom-up; YOLO expects RGB rows from the top.
                    int sourceX = Mathf.Clamp((x - padX) * width / resizedWidth, 0, width - 1);
                    int sourceY = height - 1 - Mathf.Clamp((y - padY) * height / resizedHeight, 0, height - 1);
                    var color = pixels[sourceY * width + sourceX];
                    data[pixelIndex] = color.r / 255f;
                    data[InputSize * InputSize + pixelIndex] = color.g / 255f;
                    data[InputSize * InputSize * 2 + pixelIndex] = color.b / 255f;
                }
            }

            m_Input.Upload(data);
            return m_Input;
        }

        void Decode(Tensor<float> boxes, Tensor<float> scores, Tensor<float> pose)
        {
            m_Candidates.Clear();
            var boxData = boxes.AsReadOnlyNativeArray();
            var scoreData = scores.AsReadOnlyNativeArray();
            var poseData = pose.AsReadOnlyNativeArray();
            const int anchors = AnchorCount;
            int boxChannels = m_UseYolo26 ? Yolo26BoxChannels : LegacyBoxChannels;
            const int poseChannels = KeypointCount * KeypointDimensions;

            bool boxesInterleaved = IsAnchorMajor(boxes.shape, boxChannels, anchors);
            if (scores.shape.rank != 3 || scores.shape[0] != 1 || scores.shape[1] != 1 || scores.shape[2] != anchors)
                throw new System.InvalidOperationException("Unexpected YOLO person-score tensor: " + scores.shape);
            bool poseInterleaved = IsAnchorMajor(pose.shape, poseChannels, anchors);
            if (!m_LoggedShapes)
            {
                Debug.Log($"[Pose] 输出布局 boxes={boxes.shape}, scores={scores.shape}, pose={pose.shape}; " +
                    $"交错读取 boxes={boxesInterleaved}, pose={poseInterleaved}");
                m_LoggedShapes = true;
            }

            float BoxAt(int channel, int anchor) => boxData[boxesInterleaved ? anchor * boxChannels + channel : channel * anchors + anchor];
            float ScoreAt(int anchor) => scoreData[anchor];
            float PoseAt(int channel, int anchor) => poseData[poseInterleaved ? anchor * poseChannels + channel : channel * anchors + anchor];

            m_ScoredAnchors.Clear();
            for (int anchor = 0; anchor < anchors; anchor++)
            {
                float logit = ScoreAt(anchor);
                if (!(logit >= k_MinScoreLogit) || float.IsInfinity(logit)) continue;
                m_ScoredAnchors.Add(new ScoredAnchor { anchor = anchor, score = logit });
            }
            m_ScoredAnchors.Sort((a, b) => b.score.CompareTo(a.score));
            int candidateCount = Mathf.Min(MaxDecodedAnchors, m_ScoredAnchors.Count);
            for (int candidateIndex = 0; candidateIndex < candidateCount; candidateIndex++)
            {
                int anchor = m_ScoredAnchors[candidateIndex].anchor;
                float score = Sigmoid(m_ScoredAnchors[candidateIndex].score);

                GetGridPoint(anchor, out float gridX, out float gridY, out float stride);
                float BoxDistance(int side) => (m_UseYolo26 ? BoxAt(side, anchor) :
                    DistributionExpectation(BoxAt, side, anchor)) * stride;
                float left = BoxDistance(0);
                float top = BoxDistance(1);
                float right = BoxDistance(2);
                float bottom = BoxDistance(3);
                float centerX = gridX * stride;
                float centerY = gridY * stride;
                float boxLeft = centerX - left;
                float boxTop = centerY - top;
                float boxRight = centerX + right;
                float boxBottom = centerY + bottom;
                float boxWidth = boxRight - boxLeft;
                float boxHeight = boxBottom - boxTop;
                if (!(boxWidth > 2f && boxHeight > 2f) ||
                    float.IsInfinity(boxWidth) || float.IsInfinity(boxHeight)) continue;

                Vector2 lower = ModelToViewport(new Vector2(boxLeft, boxBottom));
                Vector2 upper = ModelToViewport(new Vector2(boxRight, boxTop));
                lower = new Vector2(Mathf.Clamp01(lower.x), Mathf.Clamp01(lower.y));
                upper = new Vector2(Mathf.Clamp01(upper.x), Mathf.Clamp01(upper.y));
                if (upper.x <= lower.x || upper.y <= lower.y) continue;

                var keypoints = new Vector2[KeypointCount];
                var keypointScores = new float[KeypointCount];
                float scoreSum = 0f;

                for (int keypoint = 0; keypoint < KeypointCount; keypoint++)
                {
                    int offset = keypoint * KeypointDimensions;
                    float scale = m_UseYolo26 ? 1f : 2f;
                    float gridOffset = m_UseYolo26 ? 0f : -.5f;
                    float x = (PoseAt(offset, anchor) * scale + gridX + gridOffset) * stride;
                    float yTop = (PoseAt(offset + 1, anchor) * scale + gridY + gridOffset) * stride;
                    float keypointScore = Sigmoid(PoseAt(offset + 2, anchor));
                    Vector2 point = ModelToViewport(new Vector2(x, yTop));
                    if (point.x < 0f || point.x > 1f || point.y < 0f || point.y > 1f)
                        keypointScore = 0f;
                    keypoints[keypoint] = point;
                    keypointScores[keypoint] = keypointScore;
                    scoreSum += keypointScore;
                }

                m_Candidates.Add(new BodyPoseResult
                {
                    screenRect = Rect.MinMaxRect(lower.x, lower.y, upper.x, upper.y),
                    keypoints = keypoints,
                    keypointScores = keypointScores,
                    confidence = score * 0.7f + (scoreSum / KeypointCount) * 0.3f
                });
            }
        }

        float[] BuildAppearance(Vector2[] keypoints, float[] scores)
        {
            if (m_SourcePixels == null || m_SourceWidth <= 0 || m_SourceHeight <= 0 ||
                scores[5] < 0.35f || scores[6] < 0.35f || scores[11] < 0.30f || scores[12] < 0.30f)
                return null;

            Vector2 shoulders = (keypoints[5] + keypoints[6]) * 0.5f;
            Vector2 hips = (keypoints[11] + keypoints[12]) * 0.5f;
            float shoulderHalfWidth = Vector2.Distance(keypoints[5], keypoints[6]) * 0.32f;
            float hipHalfWidth = Vector2.Distance(keypoints[11], keypoints[12]) * 0.40f;
            if (shoulderHalfWidth < 0.01f || hipHalfWidth < 0.008f) return null;

            var histogram = new float[64];
            int samples = 0;
            const int across = 8;
            const int down = 8;
            for (int y = 0; y < down; y++)
            for (int x = 0; x < across; x++)
            {
                float vertical = (y + 0.5f) / down;
                Vector2 center = Vector2.Lerp(hips, shoulders, vertical);
                float halfWidth = Mathf.Lerp(hipHalfWidth, shoulderHalfWidth, vertical);
                float viewportX = center.x + ((x + 0.5f) / across * 2f - 1f) * halfWidth;
                float viewportY = center.y;
                int pixelX = Mathf.Clamp((int)(viewportX * m_SourceWidth), 0, m_SourceWidth - 1);
                int pixelY = Mathf.Clamp((int)(viewportY * m_SourceHeight), 0, m_SourceHeight - 1);
                Color32 color = m_SourcePixels[pixelY * m_SourceWidth + pixelX];
                int sum = color.r + color.g + color.b;
                int red = sum > 0 ? color.r * 8 / sum : 2;
                int green = sum > 0 ? color.g * 8 / sum : 2;
                int bin = sum < 100 ? 0 : sum > 570 && Mathf.Max(color.r, Mathf.Max(color.g, color.b)) -
                    Mathf.Min(color.r, Mathf.Min(color.g, color.b)) < 35 ? 63 :
                    Mathf.Clamp(red, 0, 7) * 8 + Mathf.Clamp(green, 0, 7);
                histogram[bin]++;
                samples++;
            }

            if (samples < 24) return null;
            float inverseSamples = 1f / samples;
            for (int i = 0; i < histogram.Length; i++) histogram[i] *= inverseSamples;
            return histogram;
        }

        Vector2 ModelToViewport(Vector2 point)
        {
            return new Vector2((point.x - m_InputRect.x) / m_InputRect.width,
                1f - (point.y - m_InputRect.y) / m_InputRect.height);
        }

        void SnapshotAppearanceFrame(Color32[] pixels, int width, int height)
        {
            int length = width * height;
            if (m_SourcePixels == null || m_SourcePixels.Length != length)
                m_SourcePixels = new Color32[length];
            System.Array.Copy(pixels, m_SourcePixels, length);
            m_SourceWidth = width;
            m_SourceHeight = height;
        }

        static bool IsAnchorMajor(TensorShape shape, int channels, int anchors)
        {
            if (shape.rank == 3 && shape[0] == 1 && shape[1] == channels && shape[2] == anchors) return false;
            if (shape.rank == 3 && shape[0] == 1 && shape[1] == anchors && shape[2] == channels) return true;
            if (shape.rank == 4 && shape[0] == 1 && shape[1] == channels && shape[2] == 1 && shape[3] == anchors) return false;
            if (shape.rank == 4 && shape[0] == 1 && shape[1] == 1 && shape[2] == anchors && shape[3] == channels) return true;
            throw new System.InvalidOperationException("Unsupported YOLO pose output shape: " + shape);
        }

        public void Dispose()
        {
            m_Worker?.Dispose();
            m_Worker = null;
            m_Input?.Dispose();
            m_Input = null;
            if (m_InputData.IsCreated) m_InputData.Dispose();
        }

        internal static float DistributionExpectation(System.Func<int, int, float> read, int channel, int anchor)
        {
            float max = float.NegativeInfinity;
            for (int bin = 0; bin < 16; bin++)
                max = Mathf.Max(max, read(channel * 16 + bin, anchor));

            float sum = 0f;
            float weighted = 0f;
            for (int bin = 0; bin < 16; bin++)
            {
                float value = Mathf.Exp(read(channel * 16 + bin, anchor) - max);
                sum += value;
                weighted += bin * value;
            }
            return sum <= 0f ? 0f : weighted / sum;
        }

        static void GetGridPoint(int anchor, out float x, out float y, out float stride)
        {
            if (anchor < 2704)
            {
                stride = 8f;
                int index = anchor;
                x = index % 52 + 0.5f;
                y = index / 52 + 0.5f;
            }
            else if (anchor < 3380)
            {
                stride = 16f;
                int index = anchor - 2704;
                x = index % 26 + 0.5f;
                y = index / 26 + 0.5f;
            }
            else
            {
                stride = 32f;
                int index = anchor - 3380;
                x = index % 13 + 0.5f;
                y = index / 13 + 0.5f;
            }
        }

        internal static float Sigmoid(float value)
        {
            return 1f / (1f + Mathf.Exp(-value));
        }

        internal static float IoU(Rect a, Rect b)
        {
            float width = Mathf.Max(0f, Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin));
            float height = Mathf.Max(0f, Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin));
            float intersection = width * height;
            float union = a.width * a.height + b.width * b.height - intersection;
            return union <= 0f ? 0f : intersection / union;
        }
    }
}
