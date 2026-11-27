using System;
using System.Collections.Generic;
using System.IO;
using EarthOL.BodyThrow;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace EarthOL.EditorTools
{
    /// <summary>GPU regressions for invisible proxy depth using the actual projectile prefab.</summary>
    public static class BodyOcclusionValidation
    {
        public static void ValidateAndBuild()
        {
            Validate();
            AndroidBuild.BuildApk();
        }

        public static void Validate()
        {
            var material = Resources.Load<Material>("BodyDepthOccluder");
            var prefab = Resources.Load<BodyProjectile>("BodyProjectile");
            Check(material != null && prefab != null, "Depth material and projectile must exist");
            Check(material.shader.isSupported && !ShaderUtil.ShaderHasError(material.shader), "Depth shader compilation");
            Check(material.renderQueue < prefab.GetComponent<Renderer>().sharedMaterial.renderQueue,
                "Occluder must render before the actual projectile material");

            var sceneRoot = new GameObject("BodyOcclusionValidation");
            var rigRoot = new GameObject("InvisibleBodyRig");
            rigRoot.transform.SetParent(sceneRoot.transform, false);
            var target = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32);
            var pattern = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            var images = new List<Texture2D>();
            var previous = RenderTexture.active;
            var backgroundCommands = new CommandBuffer { name = "Simulated AR background before opaque geometry" };
            Camera camera = null;
            try
            {
                var cameraObject = new GameObject("OcclusionTestCamera");
                cameraObject.transform.SetParent(sceneRoot.transform, false);
                camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.transform.position = new Vector3(0f, 1.25f, -3f);
                camera.orthographic = true;
                camera.orthographicSize = 1f;
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = 10f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.cullingMask = 1 << 30;
                camera.allowMSAA = camera.allowHDR = false;
                camera.targetTexture = target;

                var pixels = new Color32[256 * 256];
                for (int y = 0; y < 256; y++)
                    for (int x = 0; x < 256; x++)
                        pixels[y * 256 + x] = new Color(0.05f + x / 1024f, 0.1f + y / 1024f,
                            ((x / 8 + y / 8) % 2 == 0) ? 0.35f : 0.15f);
                pattern.SetPixels32(pixels);
                pattern.Apply();
                // Like ARCameraBackground, this draws color before opaque geometry.
                backgroundCommands.Blit(pattern, BuiltinRenderTextureType.CameraTarget);
                backgroundCommands.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
                camera.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, backgroundCommands);

                var lightObject = new GameObject("OcclusionTestLight");
                lightObject.transform.SetParent(sceneRoot.transform, false);
                lightObject.transform.rotation = Quaternion.Euler(25f, 30f, 0f);
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.cullingMask = 1 << 30;
                light.shadows = LightShadows.None;

                var rig = rigRoot.AddComponent<BodyProxyRig>();
                rig.BuildDefaultRig();
                rig.SetHeadPose(new Vector3(-0.25f, 1.8f, 0f), 0.18f);
                rig.SetTorsoPose(new Vector3(-0.25f, 1.5f, 0f), new Vector3(-0.25f, 0.7f, 0f), 0.24f);
                foreach (var part in rig.Parts)
                {
                    var renderer = part.GetComponentInChildren<Renderer>();
                    renderer.gameObject.layer = 30;
                    Check(renderer.enabled && renderer.sharedMaterial == material, "Invisible mesh must still render depth");
                    Check(renderer.shadowCastingMode == ShadowCastingMode.Off && !renderer.receiveShadows, "Proxy shadows disabled");
                }
                rigRoot.SetActive(false);
                var background = Capture(camera, target, images);
                rigRoot.SetActive(true);
                var invisible = Capture(camera, target, images);
                Check(MaxDifference(background, invisible) < 0.005f, "Invisible proxies must not tint, blur or replace the AR image");

                var front = Projectile(prefab, sceneRoot.transform, new Vector3(-0.25f, 1.33f, -0.5f), Vector3.one * 0.16f);
                var behind = Projectile(prefab, sceneRoot.transform, new Vector3(-0.25f, 0.94f, 0.4f), Vector3.one * 0.16f);
                var headBehind = Projectile(prefab, sceneRoot.transform, new Vector3(-0.25f, 1.8f, 0.4f), Vector3.one * 0.15f);
                var partial = Projectile(prefab, sceneRoot.transform, new Vector3(-0.01f, 1.12f, 0.4f), Vector3.one * 0.20f);
                var outside = Projectile(prefab, sceneRoot.transform, new Vector3(0.5f, 1.05f, 0.4f), Vector3.one * 0.16f);
                rigRoot.SetActive(false);
                var reference = Capture(camera, target, images);
                rigRoot.SetActive(true);
                var occluded = Capture(camera, target, images);
                float frontFraction = Visibility(camera, front, background, reference, occluded);
                float behindFraction = Visibility(camera, behind, background, reference, occluded);
                float headFraction = Visibility(camera, headBehind, background, reference, occluded);
                float partialFraction = Visibility(camera, partial, background, reference, occluded);
                float outsideFraction = Visibility(camera, outside, background, reference, occluded);
                Debug.Log($"[BodyOcclusionValidation] Visible fractions front={frontFraction:F2}, behind={behindFraction:F2}, " +
                    $"head={headFraction:F2}, partial={partialFraction:F2}, outside={outsideFraction:F2}");
                SavePreview(background, reference, occluded);
                Check(frontFraction > 0.95f && outsideFraction > 0.95f, "Projectiles in front/outside must remain visible");
                Check(behindFraction < 0.05f && headFraction < 0.05f, "Both torso and head must hide projectiles behind them");
                Check(partialFraction > 0.15f && partialFraction < 0.85f, "Only the overlapping portion must disappear");

                rig.HideHead();
                var lostHead = Capture(camera, target, images);
                Check(Visibility(camera, headBehind, background, reference, lostHead) > 0.95f &&
                    Visibility(camera, behind, background, reference, lostHead) < 0.05f,
                    "An expired head must stop occluding while the torso remains active");
                rig.HideTrackedParts();
                Check(MaxDifference(reference, Capture(camera, target, images)) < 0.005f, "Expired proxies must leave no stale depth");

                foreach (var renderer in new[] { front, behind, headBehind, partial, outside }) renderer.gameObject.SetActive(false);
                var crossing = Projectile(prefab, sceneRoot.transform, new Vector3(-0.25f, 1.12f, -0.22f), new Vector3(0.32f, 0.16f, 0.08f));
                crossing.transform.rotation = Quaternion.Euler(0f, 55f, 0f);
                rigRoot.SetActive(false);
                var crossingReference = Capture(camera, target, images);
                rigRoot.SetActive(true);
                rig.SetTorsoPose(new Vector3(-0.25f, 1.5f, 0f), new Vector3(-0.25f, 0.7f, 0f), 0.24f);
                var crossingOccluded = Capture(camera, target, images);
                float crossingFraction = Visibility(camera, crossing, background, crossingReference, crossingOccluded);
                Debug.Log($"[BodyOcclusionValidation] Intersecting projectile visible fraction={crossingFraction:F2}");
                Check(crossingFraction > 0.1f && crossingFraction < 0.9f, "Intersecting geometry must clip per pixel at the body surface");
                Debug.Log("[BodyOcclusionValidation] PASS: no proxy color/shadows, foreground visibility, head/torso depth, " +
                    "partial occlusion, surface intersection and loss cleanup with the actual projectile prefab.");
            }
            finally
            {
                if (camera != null) camera.RemoveCommandBuffer(CameraEvent.BeforeForwardOpaque, backgroundCommands);
                backgroundCommands.Release();
                RenderTexture.active = previous;
                Object.DestroyImmediate(sceneRoot);
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(pattern);
                foreach (var image in images) Object.DestroyImmediate(image);
            }
        }

        static Renderer Projectile(BodyProjectile prefab, Transform root, Vector3 position, Vector3 scale)
        {
            var projectile = Object.Instantiate(prefab, position, Quaternion.identity, root);
            projectile.enabled = false;
            projectile.transform.localScale = scale;
            var renderer = projectile.GetComponent<Renderer>();
            renderer.gameObject.layer = 30;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return renderer;
        }

        static Texture2D Capture(Camera camera, RenderTexture target, List<Texture2D> images)
        {
            camera.Render();
            RenderTexture.active = target;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            image.Apply();
            images.Add(image);
            return image;
        }

        static float Visibility(Camera camera, Renderer renderer, Texture2D background, Texture2D reference, Texture2D result)
        {
            var bounds = renderer.bounds;
            Vector2 min = Vector2.one * 256f, max = Vector2.zero;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 screen = camera.WorldToScreenPoint(corner);
                min = Vector2.Min(min, screen);
                max = Vector2.Max(max, screen);
            }
            int count = 0, visible = 0;
            for (int y = Mathf.Max(0, Mathf.FloorToInt(min.y)); y < Mathf.Min(256, Mathf.CeilToInt(max.y)); y++)
                for (int x = Mathf.Max(0, Mathf.FloorToInt(min.x)); x < Mathf.Min(256, Mathf.CeilToInt(max.x)); x++)
                {
                    Color bg = background.GetPixel(x, y);
                    if (Difference(reference.GetPixel(x, y), bg) < 0.08f) continue;
                    count++;
                    if (Difference(result.GetPixel(x, y), bg) > 0.08f) visible++;
                }
            Check(count > 20, "Reference projectile must be visibly rendered");
            return (float)visible / count;
        }

        static float MaxDifference(Texture2D a, Texture2D b)
        {
            var first = a.GetPixels();
            var second = b.GetPixels();
            float max = 0f;
            for (int i = 0; i < first.Length; i++) max = Mathf.Max(max, Difference(first[i], second[i]));
            return max;
        }

        static float Difference(Color a, Color b) => Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b));

        static void SavePreview(Texture2D background, Texture2D reference, Texture2D occluded)
        {
            string directory = Path.Combine(Path.GetTempPath(), "opencode");
            if (!Directory.Exists(directory)) return;
            var image = new Texture2D(768, 256, TextureFormat.RGBA32, false);
            image.SetPixels(0, 0, 256, 256, background.GetPixels());
            image.SetPixels(256, 0, 256, 256, reference.GetPixels());
            image.SetPixels(512, 0, 256, 256, occluded.GetPixels());
            image.Apply();
            File.WriteAllBytes(Path.Combine(directory, "body-occlusion-preview.png"), image.EncodeToPNG());
            Object.DestroyImmediate(image);
        }

        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("[BodyOcclusionValidation] FAIL: " + message);
        }
    }
}
