using UnityEngine;
using UnityEngine.SceneManagement;

namespace EarthOL.Deform
{
    /// <summary>
    /// 临时诊断：场景加载后打印场景内容，用来确认打包进包的场景到底有没有 PokeDeformer。
    /// 排完问题可以删掉这个文件。
    /// </summary>
    public static class SceneDiagnostics
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void LogSceneContents()
        {
            var scene = SceneManager.GetActiveScene();
            var deformers = Object.FindObjectsOfType<PokeDeformer>(true);
            var inputs = Object.FindObjectsOfType<PokeInput>(true);

            Debug.Log($"[Diag] 场景='{scene.name}' 路径='{scene.path}' 索引={scene.buildIndex} " +
                      $"根物体={scene.rootCount} PokeDeformer={deformers.Length} PokeInput={inputs.Length}");

            foreach (var go in scene.GetRootGameObjects())
            {
                var renderers = go.GetComponentsInChildren<Renderer>(true).Length;
                Debug.Log($"[Diag] 根 '{go.name}' 子节点={go.transform.childCount} Renderer={renderers}");
            }

            foreach (var deformer in deformers)
            {
                var renderer = deformer.GetComponent<Renderer>();
                var material = renderer != null ? renderer.sharedMaterial : null;
                var filter = deformer.GetComponent<MeshFilter>();
                var mesh = filter != null ? filter.sharedMesh : null;

                Debug.Log($"[Diag] {deformer.name}: mesh={(mesh != null ? mesh.name + "/" + mesh.vertexCount : "null")} " +
                          $"mat={(material != null ? material.name : "null")} " +
                          $"shader={(material != null && material.shader != null ? material.shader.name : "null")} " +
                          $"scale={deformer.transform.localScale} pos={deformer.transform.localPosition}");
            }
        }
    }
}
