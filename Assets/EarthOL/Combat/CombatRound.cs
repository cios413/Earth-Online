using System.Collections.Generic;
using EarthOL.BodyThrow;
using UnityEngine;

namespace EarthOL.Combat
{
    public readonly struct AttackOutcome
    {
        public readonly int PersonId, RoundToken, ComboCount;
        public readonly AttackDefinition Definition;
        public readonly Vector2 ViewportPoint;
        public readonly bool Critical, Milestone;
        public readonly float Timestamp, ComboMultiplier, FinalDamage, AppliedDamage;
        public AttackOutcome(int personId, int token, AttackDefinition definition, Vector2 point,
            float timestamp, int combo, float multiplier, bool critical, bool milestone, float damage, float applied)
        {
            PersonId = personId; RoundToken = token; Definition = definition; ViewportPoint = point;
            Timestamp = timestamp; ComboCount = combo; ComboMultiplier = multiplier; Critical = critical;
            Milestone = milestone; FinalDamage = damage; AppliedDamage = applied;
        }
    }

    /// <summary>wang health/combo/history rules scoped to one tracked person and one round.</summary>
    public sealed class CombatRound
    {
        readonly HashSet<long> m_Processed = new HashSet<long>();
        readonly List<AttackOutcome> m_History = new List<AttackOutcome>();
        public int PersonId { get; }
        public int Token { get; }
        public float MaxHealth { get; }
        public float CurrentHealth { get; private set; }
        public bool Defeated => CurrentHealth <= 0f;
        public int ComboCount { get; private set; }
        public int BestCombo { get; private set; }
        public int CriticalCount { get; private set; }
        public float ComboMultiplier { get; private set; } = 1f;
        public float LastHitTime { get; private set; } = float.NegativeInfinity;
        public float TotalDamage { get; private set; }
        public IReadOnlyList<AttackOutcome> History => m_History;

        public CombatRound(int personId, int token, float maxHealth)
        { PersonId = personId; Token = token; MaxHealth = CurrentHealth = Mathf.Max(1f, maxHealth); }

        public void BreakCombo()
        { ComboCount = 0; ComboMultiplier = 1f; LastHitTime = float.NegativeInfinity; }

        public void Tick(float timestamp, ComboConfig config)
        {
            if (!Defeated && ComboCount > 0 && timestamp - LastHitTime > config.ComboDuration) BreakCombo();
        }

        public bool TryApply(PersonInteractionHit hit, AttackDefinition definition, ComboConfig combo,
            AttackDamageConfig damageConfig, float timestamp, float criticalRoll, out AttackOutcome outcome)
        {
            outcome = default;
            if (hit.personId != PersonId || hit.context != Token || definition == null || Defeated ||
                hit.eventId <= 0 || m_Processed.Contains(hit.eventId)) return false;
            damageConfig.Resolve(definition, out float baseDamage, out float chance, out float criticalMultiplier);
            if (baseDamage <= 0f) return false;
            m_Processed.Add(hit.eventId);
            Tick(timestamp, combo);
            ComboCount++;
            LastHitTime = timestamp;
            BestCombo = Mathf.Max(BestCombo, ComboCount);
            ComboMultiplier = Mathf.Clamp(combo.MultiplierCurve.Evaluate(Mathf.Min(ComboCount, combo.MaxComboForMultiplier)),
                combo.InitialMultiplier, combo.MaxMultiplier);
            bool critical = criticalRoll < chance;
            if (critical) CriticalCount++;
            float damage = DamageCalculator.Compute(baseDamage, ComboMultiplier, critical ? criticalMultiplier : 1f);
            float applied = Mathf.Min(CurrentHealth, damage);
            CurrentHealth = Mathf.Max(0f, CurrentHealth - damage);
            TotalDamage += applied;
            bool milestone = false;
            if (combo.ComboMilestones != null)
                foreach (int value in combo.ComboMilestones) if (ComboCount == value) { milestone = true; break; }
            outcome = new AttackOutcome(PersonId, Token, definition, hit.viewportPoint, timestamp,
                ComboCount, ComboMultiplier, critical, milestone, damage, applied);
            m_History.Add(outcome);
            return true;
        }
    }
}
