using System;
using System.Collections.Generic;
using System.Reflection;
using EarthOL.BodyThrow;
using Unity.Sentis;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EarthOL.EditorTools
{
    /// <summary>Batch regressions for raw YOLO decoding, image mapping and proxy fitting.</summary>
    public static class BodyPoseValidation
    {
        const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        const int Anchors = 3549;

        public static void ValidateAndBuild()
        {
            Validate();
            AndroidBuild.BuildApk();
        }

        public static void Validate()
        {
            ValidateReadbackFallbackPolicy();
            ValidateInput();
            foreach (int anchor in new[] { 1378, 3055, 3464 })
                foreach (bool portrait in new[] { true, false })
                    ValidateDecode(anchor, portrait);
            ValidateGeometry();
            ValidateModel();
            Debug.Log("[PoseValidation] PASS: NCHW RGB orientation, letterbox round trips, YOLO26 box/pose channels, " +
                "confidence filtering, head/torso fitting and model inference.");
        }

        static void ValidateReadbackFallbackPolicy()
        {
            var method = typeof(BodyPoseDetector).GetMethod("ShouldFallbackToCpu",
                BindingFlags.NonPublic | BindingFlags.Static);
            Check((bool)method.Invoke(null, new object[]
                { BackendType.GPUCompute, new InvalidOperationException("GPU readback failed") }),
                "GPU tensor readback failure must select the CPU retry path");
            Check(!(bool)method.Invoke(null, new object[]
                { BackendType.CPU, new InvalidOperationException("CPU decode failed") }),
                "CPU inference failures must propagate instead of retrying on CPU again");
            Check(!(bool)method.Invoke(null, new object[]
                { BackendType.GPUCompute, new FormatException("Unexpected output shape") }),
                "Model output format failures must not be masked as GPU readback failures");
        }

        static void ValidateInput()
        {
            using (var detector = new BodyPoseDetector())
            {
                var pixels = new Color32[8];
                for (int i = 0; i < 8; i++) pixels[i] = i < 4 ? new Color32(255, 0, 0, 255) : new Color32(0, 255, 0, 255);
                var tensor = (Tensor<float>)Invoke(detector, "BuildInput", pixels, 2, 4);
                tensor.CompleteAllPendingOperations();
                var data = tensor.AsReadOnlyNativeArray();
                int plane = 416 * 416;
                Near(data[plane + 208], 1f, "Top input row must be green");
                Near(data[415 * 416 + 208], 1f, "Bottom input row must be red");
                Near(data[100 * 416], 114f / 255f, "Letterbox padding");
            }
        }

        static void ValidateDecode(int anchor, bool portrait)
        {
            using (var detector = new BodyPoseDetector())
            {
                int width = portrait ? 200 : 400;
                int height = portrait ? 400 : 200;
                Invoke(detector, "BuildInput", new Color32[width * height], width, height);
                using (var boxes = Output(4))
                using (var pose = Output(51))
                using (var scores = Output(1))
                {
                    for (int i = 0; i < Anchors; i++) scores[i] = -20f;
                    scores[anchor] = 10f;
                    for (int side = 0; side < 4; side++) boxes[side * Anchors + anchor] = 4f;

                    float gridX, gridY, stride;
                    if (anchor < 2704) { gridX = anchor % 52 + 0.5f; gridY = anchor / 52 + 0.5f; stride = 8f; }
                    else if (anchor < 3380) { gridX = (anchor - 2704) % 26 + 0.5f; gridY = (anchor - 2704) / 26 + 0.5f; stride = 16f; }
                    else { gridX = (anchor - 3380) % 13 + 0.5f; gridY = (anchor - 3380) / 13 + 0.5f; stride = 32f; }
                    for (int i = 0; i < 17; i++)
                    {
                        pose[i * 3 * Anchors + anchor] = 208f / stride - gridX;
                        pose[(i * 3 + 1) * Anchors + anchor] = 156f / stride - gridY;
                        pose[(i * 3 + 2) * Anchors + anchor] = 10f;
                    }
                    Invoke(detector, "Decode", boxes, scores, pose);
                    var results = (List<BodyPoseResult>)Field(detector, "m_Candidates");
                    Check(results.Count == 1, "Decode must produce one candidate");
                    Near(results[0].keypoints[0].x, 0.5f, "Letterbox x round trip");
                    Near(results[0].keypoints[0].y, portrait ? 0.625f : 0.75f, "Letterbox y round trip");
                    Check(results[0].keypointScores[0] > 0.99f, "Visibility sigmoid");
                    int duplicateAnchor = anchor + 1;
                    scores[duplicateAnchor] = 9f;
                    for (int side = 0; side < 4; side++) boxes[side * Anchors + duplicateAnchor] = 4f;
                    Invoke(detector, "Decode", boxes, scores, pose);
                    results = (List<BodyPoseResult>)Field(detector, "m_Candidates");
                    Check(results.Count == 2, "Raw one-to-many model head may contain overlapping candidates");
                    var selected = (BodyPoseResult[])Invoke(detector, "SelectResults", 6);
                    Check(selected.Length == 1, "NMS must collapse overlapping pose candidates");
                    pose[anchor] = -100f / stride - gridX;
                    scores[duplicateAnchor] = -20f;
                    Invoke(detector, "Decode", boxes, scores, pose);
                    results = (List<BodyPoseResult>)Field(detector, "m_Candidates");
                    Near(results[0].keypointScores[0], 0f, "Out-of-view points must be rejected, not clamped to the border");
                }
            }
        }

        static Tensor<float> Output(int channels) =>
            new Tensor<float>(new TensorShape(1, channels, Anchors), new float[channels * Anchors]);

        static BodyPoseResult Pose()
        {
            var pose = new BodyPoseResult
            {
                keypoints = new Vector2[17], keypointScores = new float[17],
                confidence = 0.95f, screenRect = new Rect(0.25f, 0.1f, 0.5f, 0.8f)
            };
            pose.keypoints[0] = new Vector2(0.5f, 0.80f);
            pose.keypoints[1] = new Vector2(0.47f, 0.83f);
            pose.keypoints[2] = new Vector2(0.53f, 0.83f);
            pose.keypoints[3] = new Vector2(0.43f, 0.81f);
            pose.keypoints[4] = new Vector2(0.57f, 0.81f);
            pose.keypoints[5] = new Vector2(0.35f, 0.7f);
            pose.keypoints[6] = new Vector2(0.65f, 0.7f);
            pose.keypoints[11] = new Vector2(0.4f, 0.4f);
            pose.keypoints[12] = new Vector2(0.6f, 0.4f);
            for (int i = 0; i < 17; i++) pose.keypointScores[i] = 0.95f;
            return pose;
        }

        static void ValidateGeometry()
        {
            var pose = Pose();
            Check(BodyPoseGeometry.TryFitHead(pose, 1f, out var head, out float headRadius), "Head fit");
            Check(head.y > pose.keypoints[0].y && headRadius > 0f, "Head center must not be the nose");
            Check(BodyPoseGeometry.TryFitTorso(pose, 1f, out var shoulders, out var hips, out float radius), "Torso fit");
            Near(shoulders.y, 0.7f, "Shoulder center");
            Near(hips.y, 0.4f, "Hip center");
            pose.keypointScores[11] = 0.1f;
            Check(!BodyPoseGeometry.TryFitTorso(pose, 1f, out _, out _, out _), "Low-confidence hip must reject torso");
            Check(BodyPoseGeometry.TryFitHead(pose, 1f, out _, out _), "Occluded hip must not reject head");
            for (int i = 0; i <= 4; i++) pose.keypointScores[i] = 0f;
            Check(!BodyPoseGeometry.TryFitHead(pose, 1f, out _, out _), "Missing face must reject head");

        }

        static void ValidateModel()
        {
            using (var detector = new BodyPoseDetector())
            {
                var load = detector.Load();
                while (load.MoveNext()) { }
                Check(detector.Ready, "Pose model must load: " + detector.Error);
                var detections = detector.Detect(new Color32[200 * 400], 200, 400, 3);
                Check(detections.Length == 0, "Blank image must not detect a person");
                Debug.Log("[PoseValidation] Imported model executed successfully on a portrait blank frame.");
            }
        }

        static object Invoke(object target, string name, params object[] arguments) =>
            target.GetType().GetMethod(name, PrivateInstance).Invoke(target, arguments);
        static object Field(object target, string name) => target.GetType().GetField(name, PrivateInstance).GetValue(target);
        static void SetField(object target, string name, object value) => target.GetType().GetField(name, PrivateInstance).SetValue(target, value);
        static void Near(float actual, float expected, string message) => Check(Mathf.Abs(actual - expected) < 0.0001f,
            message + $": expected {expected}, got {actual}");
        static void Check(bool passed, string message)
        {
            if (!passed) throw new InvalidOperationException("[PoseValidation] FAIL: " + message);
        }
    }
}
