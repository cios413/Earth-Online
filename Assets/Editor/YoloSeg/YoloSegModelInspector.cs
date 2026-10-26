using UnityEditor;
using UnityEngine;
using Unity.Sentis;

namespace YoloSegmentation
{
    /// <summary>
    /// Phase 0 verification tool. Run from the "YoloSeg" menu in the Unity Editor.
    /// It loads the imported ONNX model and logs its input/output names and shapes,
    /// and can optionally run a one-shot CPU smoke test to confirm the model executes.
    /// </summary>
    public static class YoloSegModelInspector
    {
        const string PrimaryPath = "Assets/Models/yolo11n-seg.onnx";
        const string Opset15Path = "Assets/Models/yolo11n-seg-opset15.onnx";

        [MenuItem("YoloSeg/Inspect ONNX Model")]
        public static void InspectModel()
        {
            InspectFile(PrimaryPath);
            InspectFile(Opset15Path);
        }

        [MenuItem("YoloSeg/Smoke Run (CPU)")]
        public static void SmokeRunCpu()
        {
            SmokeRun(PrimaryPath);
        }

        static void InspectFile(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<ModelAsset>(path);
            if (asset == null)
            {
                Debug.LogWarning("YoloSeg: model not found or not imported yet: " + path);
                return;
            }

            var model = ModelLoader.Load(asset);
            Debug.Log("YoloSeg: loaded " + path);
            foreach (var input in model.inputs)
                Debug.Log("  input  " + input.name + ": " + input.shape);
            foreach (var output in model.outputs)
                Debug.Log("  output " + output.name);
        }

        static void SmokeRun(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<ModelAsset>(path);
            if (asset == null)
            {
                Debug.LogWarning("YoloSeg: model not found or not imported yet: " + path);
                return;
            }

            var model = ModelLoader.Load(asset);
            using (var worker = new Worker(model, BackendType.CPU))
            using (var input = new Tensor<float>(new TensorShape(1, 3, 640, 640), clearOnInit: true))
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                worker.Schedule(input);
                var output0 = worker.PeekOutput("output0") as Tensor<float>;
                var output1 = worker.PeekOutput("output1") as Tensor<float>;
                stopwatch.Stop();

                Debug.Log("YoloSeg: smoke run on " + path + " took " + stopwatch.ElapsedMilliseconds + " ms");
                Debug.Log("  output0 shape = " + (output0 != null ? output0.shape.ToString() : "null"));
                Debug.Log("  output1 shape = " + (output1 != null ? output1.shape.ToString() : "null"));
            }
        }
    }
}
