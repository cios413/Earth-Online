using System.Collections.Generic;
using UnityEngine;

namespace YoloSegmentation
{
    /// <summary>
    /// Extracts the closed outer contour of an instance mask and computes the mask area
    /// centroid. The first version ignores holes inside the outer boundary.
    /// </summary>
    public static class MaskContourProcessor
    {
        public static bool TryExtractOuterContour(
            byte[] mask,
            int width,
            int height,
            int gridStep,
            float simplifyEpsilon,
            int smoothPasses,
            out Vector2[] contour)
        {
            contour = null;
            if (mask == null || mask.Length != width * height)
                return false;

            var loops = ContourExtractor.Extract(mask, width, height, gridStep, simplifyEpsilon, smoothPasses);

            Vector2[] best = null;
            var bestArea = 0f;
            foreach (var loop in loops)
            {
                if (loop == null || loop.Length < 3)
                    continue;

                var area = Mathf.Abs(SignedArea(loop));
                if (area > bestArea)
                {
                    bestArea = area;
                    best = loop;
                }
            }

            if (best == null)
                return false;

            var list = new List<Vector2>(best);

            // Remove a duplicate closing point left by the marching-squares connector.
            if (list.Count > 1 && Vector2.Distance(list[0], list[list.Count - 1]) < 0.5f)
                list.RemoveAt(list.Count - 1);

            if (list.Count < 3)
                return false;

            // Normalize to counter-clockwise order in the bottom-up image convention.
            if (SignedArea(list) < 0f)
                list.Reverse();

            contour = list.ToArray();
            return true;
        }

        public static float SignedArea(IReadOnlyList<Vector2> polygon)
        {
            var area = 0f;
            for (var i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Count];
                area += a.x * b.y - b.x * a.y;
            }
            return area * 0.5f;
        }
    }
}
