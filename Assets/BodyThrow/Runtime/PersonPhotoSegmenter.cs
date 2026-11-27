using System;
using System.Collections.Generic;
using System.Collections;
using Unity.Collections;
using Unity.Sentis;
using UnityEngine;

namespace EarthOL.BodyThrow
{
    internal sealed class PersonPhotoSegmenter : IDisposable
    {
        const int InputSize = 416;
        const int BoxChannels = 64;
        const int MaskChannels = 32;
        const int HeadChannels = BoxChannels + 1 + MaskChannels;
        const int PrototypeSize = InputSize / 4;
        const int MaxCandidates = 12;
        static readonly int[] Strides = { 8, 16, 32 };
        static readonly string[] Outputs = { "stride8", "stride16", "stride32" };

        struct Candidate
        {
            public Rect bounds;
            public float rank;
            public NativeArray<float>.ReadOnly head;
            public int coefficientOffset;
            public int coefficientPlaneSize;
        }

        readonly List<Candidate> m_Candidates = new List<Candidate>(MaxCandidates);
        Model m_Model;
        Worker m_Worker;
        bool m_UseCpu;
        public string BackendName => m_Worker?.backendType.ToString();

        public IEnumerator SegmentScheduled(Texture2D photo, PersonPoseData person, Rect photoBounds,
            Action<byte[]> completed)
        {
            byte[] alpha = null;
            var copies = new Tensor<float>[Outputs.Length + 1];
            try
            {
                if (m_Worker == null)
                {
                    var asset = Resources.Load<ModelAsset>("Models/yolov8n-person-seg");
                    if (asset == null)
                    {
                        Debug.LogWarning("[PersonShowcase] Person segmentation model is missing.");
                        completed?.Invoke(null);
                        yield break;
                    }
                    m_Model ??= ModelLoader.Load(asset);
                    m_Worker = m_UseCpu ? new Worker(m_Model, BackendType.CPU) :
                        BodyPoseDetector.CreateInferenceWorker(m_Model);
                }

                using (var input = BuildInput(photo, out Rect image))
                {
                    var outputs = new Tensor<float>[copies.Length];
                    while (true)
                    {
                        foreach (var step in BodyPoseDetector.ScheduleInference(m_Worker, input)) yield return step;
                        for (int i = 0; i < Outputs.Length; i++)
                            outputs[i] = m_Worker.PeekOutput(Outputs[i]) as Tensor<float>;
                        outputs[Outputs.Length] = m_Worker.PeekOutput("prototypes") as Tensor<float>;
                        InvalidOperationException failure = null;
                        foreach (var step in BodyPoseDetector.ReadOutputs(outputs, copies, error => failure = error))
                            yield return step;
                        if (failure == null) break;
                        if (!BodyPoseDetector.ShouldFallbackToCpu(m_Worker.backendType, failure)) throw failure;

                        Debug.LogWarning("[PersonShowcase] GPU segmentation readback failed. Retrying on CPU. " + failure.Message);
                        BodyPoseDetector.DisposeOutputs(copies);
                        Dispose();
                        m_UseCpu = true;
                        m_Worker = new Worker(m_Model, BackendType.CPU);
                    }

                    Rect target = ToModelRect(person.screenRect, photoBounds, image);
                    m_Candidates.Clear();
                    for (int i = 0; i < Strides.Length; i++)
                        ReadCandidates(copies[i], Strides[i], target);
                    if (m_Candidates.Count == 0)
                    {
                        completed?.Invoke(null);
                        yield break;
                    }

                    var prototypes = copies[Outputs.Length];
                    CheckShape(prototypes, PrototypeSize, MaskChannels);
                    var prototypeData = prototypes.AsReadOnlyNativeArray();
                    float[] bestMask = null;
                    Rect bestBounds = default;
                    float bestRank = float.NegativeInfinity;
                    var logits = new float[PrototypeSize * PrototypeSize];
                    foreach (var candidate in m_Candidates)
                    {
                        if (candidate.rank + .45f <= bestRank) break;
                        yield return null;
                        BuildPrototypeMask(candidate, prototypeData, logits);
                        float coverage = KeypointCoverage(person, photoBounds, image, candidate.bounds, logits);
                        if (coverage >= 0f && coverage < 0.4f) continue;
                        float rank = candidate.rank + 0.45f * Mathf.Max(0f, coverage);
                        if (rank <= bestRank) continue;
                        bestRank = rank;
                        bestBounds = candidate.bounds;
                        if (bestMask == null) bestMask = new float[logits.Length];
                        Array.Copy(logits, bestMask, logits.Length);
                    }
                    if (bestMask == null || !TryRasterize(photo.width, photo.height, image, bestBounds, bestMask, out alpha))
                    {
                        completed?.Invoke(null);
                        yield break;
                    }
                    RestoreLimbCoverage(alpha, photo.width, photo.height, person, photoBounds, image, bestMask);
                    RemoveUnconnectedRegions(alpha, photo.width, photo.height, person, photoBounds);
                    completed?.Invoke(HasForeground(alpha) ? alpha : null);
                }
            }
            finally
            {
                m_Candidates.Clear();
                BodyPoseDetector.DisposeOutputs(copies);
            }
        }

