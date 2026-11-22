using EarthOL.ARCore;
using EarthOL.Deform;
using UnityEngine;
using System;

namespace EarthOL.BodyThrow
{
    [DisallowMultipleComponent]
    public sealed class PersonShowcaseController : MonoBehaviour
    {
        const float LongPressSeconds = 0.48f;
        const float PreviewDepth = 1.35f;
        const float ShowcaseDepth = 2.5f;
        const int ShowcaseLayer = 31;

        Camera m_MainCamera;
        PersonTrackingManager m_Tracking;
        PoseDetectionManager m_Detection;
        PersonDeformationSurface m_DeformationSource;
        IPersonModelDeformation m_ModelDeformation;
        MeshCollider m_ModelCollider;
        GameObject m_ModelRoot;
        Mesh m_ModelMesh;
        MeshRenderer m_ModelRenderer;
        Material m_ModelMaterial, m_BackMaterial;
        Texture2D m_Photo, m_BackPhoto;
        Camera m_PreviewCamera;
        RenderTexture m_PreviewTexture;
        int m_MainCameraMask;
        int m_PointerId;
        int m_PersonId = -1;
        float m_PressTime;
        float m_ModelHeight = 1.45f;
        float m_Yaw;
        Vector2 m_LastPointer;
        bool m_InteractionEnabled;
        bool m_PointerHeld;
        bool m_ModelCreated;
        bool m_Presenting;
        bool m_Rotating;
        bool m_Deforming;
        Vector2 m_RotationOrigin;
        GameObject m_SavedProjectile;
        Texture2D m_SavedProjectilePreview;
        SavedProjectileAssets m_SavedProjectileAssets;

        sealed class SavedProjectileAssets
        {
            public Mesh mesh;
            public Material material;
            public Texture2D photo;
            int m_References = 1;

            public Action Retain()
            {
                m_References++;
                return Release;
            }

            public void Release()
            {
                if (--m_References != 0) return;
                DestroyOwned(mesh);
                DestroyOwned(material);
                DestroyOwned(photo);
            }
        }

        public bool IsPresenting => m_Presenting;
        public Texture PreviewTexture => m_PreviewTexture;
        public bool HasSavedProjectile => m_SavedProjectile != null;
        public GameObject SavedProjectileSource => m_SavedProjectile;
        public Texture2D SavedProjectilePreview => m_SavedProjectilePreview;
        public event Action<GameObject, Texture2D> ProjectileChanged;
        public event Action ProjectileCleared;

        public void Configure(Camera camera, PersonTrackingManager tracking, PoseDetectionManager detection,
            PersonDeformationSurface deformationSource = null)
        {
            m_MainCamera = camera;
            m_Tracking = tracking;
            m_Detection = detection;
            m_DeformationSource = deformationSource;
        }

        public void SetDeformationSource(PersonDeformationSurface source) => m_DeformationSource = source;

        public void SetInteractionEnabled(bool enabled)
        {
            if (m_InteractionEnabled == enabled) return;
            m_InteractionEnabled = enabled;
            if (!enabled) ClearSession();
        }

        void Update()
        {
            if (!m_InteractionEnabled || m_MainCamera == null) return;
            if (m_Presenting)
            {
                UpdateRotation();
                return;
            }

            if (!BodyPointerFrame.TryRead(out var frame))
            {
                if (m_PointerHeld) CancelPress();
                return;
            }

            if (frame.down) BeginPress(frame);
            if (!m_PointerHeld || frame.id != m_PointerId) return;
            if (frame.canceled) { CancelPress(); return; }

            if (!m_ModelCreated && frame.held && Time.unscaledTime - m_PressTime >= LongPressSeconds)
                CreatePreview(frame.position);
            if (m_ModelCreated && frame.held) MovePreview(frame.position);
            if (frame.released)
            {
                if (m_ModelCreated) BeginPresentation();
                else CancelPress();
            }
        }

