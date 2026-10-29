using UnityEngine;

namespace EarthOL.Deform
{
    /// <summary>
    /// 让一个物体"可以被戳出凹陷、松手像果冻一样弹回来"。
    ///
    /// 两件事同时发生：
    /// 1) 局部凹陷：把戳点交给 EarthOL/PokeDeform 着色器，在表面压出一个平滑的坑；
    /// 2) 整体挤压：按体积守恒沿受力方向短暂压扁再弹回。
    /// 两者的强度都跑一个弹簧模型，阻尼比 &lt; 1 时回弹会过冲（向外出鼓），就是果冻感。
    ///
    /// 配合 PokeInput.cs 使用；物体上要有 Collider（Vuforia 丢失跟踪时会自动禁用，正好天然隔离）。
    /// </summary>
    [AddComponentMenu("Earth OL/Poke Deformer (戳形变)")]
    public class PokeDeformer : MonoBehaviour, IPokeTarget
    {
        /// <summary>着色器里最多处理 4 个戳点。</summary>
        public const int MaxDents = 4;

        // 手动触发（PokeAt / 右键菜单测试）时用的假手指 id
        const int kManualPointerId = -12345;

        [Header("凹陷")]
        [Tooltip("凹陷半径，相对物体最大尺寸的比例。0.4 ≈ 半径 40% 物体尺寸")]
        [Range(0.05f, 2f)] public float dentRadiusRatio = 0.40f;

        [Tooltip("最大凹陷深度，相对物体最大尺寸的比例。越大戳得越深")]
        [Range(0.01f, 0.9f)] public float maxDepthRatio = 0.18f;

        [Tooltip("同时显示的凹陷数量上限（Shader 最多支持 4 个）")]
        [Range(1, MaxDents)] public int maxDents = MaxDents;

        [Header("回弹（弹簧）")]
        [Tooltip("回弹频率（Hz）。越大回弹越快、越硬")]
        [Range(0.2f, 12f)] public float recoveryFrequency = 2.8f;

        [Tooltip("阻尼比。小于 1 会过冲（果冻感），等于/大于 1 平滑无过冲")]
        [Range(0.05f, 2f)] public float dampingRatio = 0.32f;

        [Header("整体挤压")]
        public bool enableSquash = true;

        [Tooltip("挤压强度：沿戳的方向最多压扁多少")]
        [Range(0f, 0.5f)] public float squashAmount = 0.14f;

        [Range(0.2f, 12f)] public float squashFrequency = 3.4f;

        [Range(0.05f, 2f)] public float squashDamping = 0.45f;

        [Header("网格 / 材质")]
        [Tooltip("Unity 自带 Cube 只有 24 个顶点，戳不出凹陷 —— 选 RoundedCube 换成高密度圆角方块。\n" +
                 "Sphere 替换为细分球。KeepOriginal 表示保留原网格。")]
        public SoftMeshShape softMesh = SoftMeshShape.KeepOriginal;

        [Tooltip("细分等级：圆角方块每面 (2 + 3×等级) 段，球为 4^等级 倍三角面")]
        [Range(0, 4)] public int softMeshLevel = 3;

        [Tooltip("圆角程度：0 = 硬边方块，1 = 完全球化")]
        [Range(0f, 0.9f)] public float rounding = 0.30f;

        [Tooltip("留空则使用 Renderer 上已指定的材质。材质必须使用 EarthOL/PokeDeform 着色器")]
        public Material deformMaterial;

        [Header("反馈")]
        [Tooltip("戳中时震动。需要 Android 播放器设置里勾选 VIBRATE 权限")]
        public bool vibrateOnPoke = false;

        [Range(0f, 1f)] public float vibrateCooldown = 0.12f;

        [Header("调试")]
        [Tooltip("启动时打印一次「网格 / 材质 / 尺寸」，排查戳不出凹陷时很有用")]
        public bool logStartupInfo = true;

