using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace EarthOL.Combat
{
    /// <summary>
    /// 悬浮在目标上方的世界空间血条：红色填充、黄色闪烁、带外边框。
    /// 血量数据与动画分离，HealthSystem 立即更新真实血量，本类只负责呈现
    /// 「整体震动 → 本次扣血区域闪烁 → 缩短」的反馈。
    /// 填充宽度直接由 RectTransform 锚点驱动，扣掉的部分会真正消失；
    /// 连续攻击排队依次播放，每次闪烁只针对本次被扣除的那一段。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HealthBarUI : MonoBehaviour
    {
        [Header("跟随")]
        [SerializeField] Camera targetCamera;
        [SerializeField] Vector3 worldOffset = Vector3.zero;

        [Header("尺寸（世界单位，米）")]
        [SerializeField] Vector2 barSize = new Vector2(0.5f, 0.06f);

        [Header("颜色")]
        [SerializeField] Color fillColor = new Color(0.88f, 0.12f, 0.10f, 1f);      // 红色填充
        [SerializeField] Color flashColor = new Color(1f, 0.88f, 0.05f, 1f);        // 黄色闪烁
        [SerializeField] Color backgroundColor = new Color(0.06f, 0.06f, 0.07f, 0.85f);
        [SerializeField] Color borderColor = new Color(1f, 1f, 1f, 0.95f);

        [Header("外边框厚度（世界单位，米）")]
        [SerializeField, Min(0f)] float borderWidth = 0.006f;

        [Header("动画时长")]
        [SerializeField, Min(0f)] float shakeDuration = 0.10f;
        [SerializeField, Min(0f)] float flashDuration = 0.16f;
        [SerializeField, Min(0f)] float shrinkDuration = 0.14f;

        [Header("动画强度")]
        [SerializeField] float shakeMagnitude = 0.012f;

        Canvas canvas;
        RectTransform shakeRoot;
        RectTransform fillRect;
        RectTransform previewRect;
        Image previewImage;
        Transform anchor;
        Camera cam;

        float displayedFill = 1f;
        float targetFill = 1f;
        readonly Queue<Segment> pending = new Queue<Segment>();
        bool animating;
        bool visible;

        readonly struct Segment
        {
            public readonly float From;
            public readonly float To;
            public Segment(float from, float to) { From = from; To = to; }
        }

        public bool IsVisible => visible;

        public void Bind(Transform anchorTransform, Camera camera)
        {
            anchor = anchorTransform;
            cam = camera != null ? camera : targetCamera;
            if (cam == null) cam = Camera.main;
            BuildIfNeeded();
        }

        public void Initialize(float maxHealth)
        {
            StopAllCoroutines();
            pending.Clear();
            animating = false;
            displayedFill = 1f;
            targetFill = 1f;
            ApplyFill(1f);
            HidePreview();
        }

        public void Show()
        {
            visible = true;
            if (canvas != null) canvas.gameObject.SetActive(true);
        }

        public void Hide()
        {
            visible = false;
            StopAllCoroutines();
            pending.Clear();
            animating = false;
            if (canvas != null) canvas.gameObject.SetActive(false);
        }

        /// <summary>HealthSystem.Damaged 的回调：真实血量已经更新，这里只做呈现动画。</summary>
        public void OnDamaged(float damage, float currentHealth, float maxHealth)
        {
            if (maxHealth <= 0f) return;

            var newFill = Mathf.Clamp01(currentHealth / maxHealth);
            pending.Enqueue(new Segment(targetFill, newFill));
            targetFill = newFill;

            if (!animating)
                StartCoroutine(ProcessQueue());
        }

        void LateUpdate()
        {
            if (!visible || canvas == null || anchor == null || cam == null) return;
            canvas.transform.SetPositionAndRotation(anchor.position + worldOffset, cam.transform.rotation);
        }

        IEnumerator ProcessQueue()
        {
            animating = true;
            while (pending.Count > 0)
            {
                var segment = pending.Dequeue();
                yield return Shake();
                yield return Flash(segment);
                yield return Shrink(segment);
            }
            animating = false;
            ApplyFill(targetFill);
        }

        IEnumerator Shake()
        {
            if (shakeRoot == null || shakeDuration <= 0f) yield break;

            var elapsed = 0f;
            while (elapsed < shakeDuration)
            {
                elapsed += Time.deltaTime;
                var strength = 1f - Mathf.Clamp01(elapsed / shakeDuration);
                var offset = Random.insideUnitCircle * (shakeMagnitude * strength);
                shakeRoot.localPosition = new Vector3(offset.x, offset.y, 0f);
                yield return null;
            }
            shakeRoot.localPosition = Vector3.zero;
        }

        IEnumerator Flash(Segment segment)
        {
            var lost = segment.From - segment.To;
            if (lost <= 0.0001f || previewRect == null || previewImage == null || flashDuration <= 0f)
                yield break;

            ShowPreview(segment.From, segment.To);

            var elapsed = 0f;
            while (elapsed < flashDuration)
            {
                elapsed += Time.deltaTime;
                var alpha = Mathf.FloorToInt(elapsed * 20f) % 2 == 0 ? 0.95f : 0.15f;
                previewImage.color = new Color(flashColor.r, flashColor.g, flashColor.b, alpha);
                yield return null;
            }

            HidePreview();
        }

        IEnumerator Shrink(Segment segment)
        {
            if (fillRect == null || shrinkDuration <= 0f)
            {
                displayedFill = segment.To;
                ApplyFill(segment.To);
                yield break;
            }

            var elapsed = 0f;
            while (elapsed < shrinkDuration)
            {
                elapsed += Time.deltaTime;
                var k = Mathf.Clamp01(elapsed / shrinkDuration);
                displayedFill = Mathf.Lerp(segment.From, segment.To, k);
                ApplyFill(displayedFill);
                yield return null;
            }

            displayedFill = segment.To;
            ApplyFill(segment.To);
        }

        void ApplyFill(float fraction)
        {
            if (fillRect == null) return;
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(Mathf.Clamp01(fraction), 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
        }

        void ShowPreview(float from, float to)
        {
            if (previewRect == null || previewImage == null) return;
            previewRect.anchorMin = new Vector2(to, 0f);
            previewRect.anchorMax = new Vector2(from, 1f);
            previewRect.offsetMin = Vector2.zero;
            previewRect.offsetMax = Vector2.zero;
            previewImage.gameObject.SetActive(true);
        }

        void HidePreview()
        {
            if (previewImage != null) previewImage.gameObject.SetActive(false);
            if (previewRect != null)
            {
                previewRect.anchorMin = Vector2.zero;
                previewRect.anchorMax = new Vector2(1f, 1f);
                previewRect.offsetMin = Vector2.zero;
                previewRect.offsetMax = Vector2.zero;
            }
        }

        void BuildIfNeeded()
        {
            if (canvas != null) return;

            var canvasGo = new GameObject("HealthBarCanvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);

            canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 300;
            canvasGo.GetComponent<GraphicRaycaster>().enabled = false;

            var canvasRect = (RectTransform)canvasGo.transform;
            canvasRect.sizeDelta = barSize;

            shakeRoot = CreateRect("ShakeRoot", canvasRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            // 外边框：铺满整个血条槽。
            var borderRect = CreateRect("Border", shakeRoot, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var borderImage = borderRect.gameObject.AddComponent<Image>();
            borderImage.color = borderColor;
            borderImage.raycastTarget = false;

            // 背景：比边框内缩 borderWidth，露出外边框。
            var inset = new Vector2(borderWidth, borderWidth);
            var backgroundRect = CreateRect("Background", shakeRoot, Vector2.zero, Vector2.one, inset, -inset);
            var backgroundImage = backgroundRect.gameObject.AddComponent<Image>();
            backgroundImage.color = backgroundColor;
            backgroundImage.raycastTarget = false;

            // 红色填充与黄色闪烁区域都放在背景内部，用锚点控制宽度。
            fillRect = CreateRect("Fill", backgroundRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var fillImage = fillRect.gameObject.AddComponent<Image>();
            fillImage.color = fillColor;
            fillImage.raycastTarget = false;
            ApplyFill(1f);

            previewRect = CreateRect("DamagePreview", backgroundRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            previewImage = previewRect.gameObject.AddComponent<Image>();
            previewImage.color = flashColor;
            previewImage.raycastTarget = false;
            previewImage.gameObject.SetActive(false);

            canvas.gameObject.SetActive(false);
        }

        static RectTransform CreateRect(
            string objectName, RectTransform parent,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(objectName, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            return rect;
        }
    }
}
