namespace EarthOL.BodyThrow
{
    /// <summary>
    /// 战斗系统与识别模块之间的接口：识别模块（如 Li 的 PersonTrackingManager）
    /// 实现该接口，战斗系统只依赖它读取当前目标，不依赖具体识别实现。
    /// </summary>
    public interface ICombatTargetProvider
    {
        PersonPoseData GetCurrentTarget();
    }

    /// <summary>运行时注入的当前目标提供者（由识别模块注册）。</summary>
    public static class CombatTarget
    {
        public static ICombatTargetProvider Provider;
    }
}