        struct Dent
        {
            public bool used;
            public bool held;          // 手指还按着 -> 固定不弹回
            public int pointerId;
            public Vector3 worldPoint;
            public Vector3 worldNormal;
            public float strength;     // 弹簧位移量：1 = 满深度，<0 = 向外出鼓
            public float velocity;
        }

        Dent[] m_Dents;

        // 整体挤压用的弹簧
        Vector3 m_SquashAxis = Vector3.up;
        float m_SquashStrength;
        float m_SquashVelocity;

        Renderer m_Renderer;
        MeshFilter m_MeshFilter;
        Mesh m_GeneratedMesh;
        MaterialPropertyBlock m_Block;

        Vector3 m_BaseScale = Vector3.one;
        float m_WorldSize = 0.1f;   // 物体最大外接尺寸（米）
        float m_AvgScale = 1f;      // 平均缩放，用于世界空间 <-> 物体空间换算
        bool m_ShaderReady;
        bool m_WasActive;
        bool m_Initialized;
        float m_LastVibrateTime = -99f;

        static readonly int[] s_PokePosIds =
        {
            Shader.PropertyToID("_PokePos0"), Shader.PropertyToID("_PokePos1"),
            Shader.PropertyToID("_PokePos2"), Shader.PropertyToID("_PokePos3")
        };

        static readonly int[] s_PokeAmpIds =
        {
            Shader.PropertyToID("_PokeAmp0"), Shader.PropertyToID("_PokeAmp1"),
            Shader.PropertyToID("_PokeAmp2"), Shader.PropertyToID("_PokeAmp3")
        };

        void Awake()
        {
            m_Dents = new Dent[MaxDents];
            m_Block = new MaterialPropertyBlock();
            m_BaseScale = transform.localScale;
            m_Renderer = GetComponent<Renderer>();
            m_MeshFilter = GetComponent<MeshFilter>();

            ReplaceMesh();
            Measure();
            PrepareMaterial();
            PushToShader(force: true);

            m_Initialized = true;

            if (logStartupInfo) LogStartupInfo();
        }

        void LogStartupInfo()
        {
            var material = m_Renderer != null ? m_Renderer.sharedMaterial : null;
            var mesh = m_MeshFilter != null ? m_MeshFilter.sharedMesh : null;

            string meshInfo = mesh != null ? mesh.name + "(" + mesh.vertexCount + " 顶点)" : "无网格";
            string shaderInfo = material != null && material.shader != null ? material.shader.name : "无着色器";
            string materialInfo = material != null ? material.name : "无材质";
            string colorInfo = material != null && material.HasProperty("_Color")
                ? material.GetColor("_Color").ToString()
                : "n/a";

            Debug.Log($"[PokeDeformer] {name} | 网格={meshInfo} | 着色器={shaderInfo} | 材质={materialInfo} " +
                      $"| 颜色={colorInfo} | 尺寸={m_WorldSize:F3}m | 缩放={transform.localScale} " +
                      $"| 局部位置={transform.localPosition} | 形变就绪={m_ShaderReady}");
        }

        void OnDisable()
        {
            // 只有真正跑过 Awake 才能恢复缩放，否则（编辑器里重编译域时）会用默认值把物体缩放改坏
            if (!m_Initialized) return;

            ClearAllDents();
            m_SquashStrength = 0f;
            m_SquashVelocity = 0f;
            transform.localScale = m_BaseScale;
        }

        void OnDestroy()
        {
            if (m_GeneratedMesh != null) Destroy(m_GeneratedMesh);
        }

        // ---------------------------------------------------------------- 初始化

        void ReplaceMesh()
        {
            if (softMesh == SoftMeshShape.KeepOriginal || m_MeshFilter == null) return;

            m_GeneratedMesh = SoftMesh.Create(softMesh, softMeshLevel, rounding);
            if (m_GeneratedMesh != null) m_MeshFilter.sharedMesh = m_GeneratedMesh;
        }

