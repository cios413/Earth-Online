using UnityEngine;

namespace EarthOL.Detect
{
    /// <summary>一个被检测到的"物体"候选。</summary>
    public struct DetectedObject
    {
        /// <summary>显示用标签。</summary>
        public string label;

        /// <summary>归一化包围盒（0~1，原点在左下角，和 Unity 视口一致）。</summary>
        public Rect rect;

        /// <summary>置信度 0~1。</summary>
        public float confidence;

        public Vector2 Center => rect.center;
    }

    /// <summary>
    /// 物体检测器接口。
    ///
    /// 现在用的是不依赖模型的 <see cref="SaliencyDetector"/>；
    /// 以后拿到 .onnx 模型，只要再写一个 <c>YoloDetector : IObjectDetector</c>，
    /// 画面内点选、标记绘制、模式接入这些逻辑都不用改。
    /// </summary>
    public interface IObjectDetector
    {
        string Name { get; }

        /// <summary>从一帧画面里找出最多 <paramref name="maxResults"/> 个物体候选。</summary>
        DetectedObject[] Detect(Color32[] pixels, int width, int height, int maxResults);
    }
}
