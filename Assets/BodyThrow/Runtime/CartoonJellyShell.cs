using UnityEngine;
using UnityEngine.UI;

namespace EarthOL.BodyThrow
{
    /// <summary>One bounded UI draw unions the head, neck, torso and limbs. No joint overdraw or texture seams.</summary>
    public sealed class CartoonJellyShell : MonoBehaviour
    {
        public const int MaximumSegments = 12;
        readonly Vector4[] m_Segments = new Vector4[MaximumSegments];
        readonly Vector4[] m_Radii = new Vector4[MaximumSegments];
        readonly Vector4[] m_UploadSegments = new Vector4[MaximumSegments];
        RawImage m_Image;
        RectTransform m_Rect;
        Material m_Material;
        int m_Count;
        Vector2 m_Min, m_Max;
        Vector4 m_Head;
        public int SegmentCount => m_Count;
        public Material RuntimeMaterial => m_Material;
        public Rect LocalBounds => new Rect(m_Rect.anchoredPosition - m_Rect.sizeDelta*.5f, m_Rect.sizeDelta);
        public bool IsVisible => m_Image != null && m_Image.enabled;

        public void Configure(Transform parent)
        {
            var go = new GameObject("Continuous Jelly Envelope", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            go.transform.SetParent(parent, false);
            go.transform.SetAsFirstSibling();
            m_Rect = go.GetComponent<RectTransform>();
            m_Rect.anchorMin = m_Rect.anchorMax = m_Rect.pivot = Vector2.one*.5f;
            m_Image = go.GetComponent<RawImage>();
            m_Image.raycastTarget = false;
            m_Image.maskable = false;
            m_Image.texture = Texture2D.whiteTexture;
            var template = Resources.Load<Material>("CartoonJelly/ConnectedJelly");
            if (template == null) { Debug.LogError("[CartoonJelly] Missing connected jelly material."); return; }
            m_Material = new Material(template) { name = "Jelly envelope per person", hideFlags = HideFlags.DontSave };
            m_Image.material = m_Material;
            m_Image.enabled = false;
        }

        public void SetPalette(JellySkeletonPalette palette)
        {
            if (m_Material == null) return;
            m_Material.SetColor("_BodyColor", palette.body);
            m_Material.SetColor("_LimbColor", palette.limbs);
        }

        public void BeginPose()
        {
            m_Count = 0; m_Head = Vector4.zero;
            m_Min = Vector2.one*float.MaxValue; m_Max = Vector2.one*float.MinValue;
            for (int i=0;i<MaximumSegments;i++) m_Radii[i] = Vector4.zero;
        }

        public void AddSegment(Vector2 a, Vector2 b, float radiusA, float radiusB, bool limb)
        {
            if (m_Count >= MaximumSegments || radiusA <= 0f || radiusB <= 0f) return;
            m_Segments[m_Count] = new Vector4(a.x,a.y,b.x,b.y);
            m_Radii[m_Count] = new Vector4(radiusA,radiusB,limb?1f:0f,1f);
            float pad = Mathf.Max(radiusA,radiusB)*1.4f;
            m_Min = Vector2.Min(m_Min, Vector2.Min(a,b)-Vector2.one*pad);
            m_Max = Vector2.Max(m_Max, Vector2.Max(a,b)+Vector2.one*pad);
            m_Count++;
        }

        public void AddHead(Vector2 center, float radius)
        {
            AddSegment(center+Vector2.up*radius*.12f,center-Vector2.up*radius*.12f,radius,radius,false);
            m_Head = new Vector4(center.x,center.y,radius,1f);
        }

        public void FinishPose(float alpha, float blendRadius)
        {
            if (m_Material == null || m_Count == 0) { Hide(); return; }
            for (int i=0;i<m_Count;i++)
            {
                var s=m_Segments[i];
                m_UploadSegments[i]=new Vector4(s.x-m_Min.x,s.y-m_Min.y,s.z-m_Min.x,s.w-m_Min.y);
            }
            m_Rect.anchoredPosition=(m_Min+m_Max)*.5f;
            m_Rect.sizeDelta=m_Max-m_Min;
            m_Material.SetVectorArray("_JellySegments",m_UploadSegments);
            m_Material.SetVectorArray("_JellyRadii",m_Radii);
            m_Material.SetVector("_JellySize",new Vector4(m_Rect.sizeDelta.x,m_Rect.sizeDelta.y,0,0));
            m_Material.SetVector("_JellyHead",new Vector4(m_Head.x-m_Min.x,m_Head.y-m_Min.y,m_Head.z,m_Head.w));
            m_Material.SetFloat("_JellyBlend",Mathf.Max(2f,blendRadius));
            m_Material.SetFloat("_TrackingAlpha",alpha);
            m_Image.enabled=true;
        }

        public void Hide() { if(m_Image!=null)m_Image.enabled=false; }
        void OnDestroy()
        {
            if(m_Material==null)return;
            if(Application.isPlaying)Destroy(m_Material);else DestroyImmediate(m_Material);
        }
    }
}
