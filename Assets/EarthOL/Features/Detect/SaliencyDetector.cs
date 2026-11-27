using UnityEngine;

namespace EarthOL.Detect
{
    /// <summary>
    /// 不依赖任何模型的"明显目标"检测器：把画面缩到 16×12 的格子，用画面边缘的主色调当作背景，
    /// 找出和背景差异明显、又成片连通的区域，按面积取最大的几个。
    ///
    /// 它不是语义识别（分辨不出"杯子/书"），但足以支撑"自动发现画面里的独立物体 → 点选"这个流程，
    /// 并且完全离线、零模型、几乎不耗性能。等有了 .onnx 模型，换成 YoloDetector 即可。
    /// </summary>
    public sealed class SaliencyDetector : IObjectDetector
    {
        const int GridX = 16;
        const int GridY = 12;

        readonly float threshold;
        readonly bool[] m_Cells = new bool[GridX * GridY];
        readonly bool[] m_Visited = new bool[GridX * GridY];

        public string Name => "显著性检测（无模型）";

        public SaliencyDetector(float colorThreshold = 0.18f)
        {
            threshold = Mathf.Clamp(colorThreshold, 0.05f, 0.6f);
        }

        public DetectedObject[] Detect(Color32[] pixels, int width, int height, int maxResults)
        {
            if (pixels == null || width <= 0 || height <= 0 || maxResults <= 0)
                return System.Array.Empty<DetectedObject>();

            Color32 background = EstimateBackground(pixels, width, height);
            BuildCells(pixels, width, height, background);

            var results = new System.Collections.Generic.List<DetectedObject>();
            System.Array.Clear(m_Visited, 0, m_Visited.Length);

            for (int i = 0; i < m_Cells.Length; i++)
            {
                if (!m_Cells[i] || m_Visited[i]) continue;

                if (FloodFill(i, out int minX, out int minY, out int maxX, out int maxY, out int cellCount))
                {
                    int cellsWide = maxX - minX + 1;
                    int cellsHigh = maxY - minY + 1;
                    int boxCells = cellsWide * cellsHigh;

                    // 太小的不要；过于细长（像桌沿、线缆）的也不要
                    if (cellCount < 2 || cellCount > 120) continue;
                    if (cellsWide < 2 || cellsHigh < 2) continue;

                    float fill = cellCount / (float)boxCells;
                    if (fill < 0.35f) continue;

                    var rect = new Rect(
                        minX / (float)GridX,
                        minY / (float)GridY,
                        cellsWide / (float)GridX,
                        cellsHigh / (float)GridY);

                    results.Add(new DetectedObject
                    {
                        label = "",
                        rect = rect,
                        confidence = Mathf.Clamp01(cellCount / 24f) * Mathf.Lerp(0.6f, 1f, fill)
                    });
                }
            }

            results.Sort((a, b) => b.confidence.CompareTo(a.confidence));

            int count = Mathf.Min(maxResults, results.Count);
            var output = new DetectedObject[count];

            for (int i = 0; i < count; i++)
            {
                var item = results[i];
                item.label = "物体 " + (i + 1);
                output[i] = item;
            }

            return output;
        }

        /// <summary>用画面四周边带的中位数当背景色（桌面/墙面通常占据边缘）。</summary>
        Color32 EstimateBackground(Color32[] pixels, int width, int height)
        {
            int band = Mathf.Max(1, Mathf.RoundToInt(height * 0.12f));
            long r = 0, g = 0, b = 0, n = 0;

            for (int y = 0; y < height; y++)
            {
                bool edgeRow = y < band || y >= height - band;

                for (int x = 0; x < width; x++)
                {
                    if (!edgeRow && x >= band && x < width - band) continue;

                    var c = pixels[y * width + x];
                    r += c.r; g += c.g; b += c.b; n++;
                }
            }

            if (n == 0) return new Color32(128, 128, 128, 255);

            return new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 255);
        }

        void BuildCells(Color32[] pixels, int width, int height, Color32 background)
        {
            int cellW = Mathf.Max(1, width / GridX);
            int cellH = Mathf.Max(1, height / GridY);

            for (int gy = 0; gy < GridY; gy++)
            {
                for (int gx = 0; gx < GridX; gx++)
                {
                    int hits = 0, total = 0;

                    for (int y = gy * cellH; y < Mathf.Min(height, (gy + 1) * cellH); y += 2)
                    {
                        for (int x = gx * cellW; x < Mathf.Min(width, (gx + 1) * cellW); x += 2)
                        {
                            if (Distance(pixels[y * width + x], background) > threshold) hits++;
                            total++;
                        }
                    }

                    m_Cells[gy * GridX + gx] = total > 0 && hits >= total * 0.5f;
                }
            }
        }

        static float Distance(Color32 a, Color32 b)
        {
            float dr = (a.r - b.r) / 255f;
            float dg = (a.g - b.g) / 255f;
            float db = (a.b - b.b) / 255f;
            return Mathf.Sqrt(dr * dr + dg * dg + db * db);
        }

        bool FloodFill(int start, out int minX, out int minY, out int maxX, out int maxY, out int count)
        {
            minX = int.MaxValue; minY = int.MaxValue; maxX = -1; maxY = -1; count = 0;

            var stack = new System.Collections.Generic.Stack<int>();
            stack.Push(start);
            m_Visited[start] = true;

            while (stack.Count > 0)
            {
                int index = stack.Pop();
                int x = index % GridX;
                int y = index / GridX;

                count++;
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;

                TryPush(x - 1, y, stack);
                TryPush(x + 1, y, stack);
                TryPush(x, y - 1, stack);
                TryPush(x, y + 1, stack);
            }

            return count > 0;
        }

        void TryPush(int x, int y, System.Collections.Generic.Stack<int> stack)
        {
            if (x < 0 || y < 0 || x >= GridX || y >= GridY) return;

            int index = y * GridX + x;
            if (!m_Cells[index] || m_Visited[index]) return;

            m_Visited[index] = true;
            stack.Push(index);
        }
    }
}
