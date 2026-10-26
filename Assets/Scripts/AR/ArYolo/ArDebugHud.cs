using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace YoloSegmentation
{
    /// <summary>
    /// On-screen diagnostic HUD for the ARCore build. All values are read from live
    /// runtime state; counters only increment when the corresponding action really happens.
    /// </summary>
    public class ArDebugHud : MonoBehaviour
    {
        [SerializeField] bool showDebugHud = true;
        [SerializeField] ArCameraFrameProvider frameProvider;
        [SerializeField] YoloSegRunner runner;
        [SerializeField] SegmentationPipeline pipeline;
        [SerializeField] SegmentationTargetManager targetManager;
        [SerializeField] ArSelectedObjectMeshView meshView;
        [SerializeField] TextMeshProUGUI hudText;

        void Start()
        {
            if (frameProvider == null) frameProvider = FindObjectOfType<ArCameraFrameProvider>();
            if (runner == null) runner = FindObjectOfType<YoloSegRunner>();
            if (pipeline == null) pipeline = FindObjectOfType<SegmentationPipeline>();
            if (targetManager == null) targetManager = FindObjectOfType<SegmentationTargetManager>();
            if (meshView == null) meshView = FindObjectOfType<ArSelectedObjectMeshView>();
            if (hudText == null) hudText = CreateHudText();
        }

        void Update()
        {
            if (hudText == null)
                return;

            if (hudText.gameObject.activeSelf != showDebugHud)
                hudText.gameObject.SetActive(showDebugHud);

            if (showDebugHud)
                hudText.text = BuildText();
        }

        string BuildText()
        {
            var sb = new System.Text.StringBuilder();
            var frameW = frameProvider != null ? frameProvider.FrameWidth : 0;
            var frameH = frameProvider != null ? frameProvider.FrameHeight : 0;
            var rect = ArScreenMapper.GetRect();
            var selected = targetManager != null ? targetManager.Selected : null;

            sb.Append("Frame ").Append(frameW).Append('x').Append(frameH).Append('\n');
            sb.Append("Infer# ").Append(runner != null ? runner.InferenceVersion : 0).Append('\n');
            sb.Append("Mask ").Append(pipeline != null ? pipeline.LastMaskWidth : 0)
              .Append('x').Append(pipeline != null ? pipeline.LastMaskHeight : 0).Append('\n');
            sb.Append("Overlay ").Append(rect.x.ToString("0")).Append(',').Append(rect.y.ToString("0"))
              .Append(' ').Append(rect.width.ToString("0")).Append('x').Append(rect.height.ToString("0")).Append('\n');

            if (selected != null)
            {
                sb.Append("Sel id=").Append(selected.id)
                  .Append(" conf=").Append(selected.confidence.ToString("0.00")).Append('\n');
                sb.Append("Center ").Append(selected.centerImage.x.ToString("0")).Append(',')
                  .Append(selected.centerImage.y.ToString("0")).Append('\n');
            }
            else
            {
                sb.Append("Sel none\n");
            }

            sb.Append("Mesh rebuild=").Append(meshView != null ? meshView.MeshRebuildCount : 0)
              .Append(" place=").Append(meshView != null ? meshView.MeshPlaceCount : 0).Append('\n');

            if (meshView != null && meshView.LastPlacedCenterImage != Vector2.zero)
                sb.Append("MeshCtr ").Append(meshView.LastPlacedCenterImage.x.ToString("0")).Append(',')
                  .Append(meshView.LastPlacedCenterImage.y.ToString("0")).Append('\n');

            return sb.ToString();
        }

        TextMeshProUGUI CreateHudText()
        {
            Canvas canvas = null;
            foreach (var c in FindObjectsOfType<Canvas>(true))
            {
                if (c.renderMode == RenderMode.ScreenSpaceOverlay && c.sortingOrder >= 100)
                {
                    canvas = c;
                    break;
                }
            }

            if (canvas == null)
            {
                var go = new GameObject("YoloSegHudCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvas = go.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 300;
            }

            var textGo = new GameObject("ArDebugHud", typeof(RectTransform));
            textGo.transform.SetParent(canvas.transform, false);
            var rect = (RectTransform)textGo.transform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.anchoredPosition = new Vector2(12f, 12f);
            rect.sizeDelta = new Vector2(640f, 480f);

            var text = textGo.AddComponent<TextMeshProUGUI>();
            text.fontSize = 22f;
            text.color = Color.white;
            text.raycastTarget = false;
            text.alignment = TextAlignmentOptions.TopLeft;
            return text;
        }
    }
}
