using EarthOL.Deform;
using EarthOL.Detect;
using EarthOL.Throw;
using UnityEngine;

namespace EarthOL
{
    /// <summary>
    /// 两种可选功能的切换器 + 全局输入路由（运行时自动创建，不需要改场景）。
    ///
    ///   · 形变模式 Deform：戳"选中的物体"让它形变（没选中时戳识别图卡片）
    ///   · 投掷模式 Throw ：朝"选中的物体"发射投掷物，命中后贴附表面
    ///
    /// 画面里点物体即可选中（由 ObjectScanner 负责检测与绘制标记）。
    /// 两套功能共用同一个 Vuforia AR 相机，不存在两个 XR 栈抢摄像头的问题。
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class ModeOverlay : MonoBehaviour
    {
        public static ModeOverlay Instance { get; private set; }

        public static bool IsActive => Instance != null;
        public static GameMode Current => Instance != null ? Instance.mode : GameMode.Deform;

        /// <summary>点是否落在左上角的模式按钮上。</summary>
        public static bool IsPointOnModeButton(Vector2 screenPosition)
        {
            if (Instance == null) return false;

            var guiPoint = new Vector2(screenPosition.x, Screen.height - screenPosition.y);

            foreach (var rect in Instance.m_ButtonRects)
                if (rect.width > 0f && rect.Contains(guiPoint)) return true;

            return false;
        }

        /// <summary>
        /// 点是否落在"UI"上（模式按钮 或 物体标记）。
        /// 戳击/投掷都先问这个，避免点按钮或点物体时同时在 3D 里误操作。
        /// </summary>
        public static bool IsPointOnOverlay(Vector2 screenPosition)
        {
            return IsPointOnModeButton(screenPosition) || ObjectScanner.IsPointOnMarker(screenPosition);
        }

        GameMode mode = GameMode.Deform;
        ThrowModeRuntime throwRuntime;
        PokeInput[] pokeInputs;
        PokeWarpDeformer[] cardDeformers;
        Renderer[] cardRenderers;
        Rect[] m_ButtonRects;

        void Awake()
        {
            Instance = this;
            m_ButtonRects = new Rect[0];

            pokeInputs = FindObjectsOfType<PokeInput>(true);
            cardDeformers = FindObjectsOfType<PokeWarpDeformer>(true);

            if (cardDeformers.Length > 0)
            {
                cardRenderers = new Renderer[cardDeformers.Length];

                for (int i = 0; i < cardDeformers.Length; i++)
                    cardRenderers[i] = cardDeformers[i].GetComponent<Renderer>();
            }

            var runtime = new GameObject("EarthOL ThrowMode");
            throwRuntime = runtime.AddComponent<ThrowModeRuntime>();

            // 物体自动检测 + 画面内点选
            if (ObjectScanner.Instance == null && FindObjectOfType<ObjectScanner>() == null)
                gameObject.AddComponent<ObjectScanner>();

            Apply(mode);

            Debug.Log("[Mode] 已启动：默认形变模式；画面里的白框是自动识别到的物体，点它即可选中");
        }

        void Update()
        {
            // 键盘快捷切换（本工程只启用了新输入系统，不能用 UnityEngine.Input）
#if ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.digit1Key.wasPressedThisFrame) Apply(GameMode.Deform);
            else if (keyboard.digit2Key.wasPressedThisFrame) Apply(GameMode.Throw);
#endif
        }

        public void Apply(GameMode next)
        {
            mode = next;

            bool deform = next == GameMode.Deform;

            var scanner = ObjectScanner.Instance;
            var anchor = scanner != null ? scanner.SelectedAnchor : null;
            bool hasSelection = anchor != null;

            // 形变模式：戳击输入
            for (int i = 0; i < pokeInputs.Length; i++)
                if (pokeInputs[i] != null) pokeInputs[i].enabled = deform;

            // 识别图卡片：只有在"没选中物体"时才作为形变目标
            for (int i = 0; i < cardDeformers.Length; i++)
            {
                if (cardDeformers[i] == null) continue;

                bool active = false;   // 识别图卡片投影面已彻底关闭（会铺满视野）
                cardDeformers[i].enabled = active;

                // Collider 的开关交给 Vuforia 的识别状态管理，这里只管显隐
                if (!active && cardRenderers[i] != null) cardRenderers[i].enabled = false;
            }

            // 选中的物体：形变模式下它可以被戳
            if (hasSelection)
            {
                var warp = anchor.GetComponent<PokeWarpDeformer>();
                if (warp != null) warp.enabled = true;   // 投影要一直刷新，否则会显示冻结的旧画面
            }

            // 投掷模式：目标 = 选中的物体，没有选中就用默认目标方块
            if (throwRuntime != null)
            {
                throwRuntime.SetTarget(hasSelection ? anchor : null);
                throwRuntime.SetActive(!deform);
            }

            Debug.Log($"[Mode] 当前模式：{(deform ? "形变模式" : "投掷模式")}｜操作对象：{(hasSelection ? anchor.name : "识别图卡片")}");
        }

        void OnGUI()
        {
            float width = Mathf.Min(Screen.width * 0.62f, 520f);
            float height = Mathf.Max(72f, Screen.height * 0.055f);
            const float pad = 12f;

            var deformRect = new Rect(pad, pad, width, height);
            var throwRect = new Rect(pad, pad + height + pad * 0.5f, width, height);

            m_ButtonRects = new[] { deformRect, throwRect };

            var style = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(height * 0.38f) };

            var previous = GUI.backgroundColor;

            GUI.backgroundColor = mode == GameMode.Deform ? new Color(0.2f, 0.9f, 0.5f) : Color.white;
            if (GUI.Button(deformRect, "① 形变模式", style)) Apply(GameMode.Deform);

            GUI.backgroundColor = mode == GameMode.Throw ? new Color(0.2f, 0.9f, 0.5f) : Color.white;
            if (GUI.Button(throwRect, "② 投掷模式", style)) Apply(GameMode.Throw);

            GUI.backgroundColor = previous;

            var scaffold = ObjectScanner.Instance;
            bool hasSelection = scaffold != null && scaffold.SelectedAnchor != null;

            string hint;
            if (!hasSelection) hint = "先点画面里的物体选中它";
            else hint = mode == GameMode.Deform ? "戳选中的物体 → 形变" : "点选中的物体 → 发射";

            var hintStyle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(height * 0.28f) };
            GUI.Label(new Rect(pad, throwRect.yMax + pad, Screen.width - pad * 2f, height), hint, hintStyle);

            // ---- 状态区：一眼看出识别是否在工作 ----
            var scanner = ObjectScanner.Instance;
            if (scanner == null) return;

            var statusStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(height * 0.24f),
                alignment = TextAnchor.LowerLeft
            };

            double fps = 1.0 / Mathf.Max(0.0001f, Time.smoothDeltaTime);
            var status = $"FPS {fps:F0}   模式：{(mode == GameMode.Deform ? "形变" : "投掷")}\n{scanner.StatusText}";

            float panelHeight = height * 1.3f;
            var panel = new Rect(pad, Screen.height - panelHeight - pad, Screen.width * 0.92f, panelHeight);

            var previousColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.45f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = previousColor;

            GUI.Label(panel, status, statusStyle);

            // ---- 选中点准星 ----
            if (!scanner.HasSelection) return;

            var target = scanner.SelectionGuiPoint;
            const float arm = 22f;
            const float thickness = 3f;

            GUI.color = new Color(0.15f, 1f, 0.45f, 0.95f);
            GUI.DrawTexture(new Rect(target.x - arm, target.y, arm * 2f, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(target.x, target.y - arm, thickness, arm * 2f), Texture2D.whiteTexture);
            GUI.color = previousColor;
        }
    }
}