        void Measure()
        {
            var lossy = transform.lossyScale;
            m_AvgScale = Mathf.Max(1e-4f, (Mathf.Abs(lossy.x) + Mathf.Abs(lossy.y) + Mathf.Abs(lossy.z)) / 3f);

            // 优先用 Collider 的尺寸：它才是"手指能碰到的范围"，形变的尺度跟着它最直观
            m_WorldSize = 0f;

            var collider = GetComponent<Collider>();
            if (collider != null)
            {
                var size = collider.bounds.size;
                m_WorldSize = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            }

            if (m_WorldSize <= 1e-3f && m_Renderer != null)
            {
                var size = m_Renderer.bounds.size;
                m_WorldSize = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            }

            // 兜底：Collider / Renderer 都被禁用时，用网格自身尺寸估算，
            // 否则凹陷半径会退化成一个点，戳上去看不出变化
            if (m_WorldSize <= 1e-3f && m_MeshFilter != null && m_MeshFilter.sharedMesh != null)
            {
                var size = m_MeshFilter.sharedMesh.bounds.size;
                m_WorldSize = Mathf.Max(
                    Mathf.Abs(size.x * lossy.x),
                    Mathf.Max(Mathf.Abs(size.y * lossy.y), Mathf.Abs(size.z * lossy.z)));
            }

            m_WorldSize = Mathf.Max(1e-4f, m_WorldSize);
        }

        void PrepareMaterial()
        {
            if (m_Renderer == null) return;

            if (deformMaterial != null) m_Renderer.sharedMaterial = deformMaterial;

            var material = m_Renderer.sharedMaterial;
            if (material != null && material.HasProperty(s_PokePosIds[0]))
            {
                m_ShaderReady = true;
                return;
            }

            // 兜底：编辑期 Shader.Find 可用；打包后若着色器没被任何材质引用会被剔除，所以正常流程应该已经配好材质
            var shader = Shader.Find("EarthOL/PokeDeform");
            if (shader == null)
            {
                Debug.LogWarning($"[PokeDeformer] {name} 的材质不是 EarthOL/PokeDeform，戳不出凹陷。" +
                                 "请把物体材质换成该着色器的材质，或指定 Deform Material。", this);
                return;
            }

            var fallback = new Material(shader) { name = "PokeDeform (runtime)" };
            if (material != null && material.HasProperty("_Color")) fallback.SetColor("_Color", material.color);
            m_Renderer.material = fallback;
            m_ShaderReady = true;

            Debug.LogWarning($"[PokeDeformer] {name} 的材质没有形变属性，已在运行时临时换成 EarthOL/PokeDeform。", this);
        }

        // ---------------------------------------------------------------- IPokeTarget

        /// <summary>调试用：打开后每次戳击会打一条日志，方便在真机上确认触摸链路是否打通。</summary>
        public static bool LogPokes;

        public void OnPokeBegin(int pointerId, RaycastHit hit)
        {
            SpawnDent(pointerId, hit.point, hit.normal, hold: true);
            PlayVibration();

            if (LogPokes)
            {
                float amplitude = maxDepthRatio * m_WorldSize / m_AvgScale;
                Debug.Log($"[Poke] begin id={pointerId} point={hit.point} " +
                          $"radius={dentRadiusRatio * m_WorldSize / m_AvgScale:F3} amp={amplitude:F3} shaderReady={m_ShaderReady}");
            }
        }

        public void OnPokeDrag(int pointerId, RaycastHit hit)
        {
            for (int i = 0; i < MaxDents; i++)
            {
                if (!m_Dents[i].used || !m_Dents[i].held || m_Dents[i].pointerId != pointerId) continue;

                m_Dents[i].worldPoint = hit.point;
                m_Dents[i].worldNormal = hit.normal;
                m_SquashAxis = hit.normal;
                return;
            }
        }

