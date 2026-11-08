namespace EarthOL.Combat
{
    /// <summary>
    /// 一次成功攻击的完整描述，由 AttackDispatcher 解析攻击 ID 后发出。
    /// </summary>
    public readonly struct AttackReport
    {
        public readonly AttackDefinition Definition;

        public AttackReport(AttackDefinition definition)
        {
            Definition = definition;
        }
    }
}
