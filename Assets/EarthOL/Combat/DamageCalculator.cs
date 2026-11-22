using UnityEngine;

namespace EarthOL.Combat
{
    // Original wang damage formula.
    public static class DamageCalculator
    {
        public static float Compute(float baseDamage, float comboMultiplier, float criticalMultiplier)
            => Mathf.Max(0f, baseDamage) * Mathf.Max(1f, comboMultiplier) * Mathf.Max(1f, criticalMultiplier);
    }
}
