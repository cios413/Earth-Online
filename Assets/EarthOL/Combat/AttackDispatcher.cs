using System;
using System.Collections.Generic;
using UnityEngine;

namespace EarthOL.Combat
{
    /// <summary>
    /// 通用攻击通知总线。攻击模块在「攻击成功后」调用 Report(attackId)，
    /// 这里把攻击 ID 解析成 AttackDefinition 再广播 AttackPerformed 事件。
    /// 攻击模块不需要知道血量、血条 UI 或结算的实现。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AttackDispatcher : MonoBehaviour
    {
        public static AttackDispatcher Instance { get; private set; }

        readonly Dictionary<string, AttackDefinition> definitions = new Dictionary<string, AttackDefinition>();

        public event Action<AttackReport> AttackPerformed;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Register(IEnumerable<AttackDefinition> attacks)
        {
            definitions.Clear();
            if (attacks == null) return;

            foreach (var attack in attacks)
            {
                if (attack == null || string.IsNullOrEmpty(attack.AttackId)) continue;
                definitions[attack.AttackId] = attack;
            }
        }

        public bool TryGet(string attackId, out AttackDefinition definition)
        {
            return definitions.TryGetValue(attackId, out definition);
        }

        /// <summary>攻击模块的成功回调调用此方法上报攻击事件。</summary>
        public static void Report(string attackId)
        {
            if (Instance == null || string.IsNullOrEmpty(attackId)) return;

            if (!Instance.definitions.TryGetValue(attackId, out var definition))
            {
                Debug.LogWarning("[Combat] 未注册的攻击 ID：" + attackId + "，已忽略。");
                return;
            }

            Instance.AttackPerformed?.Invoke(new AttackReport(definition));
        }
    }
}