        void LateUpdate()
        {
            if (!m_Presenting || m_PreviewCamera == null || m_ModelRoot == null) return;
            m_ModelDeformation?.Advance(Time.unscaledDeltaTime);
            m_PreviewCamera.transform.SetPositionAndRotation(m_MainCamera.transform.position, m_MainCamera.transform.rotation);
            m_PreviewCamera.Render();
        }

        void BeginPress(BodyPointerFrame frame)
        {
            CancelPress();
            if (m_Presenting || m_Tracking == null || !m_Tracking.isActiveAndEnabled ||
                ARCoreModeOverlay.IsPointOnOverlay(frame.position)) return;
            Vector3 viewport = m_MainCamera.ScreenToViewportPoint(frame.position);
            PersonPoseData person = m_Tracking.Pick(new Vector2(viewport.x, viewport.y));
            if (person == null) return;
            m_PointerHeld = true;
            m_PointerId = frame.id;
            m_PersonId = person.personId;
            m_PressTime = Time.unscaledTime;
        }

        void CreatePreview(Vector2 position)
        {
            PersonPoseData person = m_Tracking != null ? m_Tracking.Find(m_PersonId) : null;
            if (person == null || m_Detection == null || !m_Detection.CapturePersonPhoto(person.screenRect, out m_Photo, out Rect photoBounds))
            {
                CancelPress();
                return;
            }

            m_ModelMesh = PersonPhotoSilhouetteBuilder.Build(person, m_Photo, photoBounds, m_ModelHeight);
            if (m_ModelMesh == null)
            {
                CancelPress();
                return;
            }
            m_BackPhoto = PersonPhotoSilhouetteBuilder.CreateBackTexture(m_Photo);
            if (m_BackPhoto == null) { CancelPress(); return; }

            CreateModelObject();
            if (m_ModelRoot == null) return;
            m_ModelCreated = true;
            SaveProjectileSlot();
            MovePreview(position);
        }

        void CreateModelObject()
        {
            m_ModelRoot = new GameObject("Photo Person Showcase Model");
            m_ModelRoot.transform.SetParent(m_MainCamera.transform, false);
            m_ModelRoot.layer = 0;
            m_ModelRoot.AddComponent<MeshFilter>().sharedMesh = m_ModelMesh;
            m_ModelRenderer = m_ModelRoot.AddComponent<MeshRenderer>();
            var template = Resources.Load<Material>("PersonShowcase");
            Shader shader = template != null ? template.shader : Shader.Find("EarthOL/ShowcasePhoto");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.LogError("[PersonShowcase] 找不到透明 Unlit Shader。");
                CancelPress();
                return;
            }
            m_ModelMaterial = CreatePhotoMaterial(shader, m_Photo, "Photo Person Front Material");
            m_BackMaterial = CreatePhotoMaterial(shader, m_BackPhoto, "Photo Person Back Material");
            m_ModelRenderer.sharedMaterials = new[] { m_ModelMaterial, m_BackMaterial };
            m_ModelRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_ModelRenderer.receiveShadows = false;
            m_ModelCollider = m_ModelRoot.AddComponent<MeshCollider>();
            m_ModelCollider.sharedMesh = m_ModelMesh;
            m_ModelDeformation = m_DeformationSource != null ? m_DeformationSource.CreateModelDeformation(m_ModelRoot) : null;
        }

        void MovePreview(Vector2 screenPosition)
        {
            if (m_ModelRoot == null) return;
            Vector3 viewport = m_MainCamera.ScreenToViewportPoint(screenPosition);
            viewport.x = Mathf.Clamp01(viewport.x);
            viewport.y = Mathf.Clamp01(viewport.y);
            Vector3 local = m_MainCamera.ViewportToWorldPoint(new Vector3(viewport.x, viewport.y, PreviewDepth));
            m_ModelRoot.transform.localPosition = m_MainCamera.transform.InverseTransformPoint(local);
        }

