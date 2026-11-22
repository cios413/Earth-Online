using System.Collections;
using UnityEngine;
using EarthOL.Combat;

namespace EarthOL
{
    /// <summary>游戏流程阶段。</summary>
    public enum FlowScreen { Home, Playing, Result }

    /// <summary>
    /// 游戏流程状态机：开场(双外星人选模式) → 游玩(战斗/自由 AR) → 结算(仅战斗)。
    /// 交互以 ui-simulator/inkplay.html 为准：
    ///   凶狠外星人连点 5 下（停手断连）→ 出拳 + 地球被击 → 战斗；
    ///   友好外星人长按 1.1 秒 → 压扁 + 充能环 → 自由；
    ///   选完模式 → 闪色 + 颜色圆形放大转场。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameFlow : MonoBehaviour
    {
        public static GameFlow Instance { get; private set; }

        public FlowScreen CurrentScreen { get; private set; } = FlowScreen.Home;
        public GameMode Mode => ModeOverlay.Current;

        const float kComboWindow = 1.2f;
        const float kFriendlyHold = 1.1f;
        const int kBreaksToEnter = 5;
        const float kPunchDuration = 0.24f;
        const float kHitDuration = 0.24f;

        // 结算缓存
        int m_Score, m_Throws, m_Pokes;

        // 开场：凶狠连点
        int m_Breaks;
        float m_LastBreakTime = -1f;

        // 开场：友好长按
        float m_FriendlyDownAt = -1f;
        bool m_FriendlyHolding;

        // 特效时间戳
        float m_PunchTime = -1f;
        float m_EarthHitTime = -1f;
        float m_FlashStart = -1f;
        Color m_FlashColor = Color.white;
        float m_WipeStart = -1f;
        Color m_WipeColor = Color.white;
        bool m_Entering;

        ModeOverlay m_Overlay;
        CombatManager m_Combat;

        void Awake() => Instance = this;

        void Start()
        {
            m_Overlay = ModeOverlay.Instance;
            m_Combat = CombatManager.Instance;
            if (m_Combat != null) m_Combat.Finished += OnCombatFinished;
            ApplyScreen();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (m_Combat != null) m_Combat.Finished -= OnCombatFinished;
        }

        void Update()
        {
            if (CurrentScreen == FlowScreen.Home && m_Breaks > 0 && Time.unscaledTime - m_LastBreakTime > kComboWindow)
                m_Breaks = 0;
            if (CurrentScreen == FlowScreen.Home && m_FriendlyHolding &&
                Time.unscaledTime - m_FriendlyDownAt >= kFriendlyHold)
            {
                m_FriendlyHolding = false;
                BeginPlay(GameMode.Free);
            }
        }

        void ApplyScreen()
        {
            bool playing = CurrentScreen == FlowScreen.Playing;
            if (m_Overlay != null) m_Overlay.SetPlaying(playing);
        }

        void BeginPlay(GameMode mode)
        {
            if (m_Entering) return;
            m_Entering = true;
            ResetHomeState();

            var color = mode == GameMode.Attack ? UIPalette.Pink : UIPalette.Green;
            m_FlashColor = color;
            m_FlashStart = Time.unscaledTime;
            m_WipeColor = color;
            m_WipeStart = Time.unscaledTime;

            StartCoroutine(EnterPlay(mode));
        }

        IEnumerator EnterPlay(GameMode mode)
        {
            yield return new WaitForSecondsRealtime(0.30f);   // 圆形盖满屏幕
            if (m_Overlay != null) m_Overlay.Apply(mode);
            if (m_Combat != null) m_Combat.ResetSession();
            CurrentScreen = FlowScreen.Playing;
            ApplyScreen();
            yield return new WaitForSecondsRealtime(0.40f);   // 转场淡出
            m_Entering = false;
        }

        void OnCombatFinished(int score, int throws, int pokes)
        {
            m_Score = score;
            m_Throws = throws;
            m_Pokes = pokes;
            CurrentScreen = FlowScreen.Result;
            ApplyScreen();
        }

        void Replay() => BeginPlay(Mode);

        void GoHome()
        {
            if (m_Combat != null) m_Combat.ResetSession();
            ResetHomeState();
            CurrentScreen = FlowScreen.Home;
            ApplyScreen();
        }

        void ResetHomeState()
        {
            m_Breaks = 0;
            m_LastBreakTime = -1f;
            m_FriendlyHolding = false;
            m_FriendlyDownAt = -1f;
            m_PunchTime = -1f;
            m_EarthHitTime = -1f;
            m_FlashStart = -1f;
            m_WipeStart = -1f;
        }

        // ---------------------------------------------------------------- 开场输入

        public void TapFierce()
        {
            if (CurrentScreen != FlowScreen.Home || m_Entering) return;

            if (Time.unscaledTime - m_LastBreakTime > kComboWindow) m_Breaks = 0;
            m_LastBreakTime = Time.unscaledTime;
            m_Breaks++;

            m_PunchTime = Time.unscaledTime;      // 出拳前冲
            m_EarthHitTime = Time.unscaledTime;   // 地球被击抖动

            if (m_Breaks >= kBreaksToEnter) BeginPlay(GameMode.Attack);
        }

        public void FriendlyHold(bool holding)
        {
            if (CurrentScreen != FlowScreen.Home || m_Entering)
            {
                m_FriendlyHolding = false;
                return;
            }

            if (holding)
            {
                if (!m_FriendlyHolding)
                {
                    m_FriendlyHolding = true;
                    m_FriendlyDownAt = Time.unscaledTime;
                }
            }
            else m_FriendlyHolding = false;
        }

        // ---------------------------------------------------------------- OnGUI

        void OnGUI()
        {
            if (CurrentScreen == FlowScreen.Home) DrawHome();
            else if (CurrentScreen == FlowScreen.Result) DrawResult();

            DrawFlash();
            DrawWipe();
        }

        void DrawHome()
        {
            var prev = GUI.color;
            GUI.color = UIPalette.Ink;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = prev;

            float w = Screen.width, h = Screen.height;

            // 中间地球（带被击抖动）
            var earthTex = UITextures.EarthBall;
            if (earthTex != null)
            {
                float earthSize = Mathf.Min(w * 0.22f, h * 0.26f);
                var earthRect = UITextures.FitRect(w * 0.5f, h * 0.44f, earthSize, earthTex);
                float since = Time.unscaledTime - m_EarthHitTime;
                if (since >= 0f && since < kHitDuration)
                    earthRect.x += Mathf.Sin(since * 70f) * 7f * (1f - since / kHitDuration);
                GUI.DrawTexture(earthRect, earthTex, ScaleMode.ScaleToFit);
            }

            // 凶狠外星人（左，连点；带出拳缩放）
            float alienW = w * 0.30f;
            var fierceTex = UITextures.AlienFierce;
            if (fierceTex != null)
            {
                var fierceRect = UITextures.FitRect(w * 0.21f, h * 0.52f, alienW, fierceTex);
                float since = Time.unscaledTime - m_PunchTime;
                if (since >= 0f && since < kPunchDuration)
                {
                    float k = 0.20f * (1f - since / kPunchDuration);
                    float pw = fierceRect.width * (1f + k), ph = fierceRect.height * (1f + k);
                    fierceRect = new Rect(fierceRect.center.x - pw * 0.5f, fierceRect.center.y - ph * 0.5f, pw, ph);
                }
                GUI.DrawTexture(fierceRect, fierceTex, ScaleMode.ScaleToFit);
                if (GUI.Button(fierceRect, GUIContent.none, GUIStyle.none)) TapFierce();
            }

            // 友好外星人（右，长按；带压扁 + 充能环）
            var friendlyTex = UITextures.AlienFriendly;
            if (friendlyTex != null)
            {
                var friendlyRect = UITextures.FitRect(w * 0.79f, h * 0.52f, alienW, friendlyTex);
                if (m_FriendlyHolding)
                {
                    float t = Mathf.Clamp01((Time.unscaledTime - m_FriendlyDownAt) / kFriendlyHold);
                    float sx = 1f + 0.20f * t, sy = 1f - 0.26f * t;
                    float pw = friendlyRect.width * sx, ph = friendlyRect.height * sy;
                    friendlyRect = new Rect(friendlyRect.center.x - pw * 0.5f, friendlyRect.center.y - ph * 0.5f, pw, ph);
                    DrawChargeRing(friendlyRect, t);
                }
                GUI.DrawTexture(friendlyRect, friendlyTex, ScaleMode.ScaleToFit);
                bool holding = GUI.RepeatButton(friendlyRect, GUIContent.none, GUIStyle.none);
                FriendlyHold(holding);
            }

            var hintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(Mathf.Clamp(h * 0.026f, 18f, 32f)),
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                normal = { textColor = UIPalette.Chalk }
            };
            GUI.Label(new Rect(0, h * 0.05f, w, h * 0.10f),
                "挑一个外星人进场：凶狠连点 5 下 → 战斗 · 友好长按 → 自由", hintStyle);

            // 连击计数贴 + 地球击破进度
            if (m_Breaks > 0)
            {
                var chip = new Rect(w * 0.04f, h * 0.13f, w * 0.34f, h * 0.07f);
                GUI.color = UIPalette.Yellow;
                GUI.DrawTexture(chip, Texture2D.whiteTexture);
                GUI.color = UIPalette.Ink;
                GUI.DrawTexture(new Rect(chip.x, chip.y, chip.width * m_Breaks / kBreaksToEnter, chip.height), Texture2D.whiteTexture);
                var comboStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.RoundToInt(Mathf.Clamp(h * 0.045f, 24f, 44f)),
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = UIPalette.Ink }
                };
                GUI.Label(chip, "连击 ×" + m_Breaks + "  " + m_Breaks + "/" + kBreaksToEnter, comboStyle);
            }
        }

        /// <summary>友好长按充能环：一圈虚线绕中心旋转，随按压进度转满一圈。</summary>
        void DrawChargeRing(Rect center, float progress)
        {
            var pivot = center.center;
            float radius = center.width * 0.62f;
            var prevMatrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(progress * 360f, pivot);

            var prevColor = GUI.color;
            GUI.color = UIPalette.Chalk;
            for (int i = 0; i < 12; i++)
            {
                float a = i * 30f * Mathf.Deg2Rad;
                float x = pivot.x + Mathf.Sin(a) * radius;
                float y = pivot.y - Mathf.Cos(a) * radius;
                GUI.DrawTexture(new Rect(x - 4f, y - 12f, 8f, 24f), Texture2D.whiteTexture);
            }
            GUI.color = prevColor;
            GUI.matrix = prevMatrix;
        }

        void DrawResult()
        {
            var prev = GUI.color;
            GUI.color = UIPalette.Ink;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = prev;

            float w = Screen.width, h = Screen.height;
            var box = new Rect(w * 0.12f, h * 0.16f, w * 0.76f, h * 0.68f);
            GUI.Box(box, GUIContent.none);

            var title = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(Mathf.Clamp(h * 0.06f, 30f, 60f)),
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                normal = { textColor = UIPalette.Pink }
            };
            GUI.Label(new Rect(box.x, box.y + h * 0.04f, box.width, h * 0.10f), "YOU WIN!", title);

            var stat = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(Mathf.Clamp(h * 0.028f, 20f, 40f)),
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = UIPalette.Chalk }
            };
            GUI.Label(new Rect(box.x, box.y + h * 0.20f, box.width, h * 0.08f), "得分 " + m_Score, stat);
            GUI.Label(new Rect(box.x, box.y + h * 0.30f, box.width, h * 0.06f),
                "投中 " + m_Throws + " 次  ·  形变 " + m_Pokes + " 下", stat);

            int btnFont = Mathf.RoundToInt(Mathf.Clamp(h * 0.028f, 20f, 40f));
            var againRect = new Rect(box.x + box.width * 0.10f, box.y + h * 0.46f, box.width * 0.36f, h * 0.10f);
            var homeRect = new Rect(box.x + box.width * 0.54f, box.y + h * 0.46f, box.width * 0.36f, h * 0.10f);

            var againStyle = UIChrome.Button(UIPalette.Green);
            againStyle.fontSize = btnFont;
            UIChrome.DrawShadow(againRect);
            if (GUI.Button(againRect, "再来一局", againStyle)) Replay();

            var homeStyle = UIChrome.Button(UIPalette.Pink);
            homeStyle.fontSize = btnFont;
            UIChrome.DrawShadow(homeRect);
            if (GUI.Button(homeRect, "回开场", homeStyle)) GoHome();
        }

        // ---------------------------------------------------------------- 转场特效

        void DrawFlash()
        {
            if (m_FlashStart < 0f) return;
            float since = Time.unscaledTime - m_FlashStart;
            if (since < 0f || since > 0.35f) return;

            float a = 1f - since / 0.35f;
            var prev = GUI.color;
            GUI.color = new Color(m_FlashColor.r, m_FlashColor.g, m_FlashColor.b, a);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = prev;
        }

        void DrawWipe()
        {
            if (m_WipeStart < 0f) return;
            float since = Time.unscaledTime - m_WipeStart;
            if (since < 0f || since > 0.70f) return;

            float cover = Mathf.Clamp01(since / 0.50f);                 // 前 0.5s 放大盖满
            float alpha = since < 0.50f ? 1f : 1f - (since - 0.50f) / 0.20f;
            float r = Mathf.Lerp(0.03f, Mathf.Max(Screen.width, Screen.height) * 0.75f, cover);

            var prev = GUI.color;
            GUI.color = new Color(m_WipeColor.r, m_WipeColor.g, m_WipeColor.b, alpha);
            GUI.DrawTexture(new Rect(Screen.width * 0.5f - r, Screen.height * 0.5f - r, r * 2f, r * 2f), UIChrome.Circle());
            GUI.color = prev;
        }
    }
}
