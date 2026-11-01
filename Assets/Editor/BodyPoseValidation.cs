using System;
using System.Collections.Generic;
using System.Reflection;
using EarthOL.BodyThrow;
using Unity.Barracuda;
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
            ValidateInput();
            foreach (int layout in new[] { 0, 1, 2 })
                foreach (int anchor in new[] { 1378, 3055, 3464 })
                    foreach (bool portrait in new[] { true, false })
                        ValidateDecode(layout, anchor, portrait);
            ValidateGeometry();
            ValidateTracker();
            BodyProxyStabilizerValidation.Validate();
            ValidateModel();
            Debug.Log("[PoseValidation] PASS: RGB orientation, letterbox round trips, three strides/layouts, " +
                "confidence filtering, head/torso fitting, smoothing, loss, target gating and model inference.");
        }

        static void ValidateInput()
        {
            using (var detector = new BodyPoseDetector())
            {
                var pixels = new Color32[8];
                for (int i = 0; i < 8; i++) pixels[i] = i < 4 ? new Color32(255, 0, 0, 255) : new Color32(0, 255, 0, 255);
                using (var tensor = (Tensor)Invoke(detector, "BuildInput", pixels, 2, 4))
                {
                    Near(tensor[0, 0, 208, 1], 1f, "Top input row must be green");
                    Near(tensor[0, 415, 208, 0], 1f, "Bottom input row must be red");
                    Near(tensor[0, 100, 0, 0], 114f / 255f, "Letterbox padding");
                }
            }
        }

        static void ValidateDecode(int layout, int anchor, bool portrait)
        {
            using (var detector = new BodyPoseDetector())
            {
                int width = portrait ? 200 : 400;
                int height = portrait ? 400 : 200;
                using (var input = (Tensor)Invoke(detector, "BuildInput", new Color32[width * height], width, height)) { }
                using (var boxes = Output(layout, 64))
                using (var pose = Output(layout, 51))
                using (var scores = new Tensor(1, 1, Anchors, 1))
                {
                    for (int i = 0; i < Anchors; i++) scores[i] = -20f;
                    scores[anchor] = 10f;
                    for (int side = 0; side < 4; side++)
                        for (int bin = 0; bin < 16; bin++)
                            Set(boxes, layout, 64, side * 16 + bin, anchor, bin == 3 ? 10f : -10f);

                    float gridX, gridY, stride;
                    if (anchor < 2704) { gridX = anchor % 52 + 0.5f; gridY = anchor / 52 + 0.5f; stride = 8f; }
                    else if (anchor < 3380) { gridX = (anchor - 2704) % 26 + 0.5f; gridY = (anchor - 2704) / 26 + 0.5f; stride = 16f; }
                    else { gridX = (anchor - 3380) % 13 + 0.5f; gridY = (anchor - 3380) / 13 + 0.5f; stride = 32f; }
                    for (int i = 0; i < 17; i++)
                    {
                        Set(pose, layout, 51, i * 3, anchor, (208f / stride - gridX + 0.5f) * 0.5f);
                        Set(pose, layout, 51, i * 3 + 1, anchor, (156f / stride - gridY + 0.5f) * 0.5f);
                        Set(pose, layout, 51, i * 3 + 2, anchor, 10f);
                    }
                    Invoke(detector, "Decode", boxes, scores, pose);
                    var results = (List<BodyPoseResult>)Field(detector, "m_Candidates");
                    Check(results.Count == 1, "Decode must produce one candidate");
                    Near(results[0].keypoints[0].x, 0.5f, "Letterbox x round trip");
                    Near(results[0].keypoints[0].y, portrait ? 0.625f : 0.75f, "Letterbox y round trip");
                    Check(results[0].keypointScores[0] > 0.99f, "Visibility sigmoid");
                    Set(pose, layout, 51, 0, anchor, (-100f / stride - gridX + 0.5f) * 0.5f);
                    Invoke(detector, "Decode", boxes, scores, pose);
                    Near(results[0].keypointScores[0], 0f, "Out-of-view points must be rejected, not clamped to the border");
                }
            }
        }

        static Tensor Output(int layout, int channels) => layout == 0 ? new Tensor(1, 1, Anchors, channels) :
            layout == 1 ? new Tensor(1, 1, channels, Anchors) : new Tensor(1, channels, Anchors, 1);

        static void Set(Tensor tensor, int layout, int channels, int channel, int anchor, float value) =>
            tensor[layout == 0 ? anchor * channels + channel : channel * Anchors + anchor] = value;

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

            var root = new GameObject("PoseValidationRig");
            try
            {
                var rig = root.AddComponent<BodyProxyRig>();
                rig.BuildDefaultRig();
                Check(rig.Parts.Count == 2, "Only head and torso should be generated");
                rig.SetTorsoPose(new Vector3(0f, 1.4f, 2f), new Vector3(0f, 0.9f, 2f), 0.2f);
                var capsule = root.transform.Find("ChestProxy").GetComponent<CapsuleCollider>();
                Near(capsule.height, 0.5f, "Capsule outer height must equal shoulder-to-hip distance");
                Near(capsule.transform.position.y + capsule.height * 0.5f, 1.4f, "Capsule top");
                rig.SetHeadPose(new Vector3(0f, 1.6f, 2f), 0.1f);
                foreach (var part in rig.Parts) Check(part.transform.localScale == Vector3.one, "Attachment root scale");
                rig.HideHead();
                Check(!root.transform.Find("HeadProxy").gameObject.activeSelf && capsule.gameObject.activeSelf,
                    "Head visibility and collision must be independent of torso");
                rig.HideTrackedParts();
                Check(!rig.HasTrackedPart, "Hidden parts must not allow throws");
            }
            finally { Object.DestroyImmediate(root); }
        }

        static void ValidateTracker()
        {
            var root = new GameObject("PoseValidationTracker");
            var cameraRoot = new GameObject("PoseValidationCamera");
            var rigRoot = new GameObject("PoseValidationTrackedRig");
            try
            {
                var camera = cameraRoot.AddComponent<Camera>();
                camera.pixelRect = new Rect(0f, 0f, 1000f, 1000f);
                var rig = rigRoot.AddComponent<BodyProxyRig>();
                rig.BuildDefaultRig();
                var tracker = root.AddComponent<BodyPoseTracker>();
                tracker.Configure(camera, rig);
                var first = Pose();
                Invoke(tracker, "Smooth", first);
                var second = Pose();
                second.keypoints[0] += Vector2.right * 0.03f;
                Invoke(tracker, "Smooth", second);
                Check(tracker.Current.keypoints[0].x > 0.5f && tracker.Current.keypoints[0].x < 0.53f, "Smoothing must be used");
                Invoke(tracker, "ApplyToRig", tracker.Current);
                Check(rig.HasTrackedPart, "Reliable smoothed points must activate proxies");
                var low = Pose();
                low.keypointScores[0] = 0.1f;
                Invoke(tracker, "Smooth", low);
                Near(tracker.Current.keypointScores[0], 0f, "Low-confidence point must not reuse old confidence");
                var jump = Pose();
                jump.keypoints[0] = new Vector2(0.9f, 0.2f);
                Invoke(tracker, "Smooth", jump);
                Near(tracker.Current.keypointScores[0], 0f, "First large jump must be rejected");
                Invoke(tracker, "Smooth", jump);
                Check(tracker.Current.keypointScores[0] > 0f, "Repeated motion can be confirmed");

                var other = Pose();
                other.screenRect = new Rect(0.9f, 0.1f, 0.1f, 0.4f);
                var match = (BodyPoseResult)Invoke(tracker, "SelectCandidate", (object)new[] { other });
                Check(!match.IsValid, "Unrelated person must not be automatically selected");
                SetField(tracker, "m_HeadSeenTime", Time.realtimeSinceStartup - 2f);
                SetField(tracker, "m_TorsoSeenTime", Time.realtimeSinceStartup - 2f);
                Invoke(tracker, "ExpireParts");
                Check(!rig.HasTrackedPart, "Expired parts must disable collision");
                Invoke(tracker, "ResetTracking", "test");
                Check(!tracker.HasPose && !tracker.CanThrow, "Loss must clear pose and throwing");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(cameraRoot);
                Object.DestroyImmediate(rigRoot);
            }
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
