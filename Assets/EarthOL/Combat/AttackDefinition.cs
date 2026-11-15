using UnityEngine;

namespace EarthOL.Combat
{
    /// <summary>
    /// 一种攻击行为的配置。未来新增攻击方式时，只需新增一个 AttackDefinition 资产，
    /// 并在攻击成功处调用 AttackDispatcher.Report(id)，无需修改血量系统核心逻辑。
    ///
    /// displayName 是动作动词（如 投掷 / 按压 / 射出）；
    /// itemName 是攻击物品名（如 大脑 / 箭，按压类可留空）；
    /// countUnit 是结算用计数单位（如 个 / 根 / 下）。
    /// </summary>
    [CreateAssetMenu(menuName = "Earth OL/Combat/Attack Definition", fileName = "AttackDefinition")]
    public sealed class AttackDefinition : ScriptableObject
    {
        [Header("唯一 ID（攻击模块上报时使用）")]
        [SerializeField] string attackId;

        [Header("攻击行为显示名称（动词）")]
        [SerializeField] string displayName;

        [Header("攻击物品名称（可留空）")]
        [SerializeField] string itemName;

        [Header("结算用计数单位")]
        [SerializeField] string countUnit = "个";

        [Header("基础伤害（连击/暴击在此基础上计算）")]
        [SerializeField, Min(0f)] float damage = 10f;

        [Header("攻击类型（连击/暴击按此分类；未设置时按攻击 ID 自动推断）")]
        [SerializeField] AttackType attackType = AttackType.Unspecified;

        public string AttackId => attackId;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? attackId : displayName;
        public string ItemName => string.IsNullOrEmpty(itemName) ? string.Empty : itemName;
        public string CountUnit => string.IsNullOrEmpty(countUnit) ? "个" : countUnit;
        public float Damage => Mathf.Max(0f, damage);

        public AttackType AttackType =>
            attackType == AttackType.Unspecified ? InferAttackType(attackId) : attackType;

        public void Configure(string id, string verb, string item, string unit, float damageValue)
        {
            Configure(id, verb, item, unit, damageValue, InferAttackType(id));
        }

        public void Configure(string id, string verb, string item, string unit, float damageValue, AttackType type)
        {
            attackId = id;
            displayName = verb;
            itemName = item;
            countUnit = unit;
            damage = Mathf.Max(0f, damageValue);
            attackType = type;
        }

        public static AttackType InferAttackType(string attackId)
        {
            if (string.IsNullOrEmpty(attackId)) return AttackType.ItemThrow;
            var id = attackId.Trim();

            if (id == "press") return AttackType.BarehandPress;
            if (id == "swipe") return AttackType.BarehandSwipe;
            if (id.EndsWith("Hold", System.StringComparison.OrdinalIgnoreCase)) return AttackType.ItemHold;
            return AttackType.ItemThrow;
        }
    }
}
