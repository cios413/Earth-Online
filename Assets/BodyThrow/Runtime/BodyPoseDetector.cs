using System.Collections;
using System.Collections.Generic;
using Unity.Barracuda;
using UnityEngine;

namespace EarthOL.BodyThrow
{
    /// <summary>YOLOv8-Pose decoder for one-person pose tracking.</summary>
    public sealed class BodyPoseDetector : System.IDisposable
    {
        const int InputSize = 416;
        const int KeypointCount = 17;
        const float ConfidenceThreshold = 0.25f;
        const float NmsThreshold = 0.45f;

        Model m_Model;
        IWorker m_Worker;
        readonly List<BodyPoseResult> m_Candidates = new List<BodyPoseResult>(16);
        Rect m_InputRect = new Rect(0, 0, InputSize, InputSize);
        bool m_LoggedShapes;

        public bool Ready => m_Worker != null;
        public string Error { get; private set; }

        public IEnumerator Load()
        {
            var nnModel = Resources.Load<NNModel>("Models/yolov8n-pose-raw");
            if (nnModel == null)
            {
                Error = "找不到 Assets/Resources/Models/yolov8n-pose-raw.onnx";
                Debug.LogWarning("[Pose] " + Error);
                yield break;
            }

            try
            {
                m_Model = ModelLoader.Load(nnModel);
                m_Worker = WorkerFactory.CreateWorker(WorkerFactory.Type.Auto, m_Model);
                Debug.Log("[Pose] YOLOv8-Pose 模型就绪：" + m_Worker.GetType().Name);
            }
            catch (System.Exception exception)
            {
                Error = exception.Message;
                Debug.LogException(exception);
            }

            yield return null;
        }

        public BodyPoseResult[] Detect(Color32[] pixels, int width, int height, int maxResults)
        {
            if (m_Worker == null || pixels == null || width <= 0 || height <= 0 || maxResults <= 0)
                return System.Array.Empty<BodyPoseResult>();

            using (var input = BuildInput(pixels, width, height))
            {
                m_Worker.Execute(input);
                var boxes = m_Worker.PeekOutput("boxes_raw");
                var scores = m_Worker.PeekOutput("scores_raw");
                var pose = m_Worker.PeekOutput("pose_raw");
                if (boxes == null || scores == null || pose == null)
                    return System.Array.Empty<BodyPoseResult>();
                Decode(boxes, scores, pose);
            }

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
                if (!overlaps) result.Add(candidate);
            }

            return result.ToArray();
        }

        Tensor BuildInput(Color32[] pixels, int width, int height)
        {
            var data = new float[InputSize * InputSize * 3];
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
                    int index = (y * InputSize + x) * 3;
                    if (x < padX || x >= padX + resizedWidth || y < padY || y >= padY + resizedHeight)
                    {
                        data[index] = data[index + 1] = data[index + 2] = 114f / 255f;
                        continue;
                    }

                    // ReadPixels is bottom-up; YOLO expects RGB rows from the top.
                    int sourceX = Mathf.Clamp((x - padX) * width / resizedWidth, 0, width - 1);
                    int sourceY = height - 1 - Mathf.Clamp((y - padY) * height / resizedHeight, 0, height - 1);
                    var color = pixels[sourceY * width + sourceX];
                    data[index] = color.r / 255f;
                    data[index + 1] = color.g / 255f;
                    data[index + 2] = color.b / 255f;
                }
            }

            return new Tensor(1, InputSize, InputSize, 3, data);
        }

        void Decode(Tensor boxes, Tensor scores, Tensor pose)
        {
            m_Candidates.Clear();
            var boxData = boxes.ToReadOnlyArray();
            var scoreData = scores.ToReadOnlyArray();
            var poseData = pose.ToReadOnlyArray();
            const int anchors = 3549;
            const int boxChannels = 64;
            const int poseChannels = 51;

            bool boxesInterleaved = IsAnchorMajor(boxes, boxChannels, anchors);
            bool poseInterleaved = IsAnchorMajor(pose, poseChannels, anchors);
            if (scores.length != anchors)
                throw new System.InvalidOperationException("Unexpected pose score tensor: " + scores.shape);
            if (!m_LoggedShapes)
            {
                Debug.Log($"[Pose] 输出布局 boxes={boxes.shape}, scores={scores.shape}, pose={pose.shape}; " +
                    $"交错读取 boxes={boxesInterleaved}, pose={poseInterleaved}");
                m_LoggedShapes = true;
            }

            float BoxAt(int channel, int anchor) => boxData[boxesInterleaved ? anchor * boxChannels + channel : channel * anchors + anchor];
            float ScoreAt(int anchor) => scoreData[anchor];
            float PoseAt(int channel, int anchor) => poseData[poseInterleaved ? anchor * poseChannels + channel : channel * anchors + anchor];

            for (int anchor = 0; anchor < anchors; anchor++)
            {
                float score = Sigmoid(ScoreAt(anchor));
                if (score < ConfidenceThreshold) continue;

                GetGridPoint(anchor, out float gridX, out float gridY, out float stride);
                float left = DistributionExpectation(BoxAt, 0, anchor) * stride;
                float top = DistributionExpectation(BoxAt, 1, anchor) * stride;
                float right = DistributionExpectation(BoxAt, 2, anchor) * stride;
                float bottom = DistributionExpectation(BoxAt, 3, anchor) * stride;
                float centerX = gridX * stride;
                float centerY = gridY * stride;
                float boxLeft = centerX - left;
                float boxTop = centerY - top;
                float boxRight = centerX + right;
                float boxBottom = centerY + bottom;
                float boxWidth = boxRight - boxLeft;
                float boxHeight = boxBottom - boxTop;
                if (boxWidth <= 2f || boxHeight <= 2f) continue;

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
                    int offset = keypoint * 3;
                    float x = (PoseAt(offset, anchor) * 2f + gridX - 0.5f) * stride;
                    float yTop = (PoseAt(offset + 1, anchor) * 2f + gridY - 0.5f) * stride;
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

        Vector2 ModelToViewport(Vector2 point)
        {
            return new Vector2((point.x - m_InputRect.x) / m_InputRect.width,
                1f - (point.y - m_InputRect.y) / m_InputRect.height);
        }

        static bool IsAnchorMajor(Tensor tensor, int channels, int anchors)
        {
            if (tensor.length != channels * anchors || tensor.shape.batch != 1)
                throw new System.InvalidOperationException("Unexpected pose output tensor: " + tensor.shape);
            // Standard ONNX import and legacy NHWC import can expose different layouts.
            if (tensor.shape.channels == channels && tensor.shape.width == anchors) return true;
            if (tensor.shape.channels == anchors && tensor.shape.width == channels) return false;
            if (tensor.shape.channels == 1 && tensor.shape.height == channels && tensor.shape.width == anchors) return false;
            throw new System.InvalidOperationException("Unsupported pose output layout: " + tensor.shape);
        }

        public void Dispose()
        {
            m_Worker?.Dispose();
            m_Worker = null;
        }

        static float DistributionExpectation(System.Func<int, int, float> read, int channel, int anchor)
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

        static float Sigmoid(float value)
        {
            return 1f / (1f + Mathf.Exp(-value));
        }

        static float IoU(Rect a, Rect b)
        {
            float width = Mathf.Max(0f, Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin));
            float height = Mathf.Max(0f, Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin));
            float intersection = width * height;
            float union = a.width * a.height + b.width * b.height - intersection;
            return union <= 0f ? 0f : intersection / union;
        }
    }
}
