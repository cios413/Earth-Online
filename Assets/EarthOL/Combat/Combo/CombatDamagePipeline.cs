using System;
using UnityEngine;

namespace EarthOL.Combat
{
    /// <summary>一次成功攻击处理完成后的完整结果，供 HUD 与反馈层读取。</summary>
    public readonly struct AttackOutcome
    {
        public readonly AttackDefinition Definition;
        public readonly AttackType AttackType;
        public readonly float Timestamp;
        public readonly int ComboCount;
        public readonly float ComboMultiplier;
        public readonly bool Critical;
        public readonly float CriticalMultiplier;
        public readonly float BaseDamage;
        public readonly float FinalDamage;
        public readonly bool Milestone;
        public readonly int MilestoneValue;
        public readonly bool MultiplierUpgrade;
        public readonly bool MaxReached;
        public readonly bool HealthActive;

        public AttackOutcome(AttackDefinition definition, AttackType attackType, float timestamp,
            int comboCount, float comboMultiplier, bool critical, float criticalMultiplier,
            float baseDamage, float finalDamage, bool milestone, int milestoneValue,
            bool multiplierUpgrade, bool maxReached, bool healthActive)
        {
            Definition = definition;
            AttackType = attackType;
            Timestamp = timestamp;
            ComboCount = comboCount;
            ComboMultiplier = comboMultiplier;
            Critical = critical;
            CriticalMultiplier = criticalMultiplier;
            BaseDamage = baseDamage;
            FinalDamage = finalDamage;
            Milestone = milestone;
            MilestoneValue = milestoneValue;
            MultiplierUpgrade = multiplierUpgrade;
            MaxReached = maxReached;
            HealthActive = healthActive;
        }
    }

    /// <summary>
    /// 连击/暴击的统一处理管道：攻击成功事件唯一进入点，
    /// 串起连击 → 暴击 → 伤害计算 → 血量系统 → 反馈事件。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatDamagePipeline : MonoBehaviour
    {
        HealthSystem healthSystem;
        ComboManager comboManager;
        CriticalHitManager criticalHitManager;
        AttackDamageConfig damageConfig;

        bool subscribed;

        public event Action<AttackOutcome> AttackResolved;

        public void Configure(HealthSystem health, ComboManager combo, CriticalHitManager critical, AttackDamageConfig config)
        {
            healthSystem = health;
            comboManager = combo;
            criticalHitManager = critical;
            damageConfig = config;
        }

        void OnEnable() => Subscribe();
        void OnDisable() => Unsubscribe();

        void Subscribe()
        {
            if (subscribed || AttackDispatcher.Instance == null) return;
            AttackDispatcher.Instance.AttackPerformed += OnAttackPerformed;
            subscribed = true;
        }

        void Unsubscribe()
        {
            if (!subscribed || AttackDispatcher.Instance == null) return;
            AttackDispatcher.Instance.AttackPerformed -= OnAttackPerformed;
            subscribed = false;
        }

        void OnAttackPerformed(AttackReport report)
        {
            if (report.Definition == null || healthSystem == null || comboManager == null) return;
            if (comboManager.IsFrozen) return;

            var combo = comboManager.RegisterHit();

            bool healthActive = healthSystem.IsActive;
            bool critical = false;
            float criticalMultiplier = 1f;
            if (healthActive && criticalHitManager != null)
                critical = criticalHitManager.Evaluate(report.Definition, out criticalMultiplier);

            float baseDamage = report.Definition.Damage;
            if (damageConfig != null)
                damageConfig.Resolve(report.Definition, out baseDamage, out _, out _);

            float finalDamage = DamageCalculator.Compute(baseDamage, combo.Multiplier, criticalMultiplier);

            healthSystem.ApplyDamage(finalDamage);

            var outcome = new AttackOutcome(
                report.Definition,
                report.Definition.AttackType,
                report.Timestamp,
                combo.Count,
                combo.Multiplier,
                critical,
                criticalMultiplier,
                baseDamage,
                finalDamage,
                combo.Milestone,
                combo.MilestoneValue,
                combo.MultiplierUpgrade,
                combo.MaxReached,
                healthActive);

            AttackResolved?.Invoke(outcome);
        }
    }
}
