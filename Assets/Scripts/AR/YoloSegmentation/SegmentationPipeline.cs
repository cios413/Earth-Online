using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace YoloSegmentation
{
    /// <summary>
    /// Consumes the latest runner outputs, decodes instance masks, extracts their contours
    /// and optionally shows the masks as a semi-transparent debug overlay.
    /// </summary>
    public class SegmentationPipeline : MonoBehaviour
    {
        [SerializeField] YoloSegRunner runner;
        [SerializeField] SegmentationTargetManager targetManager;

        [Tooltip("RawImage placed on top of the camera view to show semi-transparent masks.")]
        [SerializeField] RawImage maskOverlayImage;

        [Tooltip("Optional status text.")]
        [SerializeField] TextMeshProUGUI statusText;

        [SerializeField, Range(0.01f, 0.9f)] float confidenceThreshold = 0.2f;
        [SerializeField, Range(0.1f, 0.95f)] float iouThreshold = 0.45f;
        [SerializeField, Range(0.1f, 0.95f)] float maskThreshold = 0.5f;

        [Tooltip("Color of the outline drawn on the mask boundary.")]
        [SerializeField] Color outlineColor = new Color(0.05f, 0.95f, 0.25f, 1f);

        [Tooltip("Outline thickness in pixels.")]
        [SerializeField, Range(1, 8)] int outlineThickness = 2;

        [SerializeField] bool showMaskFill = true;
        [SerializeField] bool showOutline = true;

        [Tooltip("Grid step for contour extraction. Larger = fewer points, rougher outline.")]
        [SerializeField, Min(1)] int contourGridStep = 4;

        [Tooltip("Contour simplification tolerance in grid units.")]
        [SerializeField, Min(0.1f)] float contourSimplifyEpsilon = 1.2f;

        [Tooltip("Log detection and contour counts after each decode.")]
        [SerializeField] bool logDecodeInfo = true;

        static readonly Color32[] Palette =
        {
            new Color32(0, 230, 118, 130),
            new Color32(0, 188, 212, 130),
            new Color32(255, 202, 40, 130),
            new Color32(236, 64, 122, 130),
            new Color32(171, 71, 188, 130),
            new Color32(255, 138, 101, 130)
        };

        readonly List<DetectedInstance> detections = new List<DetectedInstance>();
        readonly List<List<Vector2[]>> contours = new List<List<Vector2[]>>();
        Texture2D overlayTexture;
        Color32[] overlayPixels;
        int lastDecodedVersion = -1;

        public IReadOnlyList<DetectedInstance> Detections => detections;

        /// <summary>Contours per detection, in mask pixel coordinates.</summary>
        public IReadOnlyList<List<Vector2[]>> Contours => contours;

        /// <summary>Source frame width of the latest decode.</summary>
        public int LastMaskWidth { get; private set; }

        /// <summary>Source frame height of the latest decode.</summary>
        public int LastMaskHeight { get; private set; }

        void Update()
        {
            if (runner == null || !runner.IsReady || runner.LastOutput0 == null)
                return;

            if (runner.InferenceVersion == lastDecodedVersion)
                return;

            lastDecodedVersion = runner.InferenceVersion;

            if (!SegDecoder.TryDecode(
                    runner.LastOutput0,
                    runner.LastOutput1,
                    runner.LastLetterbox,
                    confidenceThreshold,
                    iouThreshold,
                    maskThreshold,
                    detections,
                    out var error))
            {
                Debug.LogError("SegmentationPipeline: decode failed: " + error);
                return;
            }

            LastMaskWidth = runner.LastLetterbox.sourceWidth;
            LastMaskHeight = runner.LastLetterbox.sourceHeight;
            ExtractContours();

            if (logDecodeInfo)
                Debug.Log("SegmentationPipeline: detections=" + detections.Count +
                          " contourGroups=" + contours.Count);

            RenderOverlay(runner.LastLetterbox.sourceWidth, runner.LastLetterbox.sourceHeight);
            UpdateStatusText();
        }

        void ExtractContours()
        {
            contours.Clear();
            foreach (var detection in detections)
            {
                var polylines = ContourExtractor.Extract(
                    detection.mask,
                    detection.maskWidth,
                    detection.maskHeight,
                    contourGridStep,
                    contourSimplifyEpsilon);

                contours.Add(polylines);
            }
        }

        void RenderOverlay(int width, int height)
        {
            if (maskOverlayImage == null)
                return;

            ArScreenMapper.Fit(maskOverlayImage, width, height);
            ArScreenMapper.OverlayImage = maskOverlayImage;
            EnsureOverlay(width, height);

            var renderDetections = GetRenderDetections();
            var clear = new Color32(0, 0, 0, 0);
            for (var i = 0; i < overlayPixels.Length; i++)
                overlayPixels[i] = clear;

            if (showMaskFill)
            {
                foreach (var detection in renderDetections)
                {
                    if (detection.mask == null || detection.mask.Length != overlayPixels.Length)
                        continue;

                    var color = Palette[detection.classId % Palette.Length];
                    for (var i = 0; i < detection.mask.Length; i++)
                    {
                        if (detection.mask[i] != 0)
                            overlayPixels[i] = color;
                    }
                }
            }

            if (showOutline && renderDetections.Count > 0)
            {
                var boundary = new byte[width * height];
                foreach (var detection in renderDetections)
                {
                    if (detection.mask == null || detection.mask.Length != boundary.Length)
                        continue;
                    MarkBoundary(detection.mask, width, height, boundary);
                }

                for (var pass = 1; pass < outlineThickness; pass++)
                    DilateBoundary(boundary, width, height);

                var outline = (Color32)outlineColor;
                for (var i = 0; i < boundary.Length; i++)
                {
                    if (boundary[i] != 0)
                        overlayPixels[i] = outline;
                }
            }

            overlayTexture.SetPixels32(overlayPixels);
            overlayTexture.Apply(false);

            if (maskOverlayImage.texture != overlayTexture)
                maskOverlayImage.texture = overlayTexture;
        }

        IReadOnlyList<DetectedInstance> GetRenderDetections()
        {
            if (targetManager != null &&
                targetManager.Selected != null &&
                targetManager.Selected.instance.mask != null)
            {
                return new List<DetectedInstance> { targetManager.Selected.instance };
            }

            return detections;
        }

        static void MarkBoundary(byte[] mask, int width, int height, byte[] boundary)
        {
            for (var y = 1; y < height - 1; y++)
            {
                for (var x = 1; x < width - 1; x++)
                {
                    var i = y * width + x;
                    if (mask[i] == 0)
                        continue;

                    if (mask[i - width] == 0 || mask[i + width] == 0 ||
                        mask[i - 1] == 0 || mask[i + 1] == 0)
                        boundary[i] = 1;
                }
            }
        }

        static void DilateBoundary(byte[] boundary, int width, int height)
        {
            var source = (byte[])boundary.Clone();
            for (var y = 1; y < height - 1; y++)
            {
                for (var x = 1; x < width - 1; x++)
                {
                    var i = y * width + x;
                    if (source[i] == 0 &&
                        (source[i - width] != 0 || source[i + width] != 0 ||
                         source[i - 1] != 0 || source[i + 1] != 0))
                        boundary[i] = 1;
                }
            }
        }

        void EnsureOverlay(int width, int height)
        {
            if (overlayTexture != null && overlayTexture.width == width && overlayTexture.height == height)
                return;

            if (overlayTexture != null)
                Destroy(overlayTexture);

            overlayTexture = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            overlayPixels = new Color32[width * height];
        }

        void UpdateStatusText()
        {
            if (statusText == null)
                return;

            var text = "Detections: " + detections.Count;
            foreach (var detection in detections)
                text += "\n" + CocoLabels.Get(detection.classId) + " " + detection.confidence.ToString("0.00");

            statusText.text = text;
        }
    }
}
