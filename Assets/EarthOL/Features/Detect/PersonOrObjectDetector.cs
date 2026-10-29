using UnityEngine;

namespace EarthOL.Detect
{
    /// <summary>
    /// 只区分「人」和「物体」的轻量检测器 —— 不依赖任何模型，单帧 1~2 ms。
    ///
    /// 判人依据（三条同时看）：
    ///   1) 肤色占比高；
    ///   2) 竖直比例（高 > 宽）—— 人是竖长条，桌面物品通常偏扁平；
    ///   3) 有运动（和上一帧差异大）。
    /// 其余成片区域判为「物体」。一次最多输出 3 个候选。
    /// </summary>
    public sealed class PersonOrObjectDetector : IObjectDetector
    {
        const int GridX = 16;
        const int GridY = 12;

        public string Name => "人/物体分类（无模型）";

        readonly float[] m_Skin = new float[GridX * GridY];
        readonly float[] m_Motion = new float[GridX * GridY];
        readonly float[] m_Salient = new float[GridX * GridY];
        readonly bool[] m_Visited = new bool[GridX * GridY];

        Color32[] m_Previous;
        int m_PreviousWidth;
        int m_PreviousHeight;

        public DetectedObject[] Detect(Color32[] pixels, int width, int height, int maxResults)
        {
            if (pixels == null || width <= 0 || height <= 0 || maxResults <= 0)
                return System.Array.Empty<DetectedObject>();

            Color32 background = EstimateBackground(pixels, width, height);
            BuildGrids(pixels, width, height, background);

            var results = new System.Collections.Generic.List<DetectedObject>();
            System.Array.Clear(m_Visited, 0, m_Visited.Length);

            for (int i = 0; i < m_Salient.Length; i++)
            {
                if (m_Visited[i] || m_Salient[i] < 0.12f) continue;

                if (!Flood(out int minX, out int minY, out int maxX, out int maxY, out int cells,
                           out float skin, out float motion, i))
                    continue;

                int cellW = maxX - minX + 1;
                int cellH = maxY - minY + 1;

                if (cells < 1 || cells > 110) continue;
                if (cellW < 2 || cellH < 2) continue;

                float fill = cells / (float)(cellW * cellH);
                if (fill < 0.15f) continue;

                float skinRatio = skin / cells;
                float motionRatio = motion / cells;
                float aspect = cellH / (float)cellW;

                // 判人：肤色占比高 + 竖直 + 有运动
                bool isPerson = skinRatio >= 0.30f && aspect >= 1.25f;

                results.Add(new DetectedObject
                {
                    label = isPerson ? "人" : "物体",
                    rect = new Rect(
                        minX / (float)GridX,
                        minY / (float)GridY,
                        cellW / (float)GridX,
                        cellH / (float)GridY),
                    confidence = Mathf.Clamp01(
                        (isPerson ? 0.5f + skinRatio * 0.5f : 0.4f + fill * 0.4f) + motionRatio * 0.2f)
                });
            }

            // 人优先，其次按置信度
            results.Sort((a, b) =>
            {
                int byKind = (b.label == "人").CompareTo(a.label == "人");
                return byKind != 0 ? byKind : b.confidence.CompareTo(a.confidence);
            });

            int count = Mathf.Min(maxResults, results.Count);
            var output = new DetectedObject[count];
            for (int i = 0; i < count; i++) output[i] = results[i];

            Remember(pixels, width, height);
            return output;
        }

        // ---------------------------------------------------------------- 网格统计

