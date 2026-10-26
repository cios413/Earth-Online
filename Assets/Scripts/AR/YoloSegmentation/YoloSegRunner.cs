using System;
using UnityEngine;
using Unity.Sentis;

namespace YoloSegmentation
{
    /// <summary>
    /// Records how a source image is mapped into the square model input, so later
    /// stages can map mask coordinates back to the original image.
    /// </summary>
    public struct LetterboxInfo
    {
        public int sourceWidth;
        public int sourceHeight;
        public int inputWidth;
        public int inputHeight;
        public float scale;
        public int letterboxedWidth;
        public int letterboxedHeight;
        public int padX;
        public int padY;
    }

    /// <summary>
    /// Loads a YOLO segmentation ONNX model through Sentis and runs it asynchronously.
    /// The worker is scheduled and the output is downloaded only after the readback
    /// request completes, so the main thread is never blocked by inference.
    /// </summary>
    public class YoloSegRunner : MonoBehaviour
    {
        [Tooltip("The imported YOLO segmentation ONNX model.")]
        [SerializeField] ModelAsset modelAsset;

        [Tooltip("Inference backend. Start with CPU, try GPUCompute in the performance phase.")]
        [SerializeField] BackendType backend = BackendType.CPU;

        [Tooltip("Optional static image used to verify inference before wiring the camera.")]
        [SerializeField] Texture2D testImage;

        [Tooltip("Run once on the test image when the component starts.")]
        [SerializeField] bool autoRunTestImageOnStart = false;

        [Tooltip("Optional live camera frame source.")]
        [SerializeField] ArCameraFrameProvider frameProvider;

        [Tooltip("Minimum seconds between live inferences.")]
        [SerializeField, Min(0.05f)] float inferenceIntervalSeconds = 0.2f;

        [Tooltip("Log each inference time to the Console.")]
        [SerializeField] bool logInferenceTiming = true;

        [Tooltip("Model input size. YOLO11n-seg uses 640x640 RGB.")]
        [SerializeField, Min(32)] int inputWidth = 640;

        [Tooltip("Model input size. YOLO11n-seg uses 640x640 RGB.")]
        [SerializeField, Min(32)] int inputHeight = 640;

        // Ultralytics letterbox fill is gray (114) and preprocessing is a plain /255 scale.
        const float LetterboxFill = 114f / 255f;
        const float NormalizeScale = 1f / 255f;

        Model runtimeModel;
        Worker worker;
        Tensor<float> inputTensor;
        float[] inputData;
        string modelInputName = "images";
        bool shapesLogged;
        bool inferencePending;
        bool testImageScheduled;
        Tensor<float> pendingOutput0;
        Tensor<float> pendingOutput1;
        float inferenceStartRealtime;
        float lastScheduleTime = -999f;

        /// <summary>True when the model loaded and the worker is ready.</summary>
        public bool IsReady { get; private set; }

        /// <summary>Last model load error, or empty when loaded successfully.</summary>
        public string LoadError { get; private set; }

        /// <summary>Inference time in milliseconds for the last completed run.</summary>
        public float LastInferenceMs { get; private set; }

        /// <summary>Coordinate transform used by the last run.</summary>
        public LetterboxInfo LastLetterbox { get; private set; }

        /// <summary>Downloaded output0 of the last completed run.</summary>
        public float[] LastOutput0 { get; private set; }

        /// <summary>Downloaded output1 of the last completed run.</summary>
        public float[] LastOutput1 { get; private set; }

        /// <summary>Increments after every completed run so consumers can detect new results.</summary>
        public int InferenceVersion { get; private set; }

        /// <summary>True while a scheduled inference has not finished yet.</summary>
        public bool InferencePending => inferencePending;

        public Model RuntimeModel => runtimeModel;

        void Start()
        {
            LoadModel();
        }

        void Update()
        {
            if (inferencePending)
            {
                TryCompleteInference();
                return;
            }

            if (!IsReady)
                return;

            if (!testImageScheduled && autoRunTestImageOnStart && testImage != null)
            {
                testImageScheduled = true;
                Schedule(testImage);
                return;
            }

            if (frameProvider != null && frameProvider.HasFrame &&
                Time.unscaledTime - lastScheduleTime >= inferenceIntervalSeconds)
            {
                frameProvider.RefreshFrame();
                Schedule(frameProvider.FrameTexture);
            }
        }

