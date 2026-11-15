namespace EarthOL.Combat
{
    /// <summary>
    /// 攻击方式分类，连击/暴击按此分类读取配置。
    /// Unspecified 表示旧资产未显式设置，运行时按攻击 ID 自动推断。
    /// </summary>
    public enum AttackType
    {
        Unspecified = 0,
        BarehandPress = 1,
        BarehandSwipe = 2,
        ItemThrow = 3,
        ItemHold = 4,
    }
}
