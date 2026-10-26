using Unity.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace YoloSegmentation
{
    /// <summary>
    /// AR Foundation camera frame provider. Acquires the latest CPU camera image from the
    /// AR camera, converts it to RGBA32 and applies a configurable rotation/mirror so the
    /// frame matches the screen orientation before it reaches the YOLO pipeline.
    /// </summary>
    public class ArCameraFrameProvider : MonoBehaviour
    {
        [SerializeField] ARCameraManager cameraManager;

        [Tooltip("Optional RawImage that shows the camera preview.")]
        [SerializeField] RawImage debugImage;

        [SerializeField, Min(1)] int frameInterval = 1;
        [SerializeField] float displayUpdateInterval = 0.1f;
        [SerializeField] bool showDebugPreview = false;

        [Tooltip("Clockwise quarter turns applied to the camera frame to match the screen (0/1/2/3).")]
        [SerializeField, Range(0, 3)] int rotationSteps = 1;

        [Tooltip("Mirror the frame horizontally when the preview appears left-right flipped.")]
        [SerializeField] bool mirrorHorizontal = false;

        Texture2D rawTexture;
        Texture2D frameTexture;
        int updateCounter;
        float lastDisplayUpdateTime = -999f;
        bool hasFrame;

        public bool HasFrame => hasFrame;
        public int FrameWidth { get; private set; }
        public int FrameHeight { get; private set; }
        public Texture2D FrameTexture => frameTexture;

        void Awake()
        {
            if (cameraManager == null)
                cameraManager = FindObjectOfType<ARCameraManager>();
        }

        void OnDisable()
        {
            if (rawTexture != null)
                Destroy(rawTexture);
            if (frameTexture != null && frameTexture != rawTexture)
                Destroy(frameTexture);
            rawTexture = null;
            frameTexture = null;
        }

        void Update()
        {
            updateCounter++;
            if (frameInterval < 1 || updateCounter % frameInterval != 0)
                return;
            if (Time.unscaledTime - lastDisplayUpdateTime < displayUpdateInterval)
                return;

            if (!TryAcquireFrame())
                return;

            lastDisplayUpdateTime = Time.unscaledTime;
            UpdateDebugImage();
        }

        public bool TryAcquireFrame()
        {
            if (cameraManager == null || !cameraManager.TryAcquireLatestCpuImage(out var cpuImage))
                return false;

            using (cpuImage)
            {
                const TextureFormat format = TextureFormat.RGBA32;
                var conversion = new XRCpuImage.ConversionParams
                {
                    inputRect = new RectInt(0, 0, cpuImage.width, cpuImage.height),
                    outputDimensions = new Vector2Int(cpuImage.width, cpuImage.height),
                    outputFormat = format,
                    transformation = XRCpuImage.Transformation.MirrorY
                };

                var size = cpuImage.GetConvertedDataSize(conversion);
                var buffer = new NativeArray<byte>(size, Allocator.Temp);
                try
                {
                    cpuImage.Convert(conversion, buffer);
                    EnsureTexture(ref rawTexture, cpuImage.width, cpuImage.height, format);
                    var raw = rawTexture.GetRawTextureData<byte>();
                    raw.CopyFrom(buffer);
                    rawTexture.Apply(false);
                }
                finally
                {
                    if (buffer.IsCreated)
                        buffer.Dispose();
                }
            }

            var steps = ((rotationSteps % 4) + 4) % 4;
            if (steps == 0 && !mirrorHorizontal)
            {
                frameTexture = rawTexture;
                FrameWidth = rawTexture.width;
                FrameHeight = rawTexture.height;
            }
            else
            {
                var outWidth = steps % 2 == 0 ? rawTexture.width : rawTexture.height;
                var outHeight = steps % 2 == 0 ? rawTexture.height : rawTexture.width;
                EnsureTexture(ref frameTexture, outWidth, outHeight, rawTexture.format);
                TransformFrame(rawTexture, frameTexture, steps, mirrorHorizontal);
                FrameWidth = outWidth;
                FrameHeight = outHeight;
            }

            hasFrame = true;
            return true;
        }

        /// <summary>Copies the latest frame immediately (used before inference).</summary>
        public void RefreshFrame()
        {
            TryAcquireFrame();
        }

        static void EnsureTexture(ref Texture2D texture, int width, int height, TextureFormat format)
        {
            if (texture != null &&
                texture.width == width &&
                texture.height == height &&
                texture.format == format)
                return;

            if (texture != null)
                Object.Destroy(texture);

            texture = new Texture2D(width, height, format, false, false);
        }

        static void TransformFrame(Texture2D source, Texture2D destination, int steps, bool mirror)
        {
            var sourcePixels = source.GetPixels32();
            var destinationPixels = new Color32[sourcePixels.Length];
            var sourceWidth = source.width;
            var sourceHeight = source.height;
            var destinationWidth = destination.width;

            for (var y = 0; y < sourceHeight; y++)
            {
                for (var x = 0; x < sourceWidth; x++)
                {
                    int nx;
                    int ny;
                    if (steps == 0)
                    {
                        nx = x;
                        ny = y;
                    }
                    else if (steps == 1)
                    {
                        nx = sourceHeight - 1 - y;
                        ny = x;
                    }
                    else if (steps == 2)
                    {
                        nx = sourceWidth - 1 - x;
                        ny = sourceHeight - 1 - y;
                    }
                    else
                    {
                        nx = y;
                        ny = sourceWidth - 1 - x;
                    }

                    if (mirror)
                        nx = destinationWidth - 1 - nx;

                    destinationPixels[ny * destinationWidth + nx] = sourcePixels[y * sourceWidth + x];
                }
            }

            destination.SetPixels32(destinationPixels);
            destination.Apply(false);
        }

        void UpdateDebugImage()
        {
            if (debugImage == null)
                return;

            if (debugImage.enabled != showDebugPreview)
                debugImage.enabled = showDebugPreview;

            if (showDebugPreview && debugImage.texture != frameTexture)
                debugImage.texture = frameTexture;
        }
    }
}
