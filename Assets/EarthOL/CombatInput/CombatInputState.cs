using System;
using UnityEngine;

namespace EarthOL.CombatInput
{
    /// <summary>徒手（背包关闭）与物品使用（背包打开）两种互斥状态。</summary>
    public enum CombatInputMode
    {
        Barehand,
        ItemUse
    }

    [DisallowMultipleComponent]
    public sealed class PlayerCombatState : MonoBehaviour
    {
        public event Action<bool> BagOpened;
        public event Action<int> ItemChanged;

        public bool IsBagOpen { get; private set; }
        public int SelectedItemIndex { get; private set; }
        public CombatInputMode Mode => IsBagOpen ? CombatInputMode.ItemUse : CombatInputMode.Barehand;

        public void SetBagOpen(bool open)
        {
            if (IsBagOpen == open) return;
            IsBagOpen = open;
            BagOpened?.Invoke(open);
        }

        public void SelectItem(int index)
        {
            SelectedItemIndex = index;
            ItemChanged?.Invoke(index);
        }
    }
}
