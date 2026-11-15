using UnityEngine;

namespace EarthOL.Deform
{
    public interface IPokeTarget
    {
        void OnPokeBegin(int pointerId, RaycastHit hit);
        void OnPokeDrag(int pointerId, RaycastHit hit);
        void OnPokeEnd(int pointerId);
    }
}
