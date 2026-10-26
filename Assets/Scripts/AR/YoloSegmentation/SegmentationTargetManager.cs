using System.Collections.Generic;
using UnityEngine;

namespace YoloSegmentation
{
    /// <summary>
    /// Stable per-frame wrapper around a YOLO detection. The id survives across frames as
    /// long as the object stays within the matching threshold; it is not the raw array
    /// index of the detection.
    /// </summary>
    public class SegmentationTarget
    {
        public int id;
        public int classId;
        public float confidence;
        public DetectedInstance instance;
        public Vector2 centerImage;
        public Vector2 detectionBoxMin;
        public Vector2 detectionBoxMax;
        public int maskWidth;
        public int maskHeight;
        public bool isSelected;
        public float lastSeenTime;
    }

    /// <summary>
    /// Maintains stable target identities and the current selection. Matching uses the
    /// mask area centroid normalized by the mask dimensions, which is order-independent.
    /// </summary>
    public class SegmentationTargetManager : MonoBehaviour
    {
        [SerializeField] SegmentationPipeline pipeline;

        [Tooltip("Max normalized center distance for matching the same object across frames.")]
        [SerializeField, Range(0.05f, 0.5f)] float matchMaxCenterDistance = 0.25f;

        [Tooltip("Seconds to keep a target alive when it is not detected.")]
        [SerializeField, Min(0.2f)] float lostTimeout = 1.5f;

        readonly List<SegmentationTarget> targets = new List<SegmentationTarget>();
        int nextId = 1;

        public IReadOnlyList<SegmentationTarget> Targets => targets;

        public SegmentationTarget Selected
        {
            get
            {
                for (var i = 0; i < targets.Count; i++)
                {
                    if (targets[i].isSelected)
                        return targets[i];
                }
                return null;
            }
        }

        public event System.Action<SegmentationTarget> SelectionChanged;

        void Update()
        {
            if (pipeline == null)
                return;

            UpdateTargets(pipeline.Detections);
        }

        /// <summary>Selects a target, or clears the selection when target is null.</summary>
        public void Select(SegmentationTarget target)
        {
            foreach (var t in targets)
                t.isSelected = false;

            if (target != null && targets.Contains(target))
                target.isSelected = true;

            Debug.Log(target != null
                ? "SegmentationTargetManager: selected target id=" + target.id
                : "SegmentationTargetManager: selection cleared.");

            SelectionChanged?.Invoke(Selected);
        }

        void UpdateTargets(IReadOnlyList<DetectedInstance> detections)
        {
            var now = Time.time;
            var matched = new bool[detections.Count];
            var kept = new List<SegmentationTarget>();

            foreach (var existing in targets)
            {
                var bestIndex = -1;
                var bestDistance = float.MaxValue;

                for (var i = 0; i < detections.Count; i++)
                {
                    if (matched[i])
                        continue;

                    var detection = detections[i];
                    var center = MaskCentroid(detection.mask, detection.maskWidth, detection.maskHeight);
                    var dx = (center.x - existing.centerImage.x) / Mathf.Max(1, existing.maskWidth);
                    var dy = (center.y - existing.centerImage.y) / Mathf.Max(1, existing.maskHeight);
                    var distance = Mathf.Sqrt(dx * dx + dy * dy);

                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestIndex = i;
                    }
                }

                if (bestIndex >= 0 && bestDistance <= matchMaxCenterDistance)
                {
                    matched[bestIndex] = true;
                    ApplyDetection(existing, detections[bestIndex], now);
                    kept.Add(existing);
                }
                else if (now - existing.lastSeenTime <= lostTimeout || existing.isSelected)
                {
                    kept.Add(existing);
                }
            }

            for (var i = 0; i < detections.Count; i++)
            {
                if (matched[i])
                    continue;

                var target = new SegmentationTarget { id = nextId++ };
                ApplyDetection(target, detections[i], now);
                kept.Add(target);
            }

            targets.Clear();
            targets.AddRange(kept);
        }

        static void ApplyDetection(SegmentationTarget target, DetectedInstance detection, float now)
        {
            target.instance = detection;
            target.classId = detection.classId;
            target.confidence = detection.confidence;
            target.maskWidth = detection.maskWidth;
            target.maskHeight = detection.maskHeight;
            target.centerImage = MaskCentroid(detection.mask, detection.maskWidth, detection.maskHeight);
            target.detectionBoxMin = new Vector2(detection.boxMinX, detection.boxMinY);
            target.detectionBoxMax = new Vector2(detection.boxMaxX, detection.boxMaxY);
            target.lastSeenTime = now;
        }

        public static Vector2 MaskCentroid(byte[] mask, int width, int height)
        {
            long sumX = 0;
            long sumY = 0;
            long count = 0;

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (mask[y * width + x] != 0)
                    {
                        sumX += x;
                        sumY += y;
                        count++;
                    }
                }
            }

            if (count == 0)
                return new Vector2(width * 0.5f, height * 0.5f);

            return new Vector2((float)sumX / count, (float)sumY / count);
        }
    }
}
