using UnityEngine;

namespace EarthOL.Combat
{
    /// <summary>供现有输入路由（ARCoreModeOverlay）引用的静态入口。</summary>
    public static class CombatOverlay
    {
        /// <summary>设置面板或结算对话框打开时，阻断世界输入。</summary>
        public static bool BlocksWorldInput => ResultUI.IsShowing || HealthSettingsOverlay.IsPanelOpen;

        /// <summary>点是否落在血条按钮 / 设置面板 / 结算对话框上。</summary>
        public static bool IsPointOnOverlay(Vector2 screenPosition)
        {
            if (ResultUI.IsPointOnOverlay(screenPosition)) return true;
            return HealthSettingsOverlay.IsPointOnOverlay(screenPosition);
        }
    }
}
