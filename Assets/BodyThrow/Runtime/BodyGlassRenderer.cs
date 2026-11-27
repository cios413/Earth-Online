using UnityEngine;
using UnityEngine.Rendering;

namespace EarthOL.BodyThrow
{
    /// <summary>Shares one low-resolution background blur between all body proxies.</summary>
    [RequireComponent(typeof(Camera))]
    [DisallowMultipleComponent]
    public sealed class BodyGlassRenderer : MonoBehaviour
    {
        static readonly int ScreenTexture = Shader.PropertyToID("_BodyGlassScreen");
        static readonly int Ready = Shader.PropertyToID("_BodyGlassReady");
        static readonly int Radius = Shader.PropertyToID("_BlurRadius");

        [SerializeField, Range(1, 8)] private int downsample = 4;
        [SerializeField, Range(0.3f, 3f)] private float blurRadius = 1.3f;

        Camera m_Camera;
        BodyProxyRig m_Rig;
        Material m_Blur;
        RenderTexture m_Background;
        RenderTexture m_Temporary;
        CommandBuffer m_Commands;
        bool m_Warned;

        public void Configure(BodyProxyRig rig) => m_Rig = rig;

        private void OnPreCull()
        {
            if (m_Camera == null) m_Camera = GetComponent<Camera>();
            if (m_Rig != null && !m_Rig.HasTrackedPart)
            {
                if (m_Commands != null) ReleaseBuffers();
                return;
            }

            if (m_Blur == null)
            {
                var template = Resources.Load<Material>("BodyGlassBlur");
                if (template == null || !template.shader.isSupported)
                {
                    if (!m_Warned) Debug.LogWarning("[BodyGlass] 毛玻璃模糊材质不可用，使用淡色透明表面。");
                    m_Warned = true;
                    return;
                }
                m_Blur = new Material(template) { name = "Body glass blur (runtime)", hideFlags = HideFlags.HideAndDontSave };
            }
            m_Blur.SetFloat(Radius, blurRadius);

            int width = Mathf.Max(1, m_Camera.pixelWidth / downsample);
            int height = Mathf.Max(1, m_Camera.pixelHeight / downsample);
            if (m_Background != null && m_Background.width == width && m_Background.height == height) return;

            ReleaseBuffers();
            m_Background = CreateTarget(width, height, "Body frosted background");
            m_Temporary = CreateTarget(width, height, "Body glass blur intermediate");
            m_Commands = new CommandBuffer { name = "Body proxies: shared frosted camera background" };
            // AR background and opaque projectiles have rendered; glass is still pending.
            m_Commands.Blit(BuiltinRenderTextureType.CameraTarget, m_Background);
            m_Commands.Blit(m_Background, m_Temporary, m_Blur, 0);
            m_Commands.Blit(m_Temporary, m_Background, m_Blur, 1);
            m_Commands.SetGlobalTexture(ScreenTexture, m_Background);
            m_Commands.SetGlobalFloat(Ready, 1f);
            m_Commands.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            m_Camera.AddCommandBuffer(CameraEvent.BeforeForwardAlpha, m_Commands);
            Debug.Log($"[BodyGlass] 毛玻璃背景就绪：{width}x{height}，双向模糊，头部与躯干共用。");
        }

        static RenderTexture CreateTarget(int width, int height, string targetName)
        {
            var target = new RenderTexture(width, height, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Default)
            {
                name = targetName,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
                hideFlags = HideFlags.HideAndDontSave
            };
            target.Create();
            return target;
        }

        private void OnDisable()
        {
            ReleaseBuffers();
            if (m_Blur != null) DestroyResource(m_Blur);
            m_Blur = null;
        }

        void ReleaseBuffers()
        {
            if (m_Commands != null)
            {
                if (m_Camera != null) m_Camera.RemoveCommandBuffer(CameraEvent.BeforeForwardAlpha, m_Commands);
                m_Commands.Release();
                m_Commands = null;
            }
            Shader.SetGlobalFloat(Ready, 0f);
            Shader.SetGlobalTexture(ScreenTexture, Texture2D.blackTexture);
            if (m_Background != null) { m_Background.Release(); DestroyResource(m_Background); }
            if (m_Temporary != null) { m_Temporary.Release(); DestroyResource(m_Temporary); }
            m_Background = m_Temporary = null;
        }

        static void DestroyResource(Object resource)
        {
            if (Application.isPlaying) Destroy(resource);
            else DestroyImmediate(resource);
        }
    }
}
