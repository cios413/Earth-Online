namespace EarthOL
{
    /// <summary>项目的两种可选功能。</summary>
    public enum GameMode
    {
        /// <summary>2D 卡通跟踪与人物周围画面形变。</summary>
        Deform = 0,

        /// <summary>点击跟踪人物，生成一个固定的 3D Capsule 人体快照。</summary>
        Throw = 1
    }
}
