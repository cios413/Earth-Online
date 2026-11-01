using System.Collections;
using System.Collections.Generic;
using Unity.Barracuda;
using UnityEngine;

namespace EarthOL.Detect
{
    /// <summary>
    /// 只用来认「人」的检测器：YOLOv8n（320×320）跑 ONNX，但**只取 class 0 = person**，
    /// 其它 79 个类全部丢弃。比全类别检测快得多，也正是我们唯一需要模型的部分。
    ///
    /// 模型：Assets/Resources/Models/yolov8n.onnx（opset 9 + simplify + imgsz 320）
    /// </summary>
    public sealed class YoloPersonDetector
    {
        const int InputSize = 416;
        const int ClassCount = 80;
        const int Attributes = 4 + ClassCount;
        const int PersonClass = 0;
        const float Threshold = 0.25f;
        const float NmsThreshold = 0.45f;

        public bool Ready => m_Worker != null;
        public string Error { get; private set; }

        Model m_Model;
        IWorker m_Worker;
        readonly List<Rect> m_Boxes = new List<Rect>(32);
        readonly List<float> m_Scores = new List<float>(32);

        public IEnumerator Load()
        {
            var nnModel = Resources.Load<NNModel>("Models/yolov8n");

            if (nnModel == null)
            {
                Error = "找不到 NNModel（Assets/Resources/Models/yolov8n.onnx 未导入成功）";
                Debug.LogWarning("[Yolo] " + Error);
                yield break;
            }

            m_Model = ModelLoader.Load(nnModel);
            m_Worker = WorkerFactory.CreateWorker(WorkerFactory.Type.Auto, m_Model);

            Debug.Log($"[Yolo] 人检测模型就绪：{m_Worker.GetType().Name}，输入 {InputSize}");
            yield return null;
        }

        /// <summary>返回画面里检出的"人"（归一化包围盒，原点左下）。</summary>
        public DetectedObject[] DetectPersons(Color32[] pixels, int width, int height, int maxResults)
        {
            if (m_Worker == null || pixels == null || width <= 0 || height <= 0 || maxResults <= 0)
                return System.Array.Empty<DetectedObject>();

            using (var input = BuildInput(pixels, width, height))
            {
                m_Worker.Execute(input);

                var output = m_Worker.PeekOutput("output0") ?? m_Worker.PeekOutput();
                if (output == null) return System.Array.Empty<DetectedObject>();

                Decode(output);
            }

            return Finalize(maxResults);
        }

        Tensor BuildInput(Color32[] pixels, int width, int height)
        {
            var data = new float[InputSize * InputSize * 3];

            for (int y = 0; y < InputSize; y++)
            {
                int row = Mathf.Clamp(y * height / InputSize, 0, height - 1);
                int sy = (height - 1) - row;   // ReadPixels 原点在左下，YOLO 认为行 0 在顶部

                for (int x = 0; x < InputSize; x++)
                {
                    int sx = Mathf.Clamp(x * width / InputSize, 0, width - 1);
                    var c = pixels[sy * width + sx];

                    int index = (y * InputSize + x) * 3;
                    data[index] = c.r / 255f;
                    data[index + 1] = c.g / 255f;
                    data[index + 2] = c.b / 255f;
                }
            }

            return new Tensor(1, InputSize, InputSize, 3, data);
        }

        void Decode(Tensor output)
        {
            m_Boxes.Clear();
            m_Scores.Clear();

            var data = output.ToReadOnlyArray();
            int anchors = Mathf.Max(1, data.Length / Attributes);

            // 布局自动判定：正常置信度 <= 1；读到 300+ 说明那是框坐标，布局反了
            float ReadAt(int attr, int anchor, bool anchorMajor) =>
                anchorMajor ? data[anchor * Attributes + attr] : data[attr * anchors + anchor];

            float ProbeMax(bool anchorMajor)
            {
                float max = 0f;
                int sample = Mathf.Min(anchors, 300);

                for (int a = 0; a < sample; a++)
                    for (int c = 0; c < 8; c++)
                    {
                        float v = ReadAt(4 + c, a, anchorMajor);
                        if (v > max) max = v;
                    }

                return max;
            }

            bool anchorMajorLayout = ProbeMax(true) < ProbeMax(false);
            float At(int attr, int anchor) => ReadAt(attr, anchor, anchorMajorLayout);

            for (int a = 0; a < anchors; a++)
            {
                float score = At(4 + PersonClass, a);
                if (score < Threshold) continue;

                float cx = At(0, a), cy = At(1, a), w = At(2, a), h = At(3, a);
                if (w <= 2f || h <= 2f) continue;

                float nx = Mathf.Clamp01((cx - w * 0.5f) / InputSize);
                float nyTop = Mathf.Clamp01((cy - h * 0.5f) / InputSize);
                float nw = Mathf.Clamp01(w / InputSize);
                float nh = Mathf.Clamp01(h / InputSize);

                m_Boxes.Add(new Rect(nx, 1f - nyTop - nh, nw, nh));
                m_Scores.Add(score);
            }
        }

        DetectedObject[] Finalize(int maxResults)
        {
            var order = new List<int>();
            for (int i = 0; i < m_Scores.Count; i++) order.Add(i);
            order.Sort((a, b) => m_Scores[b].CompareTo(m_Scores[a]));

            var kept = new List<int>(maxResults);

            foreach (var index in order)
            {
                if (kept.Count >= maxResults) break;

                bool overlaps = false;
                foreach (var other in kept)
                {
                    if (IoU(m_Boxes[index], m_Boxes[other]) > NmsThreshold) { overlaps = true; break; }
                }

                if (!overlaps) kept.Add(index);
            }

            var result = new DetectedObject[kept.Count];

            for (int i = 0; i < kept.Count; i++)
            {
                result[i] = new DetectedObject
                {
                    label = "人",
                    rect = m_Boxes[kept[i]],
                    confidence = m_Scores[kept[i]]
                };
            }

            return result;
        }

        static float IoU(Rect a, Rect b)
        {
            float x = Mathf.Max(0f, Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin));
            float y = Mathf.Max(0f, Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin));
            float inter = x * y;
            float union = a.width * a.height + b.width * b.height - inter;
            return union <= 0f ? 0f : inter / union;
        }
    }
}
