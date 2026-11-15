using System;
using UnityEngine;

namespace EarthOL.Combat
{
    [Serializable]
    public sealed class AttackTypeEntry
    {
        [Tooltip("攻击类型")]
        public AttackType attackType;

        [Tooltip("显示名称")]
        public string displayName;

        [Tooltip("基础伤害。小于 0 时使用攻击定义自带的基础伤害。")]
        public float baseDamage = -1f;

        [Range(0f, 1f), Tooltip("暴击概率")]
        public float criticalChance = 0.1f;

        [Min(1f), Tooltip("暴击倍率")]
        public float criticalMultiplier = 1.5f;
    }

    [Serializable]
    public sealed class AttackOverrideEntry
    {
        [Tooltip("对应 AttackDefinition 的 attackId")]
        public string attackId;

        [Tooltip("基础伤害覆盖。小于 0 时使用攻击类型配置。")]
        public float baseDamage = -1f;

        [Tooltip("暴击概率覆盖。小于 0 时使用攻击类型配置。")]
        public float criticalChance = -1f;

        [Tooltip("暴击倍率覆盖。小于 0 时使用攻击类型配置。")]
        public float criticalMultiplier = -1f;
    }

    /// <summary>
    /// 按攻击类型配置暴击概率/暴击倍率，并支持按攻击 ID 做物品级覆盖。
    /// 基础伤害默认沿用 AttackDefinition.damage（-1 表示不覆盖）。
    /// </summary>
    [CreateAssetMenu(menuName = "Earth OL/Combat/Attack Damage Config", fileName = "AttackDamageConfig")]
    public sealed class AttackDamageConfig : ScriptableObject
    {
        [SerializeField] AttackTypeEntry[] attackTypes = CreateDefaultTypes();
        [SerializeField] AttackOverrideEntry[] overrides = Array.Empty<AttackOverrideEntry>();

        public AttackTypeEntry[] AttackTypes =>
            attackTypes != null && attackTypes.Length > 0 ? attackTypes : CreateDefaultTypes();

        public AttackOverrideEntry[] Overrides => overrides ?? Array.Empty<AttackOverrideEntry>();

        void OnEnable()
        {
            if (attackTypes == null || attackTypes.Length == 0)
                attackTypes = CreateDefaultTypes();
            if (overrides == null)
                overrides = Array.Empty<AttackOverrideEntry>();
        }

        public static AttackTypeEntry[] CreateDefaultTypes()
        {
            return new[]
            {
                new AttackTypeEntry { attackType = AttackType.BarehandPress, displayName = "徒手按压" },
                new AttackTypeEntry { attackType = AttackType.BarehandSwipe, displayName = "徒手挥打" },
                new AttackTypeEntry { attackType = AttackType.ItemThrow, displayName = "物品投掷" },
                new AttackTypeEntry { attackType = AttackType.ItemHold, displayName = "手持物品" },
            };
        }

        /// <summary>
        /// 解析一次攻击使用的基础伤害、暴击概率与暴击倍率。
        /// 优先级：按 attackId 覆盖 &gt; 按攻击类型配置 &gt; 攻击定义自带伤害 / 默认 0.1 / 1.5。
        /// </summary>
        public void Resolve(AttackDefinition definition, out float baseDamage, out float criticalChance, out float criticalMultiplier)
        {
            baseDamage = definition != null ? definition.Damage : 0f;
            criticalChance = 0.1f;
            criticalMultiplier = 1.5f;

            var type = definition != null ? definition.AttackType : AttackType.ItemThrow;
            var entry = FindType(type);
            if (entry != null)
            {
                if (entry.baseDamage >= 0f) baseDamage = entry.baseDamage;
                criticalChance = entry.criticalChance;
                criticalMultiplier = entry.criticalMultiplier;
            }

            var overrideEntry = definition != null ? FindOverride(definition.AttackId) : null;
            if (overrideEntry != null)
            {
                if (overrideEntry.baseDamage >= 0f) baseDamage = overrideEntry.baseDamage;
                if (overrideEntry.criticalChance >= 0f) criticalChance = overrideEntry.criticalChance;
                if (overrideEntry.criticalMultiplier >= 0f) criticalMultiplier = overrideEntry.criticalMultiplier;
            }

            baseDamage = Mathf.Max(0f, baseDamage);
            criticalChance = Mathf.Clamp01(criticalChance);
            criticalMultiplier = Mathf.Max(1f, criticalMultiplier);
        }

        AttackTypeEntry FindType(AttackType type)
        {
            foreach (var entry in AttackTypes)
                if (entry != null && entry.attackType == type)
                    return entry;
            return null;
        }

        AttackOverrideEntry FindOverride(string attackId)
        {
            if (string.IsNullOrEmpty(attackId)) return null;
            foreach (var entry in Overrides)
                if (entry != null && entry.attackId == attackId)
                    return entry;
            return null;
        }
    }
}
