using UnityEngine;

namespace EarthOL.Combat
{
    [CreateAssetMenu(menuName = "Earth OL/Combat/Combat Config")]
    public sealed class CombatConfig : ScriptableObject
    {
        public ComboConfig comboConfig;
        public AttackDamageConfig attackDamageConfig;
        public AttackDefinition[] attacks;
        public float[] healthTiers = { 60f, 120f, 240f };
    }
}
