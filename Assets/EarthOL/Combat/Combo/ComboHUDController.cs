using UnityEngine;

namespace EarthOL.Combat
{
    /// <summary>
    /// 屏幕固定连击 HUD：COMBO ×N / DAMAGE ×M，支持 MAX COMBO 与 COMBO BREAK。
    /// 与现有 IMGUI 界面保持一致，不使用 GUILayout，避免与其他 OnGUI 脚本冲突。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ComboHUDController : MonoBehaviour
    {
        ComboManager comboManager;
        CombatDamagePipeline pipeline;
        ComboConfig config;

        bool subscribed;
        bool visible;
        bool maxReached;
        bool frozen;
        bool breakActive;
        float breakUntil;
        float bounceUntil;

        const float BounceDuration = 0.16f;

        int count;
        float multiplier = 1f;

        public void Configure(ComboManager combo, CombatDamagePipeline pipe, ComboConfig comboConfig)
        {
            Unsubscribe();
            comboManager = combo;
            pipeline = pipe;
            config = comboConfig;
            Subscribe();
        }

        void OnEnable() => Subscribe();
        void OnDisable() => Unsubscribe();

        void Subscribe()
        {
            if (subscribed) return;
            if (pipeline != null) pipeline.AttackResolved += OnAttackResolved;
            if (comboManager != null)
            {
                comboManager.ComboBreak += OnComboBreak;
                comboManager.Frozen += OnFrozen;
                comboManager.ComboReset += OnReset;
            }
            subscribed = true;
        }

        void Unsubscribe()
        {
            if (!subscribed) return;
            if (pipeline != null) pipeline.AttackResolved -= OnAttackResolved;
            if (comboManager != null)
            {
                comboManager.ComboBreak -= OnComboBreak;
                comboManager.Frozen -= OnFrozen;
                comboManager.ComboReset -= OnReset;
            }
            subscribed = false;
        }

        void OnAttackResolved(AttackOutcome outcome)
        {
            count = outcome.ComboCount;
            multiplier = outcome.ComboMultiplier;
            if (outcome.MaxReached) maxReached = true;
            bounceUntil = Time.unscaledTime + BounceDuration;
            breakActive = false;
            visible = true;
        }

        void OnComboBreak()
        {
            count = 0;
            multiplier = 1f;
            maxReached = false;
            frozen = false;
            breakActive = true;
            breakUntil = Time.unscaledTime + (config != null ? config.ComboBreakDisplayDuration : 0.8f);
            visible = true;
        }

        void OnFrozen()
        {
            frozen = true;
            breakActive = false;
        }

        void OnReset()
        {
            visible = false;
            frozen = false;
            maxReached = false;
            breakActive = false;
            count = 0;
            multiplier = 1f;
        }

        void OnGUI()
        {
            if (ResultUI.IsShowing) return;
            if (!visible || config == null) return;
            float now = Time.unscaledTime;

            if (breakActive)
            {
                if (now >= breakUntil)
                {
                    visible = false;
                    breakActive = false;
                    return;
                }
                DrawBreak(now);
                return;
            }

            DrawCombo(now);
        }

        void DrawCombo(float now)
        {
            float baseSize = Mathf.Clamp(Screen.height * 0.048f, 26f, 56f);
            float bounce = 0f;
            if (now < bounceUntil)
            {
                float t = 1f - (bounceUntil - now) / BounceDuration;
                bounce = Mathf.Sin(t * Mathf.PI) * 0.16f;
            }

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(baseSize * (1f + bounce)),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperLeft
            };
            style.normal.textColor = maxReached
                ? new Color(1f, 0.82f, 0.25f)
                : frozen ? new Color(0.78f, 0.78f, 0.85f) : Color.white;

            string[] lines = maxReached
                ? new[] { "MAX COMBO", "COMBO ×" + count, "DAMAGE ×" + multiplier.ToString("0.00") }
                : new[] { "COMBO ×" + count, "DAMAGE ×" + multiplier.ToString("0.00") };

            float lineHeight = style.fontSize * 1.28f;
            float maxWidth = 0f;
            foreach (var line in lines)
            {
                var size = style.CalcSize(new GUIContent(line));
                if (size.x > maxWidth) maxWidth = size.x;
            }

            const float pad = 24f;
            var background = new Rect(pad - 12f, pad - 12f, maxWidth + 24f, lines.Length * lineHeight + 24f);
            var previousColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.45f);
            GUI.DrawTexture(background, Texture2D.whiteTexture);
            GUI.color = previousColor;

            for (int i = 0; i < lines.Length; i++)
                GUI.Label(new Rect(pad, pad + i * lineHeight, maxWidth + 24f, lineHeight), lines[i], style);
        }

        void DrawBreak(float now)
        {
            float duration = config.ComboBreakDisplayDuration;
            float t = Mathf.Clamp01((now - (breakUntil - duration)) / duration);
            float alpha = t < 0.75f ? 1f : Mathf.Lerp(1f, 0f, (t - 0.75f) / 0.25f);

            int fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.height * 0.055f, 34f, 66f));
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            style.normal.textColor = new Color(1f, 0.32f, 0.28f, alpha);

            var size = style.CalcSize(new GUIContent("COMBO BREAK"));
            var rect = new Rect((Screen.width - size.x) * 0.5f, Screen.height * 0.26f, size.x, size.y);
            GUI.Label(rect, "COMBO BREAK", style);
        }
    }
}
