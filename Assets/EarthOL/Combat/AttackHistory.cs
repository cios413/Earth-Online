using System.Collections.Generic;
using UnityEngine;

namespace EarthOL.Combat
{
    /// <summary>相邻且完全相同的攻击合并后的结果。</summary>
    public sealed class MergedAttack
    {
        public readonly AttackDefinition Definition;
        public readonly int Count;

        public MergedAttack(AttackDefinition definition, int count)
        {
            Definition = definition;
            Count = count;
        }
    }

    /// <summary>
    /// 独立的攻击历史记录模块。始终记录成功攻击，与血条模式无关。
    /// 提供 Begin / End / Clear / GetRecords / GetMerged 接口；
    /// 生命周期由外部（本轮选择）决定，不写死在血量系统里。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AttackHistory : MonoBehaviour
    {
        readonly List<AttackReport> records = new List<AttackReport>();
        bool recording;

        public IReadOnlyList<AttackReport> Records => records;
        public int Count => records.Count;
        public bool IsRecording => recording;

        void OnEnable()
        {
            if (AttackDispatcher.Instance != null)
                AttackDispatcher.Instance.AttackPerformed += Record;
        }

        void OnDisable()
        {
            if (AttackDispatcher.Instance != null)
                AttackDispatcher.Instance.AttackPerformed -= Record;
        }

        /// <summary>开始一轮新记录（清空旧记录）。</summary>
        public void Begin()
        {
            records.Clear();
            recording = true;
        }

        /// <summary>结束记录（不再追加，保留已有数据）。</summary>
        public void End()
        {
            recording = false;
        }

        /// <summary>清空当前记录。</summary>
        public void Clear()
        {
            records.Clear();
        }

        void Record(AttackReport report)
        {
            if (recording && report.Definition != null)
                records.Add(report);
        }

        /// <summary>
        /// 生成用于结算的相邻合并列表：只合并相邻且 ID 完全相同的攻击，保留原始顺序。
        /// 原始逐条记录保留在 Records 中，不丢失。
        /// </summary>
        public List<MergedAttack> GetMerged()
        {
            var merged = new List<MergedAttack>();
            string currentId = null;
            AttackDefinition currentDefinition = null;
            int count = 0;

            foreach (var report in records)
            {
                var definition = report.Definition;
                var id = definition != null ? definition.AttackId : string.Empty;

                if (currentDefinition == null)
                {
                    currentDefinition = definition;
                    currentId = id;
                    count = 1;
                }
                else if (id == currentId)
                {
                    count++;
                }
                else
                {
                    merged.Add(new MergedAttack(currentDefinition, count));
                    currentDefinition = definition;
                    currentId = id;
                    count = 1;
                }
            }

            if (currentDefinition != null)
                merged.Add(new MergedAttack(currentDefinition, count));

            return merged;
        }
    }
}