        static Tensor<float> BuildInput(Texture2D photo, out Rect image)
        {
            int width = photo.width, height = photo.height;
            float scale = InputSize / (float)Mathf.Max(width, height);
            int resizedWidth = Mathf.Max(1, Mathf.RoundToInt(width * scale));
            int resizedHeight = Mathf.Max(1, Mathf.RoundToInt(height * scale));
            int padX = (InputSize - resizedWidth) / 2, padY = (InputSize - resizedHeight) / 2;
            image = new Rect(padX, padY, resizedWidth, resizedHeight);
            Color32[] pixels = photo.GetPixels32();
            var rgb = new float[InputSize * InputSize * 3];
            for (int y = 0; y < InputSize; y++)
            for (int x = 0; x < InputSize; x++)
            {
                int index = y * InputSize + x;
                if (x < padX || x >= padX + resizedWidth || y < padY || y >= padY + resizedHeight)
                {
                    float padding = 114f / 255f;
                    rgb[index] = rgb[InputSize * InputSize + index] = rgb[InputSize * InputSize * 2 + index] = padding;
                    continue;
                }
                float sx = Mathf.Clamp((x - padX + 0.5f) * width / resizedWidth - 0.5f, 0, width - 1);
                float sy = Mathf.Clamp(height - (y - padY + 0.5f) * height / resizedHeight - 0.5f, 0, height - 1);
                int x0 = (int)sx, y0 = (int)sy;
                int x1 = Mathf.Min(x0 + 1, width - 1), y1 = Mathf.Min(y0 + 1, height - 1);
                Color lower = Color.Lerp((Color)pixels[y0 * width + x0], (Color)pixels[y0 * width + x1], sx - x0);
                Color upper = Color.Lerp((Color)pixels[y1 * width + x0], (Color)pixels[y1 * width + x1], sx - x0);
                Color sample = Color.Lerp(lower, upper, sy - y0);
                rgb[index] = sample.r;
                rgb[InputSize * InputSize + index] = sample.g;
                rgb[InputSize * InputSize * 2 + index] = sample.b;
            }
            return new Tensor<float>(new TensorShape(1, 3, InputSize, InputSize), rgb);
        }

        static Rect ToModelRect(Rect viewport, Rect photo, Rect image)
        {
            float left = image.x + (viewport.xMin - photo.xMin) * image.width / photo.width;
            float right = image.x + (viewport.xMax - photo.xMin) * image.width / photo.width;
            float top = image.y + (photo.yMax - viewport.yMax) * image.height / photo.height;
            float bottom = image.y + (photo.yMax - viewport.yMin) * image.height / photo.height;
            return Rect.MinMaxRect(left, top, right, bottom);
        }

        void ReadCandidates(Tensor<float> output, int stride, Rect target)
        {
            int side = InputSize / stride;
            CheckShape(output, side, HeadChannels);
            var data = output.AsReadOnlyNativeArray();
            int cellCount = side * side;
            float ReadBox(int channel, int cell) => data[channel * cellCount + cell];
            for (int i = 0; i < side * side; i++)
            {
                float confidence = BodyPoseDetector.Sigmoid(data[BoxChannels * cellCount + i]);
                if (confidence < 0.25f) continue;
                float x = (i % side + 0.5f) * stride;
                float y = (i / side + 0.5f) * stride;
                Rect bounds = Rect.MinMaxRect(
                    x - BodyPoseDetector.DistributionExpectation(ReadBox, 0, i) * stride,
                    y - BodyPoseDetector.DistributionExpectation(ReadBox, 1, i) * stride,
                    x + BodyPoseDetector.DistributionExpectation(ReadBox, 2, i) * stride,
                    y + BodyPoseDetector.DistributionExpectation(ReadBox, 3, i) * stride);
                float overlap = BodyPoseDetector.IoU(bounds, target);
                if (overlap < 0.25f) continue;
                float rank = overlap * 0.75f + confidence * 0.25f;
                int index = m_Candidates.FindIndex(candidate => rank > candidate.rank);
                if (index < 0) index = m_Candidates.Count;
                if (index >= MaxCandidates) continue;
                m_Candidates.Insert(index, new Candidate
                {
                    bounds = bounds, rank = rank, head = data, coefficientOffset = i,
                    coefficientPlaneSize = cellCount
                });
                if (m_Candidates.Count > MaxCandidates) m_Candidates.RemoveAt(MaxCandidates);
            }
        }

