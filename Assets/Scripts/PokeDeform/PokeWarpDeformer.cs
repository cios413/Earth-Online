using UnityEngine;

namespace EarthOL.Deform
{
    /// <summary>
    /// 让"被识别的物体本身"形变 —— 直接把识别图那张卡片（ImageTarget 自己的平面）变形成可拉伸的软片。
    ///
    /// 做法：在 ImageTarget 物体上生成一张带识别图贴图的高密度平面，
    /// 戳它时按戳点做面内局部拉伸（画面被拉开 / 压皱）+ 轻微向外鼓起，松手弹回。
    ///
    /// 注意：网格是运行时生成的，所以编辑期 Scene 视图里看不到卡片，运行后才出现。
    /// </summary>
    [AddComponentMenu("Earth OL/Poke Warp (识别图卡片形变)")]
    public class PokeWarpDeformer : MonoBehaviour, IPokeTarget
    {
        public const int MaxPokes = 4;

        [Header("卡片尺寸（米，和 ImageTarget 的宽高一致）")]
        public float planeWidth = 0.1125f;
        public float planeHeight = 0.2f;

        [Header("网格密度（越密越顺滑）")]
        [Range(4, 48)] public int gridX = 16;
        [Range(4, 64)] public int gridY = 28;

        [Header("识别图")]
        [Tooltip("勾上才会把识别图画面贴到卡片上（会和摄像头投影同时生效）")]
        public bool useTargetTexture = false;

        [Tooltip("把摄像头实时画面投影到卡片上 —— 弯起来时看起来是物理卡片本身在形变")]
        public bool useCameraFeed = true;

        [Tooltip("平面躺在 XZ 上（和卡片平行）。若看到卡片是竖着的，把这个关掉改用 XY")]
        public bool planeInXZ = true;

        public Texture2D targetTexture;
        public Material warpMaterial;

        [Tooltip("关掉摄像头投影时的纯色板颜色")]
        public Color cardColor = new Color(0.86f, 0.84f, 0.80f, 1f);

        [Header("形变强度")]
        [Tooltip("戳点处画面最多放大多少（0.45 = 局部放大 45%；回弹时会变负 = 局部压缩）")]
        [Range(0.05f, 1.5f)] public float stretchAmount = 0.45f;

        [Tooltip("向外鼓起的最大高度（米）")]
        [Range(0f, 0.05f)] public float bulgeAmount = 0.012f;

        [Tooltip("影响半径（米）。0 = 自动取短边的 45%")]
        public float pokeRadius = 0f;

        [Header("回弹弹簧")]
        [Range(0.2f, 12f)] public float frequency = 2.6f;

        [Tooltip("阻尼比 < 1 会过冲，画面会先放大再回缩抖一下")]
        [Range(0.05f, 2f)] public float dampingRatio = 0.30f;

        [Header("调试")]
        public bool logPokes = true;

        struct Poke
        {
            public bool used;
            public bool held;
            public Vector3 worldPoint;
            public float strength;
            public float velocity;
        }

        Poke[] m_Pokes;
        MeshFilter m_Filter;
        MeshRenderer m_Renderer;
        MeshCollider m_Collider;
        Mesh m_Mesh;
        MaterialPropertyBlock m_Block;
        Vector3 m_BaseScale = Vector3.one;
        float m_AvgScale = 1f;
        bool m_Ready;
        bool m_WasActive;

        static readonly int[] s_PosIds =
        {
            Shader.PropertyToID("_PokePos0"), Shader.PropertyToID("_PokePos1"),
            Shader.PropertyToID("_PokePos2"), Shader.PropertyToID("_PokePos3")
        };

        static readonly int[] s_AmpIds =
        {
            Shader.PropertyToID("_PokeAmp0"), Shader.PropertyToID("_PokeAmp1"),
            Shader.PropertyToID("_PokeAmp2"), Shader.PropertyToID("_PokeAmp3")
        };

        static readonly int[] s_BulgeIds =
        {
            Shader.PropertyToID("_PokeBulge0"), Shader.PropertyToID("_PokeBulge1"),
            Shader.PropertyToID("_PokeBulge2"), Shader.PropertyToID("_PokeBulge3")
        };

        void Awake()
        {
            m_Pokes = new Poke[MaxPokes];
            m_Block = new MaterialPropertyBlock();
            m_BaseScale = transform.localScale;

            var lossy = transform.lossyScale;
            m_AvgScale = Mathf.Max(1e-4f, (Mathf.Abs(lossy.x) + Mathf.Abs(lossy.y) + Mathf.Abs(lossy.z)) / 3f);

            BuildCard();

            m_Ready = m_Renderer != null && m_Renderer.sharedMaterial != null &&
                      m_Renderer.sharedMaterial.HasProperty(s_PosIds[0]);

            if (logPokes)
            {
                Debug.Log($"[Warp] {name} 卡片就绪={m_Ready} 尺寸={planeWidth:F3}x{planeHeight:F3}m " +
                          $"网格={(m_Mesh != null ? m_Mesh.vertexCount : 0)} 顶点 贴图={(targetTexture != null ? targetTexture.name : "无")} " +
                          $"世界位置={transform.position} 缩放={transform.lossyScale}");
            }
        }

