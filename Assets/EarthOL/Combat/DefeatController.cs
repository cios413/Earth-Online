using System;
using System.Collections;
using UnityEngine;

namespace EarthOL.Combat
{
    /// <summary>
    /// 接收血量归零事件，协调「物体视觉效果 → 结算」流程。
    ///
    /// 视觉接口通过 VisualEffectRequested 事件预留：同伴订阅该事件播放
    /// 破碎/变形/消失等效果，播放完毕后调用传入的 onComplete 回调，
    /// 结算对话框才会出现，不会与视觉同时弹出。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DefeatController : MonoBehaviour
    {
        [SerializeField, Min(0f)] float visualTimeoutSeconds = 12f;

        /// <summary>预留的物体视觉接口。订阅者负责播放效果，完成后调用 onComplete。</summary>
        public event Action<Action> VisualEffectRequested;

        /// <summary>视觉流程结束（或超时）后发出。</summary>
        public event Action VisualEffectFinished;

        /// <summary>请求弹出结算对话框。</summary>
        public event Action SettlementRequested;

        HealthSystem healthSystem;
        bool defeatHandled;
        bool visualFinished;
        Coroutine timeoutRoutine;

        public void Configure(HealthSystem system)
        {
            if (healthSystem != null)
                healthSystem.Defeated -= OnDefeated;

            healthSystem = system;
            if (healthSystem != null)
                healthSystem.Defeated += OnDefeated;
        }

        public void ResetForNewRound()
        {
            defeatHandled = false;
            visualFinished = false;
            if (timeoutRoutine != null)
            {
                StopCoroutine(timeoutRoutine);
                timeoutRoutine = null;
            }
        }

        void OnDefeated()
        {
            if (defeatHandled) return;
            defeatHandled = true;
            visualFinished = false;

            if (VisualEffectRequested == null || VisualEffectRequested.GetInvocationList().Length == 0)
            {
                FinishVisual();
                return;
            }

            VisualEffectRequested?.Invoke(OnVisualCompleted);
            if (!visualFinished && visualTimeoutSeconds > 0f)
                timeoutRoutine = StartCoroutine(VisualTimeout());
        }

        void OnVisualCompleted()
        {
            if (visualFinished) return;
            visualFinished = true;
            if (timeoutRoutine != null)
            {
                StopCoroutine(timeoutRoutine);
                timeoutRoutine = null;
            }
            FinishVisual();
        }

        IEnumerator VisualTimeout()
        {
            yield return new WaitForSeconds(visualTimeoutSeconds);
            timeoutRoutine = null;
            if (!visualFinished)
                OnVisualCompleted();
        }

        void FinishVisual()
        {
            VisualEffectFinished?.Invoke();
            SettlementRequested?.Invoke();
        }
    }
}
