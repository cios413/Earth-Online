using UnityEngine;

namespace EarthOL.BodyThrow
{
    /// <summary>A brief volume-preserving squash on the visual, anchored at its impact end.</summary>
    [DisallowMultipleComponent]
    public sealed class ProjectileImpactBounce : MonoBehaviour
    {
        Transform m_Visual;
        Vector3 m_BaseScale, m_BasePosition;
        float m_ImpactEnd;
        float m_Age;
        bool m_Active;
        public bool IsBouncing => m_Active;

        public void Play(float impactEnd)
        {
            if (m_Visual == null)
            {
                m_Visual = transform.Find("Visual");
                if (m_Visual == null) return;
                m_BaseScale = m_Visual.localScale;
                m_BasePosition = m_Visual.localPosition;
            }
            Restore();
            m_ImpactEnd = impactEnd;
            m_Age = 0f;
            m_Active = true;
            Apply(0f);
        }

        private void Update() => Advance(Time.unscaledDeltaTime);

        public void Advance(float deltaTime)
        {
            if (!m_Active || deltaTime <= 0f) return;
            m_Age += deltaTime;
            if (m_Age >= 1f) { Restore(); return; }
            Apply(m_Age);
        }

        private void Apply(float time)
        {
            float compression = 0.26f * Mathf.Exp(-5.5f * time) * Mathf.Cos(2f * Mathf.PI * 3f * time);
            float depth = 1f - compression;
            float width = 1f / Mathf.Sqrt(depth);
            m_Visual.localScale = Vector3.Scale(m_BaseScale, new Vector3(width, width, depth));
            // The contact end stays on the surface while the model body compresses.
            m_Visual.localPosition = m_BasePosition + Vector3.forward * ((m_ImpactEnd - m_BasePosition.z) * (1f - depth));
        }

        private void Restore()
        {
            m_Active = false;
            if (m_Visual == null) return;
            m_Visual.localScale = m_BaseScale;
            m_Visual.localPosition = m_BasePosition;
        }

        private void OnDisable() => Restore();
    }
}
