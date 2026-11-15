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
            var comboConfig = CreateComboConfig();
            var attackDamageConfig = CreateAttackDamageConfig();

            var config = AssetDatabase.LoadAssetAtPath<CombatConfig>(Folder + "/CombatConfig.asset");
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<CombatConfig>();
                AssetDatabase.CreateAsset(config, Folder + "/CombatConfig.asset");
            }
            config.healthSettings = settings;
            config.attacks = attacks;
            config.comboConfig = comboConfig;
            config.attackDamageConfig = attackDamageConfig;

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[Combat] 连击/暴击与血条配置已生成到 " + Folder +
                      "，可在 Inspector 中调整连击曲线、暴击概率与各攻击伤害。");
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
                CreateAttack("Press", "press", "按压", string.Empty, "下", 10f, AttackType.BarehandPress),
                CreateAttack("Swipe", "swipe", "挥打", string.Empty, "下", 8f, AttackType.BarehandSwipe),
                CreateAttack("StickyHandHold", "StickyHandHold", "手持", "粘粘手", "次", 20f, AttackType.ItemHold),
                CreateAttack("StickyHand", "StickyHand", "投掷", "粘粘手", "个", 15f, AttackType.ItemThrow),
                CreateAttack("Brain", "Brain", "投掷", "大脑", "个", 25f, AttackType.ItemThrow),
                CreateAttack("JellyCube", "JellyCube", "投掷", "方块", "个", 10f, AttackType.ItemThrow),
                CreateAttack("Arrow", "Arrow", "射出", "箭", "根", 20f, AttackType.ItemThrow)
            };
        }

        static AttackDefinition CreateAttack(string assetName, string id, string verb, string item, string unit, float damage, AttackType type)
        {
            var path = AttacksFolder + "/" + assetName + ".asset";
            var attack = AssetDatabase.LoadAssetAtPath<AttackDefinition>(path);
            if (attack == null)
            {
                attack = ScriptableObject.CreateInstance<AttackDefinition>();
                AssetDatabase.CreateAsset(attack, path);
            }
            attack.Configure(id, verb, item, unit, damage, type);
            EditorUtility.SetDirty(attack);
            return attack;
        }

        static ComboConfig CreateComboConfig()
        {
            var path = Folder + "/ComboConfig.asset";
            var config = AssetDatabase.LoadAssetAtPath<ComboConfig>(path);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<ComboConfig>();
                AssetDatabase.CreateAsset(config, path);
            }
            EditorUtility.SetDirty(config);
            return config;
        }

        static AttackDamageConfig CreateAttackDamageConfig()
        {
            var path = Folder + "/AttackDamageConfig.asset";
            var config = AssetDatabase.LoadAssetAtPath<AttackDamageConfig>(path);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<AttackDamageConfig>();
                AssetDatabase.CreateAsset(config, path);
            }
            EditorUtility.SetDirty(config);
            return config;
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
