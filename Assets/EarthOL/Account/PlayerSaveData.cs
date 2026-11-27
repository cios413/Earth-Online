using System;
using System.Collections.Generic;
using UnityEngine;

namespace EarthOL.Account
{
    /// <summary>
    /// 统一的玩家云端存档结构。所有字段均为 JSON 可序列化类型，供 JsonUtility 使用。
    /// 修改结构时请提升 <see cref="CurrentSchemaVersion"/> 并在 <see cref="PlayerDataService.Migrate"/>
    /// 中补充迁移逻辑。
    /// </summary>
    [Serializable]
    public sealed class PlayerSaveData
    {
        public const int CurrentSchemaVersion = 1;

        /// <summary>存档结构版本号，用于后续结构迁移。</summary>
        public int schemaVersion = CurrentSchemaVersion;

        /// <summary>显示昵称（本阶段直接用用户名）。</summary>
        public string playerName = string.Empty;

        /// <summary>玩家血量。</summary>
        public float health = 100f;

        /// <summary>玩家等级。</summary>
        public int level = 1;

        /// <summary>背包物品（物品 ID 列表）。</summary>
        public List<string> inventory = new List<string>();

        /// <summary>收集记录（条目 ID 列表）。</summary>
        public List<string> collection = new List<string>();

        /// <summary>游戏进度（当前阶段编号，语义由游戏系统定义）。</summary>
        public int progress = 0;

        /// <summary>最后一次修改时间（UTC，ISO-8601）。</summary>
        public string updatedAtUtc = string.Empty;

        public static PlayerSaveData CreateInitial(string playerName)
        {
            var data = new PlayerSaveData
            {
                schemaVersion = CurrentSchemaVersion,
                playerName = string.IsNullOrEmpty(playerName) ? "玩家" : playerName,
                health = 100f,
                level = 1,
                inventory = new List<string>(),
                collection = new List<string>(),
                progress = 0,
                updatedAtUtc = DateTime.UtcNow.ToString("o")
            };
            return data;
        }

        public PlayerSaveData Clone()
        {
            return new PlayerSaveData
            {
                schemaVersion = schemaVersion,
                playerName = playerName,
                health = health,
                level = level,
                inventory = inventory != null ? new List<string>(inventory) : new List<string>(),
                collection = collection != null ? new List<string>(collection) : new List<string>(),
                progress = progress,
                updatedAtUtc = updatedAtUtc
            };
        }

        public string ToJson() => JsonUtility.ToJson(this);

        public static PlayerSaveData FromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            return JsonUtility.FromJson<PlayerSaveData>(json);
        }
    }
}
