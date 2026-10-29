using System;
using System.Collections.Generic;
using EarthOL.Deform;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EarthOL.EditorTools
{
    /// <summary>
    /// 一键把"戳形变"接到当前场景里：
    ///   · 找到所有 Vuforia 的 ImageTargetBehaviour（用类型名反射，避免硬依赖 Vuforia 程序集）
    ///   · 给每个 ImageTarget 下带 Collider 的网格物体挂上 PokeDeformer、换成形变材质
    ///   · 把物体从识别图上"抬"起来，正好立在卡片平面上（z = 半个高度）
    ///   · 保证 ARCamera 上有 PokeInput
    /// 也可以反过来做校验，确认配置没错。
    /// </summary>
    public static class PokeDeformSetup
    {
        const string kShaderName = "EarthOL/PokeDeform";
        const string kWarpShaderName = "EarthOL/PokeWarp";

        // 放在 Resources 下，保证打包时一定被包含，且运行时能用 Resources.Load 兜底（PokeDeformBootstrap）
        const string kMaterialsFolder = "Assets/Resources";
        const string kLegacyMaterialsFolder = "Assets/Materials";
        const string kDefaultScenePath = "Assets/Scenes/SampleScene.unity";

        static readonly Color kMaterialColorA = new Color(0.98f, 0.58f, 0.22f); // 橙
        static readonly Color kMaterialColorB = new Color(0.25f, 0.78f, 0.75f); // 青

        // ------------------------------------------------------------------ 菜单

        [MenuItem("Tools/Earth OL/① 配置当前场景（戳形变）", false, 10)]
        public static void MenuSetupCurrentScene()
        {
            SetupCurrentScene();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[Earth OL] 已配置当前场景。");
        }

        [MenuItem("Tools/Earth OL/② 校验配置", false, 11)]
        public static void MenuValidate()
        {
            if (ValidateCurrentScene()) Debug.Log("[Earth OL] 校验通过 ✔");
        }

        [MenuItem("Tools/Earth OL/③ 重新生成形变材质", false, 12)]
        public static void MenuRebuildMaterials()
        {
            EnsureMaterial("PokeJelly_Orange", kMaterialColorA);
            EnsureMaterial("PokeJelly_Teal", kMaterialColorB);
            AssetDatabase.SaveAssets();
            Debug.Log("[Earth OL] 形变材质已生成到 " + kMaterialsFolder);
        }

        // ------------------------------------------------------------------ 批处理入口（供 -executeMethod 调用）

        public static void BatchSetup()
        {
            EditorSceneManager.OpenScene(kDefaultScenePath, OpenSceneMode.Single);
            SetupCurrentScene();
            AssetDatabase.SaveAssets();
            Debug.Log("[Earth OL] BatchSetup 完成");
        }

        public static void BatchValidate()
        {
            EditorSceneManager.OpenScene(kDefaultScenePath, OpenSceneMode.Single);
            if (!ValidateCurrentScene())
                throw new Exception("[Earth OL] 场景校验失败，详见上面的日志");
            Debug.Log("[Earth OL] BatchValidate 完成");
        }

        /// <summary>自检 + 配置 + 校验，一次跑完（批处理用），结论写到 Logs/poke_report.txt。</summary>
        public static void BatchAll()
        {
            var report = new List<string>();
            bool success = false;

            try
            {
                PokeDeformSelfTest.Run();
                report.Add("=== 自检（网格 + 弹簧）: PASS ===");
                report.Add(PokeDeformSelfTest.Results);

                EditorSceneManager.OpenScene(kDefaultScenePath, OpenSceneMode.Single);
                SetupCurrentScene();
                AssetDatabase.SaveAssets();
                report.Add("=== 场景配置: 已执行 ===");

                success = ValidateCurrentScene();
                report.Add("=== 场景校验: " + (success ? "PASS" : "FAIL") + " ===");

                if (success) EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            }
            catch (Exception exception)
            {
                report.Add("=== 异常 ===");
                report.Add(exception.ToString());
                success = false;
                throw;
            }
            finally
            {
                report.Add("=== 总结论: " + (success ? "PASS" : "FAIL") + " ===");
                WriteReport(report);
            }
        }

        static void WriteReport(List<string> lines)
        {
            try
            {
                var root = System.IO.Path.GetDirectoryName(Application.dataPath);
                var path = System.IO.Path.Combine(root ?? ".", "Logs", "poke_report.txt");
                System.IO.File.WriteAllText(path, string.Join("\n", lines.ToArray()), System.Text.Encoding.UTF8);
                Debug.Log("[Earth OL] 报告已写入 " + path);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Earth OL] 写报告失败：" + exception.Message);
            }
        }

        /// <summary>配置 + 校验 + 保存，一次跑完。</summary>
        public static void BatchSetupAndValidate()
        {
            EditorSceneManager.OpenScene(kDefaultScenePath, OpenSceneMode.Single);
            SetupCurrentScene();
            AssetDatabase.SaveAssets();

            if (!ValidateCurrentScene())
                throw new Exception("[Earth OL] 配置后校验失败，详见上面的日志");

            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            Debug.Log("[Earth OL] BatchSetupAndValidate 完成：配置成功且校验通过");
        }

        // ------------------------------------------------------------------ 配置

        public static void SetupCurrentScene()
        {
            var shader = Shader.Find(kShaderName);
            if (shader == null) throw new Exception($"找不到着色器 {kShaderName}，请确认 Assets/Shaders/PokeDeform.shader 已导入");

            var materialA = EnsureMaterial("PokeJelly_Orange", kMaterialColorA);
            var materialB = EnsureMaterial("PokeJelly_Teal", kMaterialColorB);
            var warpMaterial = EnsureWarpMaterial();

            var targets = FindComponentsByTypeName("ImageTargetBehaviour");
            if (targets.Count == 0)
            {
                Debug.LogWarning("[Earth OL] 场景里没有找到 ImageTargetBehaviour，跳过目标物配置。");
            }

            int deformableCount = 0;
            int materialIndex = 0;   // 全局交替配色，而不是每个 target 各自从 0 开始

            foreach (var target in targets)
            {
                foreach (var collider in target.GetComponentsInChildren<Collider>(true))
                {
                    var go = collider.gameObject;
                    if (go.GetComponent<MeshRenderer>() == null) continue;

                    var material = (materialIndex % 2 == 0) ? materialA : materialB;
                    var deformer = go.GetComponent<PokeDeformer>();
                    if (deformer == null) deformer = Undo.AddComponent<PokeDeformer>(go);

                    Undo.RecordObject(deformer, "配置戳形变");

                    deformer.deformMaterial = material;
                    deformer.softMesh = ChooseShape(collider);
                    deformer.softMeshLevel = 3;
                    deformer.rounding = collider is SphereCollider ? 0f : 0.30f;

                    // 同时把材质直接挂到 Renderer 上，编辑期在 Scene 视图里就能看到正确外观
                    var meshRenderer = go.GetComponent<MeshRenderer>();
                    Undo.RecordObject(meshRenderer, "配置戳形变材质");
                    meshRenderer.sharedMaterial = material;

                    // 按需求把方块/球隐藏掉：只保留"卡片本体"的形变
                    Undo.RecordObject(go, "隐藏物体");
                    go.SetActive(false);

                    // 让它正好立在识别图上（Vuforia 的 ImageTarget 局部空间里 +Z 朝出卡片方向）
                    LiftOntoImagePlane(go.transform);

                    EditorUtility.SetDirty(deformer);
                    EditorUtility.SetDirty(meshRenderer);
                    deformableCount++;
                    materialIndex++;
                }
            }

            // ARCamera 上挂输入
            var camera = FindArCamera();
            if (camera != null)
            {
                if (camera.gameObject.GetComponent<PokeInput>() == null)
                    Undo.AddComponent<PokeInput>(camera.gameObject);

                EditorUtility.SetDirty(camera.gameObject);
            }
            else
            {
                Debug.LogWarning("[Earth OL] 没有找到 AR 相机，PokeInput 需要手动挂到相机上。");
            }

            // 让"识别图卡片本身"可形变：组件直接挂在 ImageTarget 物体上，不新建任何物体
            foreach (var target in targets) EnsureCardWarp(target, warpMaterial);

            RemoveLegacyDemoObjects(targets);

            AssetDatabase.SaveAssets();
            Debug.Log($"[Earth OL] 配置完成：{targets.Count} 个 ImageTarget，{deformableCount} 个可形变物体。");
        }

        /// <summary>
        /// 让"被识别的物体本身"可形变：把网格 / 渲染器 / 碰撞体 / PokeWarpDeformer
        /// 全部挂在 ImageTarget 这个物体本身上 —— 不新建任何 GameObject。
        /// </summary>
        static void EnsureCardWarp(Component target, Material warpMaterial)
        {
            var go = target.gameObject;

            // 从 ImageTargetBehaviour 的序列化字段读宽高与识别图，避免硬依赖 Vuforia API
            float width = 0.1125f;
            float height = 0.2f;
            Texture2D texture = null;

            var serialized = new SerializedObject(target);

            var widthProperty = serialized.FindProperty("mWidth");
            if (widthProperty != null && widthProperty.floatValue > 0f) width = widthProperty.floatValue;

            var heightProperty = serialized.FindProperty("mHeight");
            if (heightProperty != null && heightProperty.floatValue > 0f) height = heightProperty.floatValue;

            var textureProperty = serialized.FindProperty("mRuntimeTexture");
            if (textureProperty != null) texture = textureProperty.objectReferenceValue as Texture2D;

            var deformer = go.GetComponent<PokeWarpDeformer>();
            if (deformer == null) deformer = Undo.AddComponent<PokeWarpDeformer>(go);

            Undo.RecordObject(deformer, "配置卡片形变");

            deformer.planeWidth = width;
            deformer.planeHeight = height;
            deformer.targetTexture = texture;
            deformer.useTargetTexture = false;
            deformer.useCameraFeed = true;    // 投影摄像头实时画面
            deformer.planeInXZ = true;        // 躺平，和物理卡片平行
            deformer.warpMaterial = warpMaterial;
            deformer.gridX = 16;
            deformer.gridY = 28;

            EditorUtility.SetDirty(deformer);
        }

        /// <summary>清掉之前版本加过的杂物（绿色球 GreenStretch）。</summary>
        static void RemoveLegacyDemoObjects(List<Component> targets)
        {
            foreach (var target in targets)
            {
                var legacy = target.transform.Find("GreenStretch");
                if (legacy == null) continue;

                Undo.DestroyObjectImmediate(legacy.gameObject);
                Debug.Log("[Earth OL] 已删除之前添加的 GreenStretch");
            }
        }

        static Material EnsureWarpMaterial()
        {
            var shader = Shader.Find(kWarpShaderName);
            if (shader == null) throw new Exception($"找不到着色器 {kWarpShaderName}");

            EnsureFolder(kMaterialsFolder);

            var path = $"{kMaterialsFolder}/PokeWarp.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                material = new Material(shader) { name = "PokeWarp" };
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        static SoftMeshShape ChooseShape(Collider collider)
        {
            switch (collider)
            {
                case SphereCollider _:
                    return SoftMeshShape.Sphere;
                case BoxCollider _:
                    return SoftMeshShape.RoundedCube;
                default:
                    // 自定义网格不擅自替换，交给校验环节提示
                    return SoftMeshShape.KeepOriginal;
            }
        }

        static void LiftOntoImagePlane(Transform transform)
        {
            var meshFilter = transform.GetComponent<MeshFilter>();
            float meshHeight = meshFilter != null && meshFilter.sharedMesh != null
                ? meshFilter.sharedMesh.bounds.size.z
                : 1f;

            if (meshHeight <= 0f) meshHeight = 1f;

            float halfHeight = 0.5f * Mathf.Abs(transform.localScale.z) * meshHeight;
            var local = transform.localPosition;
            transform.localPosition = new Vector3(local.x, local.y, halfHeight);
        }

        // ------------------------------------------------------------------ 校验

        public static bool ValidateCurrentScene()
        {
            var problems = new List<string>();

            var shader = Shader.Find(kShaderName);
            if (shader == null)
            {
                problems.Add($"找不到着色器 {kShaderName}");
            }
            else
            {
                if (!shader.isSupported) problems.Add($"{kShaderName} 在当前平台编译失败（isSupported = false）");

                try
                {
                    int messages = UnityEditor.ShaderUtil.GetShaderMessageCount(shader);
                    if (messages > 0) problems.Add($"{kShaderName} 编译有 {messages} 条警告/错误，打开材质面板查看");
                }
                catch (Exception)
                {
                    // 旧版本 API 差异，忽略
                }
            }

            var targets = FindComponentsByTypeName("ImageTargetBehaviour");
            if (targets.Count == 0) problems.Add("场景里没有 ImageTargetBehaviour");

            int deformableCount = 0;

            foreach (var target in targets)
            {
                if (target.GetComponent<PokeWarpDeformer>() == null)
                    problems.Add($"{Path(target.gameObject)} 缺少 PokeWarpDeformer（识别图卡片本体形变）");

                foreach (var collider in target.GetComponentsInChildren<Collider>(true))
                {
                    var go = collider.gameObject;
                    if (go.GetComponent<MeshRenderer>() == null) continue;

                    deformableCount++;

                    var deformer = go.GetComponent<PokeDeformer>();
                    if (deformer == null)
                    {
                        problems.Add($"{Path(go)} 缺少 PokeDeformer");
                        continue;
                    }

                    var renderer = go.GetComponent<Renderer>();
                    var material = deformer.deformMaterial != null
                        ? deformer.deformMaterial
                        : (renderer != null ? renderer.sharedMaterial : null);

                    if (material == null)
                    {
                        problems.Add($"{Path(go)} 没有材质");
                    }
                    else if (!material.HasProperty("_PokePos0"))
                    {
                        problems.Add($"{Path(go)} 的材质 {material.name} 不是 {kShaderName}");
                    }

                    var meshFilter = go.GetComponent<MeshFilter>();
                    int vertexCount = meshFilter != null && meshFilter.sharedMesh != null
                        ? meshFilter.sharedMesh.vertexCount
                        : 0;

                    var effectiveCount = deformer.softMesh != SoftMeshShape.KeepOriginal
                        ? 9999
                        : vertexCount;

                    if (effectiveCount < 50)
                    {
                        problems.Add($"{Path(go)} 只有 {vertexCount} 个顶点，戳不出凹陷（把 softMesh 改成 RoundedCube 或 Sphere）");
                    }
                }
            }

            if (deformableCount == 0) problems.Add("ImageTarget 下面没有可形变的网格物体（需要同时有 Collider 和 MeshRenderer）");

            var camera = FindArCamera();
            if (camera == null)
            {
                problems.Add("找不到 AR 相机");
            }
            else if (camera.GetComponent<PokeInput>() == null)
            {
                problems.Add("AR 相机上没有 PokeInput");
            }

            if (problems.Count == 0) return true;

            foreach (var problem in problems) Debug.LogError("[Earth OL] " + problem);
            return false;
        }

        // ------------------------------------------------------------------ 工具

        static Material EnsureMaterial(string materialName, Color color)
        {
            var shader = Shader.Find(kShaderName);
            if (shader == null) throw new Exception($"找不到着色器 {kShaderName}");

            EnsureFolder(kMaterialsFolder);

            var path = $"{kMaterialsFolder}/{materialName}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                // 旧位置（Assets/Materials）有的话直接搬过来：.meta 一起移动，GUID 不变，场景引用不会断
                var legacyPath = $"{kLegacyMaterialsFolder}/{materialName}.mat";
                if (AssetDatabase.LoadAssetAtPath<Material>(legacyPath) != null)
                {
                    var moveError = AssetDatabase.MoveAsset(legacyPath, path);
                    if (string.IsNullOrEmpty(moveError))
                        material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    else
                        Debug.LogWarning("[Earth OL] 材质迁移失败：" + moveError);
                }
            }

            if (material == null)
            {
                material = new Material(shader) { name = materialName };
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            material.SetColor("_Color", color);
            EditorUtility.SetDirty(material);
            return material;
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;

            var parent = System.IO.Path.GetDirectoryName(folder)?.Replace('\\', '/');
            var leaf = System.IO.Path.GetFileName(folder);

            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, leaf);
        }

        /// <summary>按类型名反射查找组件，避免脚本硬依赖 Vuforia 程序集。</summary>
        static List<Component> FindComponentsByTypeName(string typeName)
        {
            var result = new List<Component>();

            foreach (var behaviour in Object.FindObjectsOfType<MonoBehaviour>(true))
            {
                if (behaviour == null) continue;
                if (behaviour.GetType().Name.IndexOf(typeName, StringComparison.Ordinal) >= 0)
                    result.Add(behaviour);
            }

            return result;
        }

        static Camera FindArCamera()
        {
            // VuforiaBehaviour 所在的相机就是 AR 相机
            foreach (var behaviour in Object.FindObjectsOfType<MonoBehaviour>(true))
            {
                if (behaviour == null) continue;
                if (behaviour.GetType().Name.IndexOf("VuforiaBehaviour", StringComparison.Ordinal) < 0) continue;

                var camera = behaviour.GetComponent<Camera>();
                if (camera != null) return camera;
            }

            return Camera.main;
        }

        static string Path(GameObject go)
        {
            var path = go.name;
            var current = go.transform.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }
    }
}
