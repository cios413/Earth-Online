using System.Collections.Generic;
using EarthOL.BodyThrow;
using UnityEngine;

namespace EarthOL.ARCore
{
    [DefaultExecutionOrder(10000)]
    public sealed class ARCoreModeOverlay : MonoBehaviour
    {
        public static ARCoreModeOverlay Instance { get; private set; }
        static readonly Rect[] s_NoRects = new Rect[0];

        ARCoreMainSceneBootstrap m_Bootstrap;
        Rect[] m_ButtonRects = s_NoRects;
        Rect m_ShowcaseExitRect;
        GameMode m_Mode = GameMode.Deform;
        bool m_StickerSkinPickerOpen;
        Vector2 m_SkinScroll;

        public bool IsStickerSkinPickerOpen => m_StickerSkinPickerOpen && m_Bootstrap != null &&
            m_Bootstrap.SessionActive && m_Bootstrap.SelectedAppMode == AppMode.Free &&
            m_Mode == GameMode.Sticker && !IsShowingShowcase;

        public Rect StickerSkinPanelRect
        {
            get
            {
                float scale = Mathf.Min(Screen.width / 1260f, Screen.height / 2732f);
                float pad = Mathf.Max(10f, 24f * scale);
                float side = Mathf.Max(68f, 106f * scale);
                float right = Mathf.Min(Screen.width - pad, Screen.safeArea.xMax - pad) - side - pad;
                float width = Mathf.Min(Mathf.Max(200f, Screen.width * .30f), right - pad);
                float height = Mathf.Min(Mathf.Max(300f, 560f * scale), Screen.height * .58f);
                float top = Mathf.Clamp(Screen.height * .43f - side * 2f - pad,
                    Screen.height - Screen.safeArea.yMax + pad, Screen.height - height - pad);
                return new Rect(Mathf.Max(pad, right - width), top, width, height);
            }
        }

        public static GameMode CurrentMode => Instance != null ? Instance.m_Mode : GameMode.Deform;
        public static bool IsStickerMode => CurrentMode == GameMode.Sticker;
        public static bool IsShowcaseMode => CurrentMode == GameMode.Showcase;
        public static bool IsThrowMode => CurrentMode == GameMode.Throw;
        public static bool IsProjectilePickerOpen => Instance != null && Instance.m_Bootstrap != null &&
            Instance.m_Bootstrap.ThrowController != null && Instance.m_Bootstrap.ThrowController.IsPickerOpen;
        public static bool IsRadialHoldOpen => IsProjectilePickerOpen;
        public static bool WheelControlsPersonInput => Instance != null && Instance.m_Bootstrap != null &&
            Instance.m_Bootstrap.FreeWheel != null && Instance.m_Bootstrap.FreeWheel.ControlsPersonInput;
        public static bool WheelTapToThrowEnabled => Instance != null && Instance.m_Bootstrap != null &&
            Instance.m_Bootstrap.FreeWheel != null && Instance.m_Bootstrap.FreeWheel.TapToThrowEnabled;
        public static bool BlocksWorldInput => IsProjectilePickerOpen || Instance != null &&
            Instance.m_Bootstrap != null && (!Instance.m_Bootstrap.SessionActive ||
                Instance.m_Bootstrap.FreeWheel != null && Instance.m_Bootstrap.FreeWheel.OwnsPointer ||
                Instance.m_Bootstrap.CombatController != null && Instance.m_Bootstrap.CombatController.BlocksInput);
        public void BeginRadialSelection(Vector2 point) { }
        public void UpdateRadialSelection(Vector2 point) { }
        public void EndRadialSelection(bool canceled) { }

        public static bool IsPointOnOverlay(Vector2 screenPosition)
        {
            if (Instance == null) return false;
            if (BlocksWorldInput) return true;
            var guiPoint = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
            if (Instance.m_Bootstrap != null && Instance.m_Bootstrap.FreeWheel != null &&
                Instance.m_Bootstrap.FreeWheel.BlocksPoint(guiPoint)) return true;
            return IsPointOnControls(screenPosition);
        }

        public static bool IsPointOnControls(Vector2 screenPosition)
        {
            if (Instance == null) return false;
            var guiPoint = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
            if (Instance.IsStickerSkinPickerOpen && Instance.StickerSkinPanelRect.Contains(guiPoint)) return true;
            foreach (var rect in Instance.m_ButtonRects) if (rect.Contains(guiPoint)) return true;
            if (Instance.m_Bootstrap != null && Instance.m_Bootstrap.CombatController != null &&
                Instance.m_Bootstrap.CombatController.IsPointOnControls(guiPoint)) return true;
            return false;
        }

