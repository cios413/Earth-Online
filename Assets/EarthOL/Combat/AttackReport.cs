using UnityEngine;

namespace EarthOL.Combat
{
    /// <summary>
    /// 一次成功攻击的完整描述，由 AttackDispatcher 解析攻击 ID 后发出。
    /// </summary>
    public readonly struct AttackReport
    {
        public readonly AttackDefinition Definition;
        public readonly float Timestamp;
        public readonly string TargetId;

        public AttackReport(AttackDefinition definition)
            : this(definition, Time.unscaledTime, null)
        {
        }

        public AttackReport(AttackDefinition definition, float timestamp, string targetId)
        {
            Definition = definition;
            Timestamp = timestamp;
            TargetId = targetId;
        }
    }
}
