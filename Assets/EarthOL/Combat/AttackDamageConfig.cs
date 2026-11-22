using System;
using UnityEngine;

namespace EarthOL.Combat
{
    // wang 53a907b: per-attack override > attack type > definition.
    [Serializable]
    public sealed class AttackTypeEntry
    {
        public AttackType attackType;
        public float baseDamage = -1f;
        [Range(0f, 1f)] public float criticalChance = .1f;
        [Min(1f)] public float criticalMultiplier = 1.5f;
    }

    [Serializable]
    public sealed class AttackOverrideEntry
    {
        public string attackId;
        public float baseDamage = -1f;
        public float criticalChance = -1f;
        public float criticalMultiplier = -1f;
    }

    [CreateAssetMenu(menuName = "Earth OL/Combat/Attack Damage Config")]
    public sealed class AttackDamageConfig : ScriptableObject
    {
        [SerializeField] AttackTypeEntry[] attackTypes = {
            new AttackTypeEntry { attackType = AttackType.BarehandPress },
            new AttackTypeEntry { attackType = AttackType.BarehandSwipe },
            new AttackTypeEntry { attackType = AttackType.ItemThrow },
            new AttackTypeEntry { attackType = AttackType.ItemHold }
        };
        [SerializeField] AttackOverrideEntry[] overrides = Array.Empty<AttackOverrideEntry>();
        public AttackTypeEntry[] AttackTypes => attackTypes;
        public void Resolve(AttackDefinition definition, out float damage, out float chance, out float multiplier)
        {
            damage = definition != null ? definition.Damage : 0f; chance = .1f; multiplier = 1.5f;
            if (attackTypes != null)
                foreach (var entry in attackTypes)
                    if (entry != null && definition != null && entry.attackType == definition.AttackType)
                    {
                        if (entry.baseDamage >= 0f) damage = entry.baseDamage;
                        chance = entry.criticalChance; multiplier = entry.criticalMultiplier; break;
                    }
            if (overrides != null)
                foreach (var entry in overrides)
                    if (entry != null && definition != null && entry.attackId == definition.AttackId)
                    {
                        if (entry.baseDamage >= 0f) damage = entry.baseDamage;
                        if (entry.criticalChance >= 0f) chance = entry.criticalChance;
                        if (entry.criticalMultiplier >= 0f) multiplier = entry.criticalMultiplier;
                        break;
                    }
            damage = Mathf.Max(0f, damage); chance = Mathf.Clamp01(chance); multiplier = Mathf.Max(1f, multiplier);
        }
    }
}
