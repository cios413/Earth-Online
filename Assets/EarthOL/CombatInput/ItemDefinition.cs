using UnityEngine;

namespace EarthOL.CombatInput
{
    /// <summary>
    /// 物品的独立配置：是否可投掷、是否可手持，以及对应的攻击 ID。
    /// 投掷和手持是两种独立使用方式，不是互斥的类型划分。
    /// </summary>
    [CreateAssetMenu(menuName = "Earth OL/Combat/Item Definition", fileName = "ItemDefinition")]
    public sealed class ItemDefinition : ScriptableObject
    {
        [SerializeField] string key;
        [SerializeField] string displayName;
        [SerializeField] Texture2D preview;
        [SerializeField] bool canThrow = true;
        [SerializeField] bool canHold;
        [SerializeField] string throwAttackId;
        [SerializeField] string holdAttackId;

        public string Key => key;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? key : displayName;
        public Texture2D Preview => preview;
        public bool CanThrow => canThrow;
        public bool CanHold => canHold;
        public string ThrowAttackId => string.IsNullOrEmpty(throwAttackId) ? key : throwAttackId;
        public string HoldAttackId => string.IsNullOrEmpty(holdAttackId) ? key + "Hold" : holdAttackId;

        public void Configure(string keyValue, string name, Texture2D previewTexture, bool throwable, bool holdable)
        {
            key = keyValue;
            displayName = name;
            preview = previewTexture;
            canThrow = throwable;
            canHold = holdable;
        }
    }
}
