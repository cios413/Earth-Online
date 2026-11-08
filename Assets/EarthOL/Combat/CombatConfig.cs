using UnityEngine;

namespace EarthOL.Combat
{
    /// <summary>
    /// 血条系统的总配置文件：一份血量设置 + 一组攻击定义。
    /// 由编辑器菜单 Tools/Earth OL/Create Combat Config 生成到 Resources/Combat 下，
    /// 运行时会自动加载；未找到时使用内置默认值，保证开箱即用。
    /// </summary>
    [CreateAssetMenu(menuName = "Earth OL/Combat/Combat Config", fileName = "CombatConfig")]
    public sealed class CombatConfig : ScriptableObject
    {
        public HealthSettings healthSettings;
        public AttackDefinition[] attacks;
    }
}
