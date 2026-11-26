using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace EarthOL.BodyThrow
{
    /// <summary>Camera-facing painted grenade and a finite, locally bloomed dream burst.</summary>
    [DisallowMultipleComponent]
    public sealed class DreamGrenadeProjectile : MonoBehaviour
    {
        [SerializeField] Renderer grenadeRenderer;
        [SerializeField] Material mistMaterial;
        [SerializeField] Material glowMaterial;
        [SerializeField, Min(.05f)] float flashSeconds = .16f;
        [SerializeField, Min(.5f)] float burstSeconds = 3.4f;
        [SerializeField, Range(.5f, 2f)] float burstScale = 1f;
        [SerializeField, Tooltip("各喷射起点到中心的距离范围（随 Burst Scale 一起缩放）。")]
        Vector2 emissionRadiusRange = new Vector2(.32f, .64f);

        static readonly int FlashId = Shader.PropertyToID("_Flash");
        static readonly Color[] Palette =
        {
            new Color(.46f, .92f, .20f), new Color(.48f, .94f, .56f),
            new Color(.69f, .95f, .29f), new Color(1f, .13f, .55f),
            new Color(.30f, .83f, .40f), new Color(.90f, 1f, .45f)
        };
        // Paths fan upward from independent origins around the impact's empty center.
        static readonly Vector2[] SplashTips =
        {
            new Vector2(-1.62f,1.24f), new Vector2(-.60f,2.12f),
            new Vector2(.57f,1.67f), new Vector2(1.52f,1.85f),
            new Vector2(1.48f,.63f), new Vector2(-1.08f,.28f),
            new Vector2(.93f,1.25f), new Vector2(-.12f,1.37f),
            new Vector2(.84f,.22f), new Vector2(-.56f,.72f)
        };
        static readonly int[] SplashColors = { 2,0,0,2,0,1,1,3,0,3 };
        const int StarCount = 42;
        const int RibbonSegments = 36;
        const float StarDelay = .46f;
        MaterialPropertyBlock m_Properties;
        Camera m_Camera;
        Batch m_Mist, m_Glow;

        public float FlashSeconds => flashSeconds;
        public float TotalImpactSeconds => flashSeconds + burstSeconds;
        public bool SpriteVisible => grenadeRenderer != null && grenadeRenderer.enabled;
        public bool BurstStarted { get; private set; }
        public bool IsFinished { get; private set; }
        public int VisibleStarCount { get; private set; }
        public float CenterGapRadius => Mathf.Max(0,Mathf.Min(emissionRadiusRange.x,emissionRadiusRange.y))*burstScale*.78f;

        public void RenderFlight(Camera camera, float progress)
        {
            m_Camera = camera;
            FaceCamera();
            if (grenadeRenderer != null)
                grenadeRenderer.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(-16, 12, progress));
        }

        /// <summary>The throw controller supplies unscaled age and owns cancellation/destruction.</summary>
        public void RenderImpact(Camera camera, float age)
        {
            m_Camera = camera;
            FaceCamera();
            age = Mathf.Max(0, age);
            BurstStarted = age >= flashSeconds;
            IsFinished = age >= TotalImpactSeconds;
            VisibleStarCount = 0;
            if (grenadeRenderer != null)
            {
                grenadeRenderer.enabled = !BurstStarted;
                if (m_Properties == null) m_Properties = new MaterialPropertyBlock();
                m_Properties.SetFloat(FlashId, Mathf.SmoothStep(0, 1, age / flashSeconds));
                grenadeRenderer.SetPropertyBlock(m_Properties);
            }
            EnsureBatches();
            if (m_Mist == null || m_Glow == null) return;
            m_Mist.Clear(); m_Glow.Clear();
            float gap = BurstStarted ? CenterGapRadius : 0;
            m_Mist.SetCenterGap(gap); m_Glow.SetCenterGap(gap);
            if (!IsFinished)
            {
                if (BurstStarted) DrawBurst(age - flashSeconds);
                else
                {
                    float flash = Mathf.Clamp01(age / flashSeconds);
                    m_Glow.Quad(Vector3.zero, Vector2.one * (.45f + flash * .9f),
                        new Color(.86f, 1f, .70f, flash * .85f), 0, 0);
                }
            }
            m_Mist.Upload(); m_Glow.Upload();
        }

        void LateUpdate() => FaceCamera();

        void FaceCamera()
        {
            if (m_Camera != null) transform.rotation = m_Camera.transform.rotation;
        }

        void EnsureBatches()
        {
            if (m_Mist != null || mistMaterial == null || glowMaterial == null) return;
            m_Mist = new Batch(transform, "Pearlescent liquid and soft mist", mistMaterial, gameObject.layer, 4000);
            m_Glow = new Batch(transform, "Liquid bloom and delayed stars", glowMaterial, gameObject.layer, 2400);
        }

        void DrawBurst(float age)
        {
            float t = Mathf.Clamp01(age / burstSeconds);
            float fade = 1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.48f, 1f, t));
            float appear = Mathf.Clamp01(age / .045f);
            float scale = burstScale;
            // Small local billows preserve natural negative space between the emitters.
            for (int i = 0; i < SplashTips.Length; i++)
            {
                Vector2 tip = SplashTips[i];
                float progress = Mathf.Clamp01((age-Hash(i,3)*.09f)/(.49f+Hash(i,4)*.28f));
                float growth = 1-Mathf.Pow(1-progress,3);
                if (growth <= 0) continue;
                Vector3 origin = EmissionOrigin(tip,age,i);
                Color color = Palette[SplashColors[i]]; color.a = fade*appear;
                Color haze = color; haze.a *= .045f;
                Vector3 hazeCenter = (origin+LiquidPoint(tip,.55f*growth,age,i))*scale;
                m_Mist.Quad(hazeCenter+Vector3.forward*.18f,Vector2.one*.75f*scale,haze,0,i);
                float width = (i == 1 || i == 2 ? .20f : i < 5 ? .14f : .105f)*scale;
                // Later the roots pull away, leaving curved droplets moving through space.
                float tail = Mathf.SmoothStep(0,1,Mathf.InverseLerp(1.12f,2.6f,age))*.76f;
                width *= Mathf.Pow(1-tail,.78f);
                for (int j = 0; j < RibbonSegments; j++)
                {
                    float u0 = j/(float)RibbonSegments, u1 = (j+1f)/RibbonSegments;
                    float path0 = Mathf.Lerp(tail,growth,u0), path1 = Mathf.Lerp(tail,growth,u1);
                    Vector3 p0 = (origin+LiquidPoint(tip,path0,age,i))*scale;
                    Vector3 p1 = (origin+LiquidPoint(tip,path1,age,i))*scale;
                    Vector3 side0 = LiquidSide(tip,path0,age,i)*LiquidWidth(u0)*width;
                    Vector3 side1 = LiquidSide(tip,path1,age,i)*LiquidWidth(u1)*width;
                    m_Mist.Segment(p0,p1,side0,side1,color,u0,u1,i,4);
                    if (j%3 == 1)
                    {
                        Color halo = color; halo.a *= .19f;
                        float size = Mathf.Max(side0.magnitude*6,.035f*scale);
                        m_Glow.Quad(p0+Vector3.forward*.02f,Vector2.one*size,halo,0,i);
                    }
                }
            }
            // Detached, rounded liquid beads travel on individual curved trajectories.
            for (int i = 0; i < 19; i++)
            {
                float elapsed = age-.12f-Hash(i,5)*.25f;
                if (elapsed <= 0) continue;
                float travel = 1-Mathf.Exp(-elapsed*2.7f);
                float angle = Mathf.Lerp(-1.30f,1.30f,Hash(i,6));
                Vector2 destination = new Vector2(Mathf.Sin(angle),Mathf.Cos(angle))*(1.3f+Hash(i,7)*1.5f);
                Vector3 center = ((Vector3)EmissionOrigin(destination,age,i+10)+LiquidPoint(destination,travel,age,i+10))*scale;
                Vector3 direction = LiquidSide(destination,travel,age,i+10);
                direction = new Vector3(-direction.y,direction.x,0);
                Color color = Palette[(i+1)%Palette.Length]; color.a = fade*Mathf.Clamp01(elapsed*9);
                float radius = (.025f+Hash(i,8)*.055f)*scale;
                m_Mist.OrientedQuad(center,new Vector2(radius*2,radius*(i%4 == 0 ? 3.8f : 2.3f)),direction,color,5,i);
                color.a *= .34f;
                m_Glow.Quad(center,Vector2.one*radius*5,color,0,i);
            }
            // Stars appear after the liquid has flown out, each with its own flash envelope.
            for (int i = 0; i < StarCount; i++)
            {
                float starAge = age-StarDelay-Hash(i,9)*.85f;
                if (starAge <= 0) continue;
                float envelope = Mathf.Sin(Mathf.Clamp01(starAge/1.15f)*Mathf.PI);
                if (envelope <= .005f) continue;
                VisibleStarCount++;
                float x = (Hash(i,10)-.5f)*3.65f;
                float y = .24f+Hash(i,11)*2.45f;
                if (i == 0) { x = -.99f; y = .65f; }
                if (i == 1) { x = .86f; y = 1.68f; }
                Vector3 center = new Vector3(x,y+starAge*.045f,-.19f)*scale;
                Color color = Color.Lerp(Palette[i%Palette.Length],Color.white,i<5 ? .15f : .65f);
                color.a = fade*envelope;
                float size = (i<5 ? .24f+Hash(i,12)*.15f : .022f+Hash(i,13)*.044f)*scale;
                m_Glow.OrientedQuad(center,Vector2.one*size*(.65f+envelope*.35f),
                    new Vector3(Mathf.Sin(i*.8f)*.3f,1,0),color,i<5 ? 6 : 1,i);
            }
        }

        static Vector3 LiquidPoint(Vector2 tip, float u, float age, int seed)
        {
            float bend = (Hash(seed,2)-.5f)*.40f;
            Vector2 control1 = new Vector2(tip.x*.12f,tip.y*.52f);
            Vector2 control2 = new Vector2(tip.x*.58f+bend,tip.y*.84f);
            float v = 1-u;
            Vector2 point = 3*v*v*u*control1+3*v*u*u*control2+u*u*u*tip;
            point.y -= Mathf.Pow(Mathf.Max(0,age-1.15f),2)*.10f*u*u;
            return new Vector3(point.x,point.y,-.02f-Hash(seed,4)*.04f);
        }

        static float LiquidWidth(float u)
        {
            // Narrow liquid stem, generous round head, then a closed rounded tip.
            return Mathf.Pow(Mathf.Max(0,Mathf.Sin(u*Mathf.PI)),.53f)*(.18f+u*u*1.85f);
        }

        static Vector3 LiquidSide(Vector2 tip, float u, float age, int seed)
        {
            Vector3 tangent = LiquidPoint(tip,u+.001f,age,seed)-LiquidPoint(tip,u-.001f,age,seed);
            return Vector3.Cross(tangent.normalized,Vector3.forward);
        }

        Vector2 EmissionOrigin(Vector2 tip, float age, int seed)
        {
            float minimum = Mathf.Max(0,Mathf.Min(emissionRadiusRange.x,emissionRadiusRange.y));
            float maximum = Mathf.Max(minimum,Mathf.Max(emissionRadiusRange.x,emissionRadiusRange.y));
            float phase = Hash(seed,17)*Mathf.PI*2;
            float radius = Mathf.Lerp(minimum,maximum,
                Mathf.Clamp01(Hash(seed,18)+Mathf.Sin(age*.9f+phase)*.10f));
            float angle = Mathf.Atan2(tip.y,tip.x)+(Hash(seed,19)-.5f)*.12f;
            return new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius;
        }

        static float Hash(int index, int salt)
        {
            uint n = unchecked((uint)(index*374761393 + salt*668265263));
            n = (n ^ (n >> 13))*1274126177u;
            return (n & 0xffff)/65535f;
        }

        void OnDestroy() { m_Mist?.Dispose(); m_Glow?.Dispose(); }

        /// <summary>One reusable mesh per blend mode; no per-particle GameObjects or frame allocations.</summary>
        sealed class Batch
        {
            readonly Mesh m_Mesh;
            readonly MeshRenderer m_Renderer;
            readonly MaterialPropertyBlock m_Properties = new MaterialPropertyBlock();
            readonly List<Vector3> m_Vertices;
            readonly List<Vector2> m_Uv, m_Profile;
            readonly List<Color> m_Colors;
            readonly List<int> m_Triangles;

            public Batch(Transform parent, string name, Material material, int layer, int capacity)
            {
                m_Vertices = new List<Vector3>(capacity); m_Uv = new List<Vector2>(capacity);
                m_Profile = new List<Vector2>(capacity); m_Colors = new List<Color>(capacity);
                m_Triangles = new List<int>(capacity*2);
                var node = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
                node.layer = layer; node.transform.SetParent(parent, false);
                m_Mesh = new Mesh { name = name }; m_Mesh.MarkDynamic();
                node.GetComponent<MeshFilter>().sharedMesh = m_Mesh;
                var renderer = node.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material;
                m_Renderer = renderer;
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }

            public void Clear()
            { m_Vertices.Clear(); m_Uv.Clear(); m_Profile.Clear(); m_Colors.Clear(); m_Triangles.Clear(); }

            public void SetCenterGap(float radius)
            {
                m_Properties.SetFloat("_CenterGap",radius);
                m_Renderer.SetPropertyBlock(m_Properties);
            }

            public void Quad(Vector3 center, Vector2 size, Color color, float kind, float seed)
                => OrientedQuad(center,size,Vector3.up,color,kind,seed);

            public void OrientedQuad(Vector3 center, Vector2 size, Vector3 up, Color color, float kind, float seed)
            {
                up.Normalize();
                Vector3 x = new Vector3(up.y,-up.x,0)*(size.x*.5f), y = up*(size.y*.5f);
                Add(center-x-y, center+x-y, center+x+y, center-x+y, color, 0, 1, kind, seed);
            }

            public void Segment(Vector3 a, Vector3 b, Vector3 side0, Vector3 side1, Color color, float u0, float u1, float seed, float kind)
                => Add(a-side0, b-side1, b+side1, a+side0, color, u0, u1, kind, seed);

            void Add(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color, float u0, float u1, float kind, float seed)
            {
                int start = m_Vertices.Count;
                m_Vertices.Add(a); m_Vertices.Add(b); m_Vertices.Add(c); m_Vertices.Add(d);
                m_Uv.Add(new Vector2(u0,0)); m_Uv.Add(new Vector2(u1,0));
                m_Uv.Add(new Vector2(u1,1)); m_Uv.Add(new Vector2(u0,1));
                for (int i = 0; i < 4; i++) { m_Colors.Add(color); m_Profile.Add(new Vector2(kind,seed)); }
                m_Triangles.Add(start); m_Triangles.Add(start+1); m_Triangles.Add(start+2);
                m_Triangles.Add(start); m_Triangles.Add(start+2); m_Triangles.Add(start+3);
            }

            public void Upload()
            {
                m_Mesh.Clear(); m_Mesh.SetVertices(m_Vertices); m_Mesh.SetUVs(0,m_Uv);
                m_Mesh.SetUVs(1,m_Profile); m_Mesh.SetColors(m_Colors); m_Mesh.SetTriangles(m_Triangles,0);
                m_Mesh.RecalculateBounds();
            }

            public void Dispose()
            {
                if (Application.isPlaying) Destroy(m_Mesh); else DestroyImmediate(m_Mesh);
            }
        }
    }
}
