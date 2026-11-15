using System;
using System.Collections.Generic;
using UnityEngine;

namespace EarthOL.CombatInput
{
    /// <summary>
    /// IMGUI 背包按钮 + 横向循环物品栏。负责绘制、矩形命中、滑动选择、
    /// 中心高亮，以及手持物品的拖出跟随与归位动画。
    /// 手势决策在 AttackInputRouter，本类只提供 UI 与状态。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BackpackController : MonoBehaviour
    {
        [SerializeField] float slotSize = 108f;
        [SerializeField] float centerSlotScale = 1.25f;
        [SerializeField] float slotSpacing = 142f;
        [SerializeField] float barHeight = 240f;
        [SerializeField] float returnDuration = 0.18f;
        [SerializeField] float snapSpeed = 14f;

        ItemDefinition[] m_Items = Array.Empty<ItemDefinition>();
        PlayerCombatState m_State;
        float m_Scroll;
        int m_HeldIndex = -1;
        Vector2 m_HeldGuiPos;
        float m_ReturnStart = -1f;
        Vector2 m_ReturnFrom;
        bool m_Scrolling;

        Rect m_BackpackButtonRect;
        Rect m_ItemBarRect;
        Rect[] m_SlotRects = Array.Empty<Rect>();
        int[] m_SlotItemIndices = Array.Empty<int>();

        public ItemDefinition[] Items => m_Items;
        public bool IsHolding => m_HeldIndex >= 0;
        public int HeldItemIndex => m_HeldIndex;

        public void Configure(ItemDefinition[] items, PlayerCombatState state)
        {
            m_Items = items ?? Array.Empty<ItemDefinition>();
            m_State = state;
            if (m_Items.Length > 0 && m_State != null)
                m_State.SelectItem(0);
        }

        public ItemDefinition GetItem(int index)
        {
            if (index < 0 || index >= m_Items.Length) return null;
            return m_Items[index];
        }

        public bool IsPointOnBackpackButton(Vector2 screenPos) => m_BackpackButtonRect.Contains(ToGui(screenPos));
        public bool IsPointOnItemBar(Vector2 screenPos) => m_ItemBarRect.Contains(ToGui(screenPos));

        public int ItemIndexAt(Vector2 screenPos)
        {
            var gui = ToGui(screenPos);
            for (int i = 0; i < m_SlotRects.Length; i++)
                if (m_SlotRects[i].Contains(gui)) return m_SlotItemIndices[i];
            return -1;
        }

        public void ScrollBy(float deltaPixels)
        {
            // 手指右移 -> 内容右移（跟随手指），因此用负号。
            m_Scrolling = true;
            m_Scroll -= deltaPixels / Mathf.Max(1f, slotSpacing);
            UpdateCenterSelection();
        }

        /// <summary>停止手动滑动，开始吸附到最近的卡槽。</summary>
        public void EndScroll() => m_Scrolling = false;

        public void SelectItem(int index)
        {
            if (m_Items.Length == 0) return;
            m_Scroll = Mod(index, m_Items.Length);
            UpdateCenterSelection();
        }

        public int CenterIndex => m_Items.Length == 0 ? -1 : Mod(Mathf.RoundToInt(m_Scroll), m_Items.Length);

        public void BeginHold(int index, Vector2 screenPos)
        {
            m_HeldIndex = Mod(index, m_Items.Length);
            m_HeldGuiPos = ToGui(screenPos);
            m_ReturnStart = -1f;
        }

        public void UpdateHold(Vector2 screenPos)
        {
            if (m_HeldIndex >= 0) m_HeldGuiPos = ToGui(screenPos);
        }

        public void EndHold()
        {
            if (m_HeldIndex < 0) return;
            m_ReturnFrom = m_HeldGuiPos;
            m_ReturnStart = Time.unscaledTime;
        }

        void UpdateCenterSelection()
        {
            if (m_Items.Length == 0 || m_State == null) return;
            int center = CenterIndex;
            if (m_State.SelectedItemIndex != center)
                m_State.SelectItem(center);
        }

        void Update()
        {
            if (m_Scrolling || m_Items.Length == 0) return;
            float target = Mathf.Round(m_Scroll);
            float delta = target - m_Scroll;
            if (Mathf.Abs(delta) < 0.001f) return;
            m_Scroll = Mathf.MoveTowards(m_Scroll, target, Mathf.Abs(delta) * Mathf.Min(1f, Time.unscaledDeltaTime * snapSpeed));
            UpdateCenterSelection();
        }

        void ComputeLayout()
        {
            slotSpacing = Screen.width / 5f;
            slotSize = slotSpacing * 0.84f;
            barHeight = slotSize * 1.9f;
        }

        void OnGUI()
        {
            ComputeLayout();
            float btnSize = Mathf.Clamp(Screen.width / 7f, 96f, 180f);
            float margin = 16f;
            bool open = m_State != null && m_State.IsBagOpen;
            float buttonY = open ? Screen.height - barHeight - btnSize - margin : Screen.height - btnSize - margin;
            m_BackpackButtonRect = new Rect(Screen.width - btnSize - margin, buttonY, btnSize, btnSize);

            var style = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(btnSize * 0.30f) };
            var previous = GUI.backgroundColor;
            GUI.backgroundColor = open ? new Color(0.25f, 0.75f, 0.55f) : Color.white;
            // 开关由 AttackInputRouter 统一处理（避免与 GUI.Button 的点击回调双重切换）。
            GUI.Button(m_BackpackButtonRect, "背包", style);
            GUI.backgroundColor = previous;

            if (!open)
            {
                m_ItemBarRect = default;
                m_SlotRects = Array.Empty<Rect>();
                m_SlotItemIndices = Array.Empty<int>();
                return;
            }

            DrawItemBar();
        }

