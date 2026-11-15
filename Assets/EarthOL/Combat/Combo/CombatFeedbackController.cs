using System;
using UnityEngine;

namespace EarthOL.Combat
{
    /// <summary>
    /// 表现层反馈：普通攻击由 HUD 做轻量弹跳，这里处理暴击文字/闪屏、
    /// 里程碑、倍率升级与 MAX COMBO 的占位效果，并预留暴击音效事件。
    /// 不参与伤害计算。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatFeedbackController : MonoBehaviour
    {
        sealed class TextSlot
        {
            public string Text;
            public float Start;
            public float Duration;
            public Color Color;
            public float SizeScale;
        }

        CombatDamagePipeline pipeline;

        bool subscribed;
        TextSlot critSlot;
        TextSlot tierSlot;
        float critFlashUntil = float.NegativeInfinity;

        public event Action CriticalHitRequested; // 预留暴击音效接口

        public void Configure(CombatDamagePipeline pipe)
        {
            Unsubscribe();
            pipeline = pipe;
            Subscribe();
        }

        void OnEnable() => Subscribe();
        void OnDisable() => Unsubscribe();

        void Subscribe()
        {
            if (subscribed) return;
            if (pipeline != null) pipeline.AttackResolved += OnAttackResolved;
            subscribed = true;
        }

        void Unsubscribe()
        {
            if (!subscribed) return;
            if (pipeline != null) pipeline.AttackResolved -= OnAttackResolved;
            subscribed = false;
        }

        void Update()
        {
            float now = Time.unscaledTime;
            if (critSlot != null && now - critSlot.Start >= critSlot.Duration) critSlot = null;
            if (tierSlot != null && now - tierSlot.Start >= tierSlot.Duration) tierSlot = null;
        }

        void OnAttackResolved(AttackOutcome outcome)
        {
            if (outcome.Critical)
                TriggerCritical();

            if (outcome.MaxReached)
                TriggerMax();
            else if (outcome.Milestone)
                TriggerMilestone(outcome.MilestoneValue);
            else if (outcome.MultiplierUpgrade)
                TriggerUpgrade(outcome.ComboMultiplier);
        }

        void TriggerCritical()
        {
            critSlot = new TextSlot
            {
                Text = "暴击",
                Start = Time.unscaledTime,
                Duration = 0.75f,
                Color = new Color(1f, 0.25f, 0.18f),
                SizeScale = 1.25f
            };
            critFlashUntil = Time.unscaledTime + 0.18f;
            CriticalHitRequested?.Invoke();
        }

        void TriggerMilestone(int value)
        {
            tierSlot = new TextSlot
            {
                Text = "COMBO ×" + value + "!",
                Start = Time.unscaledTime,
                Duration = 1.0f,
                Color = new Color(1f, 0.84f, 0.25f),
                SizeScale = 1.0f
            };
        }

        void TriggerUpgrade(float comboMultiplier)
        {
            tierSlot = new TextSlot
            {
                Text = "DAMAGE UP ×" + comboMultiplier.ToString("0.00"),
                Start = Time.unscaledTime,
                Duration = 0.9f,
                Color = new Color(0.45f, 0.85f, 1f),
                SizeScale = 0.95f
            };
        }

        void TriggerMax()
        {
            tierSlot = new TextSlot
            {
                Text = "MAX COMBO!",
                Start = Time.unscaledTime,
                Duration = 1.6f,
                Color = new Color(1f, 0.82f, 0.22f),
                SizeScale = 1.2f
            };
        }

        void OnGUI()
        {
            if (ResultUI.IsShowing || HealthSettingsOverlay.IsPanelOpen) return;

            float now = Time.unscaledTime;

            if (now < critFlashUntil)
            {
                float t = 1f - (critFlashUntil - now) / 0.18f;
                var previous = GUI.color;
                GUI.color = new Color(1f, 0.1f, 0.05f, Mathf.Lerp(0.28f, 0f, t));
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = previous;
            }

            DrawSlot(critSlot, now, Screen.height * 0.40f);
            DrawSlot(tierSlot, now, Screen.height * 0.49f);
        }

        void DrawSlot(TextSlot slot, float now, float centerY)
        {
            if (slot == null) return;
            float t = Mathf.Clamp01((now - slot.Start) / Mathf.Max(0.01f, slot.Duration));
            float alpha = t < 0.72f ? 1f : Mathf.Lerp(1f, 0f, (t - 0.72f) / 0.28f);
            float pop = t < 0.18f ? Mathf.Sin((t / 0.18f) * Mathf.PI) * 0.18f : 0f;

            int fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.height * 0.05f, 30f, 60f) * slot.SizeScale * (1f + pop));
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            var color = slot.Color;
            color.a = alpha;
            style.normal.textColor = color;

            var size = style.CalcSize(new GUIContent(slot.Text));
            var rect = new Rect((Screen.width - size.x) * 0.5f, centerY - size.y * 0.5f, size.x, size.y);
            GUI.Label(rect, slot.Text, style);
        }
    }
}
