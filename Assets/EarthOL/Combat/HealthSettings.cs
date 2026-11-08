using UnityEngine;

namespace EarthOL.Combat
{
    public enum HealthTier
    {
        Low = 0,
        Medium = 1,
        High = 2
    }

    /// <summary>
    /// 血条系统总开关与血量档位配置。数值都是占位默认值，可在 Inspector 中随时调整。
    /// 修改档位不会影响当前已选中物体的剩余血量，只对下一次选择的物体生效。
    /// </summary>
    [CreateAssetMenu(menuName = "Earth OL/Combat/Health Settings", fileName = "HealthSettings")]
    public sealed class HealthSettings : ScriptableObject
    {
        [Header("总开关")]
        [SerializeField] bool healthBarModeEnabled = true;

        [Header("血量档位")]
        [SerializeField] HealthTier tier = HealthTier.Medium;

        [Header("各档位最大血量（占位默认值，可随时调整平衡）")]
        [SerializeField, Min(1f)] float lowMaxHealth = 60f;
        [SerializeField, Min(1f)] float mediumMaxHealth = 120f;
        [SerializeField, Min(1f)] float highMaxHealth = 240f;

        public bool HealthBarModeEnabled
        {
            get => healthBarModeEnabled;
            set => healthBarModeEnabled = value;
        }

        public HealthTier Tier
        {
            get => tier;
            set => tier = value;
        }

        public float CurrentMaxHealth => GetMaxHealth(tier);

        public float GetMaxHealth(HealthTier value)
        {
            switch (value)
            {
                case HealthTier.Low: return Mathf.Max(1f, lowMaxHealth);
                case HealthTier.High: return Mathf.Max(1f, highMaxHealth);
                default: return Mathf.Max(1f, mediumMaxHealth);
            }
        }

        public void SetMaxHealth(HealthTier value, float maxHealth)
        {
            switch (value)
            {
                case HealthTier.Low: lowMaxHealth = Mathf.Max(1f, maxHealth); break;
                case HealthTier.Medium: mediumMaxHealth = Mathf.Max(1f, maxHealth); break;
                case HealthTier.High: highMaxHealth = Mathf.Max(1f, maxHealth); break;
            }
        }
    }
}
