using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace YoloSegmentation
{
    /// <summary>
    /// AR Foundation version of the selected-object mesh view. The mesh is anchored to a
    /// detected AR plane through ARRaycastManager when possible, otherwise placed at a
    /// fallback depth in front of the camera.
    /// </summary>
    public class ArSelectedObjectMeshView : MonoBehaviour
    {
        [SerializeField] SegmentationTargetManager targetManager;
        [SerializeField] ArCameraFrameProvider frameProvider;
        [SerializeField] ARRaycastManager raycastManager;
        [SerializeField] YoloSegRunner runner;

        [Tooltip("Fallback distance when no plane is hit.")]
        [SerializeField, Min(0.1f)] float fallbackDepth = 1f;

        [Tooltip("Tint over the live texture. White shows the texture unchanged.")]
        [SerializeField] Color meshColor = Color.white;

        [SerializeField] bool showCenterMarker = true;
        [SerializeField, Min(0.01f)] float markerSize = 0.06f;
        [SerializeField, Min(1)] int contourGridStep = 2;
        [SerializeField, Min(0.1f)] float contourSimplifyEpsilon = 0.5f;
        [SerializeField, Range(0, 2)] int contourSmoothPasses = 1;
        [SerializeField, Min(0f)] float meshThickness = 0.1f;
        [SerializeField] bool showWireframe = true;
        [SerializeField] Color wireframeColor = Color.yellow;
        [SerializeField, Min(0.0005f)] float wireframeWidth = 0.003f;

        Camera arCamera;
        GameObject meshObject;
        GameObject centerMarker;
        GameObject wireframeRoot;
        readonly List<LineRenderer> wireframeLines = new List<LineRenderer>();
        Mesh mesh;
        Vector3[] meshVertices;
        Vector2[] liveUvs;
        MeshFilter filter;
        MeshRenderer meshRenderer;
        Material material;
        int builtTargetId = -1;
        int lastBuiltInferenceVersion = -1;

        public int MeshRebuildCount { get; private set; }
        public int MeshPlaceCount { get; private set; }
        public Vector2 LastPlacedCenterImage { get; private set; }

        void Awake()
        {
            arCamera = Camera.main;
            if (raycastManager == null)
                raycastManager = FindObjectOfType<ARRaycastManager>();
        }

        void Update()
        {
            if (targetManager == null)
            {
                ClearMesh();
                return;
            }

            var selected = targetManager.Selected;
            if (selected == null || selected.instance.mask == null)
            {
                ClearMesh();
                return;
            }

            if (selected.id != builtTargetId ||
                (runner != null && runner.InferenceVersion != lastBuiltInferenceVersion))
                Rebuild(selected);
            else
                PlaceAt(selected);

            if (mesh != null && material != null && material.mainTexture != null && arCamera != null)
                UpdateLiveUvs();
        }

        void Rebuild(SegmentationTarget target)
        {
            if (!MaskContourProcessor.TryExtractOuterContour(
                    target.instance.mask,
                    target.instance.maskWidth,
                    target.instance.maskHeight,
                    contourGridStep,
                    contourSimplifyEpsilon,
                    contourSmoothPasses,
                    out var contour))
            {
                Debug.LogWarning("ArSelectedObjectMeshView: invalid contour for target " + target.id);
                ClearMesh();
                return;
            }

            var pixelsToUnits = WorldUnitsPerMaskPixel(target);
            if (!ContourMeshGenerator.BuildMesh(
                    contour,
                    target.centerImage,
                    pixelsToUnits,
                    meshThickness,
                    target.maskWidth,
                    target.maskHeight,
                    out var newMesh))
            {
                Debug.LogWarning("ArSelectedObjectMeshView: triangulation failed for target " + target.id);
                ClearMesh();
                return;
            }

            EnsureMeshObject();
            if (mesh != null)
            {
                filter.sharedMesh = null;
                Destroy(mesh);
            }

            mesh = newMesh;
            meshVertices = newMesh.vertices;
            liveUvs = new Vector2[meshVertices.Length];
            filter.sharedMesh = mesh;
            builtTargetId = target.id;
            lastBuiltInferenceVersion = runner != null ? runner.InferenceVersion : -1;
            MeshRebuildCount++;

            if (material != null)
            {
                material.mainTexture = frameProvider != null ? frameProvider.FrameTexture : null;
                material.color = meshColor;
            }

            PlaceAt(target);
            BuildWireframe(contour, target.centerImage, pixelsToUnits, meshThickness);

            Debug.Log("ArSelectedObjectMeshView: built mesh for target id=" + target.id +
                      " with " + newMesh.vertexCount + " vertices.");
        }

        void PlaceAt(SegmentationTarget target)
        {
            if (meshObject == null || arCamera == null)
                return;

            var rect = ArScreenMapper.GetRect();
            var screen = new Vector2(
                rect.xMin + (target.centerImage.x / Mathf.Max(1, target.maskWidth)) * rect.width,
                rect.yMin + (target.centerImage.y / Mathf.Max(1, target.maskHeight)) * rect.height);

            var hitPose = new List<ARRaycastHit>();
            if (raycastManager != null &&
                raycastManager.Raycast(screen, hitPose, TrackableType.Planes))
            {
                var pose = hitPose[0].pose;
                meshObject.transform.position = pose.position;
                meshObject.transform.rotation = pose.rotation;
            }
            else
            {
                var world = arCamera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, fallbackDepth));
                meshObject.transform.position = world;
                meshObject.transform.rotation = arCamera.transform.rotation;
            }

            if (showCenterMarker)
            {
                EnsureCenterMarker();
                centerMarker.transform.position = meshObject.transform.position;
                centerMarker.transform.rotation = meshObject.transform.rotation;
                centerMarker.transform.localScale = Vector3.one * markerSize;
            }
            else if (centerMarker != null)
            {
                centerMarker.SetActive(false);
            }

            MeshPlaceCount++;
            LastPlacedCenterImage = target.centerImage;
        }

        void UpdateLiveUvs()
        {
            var rect = ArScreenMapper.GetRect();
            for (var i = 0; i < meshVertices.Length; i++)
            {
                var world = meshObject.transform.TransformPoint(meshVertices[i]);
                var screen = arCamera.WorldToScreenPoint(world);
                liveUvs[i] = new Vector2(
                    (screen.x - rect.xMin) / rect.width,
                    (screen.y - rect.yMin) / rect.height);
            }
            mesh.uv = liveUvs;
        }

        void BuildWireframe(Vector2[] contour, Vector2 centerImage, float pixelsToUnits, float thickness)
        {
            ClearWireframe();
            if (!showWireframe || contour == null || contour.Length < 3)
                return;

            var n = contour.Length;
            var half = Mathf.Max(0f, thickness) * 0.5f;
            var front = new Vector3[n];
            var back = new Vector3[n];

            for (var i = 0; i < n; i++)
            {
                var local = (contour[i] - centerImage) * pixelsToUnits;
                front[i] = new Vector3(local.x, local.y, half);
                back[i] = new Vector3(local.x, local.y, -half);
            }

            wireframeRoot = new GameObject("PrismWireframe");
            wireframeRoot.transform.SetParent(meshObject.transform, false);

            AddLineLoop(front);
            AddLineLoop(back);

            var step = Mathf.Max(1, n / 48);
            for (var i = 0; i < n; i += step)
                AddLine(front[i], back[i]);
        }

        void AddLineLoop(Vector3[] points)
        {
            var positions = new Vector3[points.Length + 1];
            for (var i = 0; i < points.Length; i++)
                positions[i] = points[i];
            positions[points.Length] = points[0];
            AddLine(positions);
        }

        void AddLine(params Vector3[] positions)
        {
            var lineGo = new GameObject("Edge");
            lineGo.transform.SetParent(wireframeRoot.transform, false);

            var line = lineGo.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = positions.Length;
            line.SetPositions(positions);
            line.startWidth = wireframeWidth;
            line.endWidth = wireframeWidth;
            line.startColor = wireframeColor;
            line.endColor = wireframeColor;
            line.numCapVertices = 4;
            line.numCornerVertices = 4;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;

            var shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader != null)
                line.material = new Material(shader);

            wireframeLines.Add(line);
        }

        void ClearWireframe()
        {
            if (wireframeRoot != null)
                Destroy(wireframeRoot);
            wireframeRoot = null;
            wireframeLines.Clear();
        }

        float WorldUnitsPerMaskPixel(SegmentationTarget target)
        {
            if (arCamera == null)
                return 0.01f;

            var rect = ArScreenMapper.GetRect();
            var screenCenter = new Vector2(
                rect.xMin + (target.centerImage.x / Mathf.Max(1, target.maskWidth)) * rect.width,
                rect.yMin + (target.centerImage.y / Mathf.Max(1, target.maskHeight)) * rect.height);

            var p0 = arCamera.ScreenToWorldPoint(new Vector3(screenCenter.x, screenCenter.y, fallbackDepth));
            var p1 = arCamera.ScreenToWorldPoint(new Vector3(screenCenter.x + 1f, screenCenter.y, fallbackDepth));

            var unitsPerScreenPixel = Vector3.Distance(p0, p1);
            var screenPixelsPerMaskPixel = rect.width / Mathf.Max(1, target.maskWidth);
            return unitsPerScreenPixel * screenPixelsPerMaskPixel;
        }

        void EnsureMeshObject()
        {
            if (meshObject != null)
                return;

            meshObject = new GameObject("SelectedObjectMesh");
            filter = meshObject.AddComponent<MeshFilter>();
            meshRenderer = meshObject.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            var shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");

            if (shader != null)
            {
                material = new Material(shader);
                material.color = meshColor;
                meshRenderer.sharedMaterial = material;
            }
        }

        void EnsureCenterMarker()
        {
            if (centerMarker != null)
                return;

            centerMarker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            centerMarker.name = "SelectedObjectCenterMarker";

            var markerRenderer = centerMarker.GetComponent<MeshRenderer>();
            if (markerRenderer != null)
                markerRenderer.material.color = Color.red;

            var markerCollider = centerMarker.GetComponent<Collider>();
            if (markerCollider != null)
                Destroy(markerCollider);
        }

        void ClearMesh()
        {
            if (filter != null)
                filter.sharedMesh = null;
            if (mesh != null)
            {
                Destroy(mesh);
                mesh = null;
            }
            meshVertices = null;
            liveUvs = null;
            if (meshObject != null)
            {
                Destroy(meshObject);
                meshObject = null;
                filter = null;
                meshRenderer = null;
            }
            if (centerMarker != null)
            {
                Destroy(centerMarker);
                centerMarker = null;
            }
            ClearWireframe();
            if (material != null)
            {
                Destroy(material);
                material = null;
            }
            builtTargetId = -1;
            lastBuiltInferenceVersion = -1;
        }

        void OnDestroy()
        {
            if (material != null)
                Destroy(material);
            if (mesh != null)
                Destroy(mesh);
            if (meshObject != null)
                Destroy(meshObject);
            if (centerMarker != null)
                Destroy(centerMarker);
            ClearWireframe();
        }
    }
}