        void OnDisable()
        {
            DisposeWorker();
        }

        public void LoadModel()
        {
            DisposeWorker();

            if (modelAsset == null)
            {
                LoadError = "No ModelAsset assigned. Drag the ONNX model asset onto the component.";
                Debug.LogError("YoloSegRunner: " + LoadError);
                return;
            }

            try
            {
                runtimeModel = ModelLoader.Load(modelAsset);
            }
            catch (Exception e)
            {
                LoadError = "Model load failed: " + e.Message;
                Debug.LogError("YoloSegRunner: " + LoadError);
                return;
            }

            LogModelInfo();

            try
            {
                worker = new Worker(runtimeModel, backend);
            }
            catch (Exception e)
            {
                LoadError = "Worker creation failed: " + e.Message;
                Debug.LogError("YoloSegRunner: " + LoadError);
                return;
            }

            LoadError = string.Empty;
            IsReady = true;
        }

        /// <summary>
        /// Schedules a non-blocking inference on the given texture. Returns false if the
        /// worker is busy or the source is invalid. Results appear in LastOutput0/LastOutput1
        /// when InferenceVersion increments.
        /// </summary>
        public bool Schedule(Texture2D source)
        {
            if (!IsReady || inferencePending)
                return false;

            if (source == null)
            {
                Debug.LogWarning("YoloSegRunner: cannot run, source texture is null.");
                return false;
            }

            Color32[] pixels;
            try
            {
                pixels = source.GetPixels32();
            }
            catch (Exception e)
            {
                Debug.LogError("YoloSegRunner: cannot read texture pixels. Enable Read/Write on imported textures. " + e.Message);
                return false;
            }

            LastLetterbox = ComputeLetterbox(source.width, source.height, inputWidth, inputHeight);
            EnsureInputTensor();
            FillInputTensor(pixels, source.width, source.height, LastLetterbox);

            worker.SetInput(modelInputName, inputTensor);
            worker.Schedule();

            pendingOutput0 = worker.PeekOutput("output0") as Tensor<float>;
            pendingOutput1 = worker.PeekOutput("output1") as Tensor<float>;

            if (!shapesLogged)
            {
                shapesLogged = true;
                Debug.Log("YoloSegRunner: output0 shape = " + (pendingOutput0 != null ? pendingOutput0.shape.ToString() : "null"));
                Debug.Log("YoloSegRunner: output1 shape = " + (pendingOutput1 != null ? pendingOutput1.shape.ToString() : "null"));
            }

            pendingOutput0?.ReadbackRequest();
            pendingOutput1?.ReadbackRequest();

            inferencePending = true;
            inferenceStartRealtime = Time.realtimeSinceStartup;
            lastScheduleTime = Time.unscaledTime;
            return true;
        }

        void TryCompleteInference()
        {
            if (pendingOutput0 == null || pendingOutput1 == null ||
                !pendingOutput0.IsReadbackRequestDone() ||
                !pendingOutput1.IsReadbackRequestDone())
                return;

            LastOutput0 = pendingOutput0.DownloadToArray();
            LastOutput1 = pendingOutput1.DownloadToArray();
            LastInferenceMs = (Time.realtimeSinceStartup - inferenceStartRealtime) * 1000f;
            InferenceVersion++;

            inferencePending = false;
            pendingOutput0 = null;
            pendingOutput1 = null;

            if (logInferenceTiming)
                Debug.Log("YoloSegRunner: inference took " + LastInferenceMs.ToString("0.0") + " ms on " + backend +
                          (LastOutput0 != null ? ", output0 elements=" + LastOutput0.Length : string.Empty) +
                          (LastOutput1 != null ? ", output1 elements=" + LastOutput1.Length : string.Empty));
        }

        /// <summary>Returns a worker-owned output tensor. Only valid until the next Schedule.</summary>
        public Tensor<float> PeekOutput(string name)
        {
            return IsReady ? worker.PeekOutput(name) as Tensor<float> : null;
        }

