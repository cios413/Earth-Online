using EarthOL.Deform;
using UnityEngine;

namespace EarthOL.Detect
{
    /// <summary>
    /// 打开就自动找画面里的物体（最多 3 个），在物体上画可点的标记；
    /// **直接在画面上点物体**即可选中，不用任何面板。
    ///
    /// 选中的物体会生成一个"操作锚点"（一块可形变的圆角块），
    /// 之后它就是当前模式的操作对象：形变模式戳它、投掷模式只朝它发射。
    /// </summary>
    [DefaultExecutionOrder(-300)]
    public sealed class ObjectScanner : MonoBehaviour
    {
        public static ObjectScanner Instance { get; private set; }

        /// <summary>点是否落在某个物体标记上（用于避免同时触发戳/投掷）。</summary>
        public static bool IsPointOnMarker(Vector2 screenPosition)
        {
            if (Instance == null) return false;

            var guiPoint = new Vector2(screenPosition.x, Screen.height - screenPosition.y);

            for (int i = 0; i < Instance.m_MarkerRects.Length; i++)
                if (Instance.m_MarkerRects[i].width > 0f && Instance.m_MarkerRects[i].Contains(guiPoint))
                    return true;

            return false;
        }

        [Header("扫描")]
        [Tooltip("每隔多久重新扫一次画面（秒）")]
        [SerializeField] private float scanInterval = 1.5f;

        [Tooltip("一次最多列出几个物体")]
        [Range(1, 3)] [SerializeField] private int maxObjects = 3;

        [Tooltip("画面差异阈值，越大越迟钝")]
        [Range(0.05f, 0.5f)] [SerializeField] private float colorThreshold = 0.18f;

        [Header("抓帧")]
        [SerializeField] private int captureWidth = 320;
        [SerializeField] private int captureHeight = 320;

        [Tooltip("抓帧时把自己的 3D 叠加物排除掉（只留摄像头画面），避免污染识别")]
        [SerializeField] private bool isolateOverlays = false;

        IObjectDetector detector;
        YoloPersonDetector m_Person;
        DetectedObject[] m_Detections = new DetectedObject[0];
        Rect[] m_MarkerRects = new Rect[0];

        RenderTexture m_Target;
        Texture2D m_Frame;
        Color32[] m_Pixels;
        Camera m_Camera;
        float m_Timer;
        bool m_LoggedDetector;
        float m_LastTapTime = -1f;

        int m_SelectedIndex = -1;
        GameObject m_Anchor;
        Transform m_AnchorTransform;

        public Transform SelectedAnchor => m_AnchorTransform;

        /// <summary>给 HUD 用的状态文本：能一眼看出识别是否在工作。</summary>
        public string StatusText { get; private set; } = "初始化中…";

        /// <summary>最近一次选中的屏幕位置（GUI 坐标），用于画准星。</summary>
        public Vector2 SelectionGuiPoint { get; private set; }

        public bool HasSelection => m_AnchorTransform != null;
        public IObjectDetector Detector => detector;
        public int CandidateCount => m_Detections.Length;

        void Awake()
        {
            Instance = this;
            detector = new PersonOrObjectDetector();
        }

        void Start()
        {
            m_Camera = Camera.main;

            if (m_Camera == null)
            {
                Debug.LogWarning("[Detect] 找不到主相机，物体扫描不可用");
                enabled = false;
                return;
            }

            m_Target = new RenderTexture(captureWidth, captureHeight, 0, RenderTextureFormat.ARGB32)
            {
                name = "ObjectScanTarget"
            };
            m_Target.Create();

            m_Frame = new Texture2D(captureWidth, captureHeight, TextureFormat.RGBA32, false);
            m_Pixels = new Color32[captureWidth * captureHeight];

            m_Person = new YoloPersonDetector();
            StartCoroutine(m_Person.Load());

            Debug.Log($"[Detect] 扫描启动：人=YOLOv8n(320) 物体={detector.Name}，最多 {maxObjects} 个，间隔 {scanInterval:F1}s");
        }

