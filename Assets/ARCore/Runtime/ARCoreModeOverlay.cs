using UnityEngine;
using EarthOL.BodyThrow;

namespace EarthOL.ARCore
{
    public sealed class ARCoreModeOverlay : MonoBehaviour
    {
        public static ARCoreModeOverlay Instance { get; private set; }

        private ARCoreMainSceneBootstrap m_Bootstrap;
        private Rect[] m_ButtonRects = new Rect[0];
        private GameMode m_Mode = GameMode.Deform;
        private bool m_ChoosingProjectile;
        private float m_InputBlockedUntil;
        private bool m_RadialHeld;
        private int m_RadialIndex = -1;
        private Vector2 m_WheelCenter;
        private Vector2 m_RadialPressPosition;
        private bool m_RadialMoved;
        private Texture2D[] m_WheelTextures;

        public static bool IsRadialHoldOpen => Instance != null && Instance.m_ChoosingProjectile && Instance.m_RadialHeld;
        public static bool IsProjectilePickerOpen => Instance != null && Instance.m_ChoosingProjectile;

        public static bool BlocksWorldInput => Instance != null &&
            (Instance.m_ChoosingProjectile || Time.unscaledTime < Instance.m_InputBlockedUntil ||
             EarthOL.Combat.CombatOverlay.BlocksWorldInput);

        public static bool IsPointOnOverlay(Vector2 screenPosition)
        {
            if (EarthOL.Combat.CombatOverlay.IsPointOnOverlay(screenPosition)) return true;
            if (Instance == null) return false;
            if (BlocksWorldInput) return true;
            var guiPoint = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
            foreach (var rect in Instance.m_ButtonRects)
                if (rect.width > 0f && rect.Contains(guiPoint)) return true;
            return false;
        }

        public void Configure(ARCoreMainSceneBootstrap bootstrap)
        {
            Instance = this;
            m_Bootstrap = bootstrap;
            Apply(GameMode.Deform);
        }

        private void Start() => EnsureWheelTextures();

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (m_WheelTextures != null)
                foreach (var texture in m_WheelTextures) if (texture != null) Destroy(texture);
        }

        private void Apply(GameMode mode)
        {
            CloseProjectilePicker();
            m_Mode = mode;
            if (m_Bootstrap != null) m_Bootstrap.SetMode(mode);
        }

        private void OpenProjectilePicker()
        {
            m_ChoosingProjectile = true;
            m_RadialHeld = false;
            m_RadialIndex = -1;
            m_RadialMoved = false;
            m_WheelCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            if (m_Bootstrap != null && m_Bootstrap.PoseTracker != null) m_Bootstrap.PoseTracker.ClearPendingInput();
        }

        public void BeginRadialSelection(Vector2 screenPosition)
        {
            OpenProjectilePicker();
            m_RadialHeld = true;
            m_RadialPressPosition = screenPosition;
            Debug.Log("[BodyThrow] 长按选择轮盘已打开，滑动选择，松手确认。");
            float radius = WheelRadius;
            float footer = Mathf.Clamp(radius * 0.15f, 36f, 68f);
            m_WheelCenter = new Vector2(Mathf.Clamp(screenPosition.x, radius + 12f, Screen.width - radius - 12f),
                Mathf.Clamp(screenPosition.y, radius + footer + 12f, Screen.height - radius - 12f));
        }

        public void UpdateRadialSelection(Vector2 screenPosition)
        {
            if (!m_ChoosingProjectile) return;
            if (m_RadialHeld && !m_RadialMoved)
            {
                if (Vector2.Distance(screenPosition, m_RadialPressPosition) < 24f)
                {
                    m_RadialIndex = -1;
                    return;
                }
                m_RadialMoved = true;
            }
            m_RadialIndex = ProjectileRadialMath.IndexAt(screenPosition - m_WheelCenter, WheelRadius * 0.40f);
        }

        public void EndRadialSelection(bool canceled)
        {
            if (!canceled && m_RadialIndex >= 0 && m_Bootstrap != null && m_Bootstrap.Spawner != null)
                m_Bootstrap.Spawner.SelectProjectile(m_RadialIndex);
            CloseProjectilePicker();
        }

