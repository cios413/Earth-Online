using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using EarthOL.ARCore;
using EarthOL.BodyThrow;
using EarthOL.Deform;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace EarthOL.EditorTools
{
    public static class InteractionUpgradeValidation
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        public static void BuildAndValidate()
        {
            ThrowableAssetBuilder.BuildAndValidate();
            ValidateGesture();
            ValidateImpact();
            ValidateSpringAndRegion();
            ValidateWarpRendering();
            ValidateBootstrap();
            Debug.Log("[InteractionUpgrade] PASS: 方块 alpha/name, long-press wheel/cancel/taps, impact squash/recovery, person region, GPU warp/rebound and mode switching without demo primitives.");
        }

        public static void BuildValidateAndApk()
        {
            BuildAndValidate();
            AndroidBuild.BuildApk();
        }

        static BodyPoseResult Pose() => new BodyPoseResult
        {
            keypoints = new Vector2[17], keypointScores = new float[17], confidence = 1f
        };

        static void ValidateGesture()
        {
            Check(ProjectileRadialMath.IndexAt(Vector2.up * 100f, 30f) == 0 &&
                ProjectileRadialMath.IndexAt(Vector2.right * 100f, 30f) == 1 &&
                ProjectileRadialMath.IndexAt(Vector2.down * 100f, 30f) == 2 &&
                ProjectileRadialMath.IndexAt(Vector2.left * 100f, 30f) == 3 &&
                ProjectileRadialMath.IndexAt(Vector2.one, 30f) == -1, "Wheel directions and central cancel zone");
            var root = new GameObject("GestureUpgradeTest");
            try
            {
                var camera = new GameObject("Camera").AddComponent<Camera>();
                camera.transform.SetParent(root.transform, false);
                camera.enabled = false;
                var rig = new GameObject("Body").AddComponent<BodyProxyRig>();
                rig.transform.SetParent(root.transform, false);
                rig.BuildDefaultRig();
                var tracker = root.AddComponent<BodyPoseTracker>();
                tracker.Configure(camera, rig);
                rig.SetHeadPose(new Vector3(0, 1, 3), 0.1f);
                var spawner = root.AddComponent<BodyProjectileSpawner>();
                spawner.ConfigureCatalog(Resources.Load<BodyProjectileCatalog>("Throwables/Catalog"));
                var bootstrap = root.AddComponent<ARCoreMainSceneBootstrap>();
                typeof(ARCoreMainSceneBootstrap).GetProperty("Spawner").SetValue(bootstrap, spawner);
                typeof(ARCoreMainSceneBootstrap).GetProperty("PoseTracker").SetValue(bootstrap, tracker);
                var overlay = root.AddComponent<ARCoreModeOverlay>();
                overlay.Configure(bootstrap);
                tracker.SetDeformationMode(false);
                Set(tracker, "m_Current", Pose());
                Set(tracker, "m_TrackingEnabled", true);
                Set(tracker, "m_LastSeenTime", Time.realtimeSinceStartup);
                Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                Frame(tracker, center, 10f, true, true, false);
                Frame(tracker, center, 10.3f, false, true, false);
                Check(!tracker.TryConsumeProjectileTap(out _), "Holding must never fire a projectile before the wheel opens");
                Frame(tracker, center, 10.5f, false, true, false);
                Check(ARCoreModeOverlay.IsRadialHoldOpen && ARCoreModeOverlay.BlocksWorldInput, "Long press must open a blocking radial wheel");
                Vector2 wheelCenter = (Vector2)Field(overlay, "m_WheelCenter");
                float radius = (float)typeof(ARCoreModeOverlay).GetProperty("WheelRadius", Private).GetValue(overlay);
                Frame(tracker, wheelCenter + Vector2.right * radius * 0.7f, 10.6f, false, true, false);
                Frame(tracker, wheelCenter + Vector2.right * radius * 0.7f, 10.7f, false, false, true);
                Check(spawner.SelectedProjectileIndex == 1 && !ARCoreModeOverlay.IsRadialHoldOpen && !tracker.TryConsumeProjectileTap(out _),
                    "Release over the right sector must select 2 without throwing");
                Set(overlay, "m_InputBlockedUntil", -1f);
                Frame(tracker, center, 12f, true, true, false);
                Frame(tracker, center, 12.5f, false, true, false);
                wheelCenter = (Vector2)Field(overlay, "m_WheelCenter");
                Frame(tracker, wheelCenter + Vector2.down * radius * 0.7f, 12.6f, false, true, false);
                Frame(tracker, wheelCenter, 12.7f, false, false, true);
                Check(spawner.SelectedProjectileIndex == 1, "Returning to wheel center must cancel the change");
                overlay.BeginRadialSelection(new Vector2(2f, 2f));
                overlay.UpdateRadialSelection(new Vector2(2f, 2f));
                Check((int)Field(overlay, "m_RadialIndex") == -1, "Clamping a wheel near the screen edge must not select without finger motion");
                overlay.EndRadialSelection(false);
                Check(spawner.SelectedProjectileIndex == 1, "A stationary hold at the screen edge must preserve selection");
                Set(overlay, "m_InputBlockedUntil", -1f);
                Frame(tracker, center, 20f, true, true, false);
                Frame(tracker, center, 20.05f, false, false, true);
                Invoke(tracker, "ProcessWorldPointer", default(BodyPointerFrame), false, 20.5f);
                Check(tracker.TryConsumeProjectileTap(out _) && !tracker.TryConsumeProjectileTap(out _), "Short tap must still produce exactly one throw");
                Frame(tracker, center, 21f, true, true, false);
                Frame(tracker, center, 21.04f, false, false, true);
                Frame(tracker, center, 21.12f, true, true, false);
                Frame(tracker, center, 21.17f, false, false, true);
                Check((bool)Field(tracker, "m_SelectionPending") && !tracker.TryConsumeProjectileTap(out _), "Double tap must select a person without throwing");
                Set(tracker, "m_SelectionPending", false);
                tracker.SetDeformationMode(true);
                Frame(tracker, center, 30f, true, true, false);
                Frame(tracker, center, 30.7f, false, true, false);
                Frame(tracker, center, 30.8f, false, false, true);
                Check(!ARCoreModeOverlay.IsRadialHoldOpen && !(bool)Field(tracker, "m_SelectionPending") && !tracker.TryConsumeProjectileTap(out _),
                    "Holding in deformation mode must neither open the weapon wheel nor throw");
                Set(tracker, "m_Current", Pose());
                Set(tracker, "m_LastSeenTime", 50f); Set(tracker, "m_HeadSeenTime", 51f); Set(tracker, "m_TorsoSeenTime", 52f);
                Check((bool)Invoke(tracker, "PausePoseForPicker", true, 100f), "Picker must defer expensive inference");
                Invoke(tracker, "PausePoseForPicker", true, 101f);
                Check(!(bool)Invoke(tracker, "PausePoseForPicker", false, 103f) && tracker.HasPose &&
                    Mathf.Abs((float)Field(tracker, "m_LastSeenTime") - 53f) < 0.001f &&
                    Mathf.Abs((float)Field(tracker, "m_HeadSeenTime") - 54f) < 0.001f,
                    "Picker pause must preserve selection and suspend loss timers until release");
                Debug.Log("[InteractionUpgrade] Gesture regression PASS.");
            }
            finally { Object.DestroyImmediate(root); }
        }

        static void Frame(BodyPoseTracker tracker, Vector2 position, float time, bool down, bool held, bool up) =>
            Invoke(tracker, "ProcessWorldPointer", new BodyPointerFrame { id = 7, position = position, down = down, held = held, released = up }, true, time);

        static void ValidateImpact()
        {
            foreach (var entry in Resources.Load<BodyProjectileCatalog>("Throwables/Catalog").entries)
            {
                var shot = Object.Instantiate(entry.prefab);
                try
                {
                    var visual = shot.transform.Find("Visual");
                    var scale = visual.localScale;
                    var position = visual.localPosition;
                    var rootPosition = shot.transform.position;
                    var rootRotation = shot.transform.rotation;
                    var box = shot.GetComponent<BoxCollider>();
                    float end = box.center.z + box.size.z * 0.5f;
                    Vector3 contact = shot.transform.TransformPoint(Vector3.forward * end);
                    Vector3 visualAnchor = visual.InverseTransformPoint(contact);
                    var bounce = shot.GetComponent<ProjectileImpactBounce>();
                    Check(bounce != null, "Every projectile must have impact deformation");
                    bounce.Play(end);
                    Check(visual.localScale.z < scale.z && visual.localScale.x > scale.x, "Impact must compress depth and widen the model");
                    Check(Vector3.Distance(visual.TransformPoint(visualAnchor), contact) < 0.0001f, "Impact contact end must remain anchored");
                    Vector3 ratio = new Vector3(visual.localScale.x / scale.x, visual.localScale.y / scale.y, visual.localScale.z / scale.z);
                    Check(Mathf.Abs(ratio.x * ratio.y * ratio.z - 1f) < 0.0001f, "Squash should conserve volume");
                    bounce.Advance(0.17f);
                    Check(visual.localScale.z > scale.z, "Impact must overshoot for a visible bounce");
                    bounce.Advance(1f);
                    Check(!bounce.IsBouncing && visual.localScale == scale && visual.localPosition == position, "Impact must restore exact original geometry");
                    Check(shot.transform.position == rootPosition && shot.transform.rotation == rootRotation && shot.transform.localScale == Vector3.one,
                        "Impact animation must not move or scale the attachment root");
                }
                finally { Object.DestroyImmediate(shot.gameObject); }
            }
            Debug.Log("[InteractionUpgrade] Four-model impact/recovery regression PASS.");
        }

        static PokeWarpDeformer CreateWarp(GameObject root, float width = 1.2f, float height = 1.2f)
        {
            root.SetActive(false);
            var warp = root.AddComponent<PokeWarpDeformer>();
            warp.createCollider = false;
            warp.lockPokesToSurface = true;
            warp.planeInXZ = false;
            warp.planeWidth = width;
            warp.planeHeight = height;
            warp.gridX = 48; warp.gridY = 64;
            warp.warpMaterial = Resources.Load<Material>("PersonWarp");
            warp.logPokes = false;
            warp.Initialize();
            root.SetActive(true);
            return warp;
        }

        static void ValidateSpringAndRegion()
        {
            var root = new GameObject("PersonWarpRegionTest");
            try
            {
                var cameraObject = new GameObject("Camera"); cameraObject.transform.SetParent(root.transform, false);
                var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
                camera.nearClipPlane = 0.01f; camera.pixelRect = new Rect(0, 0, 1000, 1000);
                var rigObject = new GameObject("Body"); rigObject.transform.SetParent(root.transform, false);
                var rig = rigObject.AddComponent<BodyProxyRig>(); rig.BuildDefaultRig();
                var tracker = root.AddComponent<BodyPoseTracker>(); tracker.Configure(camera, rig);
                rig.SetHeadPose(new Vector3(0f, 0.7f, 3f), 0.13f);
                rig.SetTorsoPose(new Vector3(0f, 0.45f, 3f), new Vector3(0f, -0.4f, 3f), 0.25f);
                Physics.SyncTransforms(); Set(tracker, "m_Current", Pose());
                var controller = root.AddComponent<PersonDeformationSurface>(); controller.Configure(camera, rig, tracker);
                Check(controller.RefreshRegion(1f), "Reliable body must create a deformation region");
                Rect region = controller.Region;
                Vector3 torso = camera.WorldToViewportPoint(new Vector3(0f, 0f, 3f));
                Check(region.Contains(torso) && region.width > 0.2f && region.xMin >= 0 && region.xMax <= 1f,
                    "Region must include body and surrounding padding, clamped to the viewport");
                Check(!controller.TryBegin(1, new Vector2(2f, 2f)), "Touches outside the person region must be ignored");
                Vector3 screen = camera.ViewportToScreenPoint(region.center);
                Check(controller.TryBegin(17, screen), "Touch inside region must deform the camera image");
                var warp = controller.Warp;
                var block = new MaterialPropertyBlock();
                for (int i = 0; i < 60; i++) warp.Advance(1f / 120f);
                warp.GetComponent<Renderer>().GetPropertyBlock(block);
                Check(Mathf.Abs(block.GetFloat("_PokeAmp0") - 0.45f) < 0.001f, "Holding must retain the old deformation strength");
                rig.SetTorsoPose(new Vector3(0.4f, 0.45f, 3f), new Vector3(0.4f, -0.4f, 3f), 0.25f);
                Physics.SyncTransforms(); controller.RefreshRegion(1f);
                Check(controller.Region == region, "Held/rebounding region must not jump with detection updates");
                controller.EndPointer();
                bool overshot = false;
                for (int i = 0; i < 600; i++)
                {
                    warp.Advance(1f / 120f);
                    warp.GetComponent<Renderer>().GetPropertyBlock(block);
                    overshot |= block.GetFloat("_PokeAmp0") < -0.01f;
                }
                Check(overshot && !warp.HasActivePokes && block.GetFloat("_PokeAmp0") == 0f, "Release must overshoot and settle back to the original image");
                controller.RefreshRegion(1f);
                Check(controller.Region.center.x > region.center.x + 0.01f, "Idle region must follow the person again");
                rig.HideTrackedParts();
                Check(!controller.RefreshRegion(1f) && !warp.gameObject.activeSelf, "Lost body must disable the deformation region");
                var fastObject = new GameObject("Spring30fps"); fastObject.transform.SetParent(root.transform, false);
                var slowObject = new GameObject("Spring5fps"); slowObject.transform.SetParent(root.transform, false);
                var fast = CreateWarp(fastObject); var slow = CreateWarp(slowObject);
                fast.BeginAt(1, fast.transform.position); slow.BeginAt(1, slow.transform.position);
                fast.OnPokeEnd(1); slow.OnPokeEnd(1);
                for (int i = 0; i < 30; i++) fast.Advance(1f / 30f);
                for (int i = 0; i < 5; i++) slow.Advance(0.2f);
                fast.GetComponent<Renderer>().GetPropertyBlock(block); float fastAmplitude = block.GetFloat("_PokeAmp0");
                slow.GetComponent<Renderer>().GetPropertyBlock(block);
                Check(Mathf.Abs(fastAmplitude - block.GetFloat("_PokeAmp0")) < 0.005f,
                    "Spring rebound timing must remain consistent under slow inference frames");
                Debug.Log("[InteractionUpgrade] Person-region hold/release/spring regression PASS.");
            }
            finally { Object.DestroyImmediate(root); }
        }

        static void ValidateWarpRendering()
        {
            var material = Resources.Load<Material>("PersonWarp");
            Check(material != null && material.shader.isSupported && !ShaderUtil.ShaderHasError(material.shader), "Person warp shader must compile");
            var root = new GameObject("PersonWarpGpuTest");
            var target = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32);
            var pattern = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            var commands = new CommandBuffer { name = "AR background for person warp regression" };
            var images = new List<Texture2D>();
            var previous = RenderTexture.active;
            Camera camera = null;
            try
            {
                camera = root.AddComponent<Camera>(); camera.enabled = false;
                camera.transform.position = new Vector3(0, 0, -2);
                camera.orthographic = true; camera.orthographicSize = 1f;
                camera.nearClipPlane = 0.01f; camera.farClipPlane = 5f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.cullingMask = 1 << 30; camera.allowHDR = camera.allowMSAA = false;
                camera.targetTexture = target;
                var pixels = new Color32[256 * 256];
                for (int y = 0; y < 256; y++) for (int x = 0; x < 256; x++)
                    pixels[y * 256 + x] = new Color(((x / 8 + y / 8) % 2 == 0) ? 0.8f : 0.2f, y / 300f, 0.2f + x / 512f);
                pattern.SetPixels32(pixels); pattern.Apply();
                commands.Blit(pattern, BuiltinRenderTextureType.CameraTarget);
                commands.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
                camera.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, commands);
                Texture2D Capture()
                {
                    camera.Render(); RenderTexture.active = target;
                    var image = new Texture2D(256, 256, TextureFormat.RGBA32, false);
                    image.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); image.Apply(); images.Add(image); return image;
                }
                var baseline = Capture();
                var surface = new GameObject("WarpSurface"); surface.transform.SetParent(root.transform, false);
                surface.transform.position = Vector3.zero; surface.transform.rotation = Quaternion.Euler(0, 180, 0); surface.layer = 30;
                var warp = CreateWarp(surface);
                var idle = Capture();
                Check(MaxDifference(baseline, idle) < 0.02f, "Warp at rest must show the original camera image with correct orientation");
                warp.BeginAt(3, Vector3.zero);
                var pressed = Capture();
                int changed = 0;
                var a = baseline.GetPixels(); var b = pressed.GetPixels();
                for (int i = 0; i < a.Length; i++) if (Difference(a[i], b[i]) > 0.04f) changed++;
                Check(changed > 500, "Pressing must visibly warp the camera image");
                Check(Difference(baseline.GetPixel(10, 10), pressed.GetPixel(10, 10)) < 0.01f, "Outside the person surface must remain unchanged");
                warp.OnPokeEnd(3);
                for (int i = 0; i < 600; i++) warp.Advance(1f / 120f);
                var restored = Capture();
                Check(MaxDifference(baseline, restored) < 0.02f, "After spring recovery the original image must be restored");
                string directory = Path.Combine(Path.GetTempPath(), "opencode");
                if (Directory.Exists(directory))
                {
                    var preview = new Texture2D(768, 256, TextureFormat.RGBA32, false);
                    preview.SetPixels(0, 0, 256, 256, baseline.GetPixels()); preview.SetPixels(256, 0, 256, 256, pressed.GetPixels());
                    preview.SetPixels(512, 0, 256, 256, restored.GetPixels()); preview.Apply();
                    File.WriteAllBytes(Path.Combine(directory, "person-deformation-preview.png"), preview.EncodeToPNG());
                    Object.DestroyImmediate(preview);
                }
                Debug.Log($"[InteractionUpgrade] GPU person warp PASS: {changed} changed pixels, outside preserved, exact recovery.");
            }
            finally
            {
                if (camera != null) camera.RemoveCommandBuffer(CameraEvent.BeforeForwardOpaque, commands);
                commands.Release(); RenderTexture.active = previous;
                Object.DestroyImmediate(root); target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(pattern);
                foreach (var image in images) Object.DestroyImmediate(image);
            }
        }

        static void ValidateBootstrap()
        {
            var scene = SceneManager.GetActiveScene();
            var original = new HashSet<int>(); foreach (var go in scene.GetRootGameObjects()) original.Add(go.GetInstanceID());
            try
            {
                var bootstrap = new GameObject("BootstrapUpgradeTest").AddComponent<ARCoreMainSceneBootstrap>();
                var start = (IEnumerator)Invoke(bootstrap, "Start"); start.MoveNext();
                Check(bootstrap.PersonDeformer != null && bootstrap.PoseTracker.DeformationMode && !bootstrap.Spawner.enabled,
                    "Startup must use person deformation mode");
                Check(GameObject.Find("AR Soft Objects") == null && GameObject.Find("Soft Cube") == null && GameObject.Find("Soft Sphere") == null,
                    "Deformation mode must not create demo cubes or spheres");
                Set(bootstrap.PoseTracker, "m_Current", Pose());
                bootstrap.SetMode(GameMode.Throw);
                Check(bootstrap.PoseTracker.HasPose && !bootstrap.PersonDeformer.enabled && bootstrap.Spawner.enabled && !bootstrap.PoseTracker.DeformationMode,
                    "Switching to throw must preserve selected person");
                bootstrap.SetMode(GameMode.Deform);
                Check(bootstrap.PoseTracker.HasPose && bootstrap.PersonDeformer.enabled && bootstrap.PoseTracker.enabled,
                    "Switching back must preserve tracking and enable person warp");
                Debug.Log("[InteractionUpgrade] Bootstrap/default-primitives/mode regression PASS.");
            }
            finally
            {
                foreach (var go in scene.GetRootGameObjects()) if (!original.Contains(go.GetInstanceID())) Object.DestroyImmediate(go);
            }
        }

        static float Difference(Color a, Color b) => Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b));
        static float MaxDifference(Texture2D a, Texture2D b)
        {
            var aa = a.GetPixels(); var bb = b.GetPixels(); float max = 0;
            for (int i = 0; i < aa.Length; i++) max = Mathf.Max(max, Difference(aa[i], bb[i])); return max;
        }
        static object Field(object target, string name) => target.GetType().GetField(name, Private).GetValue(target);
        static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
        static object Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);
        static void Check(bool passed, string message)
        {
            if (!passed) throw new InvalidOperationException("[InteractionUpgrade] FAIL: " + message);
        }
    }
}
