using System;
using UnityEngine;

namespace EarthOL.Combat
{
    /// <summary>
    /// 当前选择物体这一轮交互的血量。只负责初始化、扣血、归零判断，
    /// 归零时发出通用的 Defeated 事件，不做任何视觉表现。
    /// 关闭血条模式时 IsActive 为 false，不响应任何伤害。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HealthSystem : MonoBehaviour
    {
        public event Action<float> Initialized;                  // maxHealth
        public event Action<float, float> HealthChanged;         // current, max
        public event Action<float, float, float> Damaged;        // damage, current, max
        public event Action Defeated;

        public bool IsActive { get; private set; }
        public float MaxHealth { get; private set; }
        public float CurrentHealth { get; private set; }

        bool subscribed;

        void OnEnable() => Subscribe();
        void OnDisable() => Unsubscribe();

        void Subscribe()
        {
            if (subscribed || AttackDispatcher.Instance == null) return;
            AttackDispatcher.Instance.AttackPerformed += OnAttackPerformed;
            subscribed = true;
        }

        void Unsubscribe()
        {
            if (!subscribed || AttackDispatcher.Instance == null) return;
            AttackDispatcher.Instance.AttackPerformed -= OnAttackPerformed;
            subscribed = false;
        }

        void OnAttackPerformed(AttackReport report)
        {
            if (!IsActive) return;
            ApplyDamage(report.Definition != null ? report.Definition.Damage : 0f);
        }

        /// <summary>初始化新一轮血量，初始血量等于传入的最大血量。</summary>
        public void Initialize(float maxHealth)
        {
            MaxHealth = Mathf.Max(1f, maxHealth);
            CurrentHealth = MaxHealth;
            IsActive = true;
            Initialized?.Invoke(MaxHealth);
            HealthChanged?.Invoke(CurrentHealth, MaxHealth);
        }

        /// <summary>关闭血量：清空本轮血量，之后不响应伤害、不触发归零。</summary>
        public void Deactivate()
        {
            IsActive = false;
            MaxHealth = 0f;
            CurrentHealth = 0f;
        }

        public void ApplyDamage(float damage)
        {
            if (!IsActive || damage <= 0f) return;

            var before = CurrentHealth;
            CurrentHealth = Mathf.Max(0f, before - damage);
            var applied = before - CurrentHealth;

            HealthChanged?.Invoke(CurrentHealth, MaxHealth);
            if (applied > 0f)
                Damaged?.Invoke(applied, CurrentHealth, MaxHealth);

            if (CurrentHealth <= 0f)
            {
                IsActive = false;   // 防止重复触发击败
                Defeated?.Invoke();
            }
        }
    }
}
