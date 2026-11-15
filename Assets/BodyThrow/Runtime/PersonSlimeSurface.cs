using UnityEngine;

namespace EarthOL.BodyThrow
{
    /// <summary>A single bounded draw merges all puddles for one tracked person, including their colors.</summary>
    public sealed class PersonSlimeSurface : MonoBehaviour
    {
        public const int Capacity = 24;
        readonly Vector4[] m_Blobs = new Vector4[Capacity];
        readonly Vector4[] m_Colors = new Vector4[Capacity];
        Material m_Material;
        Mesh m_Mesh;
        MeshRenderer m_Renderer;
        Vector2 m_Min, m_Max;
        int m_Count;
        public int BlobCount => m_Count;
        public Material RuntimeMaterial => m_Material;

        public void Configure(Camera camera, Material template)
        {
            transform.SetParent(camera.transform, false);
            m_Mesh = new Mesh { name = "Bounded merged person slime quad" };
            m_Mesh.vertices = new[] { Vector3.zero, Vector3.right, new Vector3(1f, 1f, 0f), Vector3.up };
            m_Mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            m_Mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            gameObject.AddComponent<MeshFilter>().sharedMesh = m_Mesh;
            m_Renderer = gameObject.AddComponent<MeshRenderer>();
            m_Material = new Material(template) { name = "Merged person slime", hideFlags = HideFlags.DontSave };
            m_Renderer.sharedMaterial = m_Material;
            m_Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_Renderer.receiveShadows = false;
            m_Renderer.enabled = false;
        }

        public void BeginFrame()
        {
            m_Count = 0;
            m_Min = Vector2.one * float.MaxValue;
            m_Max = Vector2.one * float.MinValue;
        }

        public void Add(Vector2 center, float radius, float travel, Color color)
        {
            if (m_Count >= Capacity || radius <= 0f) return;
            m_Blobs[m_Count] = new Vector4(center.x, center.y, radius, travel);
            m_Colors[m_Count] = new Vector4(color.r, color.g, color.b, 1f);
            m_Count++;
            float pad = radius * 1.7f;
            m_Min = Vector2.Min(m_Min, center - new Vector2(pad, pad + travel));
            m_Max = Vector2.Max(m_Max, center + Vector2.one * pad);
        }

        public void FinishFrame(float depth)
        {
            if (m_Renderer == null) return;
            m_Renderer.enabled = m_Count > 0;
            if (m_Count == 0) return;
            Vector2 size = m_Max - m_Min;
            transform.localPosition = new Vector3(m_Min.x, m_Min.y, depth);
            transform.localRotation = Quaternion.identity;
            transform.localScale = new Vector3(size.x, size.y, 1f);
            m_Material.SetInt("_SlimeCount", m_Count);
            m_Material.SetVectorArray("_SlimeBlobs", m_Blobs);
            m_Material.SetVectorArray("_SlimeColors", m_Colors);
            m_Material.SetVector("_SlimeBounds", new Vector4(m_Min.x, m_Min.y, size.x, size.y));
            m_Material.SetFloat("_SlimeBlend", Mathf.Max(.004f, size.x * .035f));
        }

        void OnDestroy()
        {
            DestroyOwned(m_Mesh);
            DestroyOwned(m_Material);
        }
        static void DestroyOwned(Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
    }
}