        void OnDestroy()
        {
            if (m_Target != null)
            {
                m_Target.Release();
                m_Target = null;
            }

            if (m_Frame != null) Destroy(m_Frame);
        }

        void Update()
        {
            HandleTap();

            m_Timer += Time.deltaTime;
            if (m_Timer < scanInterval) return;
            m_Timer = 0f;

            Scan();
        }

        // ---------------------------------------------------------------- 扫描

        void Scan()
        {
            if (m_Camera == null || m_Target == null) return;

            // 把 AR 相机的画面（含 Vuforia 摄像头背景）抓到一张小图上。
            // 抓帧时 cullingMask 置 0：稍微把"我们自己的 3D 叠加物"排除掉，
            // 否则卡片/锚点/投掷物会被一起拍进去污染识别（Vuforia 背景走 command buffer，不受影响）。
            var previousTarget = m_Camera.targetTexture;
            var previousMask = m_Camera.cullingMask;

            m_Camera.cullingMask = isolateOverlays ? 0 : previousMask;
            m_Camera.targetTexture = m_Target;
            m_Camera.Render();
            m_Camera.targetTexture = previousTarget;
            m_Camera.cullingMask = previousMask;

            RenderTexture.active = m_Target;
            m_Frame.ReadPixels(new Rect(0, 0, captureWidth, captureHeight), 0, 0);
            m_Frame.Apply(false);
            RenderTexture.active = null;

            m_Pixels = m_Frame.GetPixels32();

            var watch = System.Diagnostics.Stopwatch.StartNew();

            // 物体：不再自动识别（方案 B），改由手指点选
            var objects = System.Array.Empty<DetectedObject>();

            // 人：YOLO（416，只取 person 类）
            var persons = m_Person != null && m_Person.Ready
                ? m_Person.DetectPersons(m_Pixels, captureWidth, captureHeight, maxObjects)
                : System.Array.Empty<DetectedObject>();

            m_Detections = MergePeopleAndObjects(persons, objects, maxObjects);
            watch.Stop();

            // 实测日志：有检出时才打，避免刷屏
            if (m_Detections.Length > 0)
            {
                var summary = new System.Text.StringBuilder();
                foreach (var detection in m_Detections)
                    summary.Append(detection.label).Append(' ').Append(detection.confidence.ToString("F2")).Append("  ");

                Debug.Log($"[Detect] 耗时 {watch.ElapsedMilliseconds}ms → {summary}");
            }

            string personState;
            if (m_Person == null) personState = "未创建";
            else if (m_Person.Ready) personState = "就绪";
            else personState = string.IsNullOrEmpty(m_Person.Error) ? "加载中" : "失败";

            StatusText = $"人检测：{personState}　检出人：{persons.Length}　耗时：{watch.ElapsedMilliseconds}ms　" +
                         $"选中：{(HasSelection ? "已选中" : "未选中")}";

            // 选中的物体如果这次扫描不在了，就取消选中
            if (m_SelectedIndex >= m_Detections.Length) m_SelectedIndex = -1;
        }

        // ---------------------------------------------------------------- 点选

        void HandleTap()
        {
            if (!TryGetTap(out var screenPosition)) return;

            // 防抖：多点/重复事件只算一次
            if (Time.unscaledTime - m_LastTapTime < 0.25f) return;
            m_LastTapTime = Time.unscaledTime;

            // 避开左上角的模式按钮
            if (EarthOL.ModeOverlay.IsPointOnModeButton(screenPosition)) return;

            var guiPoint = new Vector2(screenPosition.x, Screen.height - screenPosition.y);

            // 点中"人"的框 → 选中那个人
            for (int i = 0; i < m_MarkerRects.Length; i++)
            {
                if (m_MarkerRects[i].width <= 0f || !m_MarkerRects[i].Contains(guiPoint)) continue;

                SelectObject(i);
                return;
            }

            // 已经锁定目标了：点击不再重选，交给当前模式（形变/投掷）处理
            if (HasSelection) return;

            // 还没锁定 → 就在点击处放一个操作锚点
            SelectAtScreenPoint(screenPosition);
        }

