using UnityEngine;

namespace EarthOL.ARCore
{
    /// <summary>
    /// 挥扫「扇击」形变：把屏幕滑动方向映射到目标 warp 表面的面内方向，
    /// 用着色器 shear 把整个人物画面横向推开，再叠加一次局部冲击 poke，
    /// 并以橡胶弹簧回弹。整体横向位移 + 挤压 + 回弹，与按压的径向凹陷明显区分。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SwipeDeformController : MonoBehaviour
    {
        [SerializeField] PersonDeformationSurface deformer;
        [SerializeField, Min(0.05f)] float shearRatio = 0.40f;   // 相对人物面片宽度
        [SerializeField, Min(0f)] float maxTiltDegrees = 12f;
        [SerializeField, Min(0.2f)] float frequency = 2.2f;
        [SerializeField, Min(0.05f)] float dampingRatio = 0.38f;
        [SerializeField] float minSpeed = 150f;
        [SerializeField] float maxSpeed = 1400f;

        Vector2 m_ShearDir = Vector2.right;
        float m_ShearAmp;
        float m_ShearVelocity;
        Vector3 m_TiltEuler;
        Vector3 m_TiltVelocity;
        bool m_Active;

        public void Configure(PersonDeformationSurface surface) => deformer = surface;

        public void Trigger(Vector2 swipeScreenDirection, float speedPixelsPerSecond, Vector2 impactScreenPoint)
        {
            if (deformer == null || deformer.Warp == null) return;

            Camera cam = Camera.main;
            Vector3 right = cam != null ? cam.transform.right : Vector3.right;
            Vector3 up = cam != null ? cam.transform.up : Vector3.up;
            Vector3 worldDir = (right * swipeScreenDirection.x + up * swipeScreenDirection.y).normalized;

            // 屏幕方向 -> warp 表面局部面内方向
            Vector3 local = deformer.Warp.transform.InverseTransformDirection(worldDir);
            m_ShearDir = new Vector2(local.x, local.y).normalized;
            if (m_ShearDir.sqrMagnitude < 1e-6f) m_ShearDir = Vector2.right;

            float t = Mathf.Clamp01((speedPixelsPerSecond - minSpeed) / Mathf.Max(1f, maxSpeed - minSpeed));
            float amp = Mathf.Lerp(0.20f, 1f, t);
            float planeWidth = Mathf.Max(0.05f, deformer.Warp.planeWidth);
            float maxShear = planeWidth * shearRatio;

            m_ShearAmp = Mathf.Clamp(m_ShearAmp + maxShear * amp, -maxShear * 1.5f, maxShear * 1.5f);
            m_TiltEuler += new Vector3(
                swipeScreenDirection.y * maxTiltDegrees * amp,
                0f,
                -swipeScreenDirection.x * maxTiltDegrees * amp);
            m_TiltEuler = Vector3.ClampMagnitude(m_TiltEuler, maxTiltDegrees * 1.5f);

            deformer.SwipePokeAt(impactScreenPoint, Mathf.Lerp(0.5f, 1.3f, t));
            m_Active = true;
        }

        void LateUpdate()
        {
            if (deformer == null || deformer.Warp == null) return;

            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            float omega = 2f * Mathf.PI * Mathf.Max(0.2f, frequency);
            float stiffness = omega * omega;
            float damping = 2f * Mathf.Max(0.05f, dampingRatio) * omega;

            m_ShearVelocity += (-stiffness * m_ShearAmp - damping * m_ShearVelocity) * dt;
            m_ShearAmp += m_ShearVelocity * dt;
            m_TiltVelocity += (-stiffness * m_TiltEuler - damping * m_TiltVelocity) * dt;
            m_TiltEuler += m_TiltVelocity * dt;

            deformer.Warp.SetSwipeShear(m_ShearDir, m_ShearAmp);
            deformer.SetSwipeTransform(Vector3.zero, Quaternion.Euler(m_TiltEuler));

            if (m_Active && Mathf.Abs(m_ShearAmp) < 1e-5f && Mathf.Abs(m_ShearVelocity) < 1e-4f &&
                m_TiltEuler.sqrMagnitude < 1e-4f && m_TiltVelocity.sqrMagnitude < 1e-2f)
            {
                m_ShearAmp = m_ShearVelocity = 0f;
                m_TiltEuler = m_TiltVelocity = Vector3.zero;
                deformer.Warp.SetSwipeShear(m_ShearDir, 0f);
                deformer.SetSwipeTransform(Vector3.zero, Quaternion.identity);
                m_Active = false;
            }
        }
    }
}
