using UnityEngine;

namespace EarthOL.Deform
{
    /// <summary>
    /// 能被"戳"的物体实现这个接口。PokeInput 会用射线打到物体的 Collider，
    /// 然后把手指状态转交给它。
    /// </summary>
    public interface IPokeTarget
    {
        /// <summary>手指按下的那一帧（只调用一次）。</summary>
        void OnPokeBegin(int pointerId, RaycastHit hit);

        /// <summary>手指在物体表面滑动时逐帧调用，用于让凹陷跟着手指走。</summary>
        void OnPokeDrag(int pointerId, RaycastHit hit);

        /// <summary>手指抬起，开始回弹。</summary>
        void OnPokeEnd(int pointerId);
    }
}
