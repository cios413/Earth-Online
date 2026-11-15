using UnityEngine;
using EarthOL.Combat;

namespace EarthOL.ARCore
{
    /// <summary>
    /// 模式路由的薄封装：背包开合（徒手/物品使用）由 CombatInputBootstrap
    /// 直接调用 bootstrap.SetMode，本类仅保留输入阻断查询供兼容使用。
    /// 原「形变/投掷模式」按钮与长按轮盘已由背包系统替代。
    /// </summary>
    public sealed class ARCoreModeOverlay : MonoBehaviour
    {
        public static ARCoreModeOverlay Instance { get; private set; }

        ARCoreMainSceneBootstrap m_Bootstrap;

        public static bool BlocksWorldInput => CombatOverlay.BlocksWorldInput;

        public static bool IsPointOnOverlay(Vector2 screenPosition) =>
            CombatOverlay.IsPointOnOverlay(screenPosition);

        public void Configure(ARCoreMainSceneBootstrap bootstrap)
        {
            Instance = this;
            m_Bootstrap = bootstrap;
            Apply(GameMode.Deform);
        }

        public void Apply(GameMode mode)
        {
            if (m_Bootstrap != null) m_Bootstrap.SetMode(mode);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