        private float WheelRadius => Mathf.Max(60f, Mathf.Min(Mathf.Min(Screen.width, Screen.height) * 0.40f, 460f));

        private void CloseProjectilePicker()
        {
            if (!m_ChoosingProjectile) return;
            m_ChoosingProjectile = false;
            m_RadialHeld = false;
            m_InputBlockedUntil = Time.unscaledTime + 0.2f;
            if (m_Bootstrap != null && m_Bootstrap.PoseTracker != null) m_Bootstrap.PoseTracker.ClearPendingInput();
        }

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.digit1Key.wasPressedThisFrame) Apply(GameMode.Deform);
            else if (keyboard.digit2Key.wasPressedThisFrame) Apply(GameMode.Throw);
#endif
        }

        private void OnGUI()
        {
            float width = Mathf.Min(Screen.width * 0.62f, 520f);
            float height = Mathf.Max(72f, Screen.height * 0.055f);
            const float pad = 12f;
            var deformRect = new Rect(pad, pad, width, height);
            var throwRect = new Rect(pad, pad + height + pad * 0.5f, width, height);
            var changeRect = new Rect(pad, throwRect.yMax + pad * 0.5f, width, height);
            var clearRect = new Rect(pad, changeRect.yMax + pad * 0.5f, width, height);
            var diagnosticRect = new Rect(Screen.width - width - pad, Screen.height - height - pad, width, height);
            m_ButtonRects = m_Mode == GameMode.Throw
                ? new[] { deformRect, throwRect, changeRect, clearRect, diagnosticRect } : new[] { deformRect, throwRect };

            var style = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(height * 0.38f) };
            var previous = GUI.backgroundColor;
            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && !m_ChoosingProjectile;

            GUI.backgroundColor = m_Mode == GameMode.Deform ? new Color(0.2f, 0.9f, 0.5f) : Color.white;
            if (GUI.Button(deformRect, "① 形变模式", style)) Apply(GameMode.Deform);
            GUI.backgroundColor = m_Mode == GameMode.Throw ? new Color(0.2f, 0.9f, 0.5f) : Color.white;
            if (GUI.Button(throwRect, "② 投掷模式", style)) Apply(GameMode.Throw);
            GUI.backgroundColor = previous;

            if (m_Mode == GameMode.Throw && GUI.Button(changeRect, "更改投掷物", style)) OpenProjectilePicker();
            if (m_Mode == GameMode.Throw && m_Bootstrap != null && m_Bootstrap.Spawner != null &&
                GUI.Button(clearRect, "清空投掷物 (" + m_Bootstrap.Spawner.ActiveProjectileCount + ")", style))
            {
                if (m_Bootstrap.PoseTracker != null) m_Bootstrap.PoseTracker.ClearPendingInput();
                m_Bootstrap.Spawner.ClearProjectiles();
                m_InputBlockedUntil = Time.unscaledTime + 0.2f;
            }

            if (m_Mode == GameMode.Throw && m_Bootstrap != null && m_Bootstrap.PoseTracker != null)
            {
                var tracker = m_Bootstrap.PoseTracker;
                if (GUI.Button(diagnosticRect, tracker.ShowDiagnostics ? "关闭定位标记" : "显示定位标记", style))
                    tracker.ShowDiagnostics = !tracker.ShowDiagnostics;
            }

            var labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.height * 0.018f, 18f, 36f))
            };
            string hint = m_Mode == GameMode.Deform
                ? "双击人物选择目标，在人物周围按压或拖动，松手后回弹"
                : "双击人物选择目标，轻点发射；长按后滑动选择，松手确认\n当前：" + (m_Bootstrap != null && m_Bootstrap.Spawner != null
                    ? m_Bootstrap.Spawner.SelectedProjectileName : "1号 · 粘粘手");
            float hintY = (m_Mode == GameMode.Throw ? clearRect.yMax : throwRect.yMax) + pad;
            GUI.Label(new Rect(pad, hintY, Screen.width - pad * 2f, height * 2f), hint, labelStyle);
            GUI.enabled = previousEnabled;
            if (m_ChoosingProjectile) DrawProjectilePicker();
        }

        private void DrawProjectilePicker()
        {
            var previousColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.75f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = previousColor;
            EnsureWheelTextures();
            float radius = WheelRadius;
            Vector2 center = new Vector2(m_WheelCenter.x, Screen.height - m_WheelCenter.y);
            GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f), m_WheelTextures[m_RadialIndex + 1]);
            float fontSize = Mathf.Clamp(radius * 0.075f, 18f, 34f);
            var textStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(fontSize), alignment = TextAnchor.MiddleCenter,
                wordWrap = true, normal = { textColor = Color.white }
            };
            var spawner = m_Bootstrap != null ? m_Bootstrap.Spawner : null;
            var catalog = spawner != null ? spawner.Catalog : null;
            if (catalog != null)
            {
                for (int i = 0; i < catalog.entries.Length; i++)
                {
                    float angle = i * Mathf.PI * 0.5f;
                    Vector2 iconCenter = center + new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle)) * radius * 0.67f;
                    float iconSize = radius * 0.48f;
                    var tile = new Rect(iconCenter.x - iconSize * 0.5f, iconCenter.y - iconSize * 0.45f, iconSize, iconSize);
                    var entry = catalog.entries[i];
                    if (entry.preview != null) GUI.DrawTexture(new Rect(tile.x, tile.y, tile.width, tile.height * 0.7f), entry.preview, ScaleMode.ScaleToFit, true);
                    GUI.Label(new Rect(tile.x - 20f, tile.y + tile.height * 0.7f, tile.width + 40f, fontSize * 1.7f), entry.displayName, textStyle);
                    if (!m_RadialHeld && GUI.Button(tile, GUIContent.none, GUIStyle.none))
                    {
                        m_RadialIndex = i;
                        EndRadialSelection(false);
                        break;
                    }
                }
            }
            string selectedName = catalog != null && m_RadialIndex >= 0 ? catalog.entries[m_RadialIndex].displayName : "更改投掷物";
            GUI.Label(new Rect(center.x - radius * 0.36f, center.y - radius * 0.18f, radius * 0.72f, radius * 0.36f), selectedName, textStyle);
            GUI.Label(new Rect(center.x - radius, center.y + radius + 5f, radius * 2f, fontSize * 2f),
                m_RadialHeld ? "滑向选项，松手确认 · 移回中心取消" : "点击选项，或点击中心取消", textStyle);
            if (!m_RadialHeld && GUI.Button(new Rect(center.x - radius * 0.25f, center.y + radius * 0.14f, radius * 0.5f, fontSize * 1.6f), "取消"))
                CloseProjectilePicker();
        }

        private void EnsureWheelTextures()
        {
            if (m_WheelTextures != null) return;
            const int size = 384;
            m_WheelTextures = new Texture2D[5];
            for (int selected = -1; selected < 4; selected++)
            {
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        Vector2 point = new Vector2((x + 0.5f) / size - 0.5f, (y + 0.5f) / size - 0.5f);
                        float distance = point.magnitude;
                        if (distance < 0.20f || distance > 0.49f) continue;
                        int index = ProjectileRadialMath.IndexAt(point, 0f);
                        float angle = Mathf.Repeat(Mathf.Atan2(point.x, point.y) * Mathf.Rad2Deg + 45f, 90f);
                        bool border = distance < 0.207f || distance > 0.482f || angle < 0.8f || angle > 89.2f;
                        pixels[y * size + x] = border ? new Color(0.66f, 0.58f, 0.40f, 0.95f) :
                            index == selected ? new Color(0.61f, 0.40f, 0.16f, 0.96f) : new Color(0.13f, 0.14f, 0.16f, 0.94f);
                    }
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Projectile wheel " + selected, wrapMode = TextureWrapMode.Clamp };
                texture.SetPixels32(pixels);
                texture.Apply(false, true);
                m_WheelTextures[selected + 1] = texture;
            }
        }
    }
}