        void OnDestroy()
        {
            if (m_Mesh != null) Destroy(m_Mesh);
        }

        void OnDisable()
        {
            if (m_Pokes == null) return;

            for (int i = 0; i < m_Pokes.Length; i++) m_Pokes[i] = default;
            if (m_Renderer != null) PushToShader(true);
        }

        // ---------------------------------------------------------------- 建卡片

        void BuildCard()
        {
            m_Filter = GetComponent<MeshFilter>();
            if (m_Filter == null) m_Filter = gameObject.AddComponent<MeshFilter>();

            m_Renderer = GetComponent<MeshRenderer>();
            if (m_Renderer == null) m_Renderer = gameObject.AddComponent<MeshRenderer>();

            m_Mesh = CreatePlane(planeWidth, planeHeight, gridX, gridY, planeInXZ);
            m_Filter.sharedMesh = m_Mesh;

            m_Collider = GetComponent<MeshCollider>();
            if (m_Collider == null) m_Collider = gameObject.AddComponent<MeshCollider>();
            m_Collider.sharedMesh = m_Mesh;

            // 材质：优先用 Resources 里的（保证一定进包），否则运行时按着色器建一个
            Material material = warpMaterial;
            if (material == null) material = Resources.Load<Material>("PokeWarp");
            if (material == null)
            {
                var shader = Shader.Find("EarthOL/PokeWarp");
                if (shader != null) material = new Material(shader) { name = "PokeWarp (runtime)" };
            }

            if (material != null)
            {
                material = new Material(material) { name = material.name + " (instance)" };

                // 投影摄像头实时画面：卡片表面显示"物理卡片原本那个位置的相机画面"，
                // 所以卡片一弯，看起来就是物理卡片本身在弯。
                material.SetFloat("_UseCamera", useCameraFeed ? 1f : 0f);
                material.SetColor("_Color", useCameraFeed ? Color.white : cardColor);

                if (useTargetTexture && targetTexture != null)
                    material.SetTexture("_MainTex", targetTexture);

                m_Renderer.sharedMaterial = material;
            }
            else
            {
                Debug.LogWarning("[Warp] 找不到 EarthOL/PokeWarp 材质，卡片将不可见。请执行菜单 Tools → Earth OL → ① 配置当前场景。");
            }
        }

        /// <summary>
        /// 生成和识别图平面平行的高密度网格。
        /// Vuforia 的 ImageTarget 里卡片是躺在水平面上的（法线朝上），
        /// 所以默认把平面建在 XZ 平面、法线 +Y；planeInXZ=false 时退回 XY 平面（法线 +Z）。
        /// </summary>
        static Mesh CreatePlane(float width, float height, int nx, int ny, bool planeInXZ)
        {
            int cols = Mathf.Max(2, nx);
            int rows = Mathf.Max(2, ny);

            var vertices = new Vector3[(cols + 1) * (rows + 1)];
            var normals = new Vector3[vertices.Length];
            var uvs = new Vector2[vertices.Length];

            var normal = planeInXZ ? Vector3.up : Vector3.forward;

            for (int y = 0; y <= rows; y++)
            {
                for (int x = 0; x <= cols; x++)
                {
                    int i = y * (cols + 1) + x;
                    float u = (float)x / cols;
                    float v = (float)y / rows;

                    float px = (u - 0.5f) * width;
                    float pz = (v - 0.5f) * height;

                    vertices[i] = planeInXZ ? new Vector3(px, 0f, pz) : new Vector3(px, pz, 0f);
                    normals[i] = normal;
                    uvs[i] = new Vector2(u, v);
                }
            }

            var triangles = new int[cols * rows * 6];
            int t = 0;

            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    int i = y * (cols + 1) + x;

                    triangles[t++] = i;
                    triangles[t++] = i + cols + 1;
                    triangles[t++] = i + cols + 2;

                    triangles[t++] = i;
                    triangles[t++] = i + cols + 2;
                    triangles[t++] = i + 1;
                }
            }

            var mesh = new Mesh { name = "PokeWarpCard" };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        // ---------------------------------------------------------------- IPokeTarget

        public void OnPokeBegin(int pointerId, RaycastHit hit)
        {
            int slot = FindFreeSlot();
            m_Pokes[slot] = new Poke
            {
                used = true,
                held = true,
                worldPoint = hit.point,
                strength = 1f,
                velocity = 0f
            };

            PushToShader(true);

            if (logPokes)
                Debug.Log($"[Warp] begin id={pointerId} point={hit.point} 强度={stretchAmount} ");
        }