        /// <summary>在屏幕点击处生成/移动操作锚点：物件由用户手指指定，最稳、零误判。</summary>
        void SelectAtScreenPoint(Vector2 screenPosition)
        {
            if (m_Camera == null) return;

            m_SelectedIndex = -1;

            var viewport = m_Camera.ScreenToViewportPoint(new Vector3(screenPosition.x, screenPosition.y, 0f));
            var ray = m_Camera.ViewportPointToRay(viewport);

            float distance = 0.5f;
            Vector3 point;
            Quaternion rotation;

            if (Physics.Raycast(ray, out var hit, 5f))
            {
                point = hit.point;
                distance = hit.distance;
            }
            else
            {
                point = ray.origin + ray.direction * distance;
            }

            // 代理体正对相机（投影面必须朝着相机，否则投影对不上）
            rotation = m_Camera.transform.rotation * Quaternion.Euler(0f, 180f, 0f);   // 平面法线朝向相机

            // 尺寸按"点击处那块画面区域"估算
            float fraction = EstimateExtent(screenPosition);
            float worldHeight = 2f * distance * Mathf.Tan(m_Camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float size = Mathf.Clamp(worldHeight * fraction, 0.03f, 1.2f);

            EnsureAnchor(point + ray.direction * 0.002f, rotation, size);

            SelectionGuiPoint = new Vector2(screenPosition.x, Screen.height - screenPosition.y);

            Debug.Log($"[Detect] 选中物体：代理体 {size:F3}m（画面占比 {fraction:F2}），位置 {point}");

            if (EarthOL.ModeOverlay.Instance != null)
                EarthOL.ModeOverlay.Instance.Apply(EarthOL.ModeOverlay.Current);
        }

        static bool TryGetTap(out Vector2 screenPosition)
        {
            screenPosition = Vector2.zero;

#if ENABLE_INPUT_SYSTEM
            var touchscreen = UnityEngine.InputSystem.Touchscreen.current;
            if (touchscreen != null)
            {
                foreach (var touch in touchscreen.touches)
                {
                    if (!touch.press.wasPressedThisFrame) continue;
                    screenPosition = touch.position.ReadValue();
                    return true;
                }
            }

            if (!Application.isMobilePlatform)
            {
                var mouse = UnityEngine.InputSystem.Mouse.current;
                if (mouse != null && mouse.leftButton.wasPressedThisFrame)
                {
                    screenPosition = mouse.position.ReadValue();
                    return true;
                }
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.touchCount > 0)
            {
                for (int i = 0; i < Input.touchCount; i++)
                {
                    var touch = Input.GetTouch(i);
                    if (touch.phase != TouchPhase.Began) continue;
                    screenPosition = touch.position;
                    return true;
                }
            }

            if (!Application.isMobilePlatform && Input.GetMouseButtonDown(0))
            {
                screenPosition = Input.mousePosition;
                return true;
            }
#endif

            return false;
        }

        void SelectObject(int index)
        {
            if (index < 0 || index >= m_Detections.Length) return;

            m_SelectedIndex = index;
            var detection = m_Detections[index];

            Vector3 point;
            Quaternion rotation;
            float size;
            ComputeAnchorPose(detection, out point, out rotation, out size);

            EnsureAnchor(point, rotation, size);

            Debug.Log($"[Detect] 选中 {detection.label}（置信度 {detection.confidence:F2}）→ 世界位置 {point}，尺寸 {size:F3}m");

            // 通知模式管理器：操作对象变了
            if (EarthOL.ModeOverlay.Instance != null)
                EarthOL.ModeOverlay.Instance.Apply(EarthOL.ModeOverlay.Current);
        }

        void ComputeAnchorPose(DetectedObject detection, out Vector3 point, out Quaternion rotation, out float size)
        {
            float distance = 0.5f;
            var ray = m_Camera.ViewportPointToRay(new Vector3(detection.Center.x, detection.Center.y, 0f));

            if (Physics.Raycast(ray, out var hit, 5f))
            {
                point = hit.point;
                distance = hit.distance;
                rotation = Quaternion.LookRotation(-hit.normal);
            }
            else
            {
                point = ray.origin + ray.direction * distance;
                rotation = Quaternion.LookRotation(-ray.direction);
            }

            // 用包围盒在该距离下对应的实际尺寸估算锚点大小
            float worldHeight = 2f * distance * Mathf.Tan(m_Camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float worldWidth = worldHeight * m_Camera.aspect;

            size = Mathf.Max(0.03f, Mathf.Min(worldWidth * detection.rect.width, worldHeight * detection.rect.height));
            point += ray.direction * (size * 0.5f);
        }

        /// <summary>
        /// 代理模型：一块 1×1 平面片，用 EarthOL/PokeWarp 的实时摄像头投影。
        /// 静止时它显示的就是相机看到的画面 → 看起来是隐形的，不挡视野；
        /// 戳它形变时投影跟着拉伸/鼓起 → 看起来是那个真实物体在变形。
        /// 尺寸靠 transform.localScale 控制，避免每次选中都重建网格。
        /// </summary>
        void EnsureAnchor(Vector3 point, Quaternion rotation, float size)
        {
            if (m_Anchor == null)
            {
                m_Anchor = new GameObject("SelectedProxy");
                m_AnchorTransform = m_Anchor.transform;

                // AddComponent 会立刻触发 Awake，而 Awake 要用下面的参数建网格/材质，
                // 所以先失活、配置完再激活，否则会用到默认值（实色、无投影）。
                m_Anchor.SetActive(false);

                var warp = m_Anchor.AddComponent<PokeWarpDeformer>();
                warp.planeWidth = 1f;
                warp.planeHeight = 1f;
                warp.gridX = 16;
                warp.gridY = 16;
                warp.planeInXZ = false;      // 面对相机的平面
                warp.useCameraFeed = true;   // 表面实时投影（静止时隐形）
                warp.useTargetTexture = false;
                warp.stretchAmount = 0.45f;
                warp.bulgeAmount = 0.02f;
                warp.logPokes = true;

                m_Anchor.SetActive(true);

                // 零厚度平面：射线会因背面剔除而打不中、投掷物会穿透 —— 换成有厚度的盒子
                foreach (var existing in m_Anchor.GetComponents<Collider>()) Destroy(existing);
                var box = m_Anchor.AddComponent<BoxCollider>();
                box.size = new Vector3(1f, 1f, 0.04f);
            }

            m_AnchorTransform.SetPositionAndRotation(point, rotation);
            m_AnchorTransform.localScale = Vector3.one * Mathf.Max(0.02f, size);
        }

        /// <summary>从点击点向外扩张，估计那块"物体"占画面多大比例（0.10~0.60）。</summary>
        float EstimateExtent(Vector2 screenPosition)
        {
            if (m_Pixels == null || m_Pixels.Length == 0) return 0.28f;

            int fx = Mathf.Clamp((int)(screenPosition.x / Mathf.Max(1f, Screen.width) * captureWidth), 0, captureWidth - 1);
            int fy = Mathf.Clamp((int)(screenPosition.y / Mathf.Max(1f, Screen.height) * captureHeight), 0, captureHeight - 1);

            var seed = m_Pixels[fy * captureWidth + fx];
            int maxHalf = Mathf.Min(captureWidth, captureHeight) / 2;
            int half = Mathf.Max(4, maxHalf / 8);

            for (; half <= maxHalf; half += 3)
            {
                int mismatched = 0;
                int total = 0;

                for (int offset = -half; offset <= half; offset += 2)
                {
                    int left = Mathf.Clamp(fx - half, 0, captureWidth - 1);
                    int right = Mathf.Clamp(fx + half, 0, captureWidth - 1);
                    int top = Mathf.Clamp(fy + offset, 0, captureHeight - 1);
                    int bottom = Mathf.Clamp(fy - offset, 0, captureHeight - 1);

                    if (ColorDistance(m_Pixels[top * captureWidth + left], seed) > 0.22f) mismatched++;
                    if (ColorDistance(m_Pixels[top * captureWidth + right], seed) > 0.22f) mismatched++;
                    total += 2;
                }

                if (total > 0 && mismatched / (float)total > 0.35f)
                    return Mathf.Clamp(half * 2f / captureHeight, 0.10f, 0.60f);
            }

            return 0.35f;
        }

        static float ColorDistance(Color32 a, Color32 b)
        {
            float dr = (a.r - b.r) / 255f, dg = (a.g - b.g) / 255f, db = (a.b - b.b) / 255f;
            return Mathf.Sqrt(dr * dr + dg * dg + db * db);
        }

        // ---------------------------------------------------------------- 标记绘制

        /// <summary>人优先；被人框盖住的物体去掉，避免同一个人既算人又算物体。</summary>
        static DetectedObject[] MergePeopleAndObjects(DetectedObject[] persons, DetectedObject[] objects, int maxResults)
        {
            var merged = new System.Collections.Generic.List<DetectedObject>(maxResults);

            foreach (var person in persons)
            {
                if (merged.Count >= maxResults) break;
                merged.Add(person);
            }

            foreach (var item in objects)
            {
                if (merged.Count >= maxResults) break;

                bool insidePerson = false;

                foreach (var person in persons)
                {
                    if (person.rect.Contains(item.rect.center)) { insidePerson = true; break; }
                }

                if (!insidePerson) merged.Add(item);
            }

            return merged.ToArray();
        }

        void OnGUI()
        {
            if (m_Detections.Length == 0)
            {
                m_MarkerRects = new Rect[0];
                return;
            }

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.height * 0.018f, 18f, 48f)),
                alignment = TextAnchor.LowerLeft
            };

