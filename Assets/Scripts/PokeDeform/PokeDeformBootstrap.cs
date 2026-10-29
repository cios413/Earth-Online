using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EarthOL.Deform
{
    /// <summary>
    /// 运行时自愈：场景加载后检查所有 ImageTarget 下的物体，
    /// 缺 PokeDeformer / 缺 PokeInput / 材质不对，就地补齐。
    ///
    /// 这样即使打包时捡到了过期的场景缓存（组件没进包），App 里依然能看到
    /// 橙色圆角方块并且能戳出形变 —— 不再依赖场景序列化是否"听话"。
    /// </summary>
    public static class PokeDeformBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void EnsureSetup()
        {
            // 真机调试期打开戳击日志：每次戳会打一条 [Poke] 行，便于确认触摸链路
            PokeDeformer.LogPokes = true;

            var materials = LoadMaterials();

            if (materials.Count == 0)
            {
                Debug.LogWarning("[Bootstrap] 找不到 Resources 里的 PokeJelly 材质，形变可能显示为默认白色。" +
                                 "请执行菜单 Tools → Earth OL → ③ 重新生成形变材质。");
            }

            int deformers = 0;
            int index = 0;

            var targets = FindImageTargets();

            foreach (var target in targets)
            {
                foreach (var collider in target.GetComponentsInChildren<Collider>(true))
                {
                    var go = collider.gameObject;
                    var renderer = go.GetComponent<MeshRenderer>();
                    if (renderer == null) continue;

                    // 卡片本体（ImageTarget 自己）走 PokeWarpDeformer，不要再挂 PokeDeformer，
                    // 否则两个组件会抢同一个触摸事件
                    if (go == target.gameObject) continue;
                    if (go.GetComponent<PokeWarpDeformer>() != null) continue;

                    // 场景里已经有组件就不动它，只补缺失的
                    var deformer = go.GetComponent<PokeDeformer>();
                    if (deformer == null)
                    {
                        deformer = go.AddComponent<PokeDeformer>();
                    }

                    var shape = collider is SphereCollider ? SoftMeshShape.Sphere : SoftMeshShape.RoundedCube;

                    if (deformer.softMesh == SoftMeshShape.KeepOriginal && shape != SoftMeshShape.KeepOriginal)
                    {
                        deformer.softMesh = shape;
                        deformer.softMeshLevel = 3;
                        deformer.rounding = collider is SphereCollider ? 0f : 0.30f;

                        // Awake 可能已经跑过了（此时还来不及换网格），这里直接补上高密度网格
                        var filter = go.GetComponent<MeshFilter>();
                        int vertexCount = filter != null && filter.sharedMesh != null ? filter.sharedMesh.vertexCount : 0;

                        if (filter != null && vertexCount < 50)
                        {
                            var generated = SoftMesh.Create(shape, deformer.softMeshLevel, deformer.rounding);
                            if (generated != null)
                            {
                                filter.sharedMesh = generated;
                                Debug.Log($"[Bootstrap] {go.name} 换成高密度网格 {generated.name}({generated.vertexCount} 顶点)");
                            }
                        }
                    }

                    if (deformer.deformMaterial == null && materials.Count > 0)
                    {
                        var material = materials[index % materials.Count];
                        deformer.deformMaterial = material;
                        renderer.sharedMaterial = material;
                    }

                    LiftOntoImagePlane(go.transform);
                    deformers++;
                    index++;
                }
            }

            EnsurePokeInput();
            EnsureCardWarp(targets);
            EnsureModeOverlay();

            Debug.Log($"[Bootstrap] 自愈完成：ImageTarget 下可形变物体 {deformers} 个，材质 {materials.Count} 个");
        }

        /// <summary>
        /// 模式切换器。这里用已经验证能执行的 Bootstrap 来创建，
        /// 不依赖 ModeOverlay 自己的 [RuntimeInitializeOnLoadMethod]（IL2CPP 裁剪会把它去掉）。
        /// </summary>
        static void EnsureModeOverlay()
        {
            if (UnityEngine.Object.FindObjectOfType<EarthOL.ModeOverlay>(true) != null) return;

            var go = new GameObject("EarthOL ModeOverlay");
            go.AddComponent<EarthOL.ModeOverlay>();
            Debug.Log("[Bootstrap] 已创建模式切换器（左上角两个按钮）");
        }

        /// <summary>
        /// 识别图卡片本体形变：把 PokeWarpDeformer 直接补到 ImageTarget 物体上（不新建物体）。
        /// 同时清掉旧版本加过的 GreenStretch。
        /// </summary>
        static void EnsureCardWarp(List<Component> targets)
        {
            foreach (var target in targets)
            {
                var go = target.gameObject;

                var legacy = go.transform.Find("GreenStretch");
                if (legacy != null)
                {
                    UnityEngine.Object.Destroy(legacy.gameObject);
                    Debug.Log("[Bootstrap] 已删除旧版本添加的 GreenStretch");
                }

                var deformer = go.GetComponent<PokeWarpDeformer>();
                if (deformer == null)
                {
                    deformer = go.AddComponent<PokeWarpDeformer>();
                    Debug.Log("[Bootstrap] 已给 ImageTarget 补上 PokeWarpDeformer（卡片本体形变）");
                }

                if (deformer.targetTexture == null)
                {
                    // 尽力从 ImageTargetBehaviour 的私有字段取识别图（失败也不影响形变，只是卡片是白色）
                    try
                    {
                        var field = target.GetType().GetField("mRuntimeTexture",
                            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

                        if (field != null) deformer.targetTexture = field.GetValue(target) as Texture2D;
                    }
                    catch (Exception)
                    {
                        // 反射取不到就算了
                    }
                }
            }
        }

        static List<Material> LoadMaterials()
        {
            var result = new List<Material>();

            var orange = Resources.Load<Material>("PokeJelly_Orange");
            var teal = Resources.Load<Material>("PokeJelly_Teal");

            if (orange != null) result.Add(orange);
            if (teal != null) result.Add(teal);

            // 兜底：万一 Resources 里没有，就直接用着色器临时建一个
            if (result.Count == 0)
            {
                var shader = Shader.Find("EarthOL/PokeDeform");
                if (shader != null)
                {
                    var fallback = new Material(shader) { name = "PokeDeform (bootstrap)" };
                    fallback.SetColor("_Color", new Color(0.98f, 0.58f, 0.22f));
                    result.Add(fallback);
                }
            }

            return result;
        }

        static List<Component> FindImageTargets()
        {
            var result = new List<Component>();

            foreach (var behaviour in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>(true))
            {
                if (behaviour == null) continue;
                if (behaviour.GetType().Name.IndexOf("ImageTargetBehaviour", StringComparison.Ordinal) >= 0)
                    result.Add(behaviour);
            }

            return result;
        }

        static void EnsurePokeInput()
        {
            var camera = Camera.main;

            if (camera == null)
            {
                // ARCamera 可能没有 MainCamera 标签，退一步找带 VuforiaBehaviour 的相机
                foreach (var behaviour in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>(true))
                {
                    if (behaviour == null) continue;
                    if (behaviour.GetType().Name.IndexOf("VuforiaBehaviour", StringComparison.Ordinal) < 0) continue;

                    camera = behaviour.GetComponent<Camera>();
                    if (camera != null) break;
                }
            }

            if (camera == null)
            {
                Debug.LogWarning("[Bootstrap] 没找到 AR 相机，无法启用触摸戳击。");
                return;
            }

            if (camera.GetComponent<PokeInput>() == null)
            {
                camera.gameObject.AddComponent<PokeInput>();
                Debug.Log("[Bootstrap] 已给 AR 相机补上 PokeInput");
            }
        }

        static void LiftOntoImagePlane(Transform transform)
        {
            var filter = transform.GetComponent<MeshFilter>();
            float height = filter != null && filter.sharedMesh != null
                ? filter.sharedMesh.bounds.size.z
                : 1f;

            if (height <= 0f) height = 1f;

            var local = transform.localPosition;
            transform.localPosition = new Vector3(local.x, local.y, 0.5f * Mathf.Abs(transform.localScale.z) * height);
        }
    }
}
