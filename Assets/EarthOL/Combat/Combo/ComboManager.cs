using System;
using System.Collections.Generic;
using UnityEngine;

namespace EarthOL.Combat
{
    /// <summary>一次成功攻击后连击状态的变化结果。</summary>
    public readonly struct ComboResult
    {
        public readonly int Count;
        public readonly float Multiplier;
        public readonly bool Milestone;
        public readonly int MilestoneValue;
        public readonly bool MultiplierUpgrade;
        public readonly float UpgradedMultiplier;
        public readonly bool MaxReached;

        public ComboResult(int count, float multiplier, bool milestone, int milestoneValue,
            bool multiplierUpgrade, float upgradedMultiplier, bool maxReached)
        {
            Count = count;
            Multiplier = multiplier;
            Milestone = milestone;
            MilestoneValue = milestoneValue;
            MultiplierUpgrade = multiplierUpgrade;
            UpgradedMultiplier = upgradedMultiplier;
            MaxReached = maxReached;
        }
    }

    /// <summary>
    /// 连击状态机：维护连击数、倒计时、倍率、里程碑、冻结与重置。
    /// 不订阅攻击输入，只提供 RegisterHit / Break / Freeze / Reset 供管道调用。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ComboManager : MonoBehaviour
    {
        ComboConfig config;

        readonly HashSet<int> firedMilestones = new HashSet<int>();
        float lastHitTime;
        int lastMultiplierStep;

        public int ComboCount { get; private set; }
        public float CurrentMultiplier { get; private set; }
        public bool IsFrozen { get; private set; }
        public bool MaxMultiplierReached { get; private set; }

        public event Action ComboBreak;
        public event Action Frozen;
        public event Action ComboReset;

        public void Configure(ComboConfig comboConfig)
        {
            config = comboConfig;
            CurrentMultiplier = config != null ? config.InitialMultiplier : 1f;
            lastMultiplierStep = ComputeStep(CurrentMultiplier);
        }

        void Update()
        {
            if (ComboCount <= 0 || IsFrozen || config == null) return;
            if (Time.unscaledTime - lastHitTime > config.ComboDuration)
                BreakCombo();
        }

        /// <summary>成功攻击注册：连击 +1、刷新计时、重算倍率，并返回变化结果。</summary>
        public ComboResult RegisterHit()
        {
            if (IsFrozen)
                return new ComboResult(ComboCount, CurrentMultiplier, false, 0, false, 0f, false);

            ComboCount++;
            lastHitTime = Time.unscaledTime;
            CurrentMultiplier = ComputeMultiplier(ComboCount);

            bool milestone = false;
            int milestoneValue = 0;
            if (IsMilestone(ComboCount) && firedMilestones.Add(ComboCount))
            {
                milestone = true;
                milestoneValue = ComboCount;
            }

            bool upgrade = false;
            float upgradedMultiplier = 0f;
            int step = ComputeStep(CurrentMultiplier);
            if (step > lastMultiplierStep && CurrentMultiplier < config.MaxMultiplier - 0.0001f && !milestone)
            {
                upgrade = true;
                upgradedMultiplier = CurrentMultiplier;
            }
            lastMultiplierStep = step;

            bool maxReached = false;
            if (!MaxMultiplierReached && CurrentMultiplier >= config.MaxMultiplier - 0.0001f)
            {
                MaxMultiplierReached = true;
                maxReached = true;
            }

            return new ComboResult(ComboCount, CurrentMultiplier, milestone, milestoneValue,
                upgrade, upgradedMultiplier, maxReached);
        }

        public void BreakCombo()
        {
            if (ComboCount <= 0) return;
            ComboCount = 0;
            CurrentMultiplier = config != null ? config.InitialMultiplier : 1f;
            MaxMultiplierReached = false;
            firedMilestones.Clear();
            lastMultiplierStep = ComputeStep(CurrentMultiplier);
            ComboBreak?.Invoke();
        }

        public void Freeze()
        {
            if (IsFrozen) return;
            IsFrozen = true;
            Frozen?.Invoke();
        }

        /// <summary>新目标 / 重新选择时调用：完全重置，并解除冻结。</summary>
        public void ResetCombo()
        {
            ComboCount = 0;
            CurrentMultiplier = config != null ? config.InitialMultiplier : 1f;
            IsFrozen = false;
            MaxMultiplierReached = false;
            firedMilestones.Clear();
            lastMultiplierStep = ComputeStep(CurrentMultiplier);
            ComboReset?.Invoke();
        }

        float ComputeMultiplier(int comboCount)
        {
            if (config == null) return 1f;
            int clamped = Mathf.Min(comboCount, config.MaxComboForMultiplier);
            float value = config.MultiplierCurve.Evaluate(clamped);
            return Mathf.Clamp(value, config.InitialMultiplier, config.MaxMultiplier);
        }

        int ComputeStep(float multiplier)
        {
            float step = config != null ? config.MultiplierMilestoneStep : 0.5f;
            return Mathf.FloorToInt(multiplier / step + 0.0001f);
        }

        bool IsMilestone(int comboCount)
        {
            if (config == null) return false;
            foreach (var milestone in config.ComboMilestones)
                if (milestone == comboCount) return true;
            return false;
        }
    }
}
