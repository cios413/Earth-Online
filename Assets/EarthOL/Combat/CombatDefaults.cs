using System.Collections.Generic;
using EarthOL.BodyThrow;
using UnityEngine;

namespace EarthOL.Combat
{
    /// <summary>
    /// 未配置资产时的内置默认攻击定义。数值都是占位默认值，
    /// 建议用编辑器菜单 Tools/Earth OL/Create Combat Config 生成资产后按需调整。
    /// </summary>
    public static class CombatDefaults
    {
        public static HealthSettings CreateDefaultHealthSettings()
        {
            var settings = ScriptableObject.CreateInstance<HealthSettings>();
            settings.name = "HealthSettings (defaults)";
            return settings;
        }

        /// <summary>
        /// 生成默认攻击定义。传入投掷物目录时按目录条目生成，保证攻击 ID 与目录 key 一致；
        /// 目录为 null 时生成按压 + 四种已知投掷物。
        /// </summary>
        public static AttackDefinition[] BuildDefaultAttacks(BodyProjectileCatalog catalog)
        {
            var list = new List<AttackDefinition>
            {
                CreateAttack("press", "按压", string.Empty, "下", 10f)
            };

            if (catalog != null && catalog.entries != null && catalog.entries.Length > 0)
            {
                foreach (var entry in catalog.entries)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.key)) continue;
                    ApplyThrowDefaults(entry.key, entry.displayName, out var verb, out var item, out var unit, out var damage);
                    list.Add(CreateAttack(entry.key, verb, item, unit, damage));
                }
            }
            else
            {
                list.Add(CreateAttack("StickyHand", "投掷", "粘粘手", "个", 15f));
                list.Add(CreateAttack("Brain", "投掷", "大脑", "个", 25f));
                list.Add(CreateAttack("JellyCube", "投掷", "方块", "个", 10f));
                list.Add(CreateAttack("Arrow", "射出", "箭", "根", 20f));
            }

            return list.ToArray();
        }

        static AttackDefinition CreateAttack(string id, string verb, string item, string unit, float damage)
        {
            var definition = ScriptableObject.CreateInstance<AttackDefinition>();
            definition.name = "Attack_" + id;
            definition.Configure(id, verb, item, unit, damage);
            return definition;
        }

        static void ApplyThrowDefaults(string key, string displayName, out string verb, out string item, out string unit, out float damage)
        {
            switch (key)
            {
                case "StickyHand": verb = "投掷"; item = "粘粘手"; unit = "个"; damage = 15f; break;
                case "Brain": verb = "投掷"; item = "大脑"; unit = "个"; damage = 25f; break;
                case "JellyCube": verb = "投掷"; item = "方块"; unit = "个"; damage = 10f; break;
                case "Arrow": verb = "射出"; item = "箭"; unit = "根"; damage = 20f; break;
                default:
                    verb = "投掷";
                    item = string.IsNullOrEmpty(displayName) ? key : displayName;
                    unit = "个";
                    damage = 10f;
                    break;
            }
        }
    }
}
