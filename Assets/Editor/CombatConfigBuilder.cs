using EarthOL.Combat;
using UnityEditor;
using UnityEngine;

namespace EarthOL.EditorTools
{
    /// <summary>
    /// 生成可编辑的血条配置资产到 Assets/Resources/Combat。
    /// 运行 Tools/Earth OL/Create Combat Config 后，即可在 Inspector 中调整
    /// 血量档位数值与各攻击行为的伤害、名称、计数单位。
    /// </summary>
    public static class CombatConfigBuilder
    {
        const string Folder = "Assets/Resources/Combat";
        const string AttacksFolder = Folder + "/Attacks";

        [MenuItem("Tools/Earth OL/Create Combat Config")]
        public static void Build()
        {
            EnsureFolder(Folder);
            EnsureFolder(AttacksFolder);

            var settings = CreateHealthSettings();
            var attacks = CreateAttackDefinitions();

            var config = AssetDatabase.LoadAssetAtPath<CombatConfig>(Folder + "/CombatConfig.asset");
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<CombatConfig>();
                AssetDatabase.CreateAsset(config, Folder + "/CombatConfig.asset");
            }
            config.healthSettings = settings;
            config.attacks = attacks;

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[Combat] 血条配置已生成到 " + Folder +
                      "，可在 Inspector 中调整血量档位与各攻击伤害。");
        }

        static HealthSettings CreateHealthSettings()
        {
            var path = Folder + "/HealthSettings.asset";
            var settings = AssetDatabase.LoadAssetAtPath<HealthSettings>(path);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<HealthSettings>();
                AssetDatabase.CreateAsset(settings, path);
            }
            EditorUtility.SetDirty(settings);
            return settings;
        }

        static AttackDefinition[] CreateAttackDefinitions()
        {
            return new[]
            {
                CreateAttack("Press", "press", "按压", string.Empty, "下", 10f),
                CreateAttack("StickyHand", "StickyHand", "投掷", "粘粘手", "个", 15f),
                CreateAttack("Brain", "Brain", "投掷", "大脑", "个", 25f),
                CreateAttack("JellyCube", "JellyCube", "投掷", "方块", "个", 10f),
                CreateAttack("Arrow", "Arrow", "射出", "箭", "根", 20f)
            };
        }

        static AttackDefinition CreateAttack(string assetName, string id, string verb, string item, string unit, float damage)
        {
            var path = AttacksFolder + "/" + assetName + ".asset";
            var attack = AssetDatabase.LoadAssetAtPath<AttackDefinition>(path);
            if (attack == null)
            {
                attack = ScriptableObject.CreateInstance<AttackDefinition>();
                AssetDatabase.CreateAsset(attack, path);
            }
            attack.Configure(id, verb, item, unit, damage);
            EditorUtility.SetDirty(attack);
            return attack;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = path.Substring(0, path.LastIndexOf('/'));
            var leaf = path.Substring(path.LastIndexOf('/') + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