            var rects = new Rect[m_Detections.Length];

            for (int i = 0; i < m_Detections.Length; i++)
            {
                var detection = m_Detections[i];

                // 归一化（原点左下）→ GUI（原点左上）
                var rect = new Rect(
                    detection.rect.x * Screen.width,
                    (1f - detection.rect.y - detection.rect.height) * Screen.height,
                    detection.rect.width * Screen.width,
                    detection.rect.height * Screen.height);

                rects[i] = rect;

                bool selected = i == m_SelectedIndex;
                DrawBox(rect, selected ? new Color(0.15f, 1f, 0.45f, 1f) : new Color(1f, 1f, 1f, 0.75f),
                    selected ? 4f : 2f);

                GUI.Label(new Rect(rect.x, rect.yMax + 2f, 220f, 40f),
                    (selected ? "✔ " : "") + detection.label, style);
            }

            m_MarkerRects = rects;
        }

        /// <summary>画一个空心框（只用 IMGUI 的 DrawTexture，不依赖贴图资源）。</summary>
        static void DrawBox(Rect rect, Color color, float thickness)
        {
            if (Event.current.type != EventType.Repaint) return;

            var previous = GUI.color;
            GUI.color = color;

            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.height * 0.06f + 10f, rect.height * 0.06f + 10f), Texture2D.whiteTexture);

            GUI.color = previous;
        }

        [ContextMenu("测试：选中第一个候选")]
        public void TestSelectFirst()
        {
            if (m_Detections.Length > 0) SelectObject(0);
        }
    }
}
