namespace EarthOL
{
    /// <summary>左上角四个可切换的人体交互功能。</summary>
    public enum GameMode
    {
        /// <summary>对识别到的人物画面区域进行形变。</summary>
        Deform = 0,

        /// <summary>点击识别到的人物，在其身上激活 2D 卡通贴图。</summary>
        Sticker = 1,

        /// <summary>长按人物生成照片挤出模型，并进入可旋转展示界面。</summary>
        Showcase = 2,

        /// <summary>从屏幕空间投掷物品，并将其附着在跟踪人物上。</summary>
        Throw = 3
    }
}