        void LogModelInfo()
        {
            Debug.Log("YoloSegRunner: backend = " + backend);

            if (runtimeModel.inputs.Count > 0)
            {
                modelInputName = runtimeModel.inputs[0].name;
                Debug.Log("YoloSegRunner: input  " + modelInputName + " shape = " + runtimeModel.inputs[0].shape);
            }

            foreach (var output in runtimeModel.outputs)
                Debug.Log("YoloSegRunner: output " + output.name);
        }

        void EnsureInputTensor()
        {
            var elementCount = 3 * inputHeight * inputWidth;
            if (inputData == null || inputData.Length != elementCount)
                inputData = new float[elementCount];

            if (inputTensor == null)
                inputTensor = new Tensor<float>(new TensorShape(1, 3, inputHeight, inputWidth), clearOnInit: true);
        }

        void FillInputTensor(Color32[] pixels, int sourceWidth, int sourceHeight, LetterboxInfo info)
        {
            var fill = LetterboxFill;
            for (var i = 0; i < inputData.Length; i++)
                inputData[i] = fill;

            for (var y = 0; y < info.letterboxedHeight; y++)
            {
                var gy = (y + 0.5f) / info.scale - 0.5f;
                var y0 = Mathf.Clamp(Mathf.FloorToInt(gy), 0, sourceHeight - 1);
                var y1 = Mathf.Min(y0 + 1, sourceHeight - 1);
                var ty = Mathf.Clamp01(gy - y0);

                for (var x = 0; x < info.letterboxedWidth; x++)
                {
                    var gx = (x + 0.5f) / info.scale - 0.5f;
                    var x0 = Mathf.Clamp(Mathf.FloorToInt(gx), 0, sourceWidth - 1);
                    var x1 = Mathf.Min(x0 + 1, sourceWidth - 1);
                    var tx = Mathf.Clamp01(gx - x0);

                    var p00 = pixels[y0 * sourceWidth + x0];
                    var p10 = pixels[y0 * sourceWidth + x1];
                    var p01 = pixels[y1 * sourceWidth + x0];
                    var p11 = pixels[y1 * sourceWidth + x1];

                    var dstX = x + info.padX;
                    var dstY = y + info.padY;
                    var baseIndex = dstY * inputWidth + dstX;

                    inputData[baseIndex] =
                        SampleChannel(p00.r, p10.r, p01.r, p11.r, tx, ty) * NormalizeScale;
                    inputData[inputHeight * inputWidth + baseIndex] =
                        SampleChannel(p00.g, p10.g, p01.g, p11.g, tx, ty) * NormalizeScale;
                    inputData[2 * inputHeight * inputWidth + baseIndex] =
                        SampleChannel(p00.b, p10.b, p01.b, p11.b, tx, ty) * NormalizeScale;
                }
            }

            inputTensor.Upload(inputData);
        }

        static float SampleChannel(byte v00, byte v10, byte v01, byte v11, float tx, float ty)
        {
            var top = Mathf.Lerp(v00, v10, tx);
            var bottom = Mathf.Lerp(v01, v11, tx);
            return Mathf.Lerp(top, bottom, ty);
        }

        static LetterboxInfo ComputeLetterbox(int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
        {
            var scale = Mathf.Min((float)targetWidth / sourceWidth, (float)targetHeight / sourceHeight);
            var letterboxedWidth = Mathf.Max(1, Mathf.RoundToInt(sourceWidth * scale));
            var letterboxedHeight = Mathf.Max(1, Mathf.RoundToInt(sourceHeight * scale));

            return new LetterboxInfo
            {
                sourceWidth = sourceWidth,
                sourceHeight = sourceHeight,
                inputWidth = targetWidth,
                inputHeight = targetHeight,
                scale = scale,
                letterboxedWidth = letterboxedWidth,
                letterboxedHeight = letterboxedHeight,
                padX = (targetWidth - letterboxedWidth) / 2,
                padY = (targetHeight - letterboxedHeight) / 2
            };
        }

        void DisposeWorker()
        {
            if (worker != null)
            {
                worker.Dispose();
                worker = null;
            }

            if (inputTensor != null)
            {
                inputTensor.Dispose();
                inputTensor = null;
            }

            inputData = null;
            inferencePending = false;
            pendingOutput0 = null;
            pendingOutput1 = null;
            IsReady = false;
        }
    }
}
