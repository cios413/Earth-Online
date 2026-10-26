using System.Collections.Generic;
using UnityEngine;

namespace YoloSegmentation
{
    /// <summary>
    /// Phase D: extracts object boundary polylines from a binary segmentation mask.
    /// It uses marching squares on a coarse grid, connects the resulting segments into
    /// loops, then simplifies and smooths them. Output points are in mask pixel
    /// coordinates (x: 0..width-1, y: 0..height-1 bottom-up, same as Texture2D.GetPixels32).
    /// </summary>
    public static class ContourExtractor
    {
        // Marching squares edge table. Each entry is a flat list of edge pairs.
        // Edge ids: 0 bottom, 1 right, 2 top, 3 left.
        static readonly int[][] Cases =
        {
            new int[0],          // 0
            new[] { 3, 0 },      // 1
            new[] { 0, 1 },      // 2
            new[] { 3, 1 },      // 3
            new[] { 1, 2 },      // 4
            new[] { 3, 0, 1, 2 },// 5
            new[] { 0, 2 },      // 6
            new[] { 3, 2 },      // 7
            new[] { 2, 3 },      // 8
            new[] { 2, 0 },      // 9
            new[] { 0, 3, 2, 1 },// 10
            new[] { 2, 1 },      // 11
            new[] { 1, 3 },      // 12
            new[] { 1, 0 },      // 13
            new[] { 0, 3 },      // 14
            new int[0]           // 15
        };

        public static List<Vector2[]> Extract(
            byte[] mask, int width, int height, int gridStep, float simplifyEpsilon, int smoothPasses = 2)
        {
            var result = new List<Vector2[]>();
            if (mask == null || mask.Length != width * height || width < 2 || height < 2)
                return result;

            gridStep = Mathf.Max(1, gridStep);

            var gw = Mathf.Max(2, Mathf.CeilToInt((float)width / gridStep) + 1);
            var gh = Mathf.Max(2, Mathf.CeilToInt((float)height / gridStep) + 1);

            var occupied = new bool[gw * gh];
            for (var gy = 0; gy < gh; gy++)
            {
                var y = Mathf.Min(gy * gridStep, height - 1);
                for (var gx = 0; gx < gw; gx++)
                {
                    var x = Mathf.Min(gx * gridStep, width - 1);
                    occupied[gy * gw + gx] = mask[y * width + x] != 0;
                }
            }

            var segments = new List<Vector2[]>();

            for (var gy = 0; gy < gh - 1; gy++)
            {
                for (var gx = 0; gx < gw - 1; gx++)
                {
                    var a = occupied[gy * gw + gx];           // bottom-left
                    var b = occupied[gy * gw + gx + 1];       // bottom-right
                    var c = occupied[(gy + 1) * gw + gx + 1]; // top-right
                    var d = occupied[(gy + 1) * gw + gx];     // top-left

                    var caseIndex = (a ? 1 : 0) | (b ? 2 : 0) | (c ? 4 : 0) | (d ? 8 : 0);
                    var edges = Cases[caseIndex];
                    for (var i = 0; i + 1 < edges.Length; i += 2)
                    {
                        segments.Add(new[]
                        {
                            EdgePoint(edges[i], gx, gy),
                            EdgePoint(edges[i + 1], gx, gy)
                        });
                    }
                }
            }

            var loops = ConnectSegments(segments);

            foreach (var loop in loops)
            {
                var simplified = Simplify(new List<Vector2>(loop), simplifyEpsilon);
                if (simplified.Count < 3)
                    continue;

                var smoothed = Smooth(simplified, smoothPasses);
                var contour = new Vector2[smoothed.Count];
                for (var i = 0; i < smoothed.Count; i++)
                    contour[i] = smoothed[i] * gridStep;

                result.Add(contour);
            }

            return result;
        }

        static Vector2 EdgePoint(int edge, int gx, int gy)
        {
            switch (edge)
            {
                case 0: return new Vector2(gx + 0.5f, gy);
                case 1: return new Vector2(gx + 1f, gy + 0.5f);
                case 2: return new Vector2(gx + 0.5f, gy + 1f);
                default: return new Vector2(gx, gy + 0.5f);
            }
        }