        public static bool IsShowcaseExitPoint(Vector2 screenPosition)
        {
            if (Instance == null || !Instance.IsShowingShowcase) return false;
            var guiPoint = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
            return Instance.m_ShowcaseExitRect.Contains(guiPoint);
        }

        bool IsShowingShowcase => m_Bootstrap != null && m_Bootstrap.ShowcaseController != null &&
            m_Bootstrap.ShowcaseController.IsPresenting;

        public void Configure(ARCoreMainSceneBootstrap bootstrap)
        {
            Instance = this;
            m_Bootstrap = bootstrap;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        public void SelectFeature(GameMode mode) => Apply(mode);

        public void ToggleStickerSkinPicker()
        {
            if (m_Bootstrap == null || !m_Bootstrap.SessionActive || m_Bootstrap.SelectedAppMode != AppMode.Free) return;
            bool open = !IsStickerSkinPickerOpen;
            Apply(GameMode.Sticker);
            m_StickerSkinPickerOpen = open;
            m_SkinScroll = Vector2.zero;
        }

        public bool SelectStickerSkin(string skinId)
        {
            if (!IsStickerSkinPickerOpen || !m_Bootstrap.CartoonCanvas.SelectSkin(skinId)) return false;
            CloseStickerSkinPicker();
            m_Bootstrap.ClickController.ResetInput();
            return true;
        }

        public void CloseStickerSkinPicker() => m_StickerSkinPickerOpen = false;

        void Apply(GameMode mode)
        {
            if (mode != GameMode.Sticker) CloseStickerSkinPicker();
            m_Mode = mode;
            m_Bootstrap?.SetMode(mode);
        }

        void Update()
        {
            if (m_Bootstrap == null || !m_Bootstrap.SessionActive) return;
#if ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.digit1Key.wasPressedThisFrame) Apply(GameMode.Deform);
            else if (keyboard.digit3Key.wasPressedThisFrame) Apply(GameMode.Showcase);
#endif
        }

        void OnGUI()
        {
            if (m_Bootstrap == null) return;
            if (EarthOL.Account.AccountUIController.BlocksGameUi) return;
            if (!m_Bootstrap.SessionActive) { DrawEntry(); return; }
            bool presenting = IsShowingShowcase;
            if (presenting) DrawShowcaseBackground();
            DrawSharedControls(presenting);
            if (!m_Bootstrap.SessionActive) return;
            if (presenting) DrawShowcaseModel();
            else DrawDetectionStatus();
            m_Bootstrap.FreeWheel.Draw();
            if (IsStickerSkinPickerOpen) DrawStickerSkinPicker();
            m_Bootstrap.CombatController?.DrawOverlay(presenting);
        }

        void DrawSharedControls(bool presenting)
        {
            float scale = Mathf.Min(Screen.width / 1260f, Screen.height / 2732f);
            float pad = Mathf.Max(10f, 24f * scale);
            float height = Mathf.Max(44f, 76f * scale);
            float width = Mathf.Max(100f, 176f * scale);
            Rect safe = Screen.safeArea;
            float top = Mathf.Max(pad, Screen.height - safe.yMax + pad);
            var back = new Rect(Mathf.Max(pad, safe.xMin + pad), top, width, height);
            var detection = new Rect(Mathf.Min(Screen.width - pad, safe.xMax - pad) - width, top, width, height);
            var style = new GUIStyle(GUI.skin.button) { fontSize = Mathf.Max(14, Mathf.RoundToInt(25f * scale)), wordWrap = true };
            var rects = new List<Rect>(5) { back, detection };
            bool active = m_Bootstrap.DetectionManager.DetectionEnabled;
            if (GUI.Button(back, "返回选择", style)) { CloseStickerSkinPicker(); m_Bootstrap.ReturnToSelection(); m_ButtonRects = s_NoRects; return; }
            if (GUI.Button(detection, active ? "停止识别" : "开始识别", style))
            {
                if (active) m_Bootstrap.DetectionManager.StopDetection();
                else m_Bootstrap.DetectionManager.StartDetectionScheduled();
            }
            float side = Mathf.Max(68f, 106f * scale);
            float x = Mathf.Min(Screen.width - pad, safe.xMax - pad) - side;
            if (m_Bootstrap.SelectedAppMode == AppMode.Free)
            {
                var sticker = new Rect(x, Screen.height * .43f - side * 2f - pad, side, side);
                rects.Add(sticker);
                Color stickerOld = GUI.backgroundColor;
                GUI.backgroundColor = m_Mode == GameMode.Sticker ? new Color(.35f, .78f, .72f) : new Color(.68f, .75f, .82f);
                if (GUI.Button(sticker, "人物\n贴图", style))
                    ToggleStickerSkinPicker();
                GUI.backgroundColor = stickerOld;
            }
            var showcase = new Rect(x, Screen.height * .43f + pad, side, side);
            rects.Add(showcase);
            Color old = GUI.backgroundColor;
            GUI.backgroundColor = m_Mode == GameMode.Showcase ? new Color(.35f, .78f, .72f) : new Color(.68f, .75f, .82f);
            if (GUI.Button(showcase, "人物\n展示", style)) Apply(m_Mode == GameMode.Showcase ? GameMode.Deform : GameMode.Showcase);
            GUI.backgroundColor = old;
            m_ShowcaseExitRect = default;
            if (presenting)
            {
                m_ShowcaseExitRect = new Rect((Screen.width - width) * .5f, Screen.height - height - Mathf.Max(pad, safe.yMin + pad), width, height);
                rects.Add(m_ShowcaseExitRect);
                if (GUI.Button(m_ShowcaseExitRect, "退出展示", style)) m_Bootstrap.ShowcaseController.ExitPresentation();
            }
            m_ButtonRects = rects.ToArray();
        }

        void DrawStickerSkinPicker()
        {
            Rect panel = StickerSkinPanelRect;
            float gap = Mathf.Max(8f, panel.width * .045f);
            float header = Mathf.Max(32f, panel.height * .11f);
            Color previous = GUI.color;
            GUI.color = new Color(.055f, .09f, .13f, .98f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = previous;
            var title = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(16, Mathf.RoundToInt(panel.width * .078f)),
                alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } };
            GUI.Label(new Rect(panel.x + gap, panel.y + gap, panel.width * .65f, header), "贴图皮肤", title);
            if (GUI.Button(new Rect(panel.xMax - gap - header, panel.y + gap, header, header), "×"))
            { CloseStickerSkinPicker(); return; }
            var hint = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(12, Mathf.RoundToInt(panel.width * .052f)),
                wordWrap = true, normal = { textColor = new Color(.71f, .83f, .89f) } };
            GUI.Label(new Rect(panel.x + gap, panel.y + header + gap, panel.width - gap * 2, header), "用于之后生成的贴图", hint);

