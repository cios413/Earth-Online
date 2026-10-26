using TMPro;
using Unity.Sentis;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

namespace YoloSegmentation
{
    /// <summary>
    /// One-click AR Foundation + YOLO scene builder for the figure project.
    /// Run from the menu "ArYolo/Build AR Scene".
    /// </summary>
    public static class ArYoloSceneBuilder
    {
        [MenuItem("ArYolo/Build AR Scene")]
        public static void Build()
        {
            foreach (var name in new[] { "YoloSeg", "YoloSegOverlay", "AR Session", "AR Camera" })
            {
                var existing = GameObject.Find(name);
                if (existing != null)
                    Object.DestroyImmediate(existing);
            }

            // AR Session
            var sessionGo = new GameObject("AR Session");
            sessionGo.AddComponent<ARSession>();

            // AR Camera (pose driver + managers)
            var cameraGo = new GameObject("AR Camera");
            cameraGo.tag = "MainCamera";
            var camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            cameraGo.AddComponent<AudioListener>();
            cameraGo.AddComponent<ARPoseDriver>();
            var cameraManager = cameraGo.AddComponent<ARCameraManager>();
            cameraGo.AddComponent<ARCameraBackground>();
            var raycastManager = cameraGo.AddComponent<ARRaycastManager>();
            cameraGo.AddComponent<ARPlaneManager>();

            // YoloSeg root with the pipeline components
            var root = new GameObject("YoloSeg");
            var frameProvider = root.AddComponent<ArCameraFrameProvider>();
            var runner = root.AddComponent<YoloSegRunner>();
            var pipeline = root.AddComponent<SegmentationPipeline>();
            var targetManager = root.AddComponent<SegmentationTargetManager>();
            var clickSelector = root.AddComponent<MaskClickSelector>();
            var meshView = root.AddComponent<ArSelectedObjectMeshView>();
            var selectionUI = root.AddComponent<SegmentationSelectionUI>();
            var debugHud = root.AddComponent<ArDebugHud>();

            // Overlay canvas with the mask RawImage
            var canvasGo = new GameObject("YoloSegOverlay");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            var maskOverlay = CreateRawImage("MaskOverlay", canvasGo.transform);

            var statusGo = new GameObject("StatusText", typeof(RectTransform));
            statusGo.transform.SetParent(canvasGo.transform, false);
            var status = statusGo.AddComponent<TextMeshProUGUI>();
            status.fontSize = 32f;
            status.color = Color.white;
            status.raycastTarget = false;
            status.text = "ArYolo";
            var statusRect = (RectTransform)statusGo.transform;
            statusRect.anchorMin = new Vector2(0f, 1f);
            statusRect.anchorMax = new Vector2(0f, 1f);
            statusRect.pivot = new Vector2(0f, 1f);
            statusRect.anchoredPosition = new Vector2(20f, -20f);
            statusRect.sizeDelta = new Vector2(700f, 300f);

            // Wiring
            AssignObject(frameProvider, "cameraManager", cameraManager);
            AssignObject(runner, "frameProvider", frameProvider);
            AssignObject(runner, "modelAsset", AssetDatabase.LoadAssetAtPath<ModelAsset>("Assets/Models/yolo11n-seg.onnx"));
            AssignObject(pipeline, "runner", runner);
            AssignObject(pipeline, "targetManager", targetManager);
            AssignObject(pipeline, "maskOverlayImage", maskOverlay);
            AssignObject(pipeline, "statusText", status);
            AssignObject(targetManager, "pipeline", pipeline);
            AssignObject(clickSelector, "targetManager", targetManager);
            AssignObject(meshView, "targetManager", targetManager);
            AssignObject(meshView, "frameProvider", frameProvider);
            AssignObject(meshView, "raycastManager", raycastManager);
            AssignObject(meshView, "runner", runner);
            AssignObject(selectionUI, "targetManager", targetManager);
            AssignObject(debugHud, "frameProvider", frameProvider);
            AssignObject(debugHud, "runner", runner);
            AssignObject(debugHud, "pipeline", pipeline);
            AssignObject(debugHud, "targetManager", targetManager);
            AssignObject(debugHud, "meshView", meshView);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Selection.activeGameObject = root;
            Debug.Log("ArYolo: scene built. Verify the AR camera tracks the device pose; otherwise add an XR Origin.");
        }

        static RawImage CreateRawImage(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var image = go.AddComponent<RawImage>();
            image.raycastTarget = false;
            return image;
        }

        static void AssignObject(Component target, string fieldName, Object value)
        {
            if (value == null)
                return;

            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(fieldName);
            if (property != null)
            {
                property.objectReferenceValue = value;
                serialized.ApplyModifiedProperties();
            }
            else
            {
                Debug.LogWarning("ArYolo: field not found on " + target.GetType().Name + ": " + fieldName);
            }
        }
    }
}
