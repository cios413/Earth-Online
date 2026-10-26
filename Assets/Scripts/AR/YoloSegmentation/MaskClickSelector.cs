using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace YoloSegmentation
{
    /// <summary>
    /// Converts a screen click/tap into a mask-space coordinate and selects the hit target.
    /// Hit testing uses the instance Mask (not the detection box).
    /// </summary>
    public class MaskClickSelector : MonoBehaviour
    {
        [SerializeField] SegmentationTargetManager targetManager;

        void Update()
        {
            var point = ReadClickPoint();
            if (point.HasValue)
                HandleClick(point.Value);
        }

        Vector2? ReadClickPoint()
        {
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
                return mouse.position.ReadValue();

            var touch = Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.wasPressedThisFrame)
                return touch.primaryTouch.position.ReadValue();

            return null;
        }

        void HandleClick(Vector2 screenPoint)
        {
            if (targetManager == null)
                return;

            // Ignore clicks that land on UI, such as the deselect button.
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            var hit = HitTest(screenPoint);
            if (hit != null)
            {
                Debug.Log("MaskClickSelector: hit target id=" + hit.id +
                          " class=" + hit.classId +
                          " confidence=" + hit.confidence.ToString("0.00"));
                targetManager.Select(hit);
            }
            else
            {
                // Background clicks intentionally keep the current selection.
                Debug.Log("MaskClickSelector: click missed all masks (selection unchanged).");
            }
        }

        SegmentationTarget HitTest(Vector2 screenPoint)
        {
            var rect = ArScreenMapper.GetRect();
            var u = (screenPoint.x - rect.xMin) / rect.width;
            var v = (screenPoint.y - rect.yMin) / rect.height;
            if (u < 0f || u > 1f || v < 0f || v > 1f)
                return null;

            SegmentationTarget best = null;
            var bestConfidence = float.NegativeInfinity;

            foreach (var target in targetManager.Targets)
            {
                var instance = target.instance;
                if (instance.mask == null)
                    continue;

                var w = instance.maskWidth;
                var h = instance.maskHeight;
                var mx = Mathf.FloorToInt(u * w);
                var my = Mathf.FloorToInt(v * h);

                if (mx < 0 || mx >= w || my < 0 || my >= h)
                    continue;

                if (instance.mask[my * w + mx] == 0)
                    continue;

                if (target.confidence > bestConfidence)
                {
                    bestConfidence = target.confidence;
                    best = target;
                }
            }

            return best;
        }
    }
}