        void BuildGrids(Color32[] pixels, int width, int height, Color32 background)
        {
            int cellW = Mathf.Max(1, width / GridX);
            int cellH = Mathf.Max(1, height / GridY);

            bool hasPrevious = m_Previous != null && m_PreviousWidth == width && m_PreviousHeight == height;

            for (int gy = 0; gy < GridY; gy++)
            {
                for (int gx = 0; gx < GridX; gx++)
                {
                    int index = gy * GridX + gx;
                    int skin = 0, salient = 0, moved = 0, total = 0;

                    for (int y = gy * cellH; y < Mathf.Min(height, (gy + 1) * cellH); y += 2)
                    {
                        for (int x = gx * cellW; x < Mathf.Min(width, (gx + 1) * cellW); x += 2)
                        {
                            var c = pixels[y * width + x];
                            total++;

                            if (IsSkin(c)) skin++;

                            // 用"局部边缘/对比度"代替"与全局背景色的差异"：
                            // 室内桌面常常整片同色，全局背景法会完全失效；但物体一定有边缘，平面没有。
                            if (x + 2 < width)
                            {
                                var neighbour = pixels[y * width + x + 2];
                                if (LumaDiff(c, neighbour) > 0.05f) salient++;
                            }

                            if (hasPrevious && ColorDistance(c, m_Previous[y * width + x]) > 0.10f) moved++;
                        }
                    }

                    if (total == 0) { m_Skin[index] = m_Motion[index] = m_Salient[index] = 0f; continue; }

                    m_Skin[index] = skin / (float)total;
                    m_Motion[index] = moved / (float)total;
                    m_Salient[index] = salient / (float)total;
                }
            }
        }

        bool Flood(out int minX, out int minY, out int maxX, out int maxY,
                   out int cells, out float skin, out float motion, int start)
        {
            minX = int.MaxValue; minY = int.MaxValue; maxX = -1; maxY = -1;
            cells = 0; skin = 0f; motion = 0f;

            var stack = new System.Collections.Generic.Stack<int>();
            stack.Push(start);
            m_Visited[start] = true;

            while (stack.Count > 0)
            {
                int index = stack.Pop();
                int x = index % GridX;
                int y = index / GridX;

                cells++;
                skin += m_Skin[index];
                motion += m_Motion[index];

                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;

                TryPush(x - 1, y, stack);
                TryPush(x + 1, y, stack);
                TryPush(x, y - 1, stack);
                TryPush(x, y + 1, stack);
            }

            return cells > 0;
        }

        void TryPush(int x, int y, System.Collections.Generic.Stack<int> stack)
        {
            if (x < 0 || y < 0 || x >= GridX || y >= GridY) return;

            int index = y * GridX + x;
            if (m_Visited[index] || m_Salient[index] < 0.12f) return;

            m_Visited[index] = true;
            stack.Push(index);
        }

        // ---------------------------------------------------------------- 工具

        void Remember(Color32[] pixels, int width, int height)
        {
            if (m_Previous == null || m_PreviousWidth != width || m_PreviousHeight != height)
                m_Previous = new Color32[pixels.Length];

            System.Array.Copy(pixels, m_Previous, pixels.Length);
            m_PreviousWidth = width;
            m_PreviousHeight = height;
        }

        static Color32 EstimateBackground(Color32[] pixels, int width, int height)
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

        /// <summary>亮度差（0~1），用于边缘判定。</summary>
        static float LumaDiff(Color32 a, Color32 b)
        {
            float la = (0.299f * a.r + 0.587f * a.g + 0.114f * a.b) / 255f;
            float lb = (0.299f * b.r + 0.587f * b.g + 0.114f * b.b) / 255f;
            return Mathf.Abs(la - lb);
        }

        /// <summary>经典肤色判据（RGB 规则），对光照变化比较宽容。</summary>
        static bool IsSkin(Color32 c)
        {
            int max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            int min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));

            return c.r > 95 && c.g > 40 && c.b > 20 &&
                   c.r > c.g && c.r > c.b &&
                   (max - min) > 15 &&
                   Mathf.Abs(c.r - c.g) > 15;
        }

        static float ColorDistance(Color32 a, Color32 b)
        {
            float dr = (a.r - b.r) / 255f;
            float dg = (a.g - b.g) / 255f;
            float db = (a.b - b.b) / 255f;
            return Mathf.Sqrt(dr * dr + dg * dg + db * db);
        }
    }
}