            var catalog = m_Bootstrap.CartoonCanvas.SkinCatalog;
            var skins = catalog != null ? catalog.entries : null;
            Rect list = new Rect(panel.x + gap, panel.y + header * 2 + gap * 2,
                panel.width - gap * 2, panel.height - header * 3 - gap * 5);
            float rowHeight = Mathf.Max(72f, panel.width * .42f);
            int count = skins != null ? skins.Length : 0;
            m_SkinScroll = GUI.BeginScrollView(list, m_SkinScroll, new Rect(0, 0, list.width - 18f, count * (rowHeight + gap)));
            bool selected = false;
            for (int i = 0; i < count; i++)
            {
                var skin = skins[i];
                if (skin == null) continue;
                Rect row = new Rect(0, i * (rowHeight + gap), list.width - 18f, rowHeight);
                bool current = m_Bootstrap.CartoonCanvas.SelectedSkinId == skin.id;
                GUI.color = skin.Available ? current ? new Color(.16f, .50f, .46f) : new Color(.16f, .22f, .29f)
                    : new Color(.12f, .15f, .19f);
                GUI.DrawTexture(row, Texture2D.whiteTexture); GUI.color = previous;
                bool wasEnabled = GUI.enabled; GUI.enabled = wasEnabled && skin.Available;
                if (GUI.Button(row, GUIContent.none, GUIStyle.none)) selected = SelectStickerSkin(skin.id);
                GUI.enabled = wasEnabled;
                Rect icon = new Rect(row.x + gap, row.y + gap, row.height - gap * 2, row.height - gap * 2);
                if (skin.headArtwork != null)
                {
                    var sprite = skin.headArtwork; var uv = sprite.textureRect;
                    uv = new Rect(uv.x / sprite.texture.width, uv.y / sprite.texture.height,
                        uv.width / sprite.texture.width, uv.height / sprite.texture.height);
                    GUI.DrawTextureWithTexCoords(icon, sprite.texture, uv);
                }
                else GUI.Label(icon, "?", new GUIStyle(title) { alignment = TextAnchor.MiddleCenter });
                float textX = icon.xMax + gap;
                GUI.Label(new Rect(textX, row.y + gap, row.xMax - textX - gap, row.height * .48f), skin.displayName, title);
                GUI.Label(new Rect(textX, row.y + row.height * .52f, row.xMax - textX - gap, row.height * .40f),
                    !skin.Available ? "敬请期待" : current ? "已选择" : "选择", hint);
            }
            GUI.EndScrollView();
            if (selected) return;
            if (GUI.Button(new Rect(panel.x + gap, panel.yMax - header - gap, panel.width - gap * 2, header), "返回形变"))
                Apply(GameMode.Deform);
        }

