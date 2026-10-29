using UnityEngine;

namespace EarthOL.Throw
{
    /// <summary>
    /// 投掷模式的运行时搭建：一个"目标方块"（运动学 Rigidbody + 子物体 BoxCollider）
    /// 加一个 ProjectileSpawner。全部用基本体在运行时创建，不依赖任何 Prefab 资源。
    ///
    /// 目标方块放在相机前方 1.5 m 的固定世界坐标上（和原仓库一致：不要求识别平面）。
    /// </summary>
    public sealed class ThrowModeRuntime : MonoBehaviour
    {
        public const float TargetDistance = 1.5f;

        public ProjectileSpawner Spawner { get; private set; }
        public Transform Target { get; private set; }

        GameObject m_Root;
        bool m_Built;
        Transform m_PreferredTarget;

        /// <summary>指定发射目标（选中的物体）。传 null 表示用默认目标方块。</summary>
        public void SetTarget(Transform target)
        {
            m_PreferredTarget = target;

            if (Spawner != null)
                Spawner.SelectedTarget = target != null ? target : Target;
        }

        public void SetActive(bool active)
        {
            if (active && !m_Built) Build();

            if (m_Root != null) m_Root.SetActive(active);
            if (Spawner != null) Spawner.enabled = active;

            if (!active) ReturnProjectiles();
        }

        void Build()
        {
            var camera = Camera.main;
            Vector3 origin = camera != null ? camera.transform.position : Vector3.zero;
            Vector3 forward = camera != null ? camera.transform.forward : Vector3.forward;

            // 目标根节点：运动学刚体，外部可随时改它的位姿
            var target = new GameObject("ThrowTarget");
            target.transform.SetPositionAndRotation(origin + forward * TargetDistance, Quaternion.identity);

            var body = target.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            // 外观与碰撞形状（0.6 × 0.4 × 0.2 m），挂在子节点上，和原仓库结构一致
            var shape = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shape.name = "TargetShape";
            shape.transform.SetParent(target.transform, false);
            shape.transform.localScale = new Vector3(0.6f, 0.4f, 0.2f);

            var shapeRenderer = shape.GetComponent<MeshRenderer>();
            if (shapeRenderer != null)
            {
                var material = Resources.Load<Material>("PokeJelly_Teal");
                if (material != null) shapeRenderer.sharedMaterial = material;
            }

            // Spawner
            var spawnerObject = new GameObject("ProjectileSpawner");
            spawnerObject.transform.SetParent(target.transform, false);

            var spawner = spawnerObject.AddComponent<ProjectileSpawner>();
            spawner.ARCamera = camera;
            spawner.SelectedTarget = m_PreferredTarget != null ? m_PreferredTarget : target.transform;
            spawner.ProjectileMaterial = Resources.Load<Material>("PokeJelly_Orange");
            spawner.ThrowSpeed = 6f;
            spawner.SpawnDistance = 0.2f;

            Target = target.transform;
            Spawner = spawner;
            m_Root = target;
            m_Built = true;

            Debug.Log("[Throw] 投掷模式已搭建：目标在前方 1.5 m，点击屏幕发射");
        }

        /// <summary>模式切走时清掉已经贴上去的投掷物，避免下次进来还留着。</summary>
        public void ReturnProjectiles()
        {
            if (Target == null) return;

            for (int i = Target.childCount - 1; i >= 0; i--)
            {
                var child = Target.GetChild(i);
                if (child.GetComponent<Projectile>() != null) Destroy(child.gameObject);
            }
        }

        [ContextMenu("测试：朝目标扔一个")]
        public void TestThrow()
        {
            if (Spawner == null) return;
            Spawner.Throw(Target != null ? (Target.position - Spawner.ARCamera.transform.position).normalized : Vector3.forward);
        }
    }
}