        static void CheckShape(Tensor<float> tensor, int side, int channels)
        {
            if (tensor == null || tensor.shape.rank != 4 || tensor.shape[0] != 1 ||
                tensor.shape[1] != channels || tensor.shape[2] != side || tensor.shape[3] != side)
                throw new InvalidOperationException("Unexpected segmentation tensor: " + tensor?.shape);
        }

        static void BuildPrototypeMask(Candidate candidate, NativeArray<float>.ReadOnly prototypes, float[] logits)
        {
            Array.Clear(logits, 0, logits.Length);
            for (int k = 0; k < MaskChannels; k++)
            {
                float coefficient = candidate.head[(BoxChannels + 1 + k) * candidate.coefficientPlaneSize +
                    candidate.coefficientOffset];
                int planeStart = k * logits.Length;
                for (int p = 0; p < logits.Length; p++)
                    logits[p] += coefficient * prototypes[planeStart + p];
            }
        }

        static float KeypointCoverage(PersonPoseData pose, Rect photo, Rect image, Rect bounds, float[] logits)
        {
            if (pose.keypoints == null || pose.keypointConfidence == null) return -1f;
            float total = 0f, covered = 0f;
            int count = Mathf.Min(pose.keypoints.Length, pose.keypointConfidence.Length);
            for (int i = 0; i < count; i++)
            {
                float confidence = pose.keypointConfidence[i];
                Vector2 point = pose.keypoints[i];
                if (confidence < 0.35f || !photo.Contains(point)) continue;
                float x = image.x + (point.x - photo.xMin) * image.width / photo.width;
                float y = image.y + (photo.yMax - point.y) * image.height / photo.height;
                total += confidence;
                if (bounds.Contains(new Vector2(x, y)))
                    covered += confidence * BodyPoseDetector.Sigmoid(SampleLogit(logits, x, y));
            }
            return total > 0f ? covered / total : -1f;
        }

