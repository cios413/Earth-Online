using EarthOL.ARCore;
using EarthOL.Deform;
using UnityEngine;
using System;
using System.Collections;

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
        readonly PersonPhotoSegmenter m_Segmenter = new PersonPhotoSegmenter();
        Texture2D m_Photo;
        Camera m_PreviewCamera;
        RenderTexture m_PreviewTexture;
        int m_MainCameraMask;
        int m_PointerId;
        int m_PersonId = -1;
        float m_PressTime;
        float m_ModelHeight = 1.45f;
        [SerializeField, Range(0f, 1f)] float m_PoseDistortion = .85f;
        [SerializeField, Range(0f, .25f)] float m_LimbClearance = .065f;
        float m_Yaw;
        Vector2 m_LastPointer;
        bool m_InteractionEnabled;
        bool m_PointerHeld;
        bool m_ModelCreated;
        bool m_Presenting;
        bool m_Rotating;
        bool m_Deforming;
        bool m_RecognitionPausedForShowcase;
        IEnumerator m_PreviewBuild;
        Vector2 m_BuildPosition;
        bool m_PresentWhenReady;
        Quaternion m_RenderedRotation;
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

            bool hasFrame = BodyPointerFrame.TryRead(out var frame);
            if (m_PreviewBuild != null)
            {
                if (hasFrame && frame.id == m_PointerId && frame.canceled) { CancelPress(); return; }
                if (hasFrame && frame.id == m_PointerId && (frame.held || frame.released))
                    m_BuildPosition = frame.position;
                if (!hasFrame || frame.id != m_PointerId || frame.released)
                    m_PresentWhenReady = true;
                AdvancePreviewBuild();
                return;
            }

            if (!hasFrame)
            {
                if (m_PointerHeld) CancelPress();
                return;
            }

            if (frame.down) BeginPress(frame);
            if (!m_PointerHeld || frame.id != m_PointerId) return;
            if (frame.canceled) { CancelPress(); return; }

            if (!m_ModelCreated && frame.held && Time.unscaledTime - m_PressTime >= LongPressSeconds)
            {
                m_Detection?.SetShowcaseBuilding(true);
                m_RecognitionPausedForShowcase = true;
                m_BuildPosition = frame.position;
                m_PreviewBuild = CreatePreview();
                AdvancePreviewBuild();
                return;
            }
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
            bool wasDeforming = m_ModelDeformation?.HasActivePokes == true;
            m_ModelDeformation?.Advance(Time.unscaledDeltaTime);
            if (wasDeforming || m_ModelDeformation?.HasActivePokes == true ||
                m_RenderedRotation != m_ModelRoot.transform.localRotation || !m_PreviewTexture.IsCreated())
                RenderPreview();
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

        void AdvancePreviewBuild()
        {
            try
            {
                if (m_PreviewBuild.MoveNext()) return;
                ReleasePreviewBuild();
                ResumeRecognitionAfterShowcase();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                m_Detection?.RecordDiagnostic("showcase_build_error", personId: m_PersonId,
                    error: exception.ToString(), segmentationBackend: m_Segmenter.BackendName, flush: true);
                CancelPress();
                m_Segmenter.Dispose();
                return;
            }
            if (!m_ModelCreated) CancelPress();
            else if (m_PresentWhenReady) BeginPresentation();
            m_PresentWhenReady = false;
        }

        void ReleasePreviewBuild()
        {
            (m_PreviewBuild as IDisposable)?.Dispose();
            m_PreviewBuild = null;
        }

        IEnumerator CreatePreview()
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var tracked = m_Tracking != null ? m_Tracking.Find(m_PersonId) : null;
            if (tracked == null || m_Detection == null) yield break;
            var person = PersonPoseData.FromDetection(tracked.ToBodyPoseResult(), tracked.timestamp);
            if (person == null) yield break;
            person.personId = tracked.personId;
            Rect photoBounds = default;
            var capture = m_Detection.CapturePersonPhotoScheduled(PhotoCropBounds(person),
                (photo, bounds) => { m_Photo = photo; photoBounds = bounds; });
            try { while (capture.MoveNext()) yield return null; }
            finally { (capture as IDisposable)?.Dispose(); }
            if (m_Photo == null)
            {
                m_Detection?.RecordDiagnostic("showcase_capture_failed", personId: m_PersonId, flush: true);
                yield break;
            }

            float captureMs = (float)timer.Elapsed.TotalMilliseconds;
            yield return null;
            while (m_Detection.InferenceRunning || m_Detection.CapturePending) yield return null;
            var segmentationStart = (float)timer.Elapsed.TotalMilliseconds;
            byte[] alpha = null;
            var segmentation = m_Segmenter.SegmentScheduled(m_Photo, person, photoBounds, mask => alpha = mask);
            try { while (segmentation.MoveNext()) yield return null; }
            finally { (segmentation as IDisposable)?.Dispose(); }
            float segmentationMs = (float)timer.Elapsed.TotalMilliseconds - segmentationStart;
            if (alpha == null)
            {
                m_Detection.RecordDiagnostic("showcase_mask_failed", personId: person.personId,
                    width: m_Photo.width, height: m_Photo.height, captureMs: captureMs,
                    segmentationMs: segmentationMs, segmentationBackend: m_Segmenter.BackendName, flush: true);
                Debug.LogWarning("[PersonShowcase] No matching silhouette in captured photo.");
                yield break;
            }
            yield return null;
            float composeStart = (float)timer.Elapsed.TotalMilliseconds;
            Texture2D reshaped = null;
            byte[] reshapedAlpha = null;
            PersonPoseData reshapedPose = null;
            Vector2[] limbDepth = null;
            var composition = PersonShowcasePose.Compose(m_Photo, alpha, person, photoBounds,
                m_PoseDistortion, m_LimbClearance, UnityEngine.Random.Range(0, int.MaxValue),
                (texture, mask, pose, depth) =>
                {
                    reshaped = texture;
                    reshapedAlpha = mask;
                    reshapedPose = pose;
                    limbDepth = depth;
                });
            try { while (composition.MoveNext()) yield return null; }
            finally { (composition as IDisposable)?.Dispose(); }
            float composeMs = (float)timer.Elapsed.TotalMilliseconds - composeStart;
            float composeEnd = (float)timer.Elapsed.TotalMilliseconds;
            if (reshaped != null)
            {
                try
                {
                    m_ModelMesh = PersonPhotoSilhouetteBuilder.BuildFromMask(reshaped, reshapedAlpha, m_ModelHeight,
                        reshapedPose, new Rect(0f, 0f, 1f, 1f), limbDepth);
                    if (m_ModelMesh != null)
                    {
                        DestroyOwned(m_Photo);
                        m_Photo = reshaped;
                        alpha = reshapedAlpha;
                        reshaped = null;
                    }
                }
                finally { if (reshaped != null) DestroyOwned(reshaped); }
            }
            if (m_ModelMesh == null)
                m_ModelMesh = PersonPhotoSilhouetteBuilder.BuildFromMask(m_Photo, alpha, m_ModelHeight, person, photoBounds);
            float meshMs = (float)timer.Elapsed.TotalMilliseconds - composeEnd;
            int foregroundPixels = 0;
            foreach (byte coverage in alpha) if (coverage >= 128) foregroundPixels++;
            m_Detection.RecordDiagnostic(m_ModelMesh == null ? "showcase_mesh_failed" : "showcase_mesh",
                personId: person.personId, width: m_Photo.width, height: m_Photo.height,
                vertices: m_ModelMesh != null ? m_ModelMesh.vertexCount : 0,
                depth: m_ModelMesh != null ? m_ModelMesh.bounds.size.z : 0f,
                foregroundPixels: foregroundPixels, elapsedMs: (float)timer.Elapsed.TotalMilliseconds,
                captureMs: captureMs, segmentationMs: segmentationMs, composeMs: composeMs,
                meshMs: meshMs, segmentationBackend: m_Segmenter.BackendName, flush: true);
            if (m_ModelMesh == null) yield break;
            yield return null;
            CreateModelObject();
            if (m_ModelRoot == null) yield break;
            m_ModelCreated = true;
            SaveProjectileSlot();
            MovePreview(m_BuildPosition);
            Debug.Log($"[PersonShowcase] Photo silhouette ready in {timer.ElapsedMilliseconds} ms.");
        }

        public static Rect PhotoCropBounds(PersonPoseData person)
        {
            Rect bounds = person.screenRect;
            var pose = person.AsPoseView();
            for (int i = 5; i < 17; i++)
            {
                if (!BodyPoseGeometry.HasPoint(pose, i, .35f)) continue;
                Vector2 joint = pose.keypoints[i];
                Vector2 limit = bounds.size * .25f;
                if (joint.x < bounds.xMin - limit.x || joint.x > bounds.xMax + limit.x ||
                    joint.y < bounds.yMin - limit.y || joint.y > bounds.yMax + limit.y) continue;
                bounds = Rect.MinMaxRect(Mathf.Min(bounds.xMin, joint.x), Mathf.Min(bounds.yMin, joint.y),
                    Mathf.Max(bounds.xMax, joint.x), Mathf.Max(bounds.yMax, joint.y));
            }
            return bounds;
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
                DestroyModel();
                return;
            }
            m_ModelMaterial = CreatePhotoMaterial(shader, m_Photo, "Photo Person Front Material");
            m_BackMaterial = CreatePhotoMaterial(shader, m_Photo, "Photo Person Back Material");
            if (m_ModelMesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord1))
            {
                m_ModelMaterial.SetFloat("_SolidContour", 1f);
                m_BackMaterial.SetFloat("_SolidContour", 1f);
            }
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
            var descriptor = new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 16)
            { msaaSamples = 4, useMipMap = false, autoGenerateMips = false };
            descriptor.msaaSamples = SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor);
            m_PreviewTexture = new RenderTexture(descriptor) { name = "Person Showcase Preview" };
            m_PreviewTexture.Create();

            var cameraObject = new GameObject("Person Showcase Render Camera");
            m_PreviewCamera = cameraObject.AddComponent<Camera>();
            m_PreviewCamera.enabled = false;
            m_PreviewCamera.clearFlags = CameraClearFlags.SolidColor;
            m_PreviewCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            m_PreviewCamera.orthographic = true;
            m_PreviewCamera.orthographicSize = m_ModelHeight * 0.64f;
            m_PreviewCamera.aspect = width / (float)height;
            if (m_ModelMesh != null)
            {
                Bounds modelBounds = m_ModelMesh.bounds;
                Vector3 reach = modelBounds.extents + new Vector3(Mathf.Abs(modelBounds.center.x),
                    Mathf.Abs(modelBounds.center.y), Mathf.Abs(modelBounds.center.z));
                float horizontal = new Vector2(reach.x, reach.z).magnitude;
                m_PreviewCamera.orthographicSize = Mathf.Max(m_PreviewCamera.orthographicSize,
                    1.08f * Mathf.Max(reach.y, horizontal / m_PreviewCamera.aspect));
            }
            m_PreviewCamera.nearClipPlane = 0.1f;
            m_PreviewCamera.farClipPlane = 6f;
            m_PreviewCamera.cullingMask = 1 << ShowcaseLayer;
            m_PreviewCamera.allowHDR = false;
            m_PreviewCamera.allowMSAA = descriptor.msaaSamples > 1;
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
            m_Detection?.RequestFreshDetection();
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
            // Keep one face for the double-sided projectile shader.
            int count = m_ModelMesh.vertexCount / 2;
            var vertices = new Vector3[count];
            var uvs = new Vector2[count];
            Array.Copy(m_ModelMesh.vertices, vertices, count);
            Array.Copy(m_ModelMesh.uv, uvs, count);
            for (int i = 0; i < count; i++) vertices[i].z = 0f;
            var mesh = new Mesh { name = "Photo Person Projectile Sheet" };
            if (count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
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
            m_RenderedRotation = m_ModelRoot.transform.localRotation;
        }

        void CancelPress()
        {
            ReleasePreviewBuild();
            m_PresentWhenReady = false;
            ResumeRecognitionAfterShowcase();
            m_PointerHeld = false;
            m_ModelCreated = false;
            m_PersonId = -1;
            DestroyModel();
        }

        void ClearSession()
        {
            ReleasePreviewBuild();
            m_PresentWhenReady = false;
            ResumeRecognitionAfterShowcase();
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

        void ResumeRecognitionAfterShowcase()
        {
            if (!m_RecognitionPausedForShowcase) return;
            m_Detection?.SetShowcaseBuilding(false);
            m_RecognitionPausedForShowcase = false;
        }

        void DestroyModel()
        {
            m_ModelDeformation?.Clear();
            if (m_ModelRoot != null) DestroyOwned(m_ModelRoot);
            if (m_ModelMesh != null) DestroyOwned(m_ModelMesh);
            if (m_ModelMaterial != null) DestroyOwned(m_ModelMaterial);
            if (m_BackMaterial != null) DestroyOwned(m_BackMaterial);
            if (m_Photo != null) DestroyOwned(m_Photo);
            m_ModelRoot = null;
            m_ModelMesh = null;
            m_ModelRenderer = null;
            m_ModelCollider = null;
            m_ModelDeformation = null;
            m_Deforming = false;
            m_ModelMaterial = m_BackMaterial = null;
            m_Photo = null;
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

        void OnDisable() => ClearSession();

        void OnDestroy()
        {
            ClearSession();
            m_Segmenter.Dispose();
            ClearSavedProjectile();
        }
    }
}
