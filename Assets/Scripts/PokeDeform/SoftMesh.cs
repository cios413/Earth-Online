using System.Collections.Generic;
using UnityEngine;

namespace EarthOL.Deform
{
    /// <summary>运行时网格形状。顶点太少的网格（比如 Unity 自带的 24 顶点 Cube）戳不出凹陷，
    /// 所以这里提供两种高密度替代网格。</summary>
    public enum SoftMeshShape
    {
        KeepOriginal = 0,
        RoundedCube = 1,
        Sphere = 2
    }

    /// <summary>
    /// 生成"可形变"用的高密度网格。都是 1 单位尺寸、以原点为中心、内接半径 0.5，
    /// 和 Unity 自带 Cube / Sphere 的包围盒一致，所以换上去以后原有 Collider 依然贴合。
    /// </summary>
    public static class SoftMesh
    {
        // 顶点焊接精度：不同面生成的同一个角点合并成一个，法线才会是平滑的
        const float kWeldScale = 10000f;

        public static Mesh Create(SoftMeshShape shape, int level, float rounding)
        {
            level = Mathf.Clamp(level, 0, 4);

            switch (shape)
            {
                case SoftMeshShape.RoundedCube:
                {
                    int segments = Mathf.Clamp(2 + level * 3, 2, 16);
                    return CreateRoundedCube(segments, Mathf.Clamp01(rounding));
                }
                case SoftMeshShape.Sphere:
                    return CreateIcosphere(level);
                default:
                    return null;
            }
        }

        // ---------------------------------------------------------------- 圆角立方体

        static Mesh CreateRoundedCube(int segments, float rounding)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var lookup = new Dictionary<Vector3Int, int>();

            // 6 个面：法线 + 面内两个切向
            var faceNormals = new[]
            {
                Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back
            };

            foreach (var faceNormal in faceNormals)
            {
                BuildBasis(faceNormal, out var tangentU, out var tangentV);

                var grid = new int[segments + 1, segments + 1];

                for (int y = 0; y <= segments; y++)
                {
                    for (int x = 0; x <= segments; x++)
                    {
                        float fu = (float)x / segments - 0.5f;
                        float fv = (float)y / segments - 0.5f;

                        // 立方体表面上的点
                        var point = faceNormal * 0.5f + tangentU * fu + tangentV * fv;

                        // 朝球面靠拢 = 圆角。rounding=0 是硬立方体，=1 是球
                        var spherePoint = point.normalized * 0.5f;
                        point = Vector3.Lerp(point, spherePoint, rounding);

                        grid[x, y] = GetOrAddVertex(vertices, lookup, point);
                    }
                }

                for (int y = 0; y < segments; y++)
                {
                    for (int x = 0; x < segments; x++)
                    {
                        int a = grid[x, y];
                        int b = grid[x + 1, y];
                        int c = grid[x + 1, y + 1];
                        int d = grid[x, y + 1];

                        triangles.Add(a); triangles.Add(b); triangles.Add(c);
                        triangles.Add(a); triangles.Add(c); triangles.Add(d);
                    }
                }
            }

            return BuildMesh("PokeRoundedCube", vertices, triangles, fixWinding: true, normals: null);
        }

        static void BuildBasis(Vector3 normal, out Vector3 tangentU, out Vector3 tangentV)
        {
            var helper = Mathf.Abs(normal.y) > 0.9f ? Vector3.right : Vector3.up;
            tangentU = Vector3.Normalize(Vector3.Cross(helper, normal));
            tangentV = Vector3.Normalize(Vector3.Cross(normal, tangentU));
        }

        static int GetOrAddVertex(List<Vector3> vertices, Dictionary<Vector3Int, int> lookup, Vector3 point)
        {
            var key = new Vector3Int(
                Mathf.RoundToInt(point.x * kWeldScale),
                Mathf.RoundToInt(point.y * kWeldScale),
                Mathf.RoundToInt(point.z * kWeldScale));

            if (lookup.TryGetValue(key, out int index)) return index;

            index = vertices.Count;
            vertices.Add(point);
            lookup[key] = index;
            return index;
        }

        // ---------------------------------------------------------------- 球（正二十面体细分）

        static Mesh CreateIcosphere(int subdivisions)
        {
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;

            var vertices = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
            };

            for (int i = 0; i < vertices.Count; i++)
                vertices[i] = vertices[i].normalized * 0.5f;

            var triangles = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
                1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
                4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
            };

            var midpoints = new Dictionary<long, int>();

            for (int s = 0; s < subdivisions; s++)
            {
                var next = new List<int>(triangles.Count * 4);

                for (int i = 0; i < triangles.Count; i += 3)
                {
                    int a = triangles[i];
                    int b = triangles[i + 1];
                    int c = triangles[i + 2];

                    int ab = GetMidpoint(a, b, vertices, midpoints);
                    int bc = GetMidpoint(b, c, vertices, midpoints);
                    int ca = GetMidpoint(c, a, vertices, midpoints);

                    next.Add(a); next.Add(ab); next.Add(ca);
                    next.Add(b); next.Add(bc); next.Add(ab);
                    next.Add(c); next.Add(ca); next.Add(bc);
                    next.Add(ab); next.Add(bc); next.Add(ca);
                }

                triangles = next;
            }

            // 正球体：法线直接等于归一化后的位置，比 RecalculateNormals 更准
            var normals = new Vector3[vertices.Count];
            for (int i = 0; i < vertices.Count; i++) normals[i] = vertices[i].normalized;

            return BuildMesh("PokeIcosphere", vertices, triangles, fixWinding: true, normals: normals);
        }

        static int GetMidpoint(int a, int b, List<Vector3> vertices, Dictionary<long, int> cache)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

            if (cache.TryGetValue(key, out int index)) return index;

            var midpoint = (vertices[a] + vertices[b]) * 0.5f;
            index = vertices.Count;
            vertices.Add(midpoint.normalized * 0.5f);
            cache[key] = index;
            return index;
        }

        // ---------------------------------------------------------------- 公共构建

        static Mesh BuildMesh(string name, List<Vector3> vertices, List<int> triangles,
            bool fixWinding, Vector3[] normals)
        {
            if (fixWinding) FixWinding(vertices, triangles);

            // UV 只是为了让某些需要 UV 的着色器 / 工具不报错，这个 shader 不采样贴图
            var uvs = new List<Vector2>(vertices.Count);
            foreach (var v in vertices)
                uvs.Add(new Vector2(v.x + 0.5f, v.y + 0.5f));

            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);

            // 先有法线再算切线，否则 RecalculateTangents 会拿到空法线
            if (normals != null) mesh.normals = normals;
            else mesh.RecalculateNormals();

            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        /// <summary>形状是凸的且以原点为中心：三角形绕向如果背对中心就交换两个顶点。</summary>
        static void FixWinding(List<Vector3> vertices, List<int> triangles)
        {
            for (int i = 0; i < triangles.Count; i += 3)
            {
                var a = vertices[triangles[i]];
                var b = vertices[triangles[i + 1]];
                var c = vertices[triangles[i + 2]];

                var faceNormal = Vector3.Cross(b - a, c - a);
                if (Vector3.Dot(faceNormal, (a + b + c) / 3f) < 0f)
                {
                    int tmp = triangles[i + 1];
                    triangles[i + 1] = triangles[i + 2];
                    triangles[i + 2] = tmp;
                }
            }
        }
    }
}