        void BeginPresentation()
        {
            m_PointerHeld = false;
            m_PersonId = -1;
            m_Presenting = true;
            m_Yaw = 0f;
            m_ModelRoot.transform.localPosition = new Vector3(0f, 0f, ShowcaseDepth);
            m_ModelRoot.transform.localRotation = Quaternion.identity;
            SetLayerRecursively(m_ModelRoot, ShowcaseLayer);
            m_MainCameraMask = m_MainCamera.cullingMask;
            m_MainCamera.cullingMask &= ~(1 << ShowcaseLayer);
            CreatePreviewCamera();
            RenderPreview();
        }

        void CreatePreviewCamera()
        {
            float aspect = Mathf.Clamp(Screen.width / (float)Mathf.Max(1, Screen.height), 0.35f, 2.2f);
            const int maxSide = 768;
            int width = aspect < 1f ? Mathf.Max(256, Mathf.RoundToInt(maxSide * aspect)) : maxSide;
            int height = aspect < 1f ? maxSide : Mathf.Max(256, Mathf.RoundToInt(maxSide / aspect));
            m_PreviewTexture = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default)
            { name = "Person Showcase Preview", antiAliasing = 1, useMipMap = false, autoGenerateMips = false };
            m_PreviewTexture.Create();

            var cameraObject = new GameObject("Person Showcase Render Camera");
            m_PreviewCamera = cameraObject.AddComponent<Camera>();
            m_PreviewCamera.enabled = false;
            m_PreviewCamera.clearFlags = CameraClearFlags.SolidColor;
            m_PreviewCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            m_PreviewCamera.orthographic = true;
            m_PreviewCamera.orthographicSize = m_ModelHeight * 0.64f;
            m_PreviewCamera.aspect = width / (float)height;
            m_PreviewCamera.nearClipPlane = 0.1f;
            m_PreviewCamera.farClipPlane = 6f;
            m_PreviewCamera.cullingMask = 1 << ShowcaseLayer;
            m_PreviewCamera.allowHDR = false;
            m_PreviewCamera.allowMSAA = false;
            m_PreviewCamera.useOcclusionCulling = false;
            m_PreviewCamera.targetTexture = m_PreviewTexture;
        }

        void UpdateRotation()
        {
            bool hasFrame = BodyPointerFrame.TryRead(out var frame);
            ProcessPresentationPointer(frame, hasFrame);
        }

        public Rect PreviewRect
        {
            get
            {
                if (m_PreviewTexture == null) return default;
                float maxWidth = Screen.width * .88f;
                float maxHeight = Screen.height * .70f;
                float aspect = m_PreviewTexture.width / (float)m_PreviewTexture.height;
                float width = Mathf.Min(maxWidth, maxHeight * aspect);
                float height = width / aspect;
                return new Rect((Screen.width-width)*.5f,(Screen.height-height)*.5f,width,height);
            }
        }

        public void ProcessPresentationPointer(BodyPointerFrame frame, bool hasFrame)
        {
            if (!m_Presenting || m_ModelRoot == null) return;
            if (!hasFrame) { EndModelPointer(); return; }
            if (frame.down)
            {
                if (ARCoreModeOverlay.IsShowcaseExitPoint(frame.position))
                {
                    ExitPresentation();
                    return;
                }
                if (ARCoreModeOverlay.IsPointOnOverlay(frame.position)) return;
                m_Rotating = true;
                m_PointerId = frame.id;
                m_LastPointer = frame.position;
                m_RotationOrigin = frame.position;
                UpdateModelTouch(frame.position);
            }
            if (!m_Rotating || frame.id != m_PointerId) return;
            if (frame.canceled || frame.released) { EndModelPointer(); return; }
            if (!frame.held) return;
            float delta = frame.position.x - m_LastPointer.x;
            if (Vector2.Distance(frame.position,m_RotationOrigin) > 8f) m_Yaw -= delta * 0.35f;
            m_ModelRoot.transform.localRotation = Quaternion.Euler(0f, m_Yaw, 0f);
            int samples = Mathf.Clamp(Mathf.CeilToInt(Vector2.Distance(frame.position,m_LastPointer)/24f),1,12);
            for (int i=1;i<=samples;i++)
                UpdateModelTouch(Vector2.Lerp(m_LastPointer,frame.position,i/(float)samples));
            m_LastPointer = frame.position;
        }

        void EndModelPointer()
        {
            if (m_Deforming) m_ModelDeformation?.End(m_PointerId);
            m_Deforming = false;
            m_Rotating = false;
        }

        void UpdateModelTouch(Vector2 screenPosition)
        {
            if (m_ModelDeformation == null) return;
            if (TryHitModel(screenPosition,out var hit))
            {
                if (m_Deforming) m_ModelDeformation.Drag(m_PointerId,hit.point);
                else { m_ModelDeformation.Begin(m_PointerId,hit.point); m_Deforming = true; }
            }
            else if (m_Deforming)
            {
                m_ModelDeformation.End(m_PointerId);
                m_Deforming = false;
            }
        }

        bool TryHitModel(Vector2 screenPosition,out RaycastHit hit)
        {
            hit = default;
            if (m_ModelCollider == null || m_PreviewCamera == null) return false;
            Rect rect = PreviewRect;
            Vector2 gui = new Vector2(screenPosition.x,Screen.height-screenPosition.y);
            if (!rect.Contains(gui)) return false;
            m_PreviewCamera.transform.SetPositionAndRotation(m_MainCamera.transform.position,m_MainCamera.transform.rotation);
            Vector3 viewport = new Vector3((gui.x-rect.x)/rect.width,1f-(gui.y-rect.y)/rect.height,0);
            Physics.SyncTransforms();
            if (!m_ModelCollider.Raycast(m_PreviewCamera.ViewportPointToRay(viewport),out hit,6f)) return false;
            return m_Photo == null || m_Photo.GetPixelBilinear(hit.textureCoord.x,hit.textureCoord.y).a > .2f;
        }

        public void ExitPresentation()
        {
            if (!m_Presenting) return;
            m_Presenting = false;
            ClearSession();
        }

        public void ResetForDetectionStop()
        {
            ClearSession();
            ClearSavedProjectile();
        }

        public void ClearSavedProjectile()
        {
            if (m_SavedProjectile != null) DestroyOwned(m_SavedProjectile);
            m_SavedProjectileAssets?.Release();
            m_SavedProjectileAssets = null;
            m_SavedProjectile = null;
            m_SavedProjectilePreview = null;
            ProjectileCleared?.Invoke();
        }

        internal Action RetainProjectileAssets(GameObject source)
            => source == m_SavedProjectile ? m_SavedProjectileAssets?.Retain() : null;

        void SaveProjectileSlot()
        {
            ClearSavedProjectile();
            if (m_ModelMesh == null || m_ModelRenderer == null || m_Photo == null) return;
            m_SavedProjectile = new GameObject("Saved Showcase Projectile");
            m_SavedProjectile.transform.SetParent(transform, false);
            m_SavedProjectile.SetActive(false);
            // Keep an independent photo alive after the showcase session releases its textures.
            m_SavedProjectilePreview = Instantiate(m_Photo);
            m_SavedProjectileAssets = new SavedProjectileAssets
            {
                mesh = CreateProjectileMesh(),
                photo = m_SavedProjectilePreview,
                material = CreateSavedProjectileMaterial(m_ModelRenderer.sharedMaterials[0], 0)
            };
            var filter = m_SavedProjectile.AddComponent<MeshFilter>();
            filter.sharedMesh = m_SavedProjectileAssets.mesh;
            var renderer = m_SavedProjectile.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = m_SavedProjectileAssets.material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ProjectileChanged?.Invoke(m_SavedProjectile, m_SavedProjectilePreview);
        }

        Mesh CreateProjectileMesh()
        {
            // The showcase builder stores two matching surfaces. Keep the front silhouette
            // and its exact photo UVs, flattening only depth for a single double-sided sheet.
            int count = m_ModelMesh.vertexCount / 2;
            var vertices = new Vector3[count];
            var uvs = new Vector2[count];
            Array.Copy(m_ModelMesh.vertices, vertices, count);
            Array.Copy(m_ModelMesh.uv, uvs, count);
            for (int i = 0; i < count; i++) vertices[i].z = 0f;
            var mesh = new Mesh { name = "Photo Person Projectile Sheet" };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = m_ModelMesh.GetTriangles(0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        Material CreateSavedProjectileMaterial(Material source, int index)
        {
            if (source == null) return null;
            var template = Resources.Load<Material>("PersonShowcaseProjectile");
            Material material = template != null ? new Material(template) : null;
            if (material == null)
            {
                Shader shader = Shader.Find("EarthOL/ShowcaseProjectile");
                if (shader == null) shader = Shader.Find("Unlit/Transparent");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                if (shader == null) return new Material(source) { name = "Saved Showcase Projectile " + index };
                material = new Material(shader);
            }
            material.name = "Saved Showcase Projectile " + index;
            Texture texture = m_SavedProjectilePreview;
            if (texture != null) material.mainTexture = texture;
            if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
            material.renderQueue = 3000;
            return material;
        }

        void RenderPreview()
        {
            if (m_PreviewCamera == null) return;
            m_PreviewCamera.transform.SetPositionAndRotation(m_MainCamera.transform.position, m_MainCamera.transform.rotation);
            m_PreviewCamera.Render();
        }

        void CancelPress()
        {
            m_PointerHeld = false;
            m_ModelCreated = false;
            m_PersonId = -1;
            DestroyModel();
        }

        void ClearSession()
        {
            EndModelPointer();
            m_PointerHeld = false;
            m_ModelCreated = false;
            m_Rotating = false;
            m_Presenting = false;
            m_PersonId = -1;
            if (m_MainCamera != null && m_MainCameraMask != 0) m_MainCamera.cullingMask = m_MainCameraMask;
            m_MainCameraMask = 0;
            DestroyModel();
            if (m_PreviewCamera != null) DestroyOwned(m_PreviewCamera.gameObject);
            m_PreviewCamera = null;
            if (m_PreviewTexture != null)
            {
                m_PreviewTexture.Release();
                DestroyOwned(m_PreviewTexture);
                m_PreviewTexture = null;
            }
        }

        void DestroyModel()
        {
            m_ModelDeformation?.Clear();
            if (m_ModelRoot != null) DestroyOwned(m_ModelRoot);
            if (m_ModelMesh != null) DestroyOwned(m_ModelMesh);
            if (m_ModelMaterial != null) DestroyOwned(m_ModelMaterial);
            if (m_BackMaterial != null) DestroyOwned(m_BackMaterial);
            if (m_Photo != null) DestroyOwned(m_Photo);
            if (m_BackPhoto != null) DestroyOwned(m_BackPhoto);
            m_ModelRoot = null;
            m_ModelMesh = null;
            m_ModelRenderer = null;
            m_ModelCollider = null;
            m_ModelDeformation = null;
            m_Deforming = false;
            m_ModelMaterial = m_BackMaterial = null;
            m_Photo = m_BackPhoto = null;
        }

        static Material CreatePhotoMaterial(Shader shader, Texture texture, string materialName)
        {
            var material = new Material(shader) { name = materialName, hideFlags = HideFlags.DontSave };
            material.mainTexture = texture;
            if (shader.name == "Standard")
            {
                material.SetFloat("_Mode", 3f);
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 1);
                material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                material.SetFloat("_Glossiness", 0.28f);
                material.EnableKeyword("_ALPHABLEND_ON");
                material.DisableKeyword("_ALPHATEST_ON");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            }
            material.renderQueue = 3000;
            return material;
        }

        static void SetLayerRecursively(GameObject root, int layer)
        {
            root.layer = layer;
            foreach (Transform child in root.transform) SetLayerRecursively(child.gameObject, layer);
        }

        static void DestroyOwned(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }

        void OnDestroy()
        {
            ClearSession();
            ClearSavedProjectile();
        }
    }
}
