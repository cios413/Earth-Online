using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace YoloSegmentation
{
    /// <summary>
    /// Shows a "deselect" button while a target is selected. Clicking it clears the
    /// selection, which also destroys the generated mesh. Other screen clicks do not
    /// clear the selection.
    /// </summary>
    public class SegmentationSelectionUI : MonoBehaviour
    {
        [SerializeField] SegmentationTargetManager targetManager;
        [SerializeField] GameObject deselectButton;

        Button button;

        void Start()
        {
            if (EventSystem.current == null)
            {
                var eventSystemGo = new GameObject("EventSystem",
                    typeof(EventSystem), typeof(InputSystemUIInputModule));
                eventSystemGo.SetActive(true);
            }

            if (deselectButton == null)
                deselectButton = CreateDeselectButton();
            else
            {
                button = deselectButton.GetComponentInChildren<Button>(true);
                if (button != null)
                    button.onClick.AddListener(OnDeselectClicked);
            }

            if (deselectButton != null)
                deselectButton.SetActive(false);
        }

        void Update()
        {
            if (deselectButton == null)
                return;

            var selected = targetManager != null && targetManager.Selected != null;
            if (deselectButton.activeSelf != selected)
                deselectButton.SetActive(selected);
        }

        void OnDeselectClicked()
        {
            if (targetManager != null)
                targetManager.Select(null);
        }

        GameObject CreateDeselectButton()
        {
            var canvas = FindOverlayCanvas();
            if (canvas == null)
            {
                var canvasGo = new GameObject("YoloSegOverlayCanvas",
                    typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvas = canvasGo.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 200;
            }

            var buttonGo = new GameObject("DeselectButton",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            buttonGo.transform.SetParent(canvas.transform, false);

            var rect = (RectTransform)buttonGo.transform;
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 70f);
            rect.sizeDelta = new Vector2(280f, 84f);

            var image = buttonGo.GetComponent<Image>();
            image.color = new Color(1f, 0.28f, 0.2f, 0.95f);

            button = buttonGo.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(OnDeselectClicked);

            var labelGo = new GameObject("Label",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(buttonGo.transform, false);

            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            var label = labelGo.GetComponent<TextMeshProUGUI>();
            label.text = "取消选择";
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.fontSize = 30f;
            label.raycastTarget = false;

            return buttonGo;
        }

        static Canvas FindOverlayCanvas()
        {
            var canvases = Object.FindObjectsOfType<Canvas>(true);

            foreach (var canvas in canvases)
            {
                if (canvas.renderMode == RenderMode.ScreenSpaceOverlay && canvas.sortingOrder >= 100)
                    return canvas;
            }

            foreach (var canvas in canvases)
            {
                if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                    return canvas;
            }

            return null;
        }
    }
}