        public void OnPokeEnd(int pointerId)
        {
            for (int i = 0; i < MaxDents; i++)
            {
                if (!m_Dents[i].used || m_Dents[i].pointerId != pointerId) continue;
                m_Dents[i].held = false;   // 交给弹簧回弹

                if (LogPokes) Debug.Log($"[Poke] end id={pointerId}，开始回弹");
            }
        }

        /// <summary>脚本里直接戳一下（世界坐标 + 表面法线），不需要触摸屏。</summary>
        public void PokeAt(Vector3 worldPoint, Vector3 worldNormal)
        {
            SpawnDent(kManualPointerId, worldPoint, worldNormal, hold: false);
        }

        void SpawnDent(int pointerId, Vector3 worldPoint, Vector3 worldNormal, bool hold)
        {
            if (m_Dents == null) m_Dents = new Dent[MaxDents];

            int slot = FindFreeSlot();
            if (slot < 0) slot = FindWeakestSlot();

            m_Dents[slot] = new Dent
            {
                used = true,
                held = hold,
                pointerId = pointerId,
                worldPoint = worldPoint,
                worldNormal = worldNormal,
                strength = 1f,
                velocity = 0f
            };

            m_SquashAxis = worldNormal;
            m_SquashStrength = 1f;
            m_SquashVelocity = 0f;
        }

        int FindFreeSlot()
        {
            for (int i = 0; i < MaxDents; i++)
                if (!m_Dents[i].used) return i;
            return -1;
        }

        int FindWeakestSlot()
        {
            int best = 0;
            float bestMagnitude = float.MaxValue;

            for (int i = 0; i < MaxDents; i++)
            {
                if (m_Dents[i].held) continue;
                float magnitude = Mathf.Abs(m_Dents[i].strength);
                if (magnitude < bestMagnitude)
                {
                    bestMagnitude = magnitude;
                    best = i;
                }
            }

            return best;
        }

        void ClearAllDents()
        {
            if (m_Dents == null) return;
            for (int i = 0; i < m_Dents.Length; i++) m_Dents[i] = default;
        }

        void PlayVibration()
        {
            if (!vibrateOnPoke) return;
            if (Time.unscaledTime - m_LastVibrateTime < vibrateCooldown) return;

            m_LastVibrateTime = Time.unscaledTime;
            Handheld.Vibrate();
        }

        // ---------------------------------------------------------------- 每帧模拟

        // 放在 LateUpdate：Vuforia 会在这个阶段之前把 ImageTarget 的位姿更新好，形变不会滞后一帧
        void LateUpdate()
        {
            // 材质可能是运行时才补上的（见 PokeDeformBootstrap），这里延迟再确认一次
            if (!m_ShaderReady) PrepareMaterial();

            float deltaTime = Mathf.Min(Time.deltaTime, 1f / 30f);   // 掉帧时别让弹簧发散
            if (deltaTime <= 0f) return;

            UpdateDents(deltaTime);
            UpdateSquash(deltaTime);
            PushToShader(force: false);
        }

        void UpdateDents(float deltaTime)
        {
            float omega = 2f * Mathf.PI * Mathf.Max(0.2f, recoveryFrequency);
            float stiffness = omega * omega;
            float damping = 2f * Mathf.Max(0.05f, dampingRatio) * omega;

            for (int i = 0; i < MaxDents; i++)
            {
                if (!m_Dents[i].used) continue;

                if (m_Dents[i].held)
                {
                    m_Dents[i].strength = 1f;
                    m_Dents[i].velocity = 0f;
                    continue;
                }

                // 半隐式欧拉积分一个阻尼弹簧，目标是 0
                m_Dents[i].velocity += (-stiffness * m_Dents[i].strength - damping * m_Dents[i].velocity) * deltaTime;
                m_Dents[i].strength += m_Dents[i].velocity * deltaTime;

                m_Dents[i].strength = Mathf.Clamp(m_Dents[i].strength, -1.5f, 1.5f);

                if (Mathf.Abs(m_Dents[i].strength) < 0.004f && Mathf.Abs(m_Dents[i].velocity) < 0.02f)
                    m_Dents[i] = default;
            }
        }

