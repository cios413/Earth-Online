using UnityEngine;

namespace EarthOL.Combat
{
    // wang 53a907b combo defaults and multiplier curve.
    [CreateAssetMenu(menuName = "Earth OL/Combat/Combo Config")]
    public sealed class ComboConfig : ScriptableObject
    {
        [SerializeField, Min(.05f)] float comboDuration = 4f;
        [SerializeField, Min(1f)] float initialMultiplier = 1f;
        [SerializeField, Min(1f)] float maxMultiplier = 3f;
        [SerializeField, Min(1)] int maxComboForMultiplier = 20;
        [SerializeField] AnimationCurve multiplierCurve;
        [SerializeField] int[] comboMilestones = { 5, 10, 15, 20, 30, 50 };
        public float ComboDuration => Mathf.Max(.05f, comboDuration);
        public float InitialMultiplier => Mathf.Max(1f, initialMultiplier);
        public float MaxMultiplier => Mathf.Max(InitialMultiplier, maxMultiplier);
        public int MaxComboForMultiplier => Mathf.Max(1, maxComboForMultiplier);
        public int[] ComboMilestones => comboMilestones;
        public AnimationCurve MultiplierCurve => multiplierCurve ?? (multiplierCurve = CreateDefaultCurve());
        public static AnimationCurve CreateDefaultCurve() => new AnimationCurve(
            new Keyframe(1f, 1f), new Keyframe(2f, 1.25f), new Keyframe(3f, 1.48f),
            new Keyframe(5f, 1.85f), new Keyframe(8f, 2.25f), new Keyframe(10f, 2.45f),
            new Keyframe(12f, 2.62f), new Keyframe(15f, 2.80f), new Keyframe(18f, 2.94f), new Keyframe(20f, 3f));
    }
}
