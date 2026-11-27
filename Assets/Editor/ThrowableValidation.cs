using System;
using System.Reflection;
using EarthOL.ARCore;
using EarthOL.BodyThrow;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EarthOL.EditorTools
{
    public static class ThrowableValidation
    {
        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        public static void Validate()
        {
            var catalog = Resources.Load<BodyProjectileCatalog>("Throwables/Catalog");
            Check(catalog != null && catalog.entries.Length == 4, "Exactly four projectile choices");
            string[] keys = { "StickyHand", "Brain", "JellyCube", "Arrow" };
            float[] expectedLengths = { 0.24f, 0.24f, 0.36f, 0.44f };
            var root = new GameObject("ThrowableValidation");
            var cameraObject = new GameObject("ThrowableTestCamera");
            var rigObject = new GameObject("ThrowableTestBody");
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.transform.position = new Vector3(0f, 1.1f, 0f);
                var rig = rigObject.AddComponent<BodyProxyRig>();
                rig.BuildDefaultRig();
                rig.HideHead();
                rig.SetTorsoPose(new Vector3(0f, 1.5f, 2f), new Vector3(0f, 0.7f, 2f), 0.3f);
                var spawner = root.AddComponent<BodyProjectileSpawner>();
                spawner.ARCamera = camera;
                spawner.SelectedTarget = rig.transform;
                spawner.ConfigureCatalog(catalog);
                Check(spawner.SelectedProjectileIndex == 0 && spawner.ProjectilePrefab == catalog.entries[0].prefab, "Default must be 1号粘粘手");
                var firstShot = spawner.Throw(Vector3.forward);
                var secondShot = spawner.Throw(Vector3.forward);
                Check(firstShot != null && secondShot != null && spawner.ActiveProjectileCount == 2,
                    "Spawner must track active projectiles");
                spawner.ClearProjectiles();
                Check(spawner.ActiveProjectileCount == 0 && firstShot == null && secondShot == null,
                    "Clear option must destroy flying projectiles");
                Check(!spawner.SelectProjectile(-1) && !spawner.SelectProjectile(4) && spawner.SelectedProjectileIndex == 0,
                    "Invalid selection must not change current projectile");

                for (int i = 0; i < 4; i++)
                {
                    var entry = catalog.entries[i];
                    Check(entry.key == keys[i] && entry.prefab != null && entry.preview != null, "Ordered model, prefab and thumbnail");
                    Check(entry.prefab.transform.localScale == Vector3.one, "Projectile roots must have unit scale");
                    Check(entry.prefab.GetComponentsInChildren<MeshRenderer>().Length == 1, "Complete model mesh count");
                    Check(spawner.SelectProjectile(i), "Selection must succeed");
                    var shot = spawner.Throw(Vector3.forward);
                    Check(shot != null && shot.name.StartsWith(entry.prefab.name), "Shot must use the selected model");
                    try
                    {
                        var bounds = ThrowableAssetBuilder.BoundsOf(shot.gameObject);
                        var box = shot.GetComponent<BoxCollider>();
                        Check(Vector3.Distance(bounds.center, shot.transform.position) < 0.001f, "Visuals must be centered on collision/attachment root");
                        Check(Mathf.Abs(Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z) - expectedLengths[i]) < 0.001f,
                            "Each projectile must be exactly twice its original linear size");
                        Check(box.size.z >= 0.012f, "Thin-projectile collision thickness must also double");
                        float nearExtent = Vector3.Dot(shot.transform.position - camera.transform.position, shot.transform.forward)
                            - (box.size.z * 0.5f + Mathf.Abs(box.center.z));
                        Check(nearExtent > camera.nearClipPlane, "Large projectiles must spawn clear of the near clip plane");
                        if (entry.key == "JellyCube")
                        {
                            Check(Mathf.Abs(bounds.size.x - 0.36f) < 0.001f && Mathf.Abs(bounds.size.y - 0.36f) < 0.001f &&
                                Mathf.Abs(bounds.size.z - 0.36f) < 0.001f, "Slot 3 must use the new cube, not the former road sign");
                            var cubeMaterial = shot.GetComponentInChildren<Renderer>().sharedMaterial;
                            Check(Mathf.Abs(cubeMaterial.color.a - 0.8f) < 0.001f && cubeMaterial.renderQueue == 3000 &&
                                entry.displayName == "3号 · 方块", "Cube must use the 方块 label and alpha 0.8");
                        }
                        foreach (var renderer in shot.GetComponentsInChildren<Renderer>())
                            foreach (var material in renderer.sharedMaterials)
                                Check(material != null && material.mainTexture != null && !ShaderUtil.ShaderHasError(material.shader) && material.renderQueue > 1990,
                                    "Baked materials must render after invisible body depth");
                        Physics.SyncTransforms();
                        Check(shot.GetComponent<Rigidbody>().SweepTest(Vector3.forward, out var hit, 4f, QueryTriggerInteraction.Ignore), "Projectile sweep must reach the body");
                        var part = hit.collider.GetComponent<BodyPartProxy>();
                        Check(part != null, "Projectile must collide with a body proxy");
                        Invoke(shot, "Attach", part, hit.point, hit.normal);
                        Check(shot.transform.parent == part.AttachmentTransform && shot.GetComponent<Rigidbody>().isKinematic,
                            "Hit must attach to the body part");
                        Check(Vector3.Dot(shot.transform.forward, -hit.normal) > 0.99f, "Tip must point into the body after impact");
                        Check(Vector3.Distance(shot.transform.lossyScale, Vector3.one) < 0.001f, "Attached models must not stretch");
                        string attachedName = shot.name;
                        spawner.SelectProjectile((i + 1) % 4);
                        Check(shot.name == attachedName && shot.transform.parent == part.AttachmentTransform, "Changing selection must not replace existing shots");
                    }
                    finally { Object.DestroyImmediate(shot.gameObject); }
                }

                var tracker = root.AddComponent<BodyPoseTracker>();
                tracker.Configure(camera, rig);
                spawner.PoseTracker = tracker;
                var overlay = root.AddComponent<ARCoreModeOverlay>();
                overlay.Configure(null);
                SetField(tracker, "m_HasPendingTap", true);
                SetField(tracker, "m_HasProjectileTap", true);
                tracker.ClearPendingInput();
                Check(!(bool)Field(tracker, "m_HasPendingTap") && !tracker.TryConsumeProjectileTap(out _), "UI must clear delayed input");
                Invoke(overlay, "OpenProjectilePicker");
                Check(ARCoreModeOverlay.BlocksWorldInput && ARCoreModeOverlay.IsPointOnOverlay(Vector2.zero), "Picker must block the entire screen");
                Check(spawner.Throw(Vector3.forward) == null, "Picker must suppress spawning");
                SetField(tracker, "m_HasPendingTap", true);
                SetField(tracker, "m_HasProjectileTap", true);
                Invoke(tracker, "PollTapGesture");
                Check(!(bool)Field(tracker, "m_HasPendingTap") && !tracker.TryConsumeProjectileTap(out _), "Picker must not select people or queue shots");
                Invoke(overlay, "CloseProjectilePicker");
                Check(ARCoreModeOverlay.BlocksWorldInput, "Closing tap must not fall through to world input");
                Debug.Log("[ThrowableValidation] PASS: four ordered models/thumbnails, default 1, switching, fitted collisions, sweep/attachment, tip orientation and modal input isolation.");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(rigObject);
            }
            ValidateRendering(catalog);
        }

        static void ValidateRendering(BodyProjectileCatalog catalog)
        {
            var root = new GameObject("ThrowableRenderValidation");
            var target = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            try
            {
                var camera = root.AddComponent<Camera>();
                camera.enabled = false;
                camera.transform.position = new Vector3(0f, 0f, -2f);
                camera.orthographic = true;
                camera.orthographicSize = 0.15f;
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = 5f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.08f, 0.1f, 0.15f);
                camera.cullingMask = 1 << 30;
                camera.allowMSAA = camera.allowHDR = false;
                camera.targetTexture = target;
                var lightObject = new GameObject("ThrowableRenderLight");
                lightObject.transform.SetParent(root.transform, false);
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.5f;
                light.cullingMask = 1 << 30;
                lightObject.transform.rotation = Quaternion.Euler(30f, 20f, 0f);
                var occluder = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                occluder.transform.SetParent(root.transform, false);
                occluder.transform.position = Vector3.zero;
                occluder.transform.localScale = Vector3.one * 1.2f;
                occluder.layer = 30;
                occluder.GetComponent<Renderer>().sharedMaterial = Resources.Load<Material>("BodyDepthOccluder");
                occluder.SetActive(false);
                var baseline = Capture(camera, target);
                foreach (var entry in catalog.entries)
                {
                    var shot = Object.Instantiate(entry.prefab, root.transform);
                    try
                    {
                        shot.transform.position = new Vector3(0f, 0f, -1f);
                        if (entry.key == "Arrow") shot.transform.rotation = Quaternion.Euler(0f, 65f, 0f);
                        foreach (Transform child in shot.GetComponentsInChildren<Transform>()) child.gameObject.layer = 30;
                        occluder.SetActive(true);
                        int frontPixels = DifferentPixels(baseline, Capture(camera, target));
                        Check(frontPixels > 40, "Model must visibly render in front: " + entry.key);
                        shot.transform.position = new Vector3(0f, 0f, 0.8f);
                        int hiddenPixels = DifferentPixels(baseline, Capture(camera, target));
                        Check(hiddenPixels == 0, "Invisible body must occlude the model: " + entry.key);
                        Debug.Log($"[ThrowableValidation] GPU {entry.key}: front={frontPixels} pixels, behind={hiddenPixels} pixels.");
                    }
                    finally { Object.DestroyImmediate(shot.gameObject); }
                }
                Debug.Log("[ThrowableValidation] PASS: all four authored meshes/materials render and obey invisible body depth.");
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(root);
                target.Release();
                Object.DestroyImmediate(target);
            }
        }

        static Color32[] Capture(Camera camera, RenderTexture target)
        {
            camera.Render();
            RenderTexture.active = target;
            var texture = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
            texture.Apply();
            var pixels = texture.GetPixels32();
            Object.DestroyImmediate(texture);
            return pixels;
        }

        static int DifferentPixels(Color32[] a, Color32[] b)
        {
            int count = 0;
            for (int i = 0; i < a.Length; i++)
                if (Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b) > 15) count++;
            return count;
        }
        static object Field(object target, string name) => target.GetType().GetField(name, PrivateInstance).GetValue(target);
        static void SetField(object target, string name, object value) => target.GetType().GetField(name, PrivateInstance).SetValue(target, value);
        static object Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, PrivateInstance).Invoke(target, args);
        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("[ThrowableValidation] FAIL: " + message);
        }
    }
}
