using EarthOL.Combat;
using UnityEditor;
using UnityEngine;

namespace EarthOL.EditorTools
{
    /// <summary>
    /// 连击/暴击系统的无播放模式自检：验证倍率曲线、里程碑、冻结/重置、
    /// 伤害公式与攻击类型推断。通过菜单 Tools/Earth OL/Validate Combo System 运行。
    /// </summary>
    public static class ComboSystemValidation
    {
        [MenuItem("Tools/Earth OL/Validate Combo System")]
        public static void Run()
        {
            int passed = 0;
            int failed = 0;

            void Check(string name, bool ok)
            {
                if (ok)
                {
                    passed++;
                    Debug.Log("[ComboValidation] PASS: " + name);
                }
                else
                {
                    failed++;
                    Debug.LogError("[ComboValidation] FAIL: " + name);
                }
            }

            var comboConfig = ScriptableObject.CreateInstance<ComboConfig>();
            var damageConfig = ScriptableObject.CreateInstance<AttackDamageConfig>();
            var go = new GameObject("ComboSystemValidation");

            try
            {
                var manager = go.AddComponent<ComboManager>();
                manager.Configure(comboConfig);

                int breakCount = 0;
                int freezeCount = 0;
                int resetCount = 0;
                int milestone5Count = 0;
                manager.ComboBreak += () => breakCount++;
                manager.Frozen += () => freezeCount++;
                manager.ComboReset += () => resetCount++;

                var expected = new (int combo, float multiplier)[]
                {
                    (1, 1.00f), (2, 1.25f), (3, 1.48f), (5, 1.85f), (8, 2.25f),
                    (10, 2.45f), (12, 2.62f), (15, 2.80f), (18, 2.94f), (20, 3.00f)
                };

                int next = 0;
                for (int i = 1; i <= 20; i++)
                {
                    var result = manager.RegisterHit();
                    if (next < expected.Length && expected[next].combo == i)
                    {
                        Check("连击 " + i + " 倍率 " + expected[next].multiplier.ToString("0.00"),
                            result.Count == i && Nearly(result.Multiplier, expected[next].multiplier));
                        next++;
                    }
                    if (i == 5 && result.Milestone) milestone5Count++;
                }

                Check("里程碑 5 只触发一次", milestone5Count == 1);
                Check("连击 20 达到最大倍率", manager.MaxMultiplierReached && Nearly(manager.CurrentMultiplier, 3f));

                var after20 = manager.RegisterHit();
                Check("连击超过 20 继续增长", after20.Count == 21);
                Check("连击超过 20 倍率封顶 ×3", Nearly(after20.Multiplier, 3f) && !after20.MaxReached);

                manager.BreakCombo();
                Check("COMBO BREAK 清零连击", manager.ComboCount == 0);
                Check("COMBO BREAK 只触发一次", breakCount == 1);
                Check("COMBO BREAK 重置倍率", Nearly(manager.CurrentMultiplier, 1f));

                manager.RegisterHit();
                manager.RegisterHit();
                manager.Freeze();
                Check("击败后冻结", manager.IsFrozen);
                var frozenCount = manager.ComboCount;
                var frozenResult = manager.RegisterHit();
                Check("冻结后不再增长", frozenResult.Count == frozenCount && manager.ComboCount == frozenCount);
                Check("冻结事件只触发一次", freezeCount == 1);

                manager.ResetCombo();
                Check("重新选择解除冻结并清零", !manager.IsFrozen && manager.ComboCount == 0);
                Check("重置事件触发", resetCount == 1);

                Check("伤害公式 无暴击 D=20", Nearly(DamageCalculator.Compute(10f, 2f, 1f), 20f));
                Check("伤害公式 暴击 D=30", Nearly(DamageCalculator.Compute(10f, 2f, 1.5f), 30f));

                Check("攻击类型推断 press", AttackDefinition.InferAttackType("press") == AttackType.BarehandPress);
                Check("攻击类型推断 swipe", AttackDefinition.InferAttackType("swipe") == AttackType.BarehandSwipe);
                Check("攻击类型推断 xxxHold", AttackDefinition.InferAttackType("StickyHandHold") == AttackType.ItemHold);
                Check("攻击类型推断 投掷", AttackDefinition.InferAttackType("Brain") == AttackType.ItemThrow);

                var definition = ScriptableObject.CreateInstance<AttackDefinition>();
                definition.Configure("press", "按压", string.Empty, "下", 10f, AttackType.BarehandPress);
                damageConfig.Resolve(definition, out var baseDamage, out var criticalChance, out var criticalMultiplier);
                Check("基础伤害沿用攻击定义", Nearly(baseDamage, 10f));
                Check("默认暴击概率 0.1", Nearly(criticalChance, 0.1f));
                Check("默认暴击倍率 1.5", Nearly(criticalMultiplier, 1.5f));
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(comboConfig);
                Object.DestroyImmediate(damageConfig);
            }

            Debug.Log("[ComboValidation] 完成：" + passed + " 通过，" + failed + " 失败。");
        }

        static bool Nearly(float a, float b) => Mathf.Abs(a - b) < 0.005f;
    }
}
