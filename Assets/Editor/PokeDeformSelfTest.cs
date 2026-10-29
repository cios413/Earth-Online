using System;
using System.Collections.Generic;
using EarthOL.Deform;
using UnityEditor;
using UnityEngine;

namespace EarthOL.EditorTools
{
    /// <summary>
    /// 不依赖手机、也不进 Play 模式就能跑的自检：
    ///   · SoftMesh 生成的网格顶点数、包围盒、法线朝向、三角形是否有退化
    ///   · PokeDeformer 的弹簧参数是否既能"过冲"（果冻感）又能收敛（不会抖到停不下来）
    /// 任何一项不达标就抛异常，批处理模式下会以非 0 退出码结束。
    /// </summary>
    public static class PokeDeformSelfTest
    {
        [MenuItem("Tools/Earth OL/④ 自检（网格 + 弹簧）", false, 13)]
        public static void MenuSelfTest()
        {
            Run();
            Debug.Log("[Earth OL] 自检通过 ✔");
        }

        public static void BatchSelfTest()
        {
            Run();
            Debug.Log("[Earth OL] BatchSelfTest 完成：全部通过");
        }

        static readonly List<string> s_Results = new List<string>();

        /// <summary>上一次自检的逐项结果（纯文本），便于批处理时把结论落盘核对。</summary>
        public static string Results
        {
            get { return string.Join("\n", s_Results.ToArray()); }
        }

        public static void Run()
        {
            s_Results.Clear();
            TestRoundedCube();
            TestIcosphere();
            TestSpring();
        }

        // ------------------------------------------------------------------ 网格

        static void TestRoundedCube()
        {
            var mesh = SoftMesh.Create(SoftMeshShape.RoundedCube, 3, 0.30f);
            if (mesh == null) throw new Exception("RoundedCube 网格生成失败");

            Check($"RoundedCube 顶点数 ({mesh.vertexCount})", mesh.vertexCount > 500,
                $"顶点太少，戳不出凹陷（期望 > 500）");

            CheckBounds(mesh, "RoundedCube");
            CheckNormalsOutward(mesh, "RoundedCube");
            CheckNoDegenerateTriangles(mesh, "RoundedCube");

            UnityEngine.Object.DestroyImmediate(mesh);
        }

        static void TestIcosphere()
        {
            var mesh = SoftMesh.Create(SoftMeshShape.Sphere, 3, 0f);
            if (mesh == null) throw new Exception("Sphere 网格生成失败");

            // level 3 -> 20 * 4^3 = 1280 个三角面，10*4^3+2 = 642 个顶点
            Check($"Sphere 顶点数 ({mesh.vertexCount})", mesh.vertexCount == 642,
                $"细分等级 3 期望 642 个顶点");

            // 细分球的顶点不落在坐标轴上，顶点包围盒理论上比理想球的 1 单位小（约 0.851）
            var size = mesh.bounds.size;
            Check($"Sphere 包围盒 = {size.x:F3}", size.x > 0.80f && size.x < 1.02f,
                "超出预期范围：顶点数太少或半径算错");

            CheckNormalsOutward(mesh, "Sphere");
            CheckNoDegenerateTriangles(mesh, "Sphere");

            UnityEngine.Object.DestroyImmediate(mesh);
        }

        static void CheckBounds(Mesh mesh, string label)
        {
            var size = mesh.bounds.size;

            for (int axis = 0; axis < 3; axis++)
            {
                float value = size[axis];
                Check($"{label} 包围盒轴{axis} = {value:F3}", Mathf.Abs(value - 1f) < 0.02f,
                    "包围盒应该约等于 1 单位，否则原有 Collider 会不贴合");
            }
        }

        static void CheckNormalsOutward(Mesh mesh, string label)
        {
            var vertices = mesh.vertices;
            var normals = mesh.normals;

            if (vertices.Length != normals.Length)
                throw new Exception($"{label} 顶点数({vertices.Length}) 与法线数({normals.Length}) 不一致");

            for (int i = 0; i < vertices.Length; i++)
            {
                if (Mathf.Abs(normals[i].magnitude - 1f) > 0.01f)
                    throw new Exception($"{label} 第 {i} 个顶点法线没有归一化：{normals[i].magnitude}");

                if (Vector3.Dot(normals[i], vertices[i]) <= 0f)
                    throw new Exception($"{label} 第 {i} 个顶点法线朝内（会导致背面发黑）");
            }

            Check($"{label} 法线朝外 ({vertices.Length} 个顶点)", true, string.Empty);
        }

        static void CheckNoDegenerateTriangles(Mesh mesh, string label)
        {
            var vertices = mesh.vertices;
            var triangles = mesh.triangles;

            for (int i = 0; i < triangles.Length; i += 3)
            {
                var a = vertices[triangles[i]];
                var b = vertices[triangles[i + 1]];
                var c = vertices[triangles[i + 2]];

                if (Vector3.Cross(b - a, c - a).magnitude < 1e-8f)
                    throw new Exception($"{label} 第 {i / 3} 个三角面退化（面积为 0）");
            }

            Check($"{label} 无退化三角面 ({triangles.Length / 3} 个面)", true, string.Empty);
        }

        // ------------------------------------------------------------------ 弹簧

        static void TestSpring()
        {
            // 复现 PokeDeformer 默认参数下的回弹积分
            const float frequency = 2.8f;
            const float dampingRatio = 0.32f;
            const float step = 1f / 60f;

            float omega = 2f * Mathf.PI * frequency;
            float stiffness = omega * omega;
            float damping = 2f * dampingRatio * omega;

            float value = 1f;
            float velocity = 0f;
            float minimum = 1f;
            int settleStep = -1;

            for (int i = 0; i < 600; i++)
            {
                velocity += (-stiffness * value - damping * velocity) * step;
                value += velocity * step;

                minimum = Mathf.Min(minimum, value);

                if (settleStep < 0 && Mathf.Abs(value) < 0.01f && Mathf.Abs(velocity) < 0.05f)
                    settleStep = i;
            }

            Check($"弹簧过冲最小值 = {minimum:F3}", minimum < -0.05f,
                "没有过冲就不像果冻，检查 dampingRatio 是否 < 1");

            Check($"弹簧收敛步数 = {settleStep}", settleStep > 0 && settleStep < 240,
                "回弹应该在 4 秒内收敛");

            Check($"弹簧最终值 = {value:F4}", Mathf.Abs(value) < 0.01f, "弹簧没有收敛到 0");
        }

        // ------------------------------------------------------------------ 断言

        static void Check(string label, bool passed, string message)
        {
            s_Results.Add((passed ? "  PASS  " : "  FAIL  ") + label);

            if (passed)
            {
                Debug.Log("[Earth OL] ✔ " + label);
                return;
            }

            Debug.LogError("[Earth OL] ✘ " + label);
            throw new Exception($"{label} 失败：{message}");
        }
    }
}
