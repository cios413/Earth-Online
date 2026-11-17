using EarthOL.ARCore;
using EarthOL.BodyThrow;
using UnityEngine;

namespace EarthOL.CombatInput
{
    /// <summary>
    /// 统一攻击输入路由：唯一消费 BodyPointerFrame 的地方。
    /// 识别点击 / 滑动 / 拖拽 / 长按，结合背包状态与物品栏矩形，
    /// 分发到徒手按压、徒手挥打、物品投掷、手持攻击。
    /// 只有攻击真正命中/触发时，才通过 AttackDispatcher 上报成功事件。
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(50)]
    public sealed class AttackInputRouter : MonoBehaviour
    {
        const float TapMaxMovement = 40f;
        const float TapMaxDuration = 0.5f;
        const float DragStartMovement = 22f;
        const float DoubleTapInterval = 0.38f;
        const float DoubleTapDistance = 80f;

        enum Phase { None, BackpackButton, ItemBarScroll, HoldCandidate, Hold, Press, Swipe, ThrowTap }

        [SerializeField] PersonTrackingManager personTracking;
        [SerializeField] PersonDeformationSurface personDeformer;
        [SerializeField] BodyProjectileSpawner spawner;
        [SerializeField] PlayerCombatState state;
        [SerializeField] BackpackController backpack;
        [SerializeField] SwipeDeformController swipeDeform;
        [SerializeField] Camera arCamera;

        Phase phase = Phase.None;
        int pointerId;
        Vector2 downPosition;
        float downTime;
        bool moved;
        Vector2 lastPosition;

        bool wasInsideTarget;
        bool holdWasInsideTarget;
        int holdItemIndex = -1;

        float lastTapTime = -999f;
        Vector2 lastTapPosition;
        bool pendingThrow;
        float pendingThrowTime;
        Vector2 pendingThrowPosition;

        public void Configure(PersonTrackingManager tracking, PersonDeformationSurface deformer, BodyProjectileSpawner projectileSpawner,
            PlayerCombatState combatState, BackpackController backpackController,
            SwipeDeformController swipeDeformController, Camera camera)
        {
            personTracking = tracking;
            personDeformer = deformer;
            spawner = projectileSpawner;
            state = combatState;
            backpack = backpackController;
            swipeDeform = swipeDeformController;
            arCamera = camera;
        }

        void Update()
        {
            if (pendingThrow && Time.unscaledTime >= pendingThrowTime)
            {
                pendingThrow = false;
                if (spawner != null) spawner.ThrowAtScreenPoint(pendingThrowPosition);
            }

            if (!BodyPointerFrame.TryRead(out var frame)) return;
            if (frame.down) OnDown(frame.id, frame.position);
            else if (frame.id == pointerId && (frame.held || frame.released || frame.canceled)) OnHeldOrReleased(frame);
        }

        void OnDown(int id, Vector2 screenPos)
        {
            pointerId = id;
            downPosition = screenPos;
            downTime = Time.unscaledTime;
            moved = false;
            lastPosition = screenPos;
            holdItemIndex = -1;
            wasInsideTarget = false;
            holdWasInsideTarget = false;

            if (EarthOL.Combat.CombatOverlay.BlocksWorldInput || EarthOL.Combat.CombatOverlay.IsPointOnOverlay(screenPos))
            {
                phase = Phase.None;
                return;
            }

            phase = ClassifyDown(screenPos);

            if (phase == Phase.Press)
            {
                // 按压形变由 PersonDeformationSurface 自己读取触摸（Li 方式）；这里只负责攻击上报。
            }
            else if (phase == Phase.Swipe)
            {
                wasInsideTarget = PointInsideTarget(screenPos);
            }
            else if (phase == Phase.HoldCandidate)
            {
                holdItemIndex = backpack != null ? backpack.ItemIndexAt(screenPos) : -1;
            }
        }

        Phase ClassifyDown(Vector2 screenPos)
        {
            bool bagOpen = state != null && state.IsBagOpen;

            if (backpack != null && backpack.IsPointOnBackpackButton(screenPos)) return Phase.BackpackButton;

            if (bagOpen)
            {
                if (backpack != null && backpack.IsPointOnItemBar(screenPos))
                {
                    int slot = backpack.ItemIndexAt(screenPos);
                    var item = slot >= 0 ? backpack.GetItem(slot) : null;
                    return slot >= 0 && item != null && item.CanHold ? Phase.HoldCandidate : Phase.ItemBarScroll;
                }
                return Phase.ThrowTap;
            }

            return PointInsideTarget(screenPos) ? Phase.Press : Phase.Swipe;
        }

        void OnHeldOrReleased(BodyPointerFrame frame)
        {
            if (phase == Phase.None) return;

            float distance = Vector2.Distance(frame.position, downPosition);
            if (distance > TapMaxMovement) moved = true;
            Vector2 moveDelta = frame.position - lastPosition;
            float deltaX = moveDelta.x;
            lastPosition = frame.position;

            switch (phase)
            {
                case Phase.BackpackButton:
                    if (frame.released) { if (state != null) state.SetBagOpen(!state.IsBagOpen); }
                    break;

                case Phase.ItemBarScroll:
                    if (frame.held && Mathf.Abs(deltaX) > 0.01f) backpack?.ScrollBy(deltaX);
                    else if (frame.released || frame.canceled) { backpack?.EndScroll(); phase = Phase.None; }
                    break;

                case Phase.HoldCandidate:
                    if (frame.held && distance > DragStartMovement)
                    {
                        phase = Phase.Hold;
                        if (backpack != null) backpack.BeginHold(holdItemIndex, downPosition);
                        holdWasInsideTarget = false;
                    }
                    else if (frame.released && !moved)
                    {
                        if (holdItemIndex >= 0 && backpack != null) backpack.SelectItem(holdItemIndex);
                    }
                    break;

                case Phase.Hold:
                    if (frame.held)
                    {
                        backpack?.UpdateHold(frame.position);
                        UpdateHoldCrossing(frame.position, moveDelta);
                    }
                    else if (frame.released || frame.canceled)
                    {
                        backpack?.EndHold();
                        phase = Phase.None;
                    }
                    break;

                case Phase.Press:
                    if (frame.held && moved)
                    {
                        // 起手在目标上但发生了滑动 -> 转为挥扫。
                        phase = Phase.Swipe;
                        wasInsideTarget = false;
                        UpdateSwipeCrossing(frame.position, moveDelta);
                    }
                    else if (frame.released || frame.canceled)
                    {
                        phase = Phase.None;
                        if (!frame.canceled && IsTap())
                        {
                            HandleTap(frame.position);
                            // 只有「点击（松手且未移动）」才算一次成功的徒手按压。
                            EarthOL.Combat.AttackDispatcher.Report("press");
                        }
                    }
                    break;

                case Phase.Swipe:
                    if (frame.held) UpdateSwipeCrossing(frame.position, moveDelta);
                    else if (frame.released || frame.canceled)
                    {
                        phase = Phase.None;
                        if (!frame.canceled && IsTap()) HandleTap(frame.position);
                    }
                    break;

                case Phase.ThrowTap:
                    if (frame.released || frame.canceled)
                    {
                        phase = Phase.None;
                        if (!frame.canceled && IsTap() && !HandleTap(frame.position))
                            ScheduleThrow(frame.position);
                    }
                    break;
            }
        }

        bool IsTap() => !moved && Time.unscaledTime - downTime < TapMaxDuration;

        /// <summary>双击检测：是第二次点击则触发选人并返回 true。</summary>
        bool HandleTap(Vector2 screenPos)
        {
            bool isDouble = Time.unscaledTime - lastTapTime <= DoubleTapInterval &&
                            Vector2.Distance(screenPos, lastTapPosition) <= DoubleTapDistance;
            if (isDouble)
            {
                lastTapTime = -999f;
                pendingThrow = false;
                return true;
            }

            lastTapTime = Time.unscaledTime;
            lastTapPosition = screenPos;
            return false;
        }

        void ScheduleThrow(Vector2 screenPos)
        {
            pendingThrow = true;
            pendingThrowTime = Time.unscaledTime + DoubleTapInterval;
            pendingThrowPosition = screenPos;
        }

        void UpdateSwipeCrossing(Vector2 screenPos, Vector2 moveDelta)
        {
            bool inside = PointInsideTarget(screenPos);
            if (inside && !wasInsideTarget)
            {
                wasInsideTarget = true;
                EarthOL.Combat.AttackDispatcher.Report("swipe");

                if (moveDelta.sqrMagnitude > 1e-4f)
                {
                    Vector2 direction = moveDelta.normalized;
                    float speed = moveDelta.magnitude / Mathf.Max(0.0001f, Time.unscaledDeltaTime);
                    swipeDeform?.Trigger(direction, speed, screenPos);
                }
            }
            else if (!inside && wasInsideTarget) wasInsideTarget = false;
        }

        void UpdateHoldCrossing(Vector2 screenPos, Vector2 moveDelta)
        {
            bool inside = PointInsideTarget(screenPos);
            if (inside && !holdWasInsideTarget)
            {
                holdWasInsideTarget = true;
                var item = backpack != null ? backpack.GetItem(holdItemIndex) : null;
                string attackId = item != null ? item.HoldAttackId : "StickyHandHold";
                EarthOL.Combat.AttackDispatcher.Report(attackId);

                if (moveDelta.sqrMagnitude > 1e-4f)
                {
                    Vector2 direction = moveDelta.normalized;
                    float speed = moveDelta.magnitude / Mathf.Max(0.0001f, Time.unscaledDeltaTime);
                    swipeDeform?.Trigger(direction, speed, screenPos);
                }
            }
            else if (!inside && holdWasInsideTarget) holdWasInsideTarget = false;
        }

        bool PointInsideTarget(Vector2 screenPos)
        {
            var person = personTracking != null ? personTracking.BestPerson() : null;
            if (person == null || arCamera == null) return false;
            Vector2 min = arCamera.ViewportToScreenPoint(new Vector3(person.screenRect.xMin, person.screenRect.yMin, 0f));
            Vector2 max = arCamera.ViewportToScreenPoint(new Vector3(person.screenRect.xMax, person.screenRect.yMax, 0f));
            var rect = Rect.MinMaxRect(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y),
                Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
            return rect.Contains(screenPos);
        }

    }
}