        static List<Vector2[]> ConnectSegments(List<Vector2[]> segments)
        {
            var result = new List<Vector2[]>();
            if (segments.Count == 0)
                return result;

            var used = new bool[segments.Count];
            var adjacency = new Dictionary<PointKey, List<int>>();

            for (var i = 0; i < segments.Count; i++)
            {
                AddAdjacency(adjacency, segments[i][0], i);
                AddAdjacency(adjacency, segments[i][1], i);
            }

            for (var i = 0; i < segments.Count; i++)
            {
                if (used[i])
                    continue;

                used[i] = true;
                var loop = new List<Vector2> { segments[i][0], segments[i][1] };
                var currentPoint = segments[i][1];

                while (true)
                {
                    var key = ToKey(currentPoint);
                    var nextIndex = -1;
                    if (adjacency.TryGetValue(key, out var list))
                    {
                        foreach (var candidate in list)
                        {
                            if (!used[candidate])
                            {
                                nextIndex = candidate;
                                break;
                            }
                        }
                    }

                    if (nextIndex < 0)
                        break;

                    used[nextIndex] = true;
                    var next = segments[nextIndex];
                    var nextPoint = ToKey(next[0]).Equals(key) ? next[1] : next[0];
                    currentPoint = nextPoint;
                    loop.Add(currentPoint);
                }

                result.Add(loop.ToArray());
            }

            return result;
        }

        static void AddAdjacency(Dictionary<PointKey, List<int>> adjacency, Vector2 point, int segmentIndex)
        {
            var key = ToKey(point);
            if (!adjacency.TryGetValue(key, out var list))
            {
                list = new List<int>();
                adjacency[key] = list;
            }

            list.Add(segmentIndex);
        }

        static PointKey ToKey(Vector2 p)
        {
            return new PointKey(Mathf.RoundToInt(p.x * 2f), Mathf.RoundToInt(p.y * 2f));
        }

        readonly struct PointKey
        {
            readonly int x;
            readonly int y;

            public PointKey(int x, int y)
            {
                this.x = x;
                this.y = y;
            }

            public bool Equals(PointKey other) => x == other.x && y == other.y;
            public override bool Equals(object obj) => obj is PointKey other && Equals(other);
            public override int GetHashCode() => (x * 397) ^ y;
        }

        static List<Vector2> Simplify(List<Vector2> points, float epsilon)
        {
            if (points.Count <= 2)
                return new List<Vector2>(points);

            var keep = new bool[points.Count];
            keep[0] = true;
            keep[points.Count - 1] = true;
            SimplifyRange(points, 0, points.Count - 1, Mathf.Max(0.01f, epsilon), keep);

            var result = new List<Vector2>();
            for (var i = 0; i < points.Count; i++)
            {
                if (keep[i])
                    result.Add(points[i]);
            }

            return result;
        }

        static void SimplifyRange(List<Vector2> points, int start, int end, float epsilon, bool[] keep)
        {
            if (end <= start + 1)
                return;

            var maxDistance = 0f;
            var maxIndex = -1;
            var line = points[end] - points[start];
            var lineLength = line.magnitude;

            for (var i = start + 1; i < end; i++)
            {
                var distance = lineLength > 0.0001f
                    ? Vector3.Cross(points[i] - points[start], line).magnitude / lineLength
                    : (points[i] - points[start]).magnitude;

                if (distance > maxDistance)
                {
                    maxDistance = distance;
                    maxIndex = i;
                }
            }

            if (maxIndex >= 0 && maxDistance > epsilon)
            {
                keep[maxIndex] = true;
                SimplifyRange(points, start, maxIndex, epsilon, keep);
                SimplifyRange(points, maxIndex, end, epsilon, keep);
            }
        }

        static List<Vector2> Smooth(List<Vector2> points, int passes)
        {
            if (points.Count < 3)
                return new List<Vector2>(points);

            var current = new List<Vector2>(points);
            var next = new List<Vector2>(points);

            for (var pass = 0; pass < passes; pass++)
            {
                for (var i = 0; i < current.Count; i++)
                {
                    var prev = current[(i - 1 + current.Count) % current.Count];
                    var self = current[i];
                    var following = current[(i + 1) % current.Count];
                    next[i] = (prev + self + following) / 3f;
                }

                var tmp = current;
                current = next;
                next = tmp;
            }

            return current;
        }
    }
}
