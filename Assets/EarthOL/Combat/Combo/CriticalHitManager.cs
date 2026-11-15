using UnityEngine;

namespace EarthOL.Combat
{
    /// <summary>
    /// 独立暴击判定：每次成功攻击独立掷骰，无冷却、不因连击数自动提概率。
    /// </summary>
    public sealed class CriticalHitManager
    {
        AttackDamageConfig config;

        public void Configure(AttackDamageConfig damageConfig)
        {
            config = damageConfig;
        }

        public bool Evaluate(AttackDefinition definition, out float multiplier)
        {
            multiplier = 1f;
            if (definition == null) return false;

            float chance = 0.1f;
            float criticalMultiplier = 1.5f;
            if (config != null)
                config.Resolve(definition, out _, out chance, out criticalMultiplier);

            chance = Mathf.Clamp01(chance);
            if (Random.value >= chance) return false;

            multiplier = Mathf.Max(1f, criticalMultiplier);
            return true;
        }
    }
}
