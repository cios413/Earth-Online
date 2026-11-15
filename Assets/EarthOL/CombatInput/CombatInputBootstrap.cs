using System;
using System.Collections;
using System.Collections.Generic;
using EarthOL.ARCore;
using EarthOL.BodyThrow;
using UnityEngine;

namespace EarthOL.CombatInput
{
    /// <summary>
    /// 攻击输入系统的接入层：在 ARCoreMainSceneBootstrap 建好后，
    /// 组装状态、背包、路由与视觉反馈，并把背包开合映射到 Deform/Throw 模式。
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1000)]
    public sealed class CombatInputBootstrap : MonoBehaviour
    {
        ARCoreMainSceneBootstrap bootstrap;
        PlayerCombatState state;
        BackpackController backpack;
        AttackInputRouter router;
        SwipeDeformController swipeDeform;
        HumanMaskOverlay maskOverlay;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void EnsureExists()
        {
            if (FindObjectOfType<CombatInputBootstrap>() != null) return;
            new GameObject("Combat Input System").AddComponent<CombatInputBootstrap>();
        }

        void Start() => StartCoroutine(Bind());

        IEnumerator Bind()
        {
            while (bootstrap == null)
            {
                bootstrap = FindObjectOfType<ARCoreMainSceneBootstrap>();
                yield return null;
            }
            while (bootstrap.ARCamera == null || bootstrap.PoseTracker == null ||
                   bootstrap.Spawner == null || bootstrap.PersonDeformer == null)
                yield return null;

            state = gameObject.AddComponent<PlayerCombatState>();
            backpack = gameObject.AddComponent<BackpackController>();
            swipeDeform = gameObject.AddComponent<SwipeDeformController>();
            router = gameObject.AddComponent<AttackInputRouter>();
            maskOverlay = gameObject.AddComponent<HumanMaskOverlay>();

            var items = BuildItems(bootstrap.Spawner.Catalog);
            backpack.Configure(items, state);
            swipeDeform.Configure(bootstrap.PersonDeformer);
            maskOverlay.Configure(bootstrap.PoseTracker, bootstrap.ARCamera);
            router.Configure(bootstrap.PoseTracker, bootstrap.PersonDeformer, bootstrap.Spawner,
                state, backpack, swipeDeform, bootstrap.ARCamera);

            state.BagOpened += OnBagOpened;
            state.ItemChanged += OnItemChanged;

            OnBagOpened(false);
            Debug.Log("[CombatInput] 背包/攻击输入系统已接入（IMGUI，默认徒手）。");
        }

        void OnBagOpened(bool open)
        {
            if (bootstrap != null)
                bootstrap.SetMode(open ? GameMode.Throw : GameMode.Deform);
        }

        void OnItemChanged(int index)
        {
            if (bootstrap != null && bootstrap.Spawner != null)
                bootstrap.Spawner.SelectProjectile(index);
        }

        static ItemDefinition[] BuildItems(BodyProjectileCatalog catalog)
        {
            if (catalog == null || catalog.entries == null || catalog.entries.Length == 0)
                return Array.Empty<ItemDefinition>();

            var list = new List<ItemDefinition>();
            foreach (var entry in catalog.entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.key)) continue;
                var def = ScriptableObject.CreateInstance<ItemDefinition>();
                def.name = "Item_" + entry.key;
                // 当前只有「粘粘手」支持手持，其余仅投掷（可后续用配置资产覆盖）。
                def.Configure(entry.key, entry.displayName, entry.preview, true, entry.key == "StickyHand");
                list.Add(def);
            }
            return list.ToArray();
        }
    }
}
