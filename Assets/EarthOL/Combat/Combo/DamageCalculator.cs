using UnityEngine;

namespace EarthOL.Combat
{
    /// <summary>最终伤害统一入口：D = B × Mc × Mk。</summary>
    public static class DamageCalculator
    {
        public static float Compute(float baseDamage, float comboMultiplier, float criticalMultiplier)
        {
            float b = Mathf.Max(0f, baseDamage);
            float mc = Mathf.Max(1f, comboMultiplier);
            float mk = Mathf.Max(1f, criticalMultiplier);
            return b * mc * mk;
        }
    }
}
