using UnityEngine;

namespace EarthOL.Combat
{
    // Adapted from wang 53a907b: configuration and attack ID inference retained.
    public enum AttackType { Unspecified, BarehandPress, BarehandSwipe, ItemThrow, ItemHold }

    [CreateAssetMenu(menuName = "Earth OL/Combat/Attack Definition")]
    public sealed class AttackDefinition : ScriptableObject
    {
        [SerializeField] string attackId;
        [SerializeField] string displayName;
        [SerializeField] string itemName;
        [SerializeField] string countUnit = "次";
        [SerializeField, Min(0f)] float damage = 10f;
        [SerializeField] AttackType attackType;
        public string AttackId => attackId;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? attackId : displayName;
        public string ItemName => itemName ?? string.Empty;
        public string CountUnit => countUnit;
        public float Damage => Mathf.Max(0f, damage);
        public AttackType AttackType => attackType == AttackType.Unspecified ? InferAttackType(attackId) : attackType;

        public void Configure(string id, string verb, string item, string unit, float value, AttackType type)
        { attackId = id; displayName = verb; itemName = item; countUnit = unit; damage = Mathf.Max(0f, value); attackType = type; }

        public static AttackType InferAttackType(string id)
        {
            if (id == "press") return AttackType.BarehandPress;
            if (id == "swipe") return AttackType.BarehandSwipe;
            return !string.IsNullOrEmpty(id) && id.EndsWith("Hold", System.StringComparison.OrdinalIgnoreCase)
                ? AttackType.ItemHold : AttackType.ItemThrow;
        }
    }
}
