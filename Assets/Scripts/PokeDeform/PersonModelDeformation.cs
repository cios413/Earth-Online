using UnityEngine;

namespace EarthOL.Deform
{
    /// <summary>Adapts the existing warp spring to a model renderer without replacing the model mesh or materials.</summary>
    public sealed class PersonModelDeformation : MonoBehaviour, IPersonModelDeformation
    {
        PokeWarpDeformer m_Spring;
        Renderer m_Target;
        public bool HasActivePokes => m_Spring != null && m_Spring.HasActivePokes;

        public void Configure(PokeWarpDeformer source, Renderer target, Bounds modelBounds)
        {
            m_Target = target;
            var driver = new GameObject("Person deformation spring driver");
            driver.SetActive(false);
            driver.transform.SetParent(target.transform, false);
            m_Spring = driver.AddComponent<PokeWarpDeformer>();
            m_Spring.createCollider = false;
            m_Spring.lockPokesToSurface = source.lockPokesToSurface;
            m_Spring.planeInXZ = false;
            m_Spring.planeWidth = Mathf.Max(.05f, modelBounds.size.x);
            m_Spring.planeHeight = Mathf.Max(.05f, modelBounds.size.y);
            m_Spring.pokeRadius = Mathf.Min(m_Spring.planeWidth, m_Spring.planeHeight) * .40f;
            m_Spring.stretchAmount = source.stretchAmount;
            m_Spring.bulgeAmount = source.bulgeAmount;
            m_Spring.frequency = source.frequency;
            m_Spring.dampingRatio = source.dampingRatio;
            m_Spring.warpMaterial = source.warpMaterial;
            m_Spring.logPokes = false;
            m_Spring.Initialize();
            driver.GetComponent<Renderer>().enabled = false;
            m_Spring.enabled = false; // The showcase render clock drives Advance exactly once.
            driver.SetActive(true);
        }

        public void Begin(int pointerId, Vector3 worldPoint)
        {
            m_Spring.BeginAt(pointerId, worldPoint);
            m_Spring.CopyDeformationTo(m_Target);
        }
        public void Drag(int pointerId, Vector3 worldPoint)
        {
            m_Spring.DragAt(pointerId, worldPoint);
            m_Spring.CopyDeformationTo(m_Target);
        }
        public void End(int pointerId) { if (m_Spring != null) m_Spring.OnPokeEnd(pointerId); }
        public void Advance(float deltaTime)
        {
            if (m_Spring == null || m_Target == null) return;
            m_Spring.Advance(deltaTime);
            m_Spring.CopyDeformationTo(m_Target);
        }
        public void Clear()
        {
            if (m_Spring == null) return;
            m_Spring.ResetPokes();
            if (m_Target != null) m_Spring.CopyDeformationTo(m_Target);
        }
        void OnDisable() => Clear();
    }
}