        public void OnPokeDrag(int pointerId, RaycastHit hit)
        {
            for (int i = 0; i < MaxPokes; i++)
            {
                if (!m_Pokes[i].used || !m_Pokes[i].held) continue;
                m_Pokes[i].worldPoint = hit.point;
                return;
            }
        }

        public void OnPokeEnd(int pointerId)
        {
            for (int i = 0; i < MaxPokes; i++)
            {
                if (!m_Pokes[i].used || !m_Pokes[i].held) continue;
                m_Pokes[i].held = false;

                if (logPokes) Debug.Log($"[Warp] end id={pointerId} 开始回弹");
                return;
            }
        }

        int FindFreeSlot()
        {
            for (int i = 0; i < MaxPokes; i++)
                if (!m_Pokes[i].used) return i;

            // 都占满了就抢最弱的那个
            int weakest = 0;
            float best = float.MaxValue;

            for (int i = 0; i < MaxPokes; i++)
            {
                if (m_Pokes[i].held) continue;
                float magnitude = Mathf.Abs(m_Pokes[i].strength);
                if (magnitude < best)
                {
                    best = magnitude;
                    weakest = i;
                }
            }

            return weakest;
        }

        // ---------------------------------------------------------------- 每帧

        void LateUpdate()
        {
            // 卡片只有在"目标被识别"时才显示：
            // Vuforia 的 DefaultObserverEventHandler 会同步开关目标下的 Collider，
            // 所以这里直接跟随自己的 Collider 状态，和方块/球的显隐保持一致。
            if (m_Renderer != null && m_Collider != null) m_Renderer.enabled = m_Collider.enabled;

            float deltaTime = Mathf.Min(Time.deltaTime, 1f / 30f);
            if (deltaTime <= 0f) return;

            float omega = 2f * Mathf.PI * Mathf.Max(0.2f, frequency);
            float stiffness = omega * omega;
            float damping = 2f * Mathf.Max(0.05f, dampingRatio) * omega;

            for (int i = 0; i < MaxPokes; i++)
            {
                if (!m_Pokes[i].used) continue;

                if (m_Pokes[i].held)
                {
                    m_Pokes[i].strength = 1f;
                    m_Pokes[i].velocity = 0f;
                    continue;
                }

                m_Pokes[i].velocity += (-stiffness * m_Pokes[i].strength - damping * m_Pokes[i].velocity) * deltaTime;
                m_Pokes[i].strength += m_Pokes[i].velocity * deltaTime;
                m_Pokes[i].strength = Mathf.Clamp(m_Pokes[i].strength, -1.5f, 1.5f);

                if (Mathf.Abs(m_Pokes[i].strength) < 0.004f && Mathf.Abs(m_Pokes[i].velocity) < 0.02f)
                    m_Pokes[i] = default;
            }

            PushToShader(false);
        }

        void PushToShader(bool force)
        {
            if (m_Renderer == null || m_Block == null || m_Pokes == null) return;
            if (!m_Ready)
            {
                var material = m_Renderer.sharedMaterial;
                m_Ready = material != null && material.HasProperty(s_PosIds[0]);
                if (!m_Ready) return;
            }

            bool anyActive = false;

            for (int i = 0; i < MaxPokes; i++)
            {
                if (m_Pokes[i].used && Mathf.Abs(m_Pokes[i].strength) > 1e-5f)
                {
                    anyActive = true;
                    break;
                }
            }

            if (!anyActive && !m_WasActive && !force) return;

            float radiusWorld = pokeRadius > 0f ? pokeRadius : 0.45f * Mathf.Min(planeWidth, planeHeight);
            float radiusObject = radiusWorld / m_AvgScale;

            for (int i = 0; i < MaxPokes; i++)
            {
                var position = Vector4.zero;
                float stretch = 0f;
                float bulge = 0f;

                if (m_Pokes[i].used)
                {
                    var local = transform.InverseTransformPoint(m_Pokes[i].worldPoint);
                    position = new Vector4(local.x, local.y, local.z, radiusObject);
                    stretch = m_Pokes[i].strength * stretchAmount;
                    bulge = m_Pokes[i].strength * bulgeAmount / m_AvgScale;
                }

                m_Block.SetVector(s_PosIds[i], position);
                m_Block.SetFloat(s_AmpIds[i], stretch);
                m_Block.SetFloat(s_BulgeIds[i], bulge);
            }

            m_Renderer.SetPropertyBlock(m_Block);
            m_WasActive = anyActive;
        }

        // ---------------------------------------------------------------- 调试

        [ContextMenu("测试：拉一下卡片（Play 模式）")]
        public void TestWarp()
        {
            var point = transform.TransformPoint(Vector3.zero);
            var camera = Camera.main;
            var normal = camera != null ? (point - camera.transform.position).normalized : transform.forward;

            int slot = FindFreeSlot();
            m_Pokes[slot] = new Poke { used = true, held = false, worldPoint = point, strength = 1f };
            PushToShader(true);
        }
    }
}