        void DrawEntry()
        {
            m_ButtonRects = s_NoRects;
            Color previous = GUI.color;
            GUI.color = new Color(.06f, .09f, .14f, 1f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = previous;
            float width = Mathf.Min(Screen.width * .80f, 700f);
            float x = (Screen.width - width) * .5f;
            float height = Mathf.Clamp(Screen.height * .085f, 80f, 140f);
            var title = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.width * .055f, 28f, 58f)),
                normal = { textColor = Color.white } };
            GUI.Label(new Rect(x, Screen.height * .23f, width, height), "Earth Online", title);
            var button = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(height * .30f) };
            bool wasEnabled = GUI.enabled;
            GUI.enabled = !m_Bootstrap.IsInitializing;
            if (GUI.Button(new Rect(x, Screen.height * .40f, width, height), "战斗模式", button))
                m_Bootstrap.EnterApp(AppMode.Combat);
            if (GUI.Button(new Rect(x, Screen.height * .40f + height * 1.3f, width, height), "自由模式", button))
                m_Bootstrap.EnterApp(AppMode.Free);
            GUI.enabled = wasEnabled;
            var subtitle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.RoundToInt(Mathf.Clamp(height * .19f, 15f, 25f)), wordWrap = true,
                normal = { textColor = new Color(.75f, .82f, .89f) } };
            GUI.Label(new Rect(x, Screen.height * .40f + height * 2.8f, width, height),
                m_Bootstrap.IsInitializing ? "正在准备相机与人物识别…" : "选择你的玩法", subtitle);
        }

        void DrawDetectionStatus()
        {
            var manager = m_Bootstrap != null ? m_Bootstrap.DetectionManager : null;
            if (manager == null) return;
            float scale = Mathf.Min(Screen.width / 1260f, Screen.height / 2732f);
            float pad = Mathf.Max(10f, 24f * scale);
            float top = Mathf.Max(pad, Screen.height - Screen.safeArea.yMax + pad) + Mathf.Max(44f, 76f * scale) + pad * .5f;
            string state = manager.State switch
            {
                PersonDetectionState.LoadingModel => "加载模型",
                PersonDetectionState.WaitingForCamera => "等待相机",
                PersonDetectionState.SearchingForPeople => "搜索人物",
                PersonDetectionState.BuildingShowcase => "生成模型",
                PersonDetectionState.Error => "识别错误",
                _ => "运行中"
            };
            string text = $"{state}   人物 {manager.TrackedPersonCount}   " +
                $"YOLO {manager.MeasuredDetectionFramesPerSecond:F1}/{manager.EffectiveDetectionFramesPerSecond:F1} Hz";
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.height * .014f, 15f, 22f)),
                normal = { textColor = Color.white }
            };
            GUI.Label(new Rect(pad, top, Screen.width - pad * 2f, 32f), text, style);
        }

        static void DrawShowcaseBackground()
        {
            Color previous = GUI.color;
            GUI.color = new Color(.20f, .22f, .24f, .70f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = previous;
        }

        void DrawShowcaseModel()
        {
            Texture preview = m_Bootstrap.ShowcaseController.PreviewTexture;
            if (preview == null) return;
            var rect = m_Bootstrap.ShowcaseController.PreviewRect;
            GUI.DrawTexture(rect, preview, ScaleMode.ScaleToFit, true);
        }
    }
}
