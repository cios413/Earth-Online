using UnityEngine;

namespace EarthOL.BodyThrow
{
    /// <summary>Finite impact/dissolve motion. Once settled, neither the model nor its slime keeps drifting.</summary>
    public static class ProjectileSlimeMotion
    {
        public const float SettleSeconds = 2.2f;

        public static Vector3 ModelScale(float age)
        {
            float dissolve = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((age - .22f) / 1.7f));
            float spring = age < .7f ? Mathf.Exp(-age * 7f) * Mathf.Cos(age * 22f) : 0f;
            float lateral = Mathf.Lerp(1f, 1.65f, dissolve) * (1f + .14f * spring);
            float depth = Mathf.Lerp(1f, .055f, dissolve) * (1f - .32f * spring);
            return new Vector3(lateral, lateral, depth);
        }

        public static float Rebound(float age) => age < .65f ?
            Mathf.Sin(Mathf.Clamp01(age / .65f) * Mathf.PI) * Mathf.Exp(-age * 4f) * .065f : 0f;

        public static float Spread(float age) => Mathf.Lerp(.18f, 1f,
            Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / .65f)));

        public static float Flow(float age) => Mathf.SmoothStep(0f, 1f,
            Mathf.Clamp01((age - .18f) / (SettleSeconds - .18f)));
    }
}
