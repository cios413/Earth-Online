using UnityEngine;

namespace EarthOL.Deform
{
    /// <summary>Interaction contract exposed by person deformation to independently rendered models.</summary>
    public interface IPersonModelDeformation
    {
        bool HasActivePokes { get; }
        void Begin(int pointerId, Vector3 worldPoint);
        void Drag(int pointerId, Vector3 worldPoint);
        void End(int pointerId);
        void Advance(float deltaTime);
        void Clear();
    }
}