        static bool TryRasterize(int width, int height, Rect image, Rect bounds, float[] logits, out byte[] alpha)
        {
            alpha = new byte[width * height];
            bool hasForeground = false;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float mx = image.x + (x + 0.5f) * image.width / width;
                float my = image.y + (1f - (y + 0.5f) / height) * image.height;
                float boxEdge = Mathf.Min(Mathf.Min(mx - bounds.xMin, bounds.xMax - mx),
                    Mathf.Min(my - bounds.yMin, bounds.yMax - my));
                if (boxEdge <= -1f) continue;
                float probability = BodyPoseDetector.Sigmoid(SampleLogit(logits, mx, my));
                float coverage = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.50f, 0.72f, probability));
                coverage *= Mathf.SmoothStep(0f, 1f, (boxEdge + 1f) * 0.5f);
                alpha[y * width + x] = (byte)Mathf.RoundToInt(coverage * 255f);
                hasForeground |= coverage >= 0.5f;
            }
            if (!hasForeground) alpha = null;
            return hasForeground;
        }

        static bool HasForeground(byte[] alpha)
        {
            if (alpha == null) return false;
            for (int i = 0; i < alpha.Length; i++)
                if (alpha[i] >= 128) return true;
            return false;
        }

        static void RestoreLimbCoverage(byte[] alpha, int width, int height, PersonPoseData person, Rect photo,
            Rect image, float[] logits)
        {
            if (person?.keypoints == null || person.keypointConfidence == null) return;
            int[,] links =
            {
                { 5, 7 }, { 7, 9 }, { 6, 8 }, { 8, 10 },
                { 11, 13 }, { 13, 15 }, { 12, 14 }, { 14, 16 }
            };
            for (int i = 0; i < links.GetLength(0); i++)
            {
                int a = links[i, 0], b = links[i, 1];
                if (a >= person.keypoints.Length || b >= person.keypoints.Length ||
                    a >= person.keypointConfidence.Length || b >= person.keypointConfidence.Length ||
                    person.keypointConfidence[a] < .35f || person.keypointConfidence[b] < .35f) continue;
                if (!photo.Contains(person.keypoints[a]) || !photo.Contains(person.keypoints[b])) continue;
                Vector2 start = ToPhotoPixel(person.keypoints[a], photo, width, height);
                Vector2 end = ToPhotoPixel(person.keypoints[b], photo, width, height);
                Vector2 segment = end - start;
                if (segment.sqrMagnitude < 1f) continue;
                int radius = Mathf.Clamp(Mathf.RoundToInt(Mathf.Min(width, height) * .012f), 2, 8);
                int left = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(start.x, end.x)) - radius);
                int right = Mathf.Min(width - 1, Mathf.CeilToInt(Mathf.Max(start.x, end.x)) + radius);
                int bottom = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(start.y, end.y)) - radius);
                int top = Mathf.Min(height - 1, Mathf.CeilToInt(Mathf.Max(start.y, end.y)) + radius);
                for (int y = bottom; y <= top; y++)
                for (int x = left; x <= right; x++)
                {
                    Vector2 offset = new Vector2(x, y) - start;
                    float along = Mathf.Clamp01(Vector2.Dot(offset, segment) / segment.sqrMagnitude);
                    if ((offset - along * segment).sqrMagnitude > radius * radius) continue;
                    int index = y * width + x;
                    if (alpha[index] >= 128) continue;
                    float mx = image.x + (x + .5f) * image.width / width;
                    float my = image.y + (1f - (y + .5f) / height) * image.height;
                    float probability = BodyPoseDetector.Sigmoid(SampleLogit(logits, mx, my));
                    float coverage = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.43f, .62f, probability));
                    alpha[index] = (byte)Mathf.Max(alpha[index], Mathf.RoundToInt(coverage * 255f));
                }
            }
        }

        static Vector2 ToPhotoPixel(Vector2 viewport, Rect photo, int width, int height)
        {
            return new Vector2(
                (viewport.x - photo.xMin) / photo.width * width - .5f,
                (viewport.y - photo.yMin) / photo.height * height - .5f);
        }

        static void RemoveUnconnectedRegions(byte[] alpha, int width, int height, PersonPoseData person, Rect photo)
        {
            if (person.keypoints == null || person.keypointConfidence == null) return;
            var keep = new bool[alpha.Length];
            var queue = new Queue<int>();
            for (int i = 0; i < person.keypoints.Length; i++)
            {
                if (person.keypointConfidence == null || i >= person.keypointConfidence.Length ||
                    person.keypointConfidence[i] < 0.30f) continue;
                Vector2 point = person.keypoints[i];
                if (!photo.Contains(point)) continue;
                Vector2 pixel = ToPhotoPixel(point, photo, width, height);
                int cx = Mathf.RoundToInt(pixel.x), cy = Mathf.RoundToInt(pixel.y);
                int radius = Mathf.Clamp(Mathf.RoundToInt(Mathf.Min(width, height) * .025f), 2, 12);
                for (int y = Mathf.Max(0, cy - radius); y <= Mathf.Min(height - 1, cy + radius); y++)
                for (int x = Mathf.Max(0, cx - radius); x <= Mathf.Min(width - 1, cx + radius); x++)
                {
                    int index = y * width + x;
                    if (alpha[index] < 96 || keep[index] || (new Vector2(x, y) - pixel).sqrMagnitude > radius * radius) continue;
                    keep[index] = true;
                    queue.Enqueue(index);
                }
            }

            if (queue.Count == 0) return;
            int[] offsets = { -1, 1, -width, width };
            while (queue.Count > 0)
            {
                int index = queue.Dequeue();
                int x = index % width, y = index / width;
                for (int i = 0; i < offsets.Length; i++)
                {
                    int neighbor = index + offsets[i];
                    if (neighbor < 0 || neighbor >= alpha.Length) continue;
                    int nx = neighbor % width, ny = neighbor / width;
                    if (Mathf.Abs(nx - x) > 1 || Mathf.Abs(ny - y) > 1 ||
                        keep[neighbor] || alpha[neighbor] < 96) continue;
                    keep[neighbor] = true;
                    queue.Enqueue(neighbor);
                }
            }

            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                if (keep[i]) continue;
                bool softEdge = alpha[i] < 96 &&
                    (x > 0 && keep[i - 1] || x + 1 < width && keep[i + 1] ||
                     y > 0 && keep[i - width] || y + 1 < height && keep[i + width]);
                if (!softEdge) alpha[i] = 0;
            }
        }

        static float SampleLogit(float[] logits, float modelX, float modelY)
        {
            float x = Mathf.Clamp(modelX / 4f - 0.5f, 0f, PrototypeSize - 1);
            float y = Mathf.Clamp(modelY / 4f - 0.5f, 0f, PrototypeSize - 1);
            int x0 = (int)x, y0 = (int)y;
            int x1 = Mathf.Min(x0 + 1, PrototypeSize - 1), y1 = Mathf.Min(y0 + 1, PrototypeSize - 1);
            float top = Mathf.Lerp(logits[y0 * PrototypeSize + x0], logits[y0 * PrototypeSize + x1], x - x0);
            float bottom = Mathf.Lerp(logits[y1 * PrototypeSize + x0], logits[y1 * PrototypeSize + x1], x - x0);
            return Mathf.Lerp(top, bottom, y - y0);
        }

        public void Dispose()
        {
            m_Worker?.Dispose();
            m_Worker = null;
        }
    }
}