        void DrawItemBar()
        {
            float barW = Screen.width;
            float barX = 0f;
            float barY = Screen.height - barHeight;
            m_ItemBarRect = new Rect(barX, barY, barW, barHeight);

            var previousColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(m_ItemBarRect, Texture2D.whiteTexture);
            GUI.color = previousColor;

            if (m_Items.Length == 0) { m_SlotRects = Array.Empty<Rect>(); m_SlotItemIndices = Array.Empty<int>(); return; }

            int center = CenterIndex;
            float frac = m_Scroll - center;
            float centerX = barX + barW * 0.5f;
            int visibleHalf = Mathf.CeilToInt((barW * 0.5f + slotSpacing) / slotSpacing) + 1;
            var slotRects = new List<Rect>(visibleHalf * 2 + 1);
            var slotIndices = new List<int>(visibleHalf * 2 + 1);

            for (int offset = -visibleHalf; offset <= visibleHalf; offset++)
            {
                int index = Mod(center + offset, m_Items.Length);
                float size = offset == 0 ? slotSize * centerSlotScale : slotSize;
                float x = centerX + (offset - frac) * slotSpacing;
                float y = barY + (barHeight - size) * 0.5f + (offset == 0 ? -10f : 0f);
                var rect = new Rect(x - size * 0.5f, y, size, size);
                slotRects.Add(rect);
                slotIndices.Add(index);

                if (m_HeldIndex == index)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.22f);
                    GUI.DrawTexture(rect, Texture2D.whiteTexture);
                    GUI.color = previousColor;
                    continue;
                }

                var item = m_Items[index];
                if (item != null && item.Preview != null)
                    GUI.DrawTexture(rect, item.Preview, ScaleMode.ScaleToFit, true);
                else
                {
                    GUI.color = new Color(0.7f, 0.7f, 0.7f, 0.9f);
                    GUI.DrawTexture(rect, Texture2D.whiteTexture);
                    GUI.color = previousColor;
                }

                if (offset == 0)
                {
                    GUI.color = new Color(0.2f, 0.95f, 0.5f, 0.95f);
                    DrawBorder(rect, 4f);
                    GUI.color = previousColor;
                }
            }

            m_SlotRects = slotRects.ToArray();
            m_SlotItemIndices = slotIndices.ToArray();

            DrawHeldItem();
        }

        void DrawHeldItem()
        {
            if (m_HeldIndex < 0 || m_HeldIndex >= m_Items.Length) return;

            Vector2 held = m_HeldGuiPos;
            if (m_ReturnStart >= 0f)
            {
                float t = Mathf.Clamp01((Time.unscaledTime - m_ReturnStart) / Mathf.Max(0.01f, returnDuration));
                held = Vector2.Lerp(m_ReturnFrom, SlotCenter(m_HeldIndex), t);
                if (t >= 1f) { m_HeldIndex = -1; m_ReturnStart = -1f; return; }
            }

            var item = m_Items[m_HeldIndex];
            float heldSize = slotSize * centerSlotScale;
            var heldRect = new Rect(held.x - heldSize * 0.5f, held.y - heldSize * 0.5f, heldSize, heldSize);
            if (item != null && item.Preview != null)
                GUI.DrawTexture(heldRect, item.Preview, ScaleMode.ScaleToFit, true);
        }

        Vector2 SlotCenter(int index)
        {
            int count = m_Items.Length;
            if (count == 0 || m_ItemBarRect.width <= 0f) return Vector2.zero;
            int center = CenterIndex;
            float frac = m_Scroll - center;
            int delta = Mod(index - center, count);
            if (delta > count / 2) delta -= count;
            float size = delta == 0 ? slotSize * centerSlotScale : slotSize;
            float x = m_ItemBarRect.x + m_ItemBarRect.width * 0.5f + (delta - frac) * slotSpacing;
            float y = m_ItemBarRect.y + (m_ItemBarRect.height - size) * 0.5f + (delta == 0 ? -10f : 0f);
            return new Vector2(x, y + size * 0.5f);
        }

        static void DrawBorder(Rect rect, float thickness)
        {
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), Texture2D.whiteTexture);
        }

        static Vector2 ToGui(Vector2 screenPos) => new Vector2(screenPos.x, Screen.height - screenPos.y);
        static int Mod(int a, int b) { int r = a % b; return r < 0 ? r + b : r; }
    }
}