        void UpdateSquash(float deltaTime)
        {
            if (!enableSquash)
            {
                m_SquashStrength = 0f;
                m_SquashVelocity = 0f;
                if (transform.localScale != m_BaseScale) transform.localScale = m_BaseScale;
                return;
            }

            float omega = 2f * Mathf.PI * Mathf.Max(0.2f, squashFrequency);
            float stiffness = omega * omega;
            float damping = 2f * Mathf.Max(0.05f, squashDamping) * omega;

            m_SquashVelocity += (-stiffness * m_SquashStrength - damping * m_SquashVelocity) * deltaTime;
            m_SquashStrength += m_SquashVelocity * deltaTime;

            if (Mathf.Abs(m_SquashStrength) < 0.002f && Mathf.Abs(m_SquashVelocity) < 0.02f)
            {
                m_SquashStrength = 0f;
                m_SquashVelocity = 0f;
            }

            if (m_SquashStrength == 0f)
            {
                if (transform.localScale != m_BaseScale) transform.localScale = m_BaseScale;
                return;
            }

            // 把"受力轴"转到本地空间，取各轴占比
            var axis = transform.InverseTransformDirection(m_SquashAxis);
            axis = new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z));

            float sum = axis.x + axis.y + axis.z;
            axis = sum < 1e-5f ? new Vector3(1f / 3f, 1f / 3f, 1f / 3f) : axis / sum;

            float amount = squashAmount * Mathf.Clamp(m_SquashStrength, -1.5f, 1.5f);

            // 沿受力轴压扁，垂直方向按体积守恒膨胀
            float expand = Mathf.Sqrt(Mathf.Abs(1f + amount)) - 1f;
            var perpendicular = Vector3.one - axis;
            var scale = Vector3.one - axis * amount + perpendicular * expand;

            transform.localScale = Vector3.Scale(m_BaseScale, scale);   // 逐分量相乘
        }

        void PushToShader(bool force)
        {
            if (m_Renderer == null || m_Block == null) return;

            bool anyActive = false;
            for (int i = 0; i < MaxDents; i++)
            {
                if (i < maxDents && m_Dents != null && m_Dents[i].used && Mathf.Abs(m_Dents[i].strength) > 1e-5f)
                {
                    anyActive = true;
                    break;
                }
            }

            // 没有活动凹陷、上一帧也没有，就不用重复刷 MaterialPropertyBlock
            if (!anyActive && !m_WasActive && !force) return;
            if (!m_ShaderReady) return;

            float radiusWorld = dentRadiusRatio * m_WorldSize;
            float depthWorld = maxDepthRatio * m_WorldSize;

            for (int i = 0; i < MaxDents; i++)
            {
                var position = Vector4.zero;
                float amplitude = 0f;

                if (i < maxDents && m_Dents != null && m_Dents[i].used)
                {
                    var dent = m_Dents[i];
                    var localPoint = transform.InverseTransformPoint(dent.worldPoint);

                    position = new Vector4(localPoint.x, localPoint.y, localPoint.z, radiusWorld / m_AvgScale);
                    amplitude = dent.strength * depthWorld / m_AvgScale;
                }

                m_Block.SetVector(s_PokePosIds[i], position);
                m_Block.SetFloat(s_PokeAmpIds[i], amplitude);
            }

            m_Renderer.SetPropertyBlock(m_Block);
            m_WasActive = anyActive;
        }

        // ---------------------------------------------------------------- 调试

        [ContextMenu("测试：戳一下（Play 模式）")]
        public void TestPoke()
        {
            var bounds = m_Renderer != null
                ? m_Renderer.bounds
                : new Bounds(transform.position, Vector3.one * 0.1f);

            var cam = Camera.main;
            var direction = cam != null ? (bounds.center - cam.transform.position).normalized : -transform.forward;

            PokeAt(bounds.center + direction * bounds.extents.magnitude * 0.6f, direction);
        }
    }
}
