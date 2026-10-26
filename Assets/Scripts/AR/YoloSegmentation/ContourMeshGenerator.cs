using System.Collections.Generic;
using UnityEngine;

namespace YoloSegmentation
{
    /// <summary>
    /// Triangulates a simple (possibly concave, hole-free) 2D polygon with ear clipping and
    /// builds a flat mesh whose local origin is the given image-space center.
    /// </summary>
    public static class ContourMeshGenerator
    {
        public static bool TryTriangulate(IReadOnlyList<Vector2> polygon, out int[] triangles)
        {
            triangles = null;
            if (polygon == null || polygon.Count < 3)
                return false;

            var points = new List<Vector2>(polygon);
            if (SignedArea(points) < 0f)
                points.Reverse();

            var indices = new List<int>(points.Count);
            for (var i = 0; i < points.Count; i++)
                indices.Add(i);

            var result = new List<int>();
            var maxGuard = points.Count * points.Count + points.Count;
            var guard = 0;

            while (indices.Count > 3)
            {
                var count = indices.Count;
                var earFound = false;

                for (var k = 0; k < count; k++)
                {
                    var prev = indices[(k - 1 + count) % count];
                    var cur = indices[k];
                    var next = indices[(k + 1) % count];

                    var a = points[prev];
                    var b = points[cur];
                    var c = points[next];

                    if (!IsConvex(a, b, c))
                        continue;
                    if (AnyPointInsideTriangle(points, indices, prev, cur, next))
                        continue;

                    result.Add(prev);
                    result.Add(cur);
                    result.Add(next);
                    indices.RemoveAt(k);
                    earFound = true;
                    break;
                }

                if (!earFound)
                    return false;
                if (++guard > maxGuard)
                    return false;
            }

            result.Add(indices[0]);
            result.Add(indices[1]);
            result.Add(indices[2]);
            triangles = result.ToArray();
            return true;
        }

        /// <summary>
        /// Builds a flat mesh in the XY plane with the local origin at centerImage. The Y
        /// axis follows the bottom-up image convention (image up = mesh up).
        /// </summary>
        public static bool BuildMesh(
            IReadOnlyList<Vector2> polygon,
            Vector2 centerImage,
            float pixelsToUnits,
            float thickness,
            int sourceWidth,
            int sourceHeight,
            out Mesh mesh)
        {
            mesh = null;
            if (!TryTriangulate(polygon, out var faceTriangles))
                return false;

            var n = polygon.Count;
            var local = new Vector2[n];
            for (var i = 0; i < n; i++)
            {
                local[i] = (polygon[i] - centerImage) * pixelsToUnits;
            }

            var half = Mathf.Max(0f, thickness) * 0.5f;
            var uvScaleX = 1f / Mathf.Max(1, sourceWidth);
            var uvScaleY = 1f / Mathf.Max(1, sourceHeight);

            var vertices = new List<Vector3>(n * 6);
            var normals = new List<Vector3>(n * 6);
            var uvs = new List<Vector2>(n * 6);
            var triangles = new List<int>();

            // Front face (+Z).
            var frontBase = vertices.Count;
            for (var i = 0; i < n; i++)
            {
                vertices.Add(new Vector3(local[i].x, local[i].y, half));
                normals.Add(Vector3.forward);
                uvs.Add(new Vector2(polygon[i].x * uvScaleX, polygon[i].y * uvScaleY));
            }
            foreach (var t in faceTriangles)
                triangles.Add(frontBase + t);

            // Back face (-Z) with reversed winding.
            var backBase = vertices.Count;
            for (var i = 0; i < n; i++)
            {
                vertices.Add(new Vector3(local[i].x, local[i].y, -half));
                normals.Add(Vector3.back);
                uvs.Add(new Vector2(polygon[i].x * uvScaleX, polygon[i].y * uvScaleY));
            }
            for (var t = faceTriangles.Length - 1; t >= 2; t -= 3)
            {
                triangles.Add(backBase + faceTriangles[t]);
                triangles.Add(backBase + faceTriangles[t - 1]);
                triangles.Add(backBase + faceTriangles[t - 2]);
            }

            // Side walls.
            for (var i = 0; i < n; i++)
            {
                var j = (i + 1) % n;
                var edge = local[j] - local[i];
                var outward = new Vector2(edge.y, -edge.x).normalized;
                var wallNormal = new Vector3(outward.x, outward.y, 0f);

                var wallBase = vertices.Count;
                vertices.Add(new Vector3(local[i].x, local[i].y, half));
                vertices.Add(new Vector3(local[j].x, local[j].y, half));
                vertices.Add(new Vector3(local[j].x, local[j].y, -half));
                vertices.Add(new Vector3(local[i].x, local[i].y, -half));

                normals.Add(wallNormal);
                normals.Add(wallNormal);
                normals.Add(wallNormal);
                normals.Add(wallNormal);

                var uv0 = new Vector2(polygon[i].x * uvScaleX, polygon[i].y * uvScaleY);
                var uv1 = new Vector2(polygon[j].x * uvScaleX, polygon[j].y * uvScaleY);
                uvs.Add(uv0);
                uvs.Add(uv1);
                uvs.Add(uv1);
                uvs.Add(uv0);

                triangles.Add(wallBase);
                triangles.Add(wallBase + 1);
                triangles.Add(wallBase + 2);
                triangles.Add(wallBase);
                triangles.Add(wallBase + 2);
                triangles.Add(wallBase + 3);
            }

            mesh = new Mesh();
            mesh.name = "SelectedObjectMesh";
            mesh.vertices = vertices.ToArray();
            mesh.normals = normals.ToArray();
            mesh.uv = uvs.ToArray();
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateBounds();
            return true;
        }

        static float SignedArea(IReadOnlyList<Vector2> polygon)
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

        static bool IsConvex(Vector2 a, Vector2 b, Vector2 c)
        {
            return Cross(b - a, c - b) > 0f;
        }

        static float Cross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        static bool AnyPointInsideTriangle(List<Vector2> points, List<int> indices, int i0, int i1, int i2)
        {
            var a = points[i0];
            var b = points[i1];
            var c = points[i2];

            for (var i = 0; i < indices.Count; i++)
            {
                var index = indices[i];
                if (index == i0 || index == i1 || index == i2)
                    continue;
                if (PointInTriangle(points[index], a, b, c))
                    return true;
            }

            return false;
        }

        static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            var d1 = Cross(b - a, p - a);
            var d2 = Cross(c - b, p - b);
            var d3 = Cross(a - c, p - c);

            var hasNegative = d1 < 0f || d2 < 0f || d3 < 0f;
            var hasPositive = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(hasNegative && hasPositive);
        }
    }
}
