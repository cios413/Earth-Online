using System.Collections.Generic;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EarthOL.BodyThrow
{
    /// <summary>Head and torso collision proxies driven by the selected person's pose.</summary>
    [DisallowMultipleComponent]
    public sealed class BodyProxyRig : MonoBehaviour
    {
        public const float DefaultHeight = 1.72f;

        [SerializeField] private bool debugVisible = true;
        [SerializeField] private float bodyHeight = DefaultHeight;

        private readonly List<BodyPartProxy> m_Parts = new List<BodyPartProxy>();
        private BodyPartProxy m_Head;
        private BodyPartProxy m_Chest;
        private Material m_Material;
        private readonly BodyProxyStabilizer m_Stabilizer = new BodyProxyStabilizer();

        public IReadOnlyList<BodyPartProxy> Parts => m_Parts;
        public Transform TargetRoot => transform;
        public bool DebugVisible => debugVisible;
        public bool HasTrackedPart => m_Head != null && m_Head.gameObject.activeInHierarchy ||
            m_Chest != null && m_Chest.gameObject.activeInHierarchy;

        public void SetHeadPose(Vector3 center, float radius)
        {
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            m_Head.SetWorldSphere(center, radius);
            m_Head.gameObject.SetActive(true);
        }

        public void SetTorsoPose(Vector3 shoulders, Vector3 hips, float radius)
        {
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            UpdateTorsoShape(shoulders, hips, radius);
            m_Chest.gameObject.SetActive(true);
        }

        public void SetTrackedPose(bool hasHead, Vector3 head, float headRadius,
            bool hasTorso, Vector3 shoulders, Vector3 hips, float torsoRadius)
        {
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            m_Stabilizer.SetTarget(hasHead, head, headRadius, hasTorso, shoulders, hips, torsoRadius);
            ApplyStabilizedPose();
            if (hasHead) m_Head.gameObject.SetActive(true);
            if (hasTorso) m_Chest.gameObject.SetActive(true);
        }

        private void ApplyStabilizedPose()
        {
            if (m_Stabilizer.HasHead)
                m_Head.SetWorldSphere(m_Stabilizer.Head, m_Stabilizer.HeadRadius);
            if (m_Stabilizer.HasTorso)
                UpdateTorsoShape(m_Stabilizer.Shoulders, m_Stabilizer.Hips, m_Stabilizer.TorsoRadius);
        }

        private void UpdateTorsoShape(Vector3 shoulders, Vector3 hips, float radius)
        {
            Vector3 axis = shoulders - hips;
            radius = Mathf.Min(radius, axis.magnitude * 0.45f);
            Vector3 inset = axis.normalized * radius;
            // SetWorldSegment adds hemispherical caps. Inset the axis so its outer
            // ends stay at the shoulders and hips rather than extending beyond them.
            m_Chest.SetWorldSegment(hips + inset, shoulders - inset, radius);
        }

        public void HideHead()
        {
            m_Head.gameObject.SetActive(false);
            m_Stabilizer.ForgetHead();
        }

        public void HideTorso()
        {
            m_Chest.gameObject.SetActive(false);
            m_Stabilizer.ForgetTorso();
        }

        public void HideTrackedParts()
        {
            HideHead();
            HideTorso();
            m_Stabilizer.Reset();
        }

        private void Awake()
        {
            var body = GetComponent<Rigidbody>();
            if (body == null) body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            BuildDefaultRig();
        }

        private void Update()
        {
            HandleTestControls();
        }

        private void LateUpdate()
        {
            m_Stabilizer.Step(Time.unscaledDeltaTime);
            ApplyStabilizedPose();
            Physics.SyncTransforms();
        }

        private void OnDisable()
        {
            m_Stabilizer.Reset();
        }

        public void BuildDefaultRig()
        {
            if (m_Parts.Count > 0) return;

            m_Material = Resources.Load<Material>("BodyDepthOccluder");
            if (m_Material == null) Debug.LogError("[BodyThrow] 缺少 BodyDepthOccluder 隐形遮挡材质。");
            m_Head = CreatePart("HeadProxy", BodyPartType.Head, PrimitiveType.Sphere);
            m_Chest = CreatePart("ChestProxy", BodyPartType.Chest, PrimitiveType.Capsule);
            ApplyTestPose();
            Debug.Log("[BodyThrow] 头部和躯干使用隐形深度遮挡，颜色不显示，保留遮挡与碰撞。");
        }

        private BodyPartProxy CreatePart(string objectName, BodyPartType type, PrimitiveType primitiveType)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(transform, false);
            if (primitiveType == PrimitiveType.Sphere)
                go.AddComponent<SphereCollider>();
            else
                go.AddComponent<CapsuleCollider>();

            var visual = GameObject.CreatePrimitive(primitiveType);
            visual.name = "Visual";
            visual.transform.SetParent(go.transform, false);
            var visualCollider = visual.GetComponent<Collider>();
            if (visualCollider != null) visualCollider.enabled = false;

            var part = go.AddComponent<BodyPartProxy>();
            part.Configure(type, m_Material, debugVisible);
            m_Parts.Add(part);
            return part;
        }

        public void ApplyTestPose()
        {
            float scale = Mathf.Max(0.1f, bodyHeight / DefaultHeight);
            m_Head.SetLocalSphere(new Vector3(0f, 1.63f * scale, 0f), 0.115f * scale);
            m_Chest.SetLocalSegment(
                new Vector3(0f, 0.86f * scale, 0f),
                new Vector3(0f, 1.43f * scale, 0f),
                0.24f * scale);
        }

        private void HandleTestControls()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            const float moveSpeed = 0.8f;
            const float rotateSpeed = 70f;
            Vector3 move = Vector3.zero;
            if (keyboard.wKey.isPressed) move += Vector3.forward;
            if (keyboard.sKey.isPressed) move += Vector3.back;
            if (keyboard.aKey.isPressed) move += Vector3.left;
            if (keyboard.dKey.isPressed) move += Vector3.right;
            if (keyboard.qKey.isPressed) transform.Rotate(Vector3.up, -rotateSpeed * Time.deltaTime, Space.Self);
            if (keyboard.eKey.isPressed) transform.Rotate(Vector3.up, rotateSpeed * Time.deltaTime, Space.Self);
            if (move.sqrMagnitude > 0f) transform.position += move.normalized * moveSpeed * Time.deltaTime;
#endif
        }
    }
}
