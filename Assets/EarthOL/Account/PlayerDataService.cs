using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace EarthOL.Account
{
    /// <summary>
    /// 当前玩家的内存存档与云端存档之间的桥梁。
    /// 本阶段只提供统一的数据入口，不擅自改造现有战斗 / 背包 / 交互系统；
    /// 游戏系统可调用 <see cref="SetHealth"/> 等入口，并在合适时机调用 <see cref="SaveAsync"/>。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerDataService : MonoBehaviour
    {
        public static PlayerDataService Instance { get; private set; }

        public PlayerSaveData Current { get; private set; }

        public bool HasData => Current != null;

        /// <summary>自上次成功保存以来是否有未保存的修改。</summary>
        public bool IsDirty { get; private set; }

        public string DisplayName => Current != null && !string.IsNullOrEmpty(Current.playerName)
            ? Current.playerName
            : "玩家";

        public event Action<PlayerSaveData> DataLoaded;
        public event Action<PlayerSaveData> DataChanged;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>为新账号建立初始存档（不立即写云，由调用方决定保存时机）。</summary>
        public void SetInitial(string playerName)
        {
            Current = PlayerSaveData.CreateInitial(playerName);
            IsDirty = true;
            DataLoaded?.Invoke(Current);
            DataChanged?.Invoke(Current);
        }

        /// <summary>用云端读回的 JSON 覆盖内存存档。解析失败返回 false，不会产生半初始化状态。</summary>
        public bool TryApplyJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return false;

            var data = PlayerSaveData.FromJson(json);
            if (data == null) return false;

            Migrate(data);
            Current = data;
            IsDirty = false;
            DataLoaded?.Invoke(Current);
            DataChanged?.Invoke(Current);
            return true;
        }

        /// <summary>保存当前存档到云端（使用当前登录玩家身份）。</summary>
        public async Task<bool> SaveAsync()
        {
            if (Current == null || CloudSaveManager.Instance == null) return false;

            Current.updatedAtUtc = DateTime.UtcNow.ToString("o");
            var ok = await CloudSaveManager.Instance.SaveAsync(Current.ToJson());
            if (ok)
            {
                IsDirty = false;
            }
            else
            {
                // 保存失败时不丢弃内存中的有效数据，等待下一次保存时机重试；
                // 云端仍保留上一次的有效存档，不会被覆盖。
                Debug.LogWarning("[Account] 存档保存失败，已保留本地待保存数据。");
            }
            return ok;
        }

        /// <summary>登出后清空当前玩家数据。</summary>
        public void Clear()
        {
            Current = null;
            IsDirty = false;
        }

        // ---------------------------------------------------------------------
        // 游戏系统接入点：只修改内存并标记脏，不直接触发网络。
        // ---------------------------------------------------------------------

        public void SetHealth(float value)
        {
            if (Current == null) return;
            Current.health = Mathf.Max(0f, value);
            Touch();
        }

        public void SetLevel(int value)
        {
            if (Current == null) return;
            Current.level = Mathf.Max(1, value);
            Touch();
        }

        public void SetProgress(int stage)
        {
            if (Current == null) return;
            Current.progress = Mathf.Max(0, stage);
            Touch();
        }

        public void AddInventoryItem(string itemId)
        {
            if (Current == null || string.IsNullOrEmpty(itemId)) return;
            if (Current.inventory == null) Current.inventory = new List<string>();
            if (!Current.inventory.Contains(itemId)) Current.inventory.Add(itemId);
            Touch();
        }

        public void AddCollectionEntry(string entryId)
        {
            if (Current == null || string.IsNullOrEmpty(entryId)) return;
            if (Current.collection == null) Current.collection = new List<string>();
            if (!Current.collection.Contains(entryId)) Current.collection.Add(entryId);
            Touch();
        }

        void Touch()
        {
            IsDirty = true;
            DataChanged?.Invoke(Current);
        }

        /// <summary>存档结构迁移入口。旧版本数据在此逐级升级到最新结构。</summary>
        static void Migrate(PlayerSaveData data)
        {
            if (data == null) return;
            if (data.inventory == null) data.inventory = new List<string>();
            if (data.collection == null) data.collection = new List<string>();
            if (data.schemaVersion <= 0) data.schemaVersion = PlayerSaveData.CurrentSchemaVersion;
            if (string.IsNullOrEmpty(data.playerName)) data.playerName = "玩家";

            // 未来版本迁移示例：
            // if (data.schemaVersion < 2) { ...; data.schemaVersion = 2; }
            // ...
            data.schemaVersion = PlayerSaveData.CurrentSchemaVersion;
        }
    }
}
