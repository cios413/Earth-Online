namespace EarthOL
{
    /// <summary>项目的两种可选功能。</summary>
    public enum GameMode
    {
        /// <summary>形变模式：识别图卡片本体被拉伸/鼓起（Vuforia + PokeWarp）。</summary>
        Deform = 0,

        /// <summary>投掷模式：点击目标发射投掷物，命中后贴附在表面（移植自 Earth-Online 仓库）。</summary>
        Throw = 1
    }
}
