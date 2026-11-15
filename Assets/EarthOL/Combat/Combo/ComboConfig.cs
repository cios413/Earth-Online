using UnityEngine;

namespace EarthOL.Combat
{
    /// <summary>
    /// 连击系统配置：时间窗口、倍率曲线、里程碑与反馈步长。
    /// 所有数值均可在 Inspector 中调整，缺失配置时使用内置默认值。
    /// </summary>
    [CreateAssetMenu(menuName = "Earth OL/Combat/Combo Config", fileName = "ComboConfig")]
    public sealed class ComboConfig : ScriptableObject
    {
        [Header("连击时间窗口（秒）")]
        [SerializeField, Min(0.05f)] float comboDuration = 4f;

        [Header("初始倍率")]
        [SerializeField, Min(1f)] float initialMultiplier = 1f;

        [Header("最大倍率")]
        [SerializeField, Min(1f)] float maxMultiplier = 3f;

        [Header("达到最大倍率所需连击数")]
        [SerializeField, Min(1)] int maxComboForMultiplier = 20;

        [Header("连击倍率曲线（横轴=连击数，纵轴=倍率）")]
        [SerializeField] AnimationCurve multiplierCurve;

        [Header("固定连击里程碑")]
        [SerializeField] int[] comboMilestones = { 5, 10, 15, 20, 30, 50 };

        [Header("倍率升级反馈步长")]
        [SerializeField, Min(0.01f)] float multiplierMilestoneStep = 0.5f;

        [Header("COMBO BREAK 显示时长（秒）")]
        [SerializeField, Min(0.05f)] float comboBreakDisplayDuration = 0.8f;

        public float ComboDuration => Mathf.Max(0.05f, comboDuration);
        public float InitialMultiplier => Mathf.Max(1f, initialMultiplier);
        public float MaxMultiplier => Mathf.Max(InitialMultiplier, maxMultiplier);
        public int MaxComboForMultiplier => Mathf.Max(1, maxComboForMultiplier);
        public float MultiplierMilestoneStep => Mathf.Max(0.01f, multiplierMilestoneStep);
        public float ComboBreakDisplayDuration => Mathf.Max(0.05f, comboBreakDisplayDuration);

        public AnimationCurve MultiplierCurve
        {
            get
            {
                if (multiplierCurve == null || multiplierCurve.keys.Length == 0)
                    multiplierCurve = CreateDefaultCurve();
                return multiplierCurve;
            }
        }

        public int[] ComboMilestones
        {
            get
            {
                if (comboMilestones == null || comboMilestones.Length == 0)
                    comboMilestones = new[] { 5, 10, 15, 20, 30, 50 };
                return comboMilestones;
            }
        }

        void OnEnable()
        {
            // 保证新建或空资产在 Inspector 中也有可用曲线与里程碑。
            _ = MultiplierCurve;
            _ = ComboMilestones;
        }

        public static AnimationCurve CreateDefaultCurve()
        {
            return new AnimationCurve(
                new Keyframe(1f, 1.00f),
                new Keyframe(2f, 1.25f),
                new Keyframe(3f, 1.48f),
                new Keyframe(5f, 1.85f),
                new Keyframe(8f, 2.25f),
                new Keyframe(10f, 2.45f),
                new Keyframe(12f, 2.62f),
                new Keyframe(15f, 2.80f),
                new Keyframe(18f, 2.94f),
                new Keyframe(20f, 3.00f));
        }
    }
}
