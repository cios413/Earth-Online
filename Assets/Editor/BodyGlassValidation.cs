using System;
using System.IO;
using System.Reflection;
using EarthOL.BodyThrow;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace EarthOL.EditorTools
{
    /// <summary>Renders an asymmetric background to verify blur, UVs and depth.</summary>
    public static class BodyGlassValidation
    {
        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        public static void ValidateAndBuild()
        {
            Validate();
            AndroidBuild.BuildApk();
        }

        public static void Validate()
        {
            var glassAsset = Resources.Load<Material>("BodyFrostedGlass");
            var blurAsset = Resources.Load<Material>("BodyGlassBlur");
            Check(glassAsset != null && blurAsset != null, "Glass material resources must exist");
            Check(glassAsset.shader.isSupported && blurAsset.shader.isSupported, "Glass shaders must be supported");
            Check(!ShaderUtil.ShaderHasError(glassAsset.shader) && !ShaderUtil.ShaderHasError(blurAsset.shader), "Shader compilation");
            Check(glassAsset.renderQueue == 3000 && blurAsset.passCount == 2, "Transparent queue and two-pass blur");

            var root = new GameObject("BodyGlassValidation");
            var target = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32);
            var resized = new RenderTexture(128, 256, 24, RenderTextureFormat.ARGB32);
            var pattern = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            var backgroundMaterial = new Material(Shader.Find("Unlit/Texture"));
            var projectileMaterial = new Material(Shader.Find("Unlit/Color"));
            var glassMaterial = new Material(glassAsset);
            Texture2D before = null, after = null;
            BodyGlassRenderer renderer = null;
            var previousActive = RenderTexture.active;
            try
            {
                var camera = root.AddComponent<Camera>();
                camera.enabled = false;
                camera.transform.position = new Vector3(0f, 0f, -3f);
                camera.orthographic = true;
                camera.orthographicSize = 1f;
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = 10f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.cullingMask = 1 << 30;
                camera.allowHDR = camera.allowMSAA = false;
                camera.targetTexture = target;

                var pixels = new Color32[256 * 256];
                for (int y = 0; y < 256; y++)
                    for (int x = 0; x < 256; x++)
                    {
                        float checker = ((x / 4 + y / 4) % 2 == 0) ? 0.1f : -0.1f;
                        Color color = y >= 128 ? new Color(0.2f, 0.35f, 0.7f) : new Color(0.7f, 0.35f, 0.2f);
                        pixels[y * 256 + x] = color + new Color(checker, checker, checker, 0f);
                    }
                pattern.SetPixels32(pixels);
                pattern.filterMode = FilterMode.Point;
                pattern.Apply();
                backgroundMaterial.mainTexture = pattern;
                CreatePrimitive(root.transform, PrimitiveType.Quad, backgroundMaterial,
                    new Vector3(0f, 0f, 1f), new Vector3(2f, 2f, 1f));
                projectileMaterial.color = new Color(0.05f, 0.9f, 0.1f);
                CreatePrimitive(root.transform, PrimitiveType.Cube, projectileMaterial,
                    new Vector3(0.25f, 0.25f, -0.5f), Vector3.one * 0.12f);
                var surface = CreatePrimitive(root.transform, PrimitiveType.Quad, glassMaterial,
                    Vector3.zero, new Vector3(0.9f, 0.9f, 1f));
                surface.SetActive(false);
                camera.Render();
                before = Read(target);

                // Isolate image transmission from the artistic tint and rim.
                glassMaterial.SetColor("_Tint", new Color(1f, 1f, 1f, 0f));
                glassMaterial.SetFloat("_RimStrength", 0f);
                renderer = root.AddComponent<BodyGlassRenderer>();
                Invoke(renderer, "OnPreCull");
                surface.SetActive(true);
                camera.Render();
                after = Read(target);

                float inputContrast = Contrast(before);
                float glassContrast = Contrast(after);
                Debug.Log($"[BodyGlassValidation] Background detail contrast {inputContrast:F3} -> {glassContrast:F3}");
                SavePreview(before, after);
                Check(inputContrast > 0.1f && glassContrast < inputContrast * 0.5f, "Glass must visibly blur background detail");
                Color top = Average(after, 96, 144);
                Color bottom = Average(after, 96, 96);
                Check(top.b > top.r + 0.2f && bottom.r > bottom.b + 0.2f, "Screen transmission must preserve vertical orientation");
                Check(ColorDistance(before.GetPixel(20, 20), after.GetPixel(20, 20)) < 0.02f, "Outside glass must stay sharp and unchanged");
                Check(ColorDistance(before.GetPixel(160, 160), after.GetPixel(160, 160)) < 0.02f,
                    "Opaque projectiles in front of glass must remain clear");
                Check(ColorDistance(Average(before, 96, 144), top) < 0.08f, "Transparent glass must preserve background color");

                camera.targetTexture = resized;
                Invoke(renderer, "OnPreCull");
                var buffer = (RenderTexture)typeof(BodyGlassRenderer).GetField("m_Background", PrivateInstance).GetValue(renderer);
                Check(buffer.width == 32 && buffer.height == 64, "Blur buffers must resize with the camera");
                Check(camera.GetCommandBuffers(CameraEvent.BeforeForwardAlpha).Length == 1, "Resize must not accumulate command buffers");
                Invoke(renderer, "OnDisable");
                Check(camera.GetCommandBuffers(CameraEvent.BeforeForwardAlpha).Length == 0, "Disable must release rendering commands");
                Debug.Log("[BodyGlassValidation] PASS: GPU blur, transparent color, screen orientation, foreground depth, resize and cleanup.");
            }
            finally
            {
                RenderTexture.active = previousActive;
                if (renderer != null) Invoke(renderer, "OnDisable");
                Object.DestroyImmediate(root);
                target.Release(); resized.Release();
                Object.DestroyImmediate(target); Object.DestroyImmediate(resized);
                Object.DestroyImmediate(pattern); Object.DestroyImmediate(backgroundMaterial);
                Object.DestroyImmediate(projectileMaterial); Object.DestroyImmediate(glassMaterial);
                if (before != null) Object.DestroyImmediate(before);
                if (after != null) Object.DestroyImmediate(after);
            }
        }

        static GameObject CreatePrimitive(Transform root, PrimitiveType type, Material material, Vector3 position, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(type);
            go.transform.SetParent(root, false);
            go.transform.position = position;
            go.transform.localScale = scale;
            go.layer = 30;
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        static Texture2D Read(RenderTexture source)
        {
            var texture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            RenderTexture.active = source;
            texture.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            texture.Apply();
            return texture;
        }

        static float Contrast(Texture2D texture)
        {
            float contrast = 0f;
            for (int y = 96; y < 160; y++)
                for (int x = 96; x < 136; x++)
                    contrast += Mathf.Abs(texture.GetPixel(x, y).r - texture.GetPixel(x + 4, y).r);
            return contrast / (64 * 40);
        }

        static Color Average(Texture2D texture, int startX, int startY)
        {
            Color color = Color.clear;
            for (int y = startY; y < startY + 8; y++)
                for (int x = startX; x < startX + 8; x++) color += texture.GetPixel(x, y);
            return color / 64f;
        }

        static float ColorDistance(Color a, Color b) => Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b));
        static void Invoke(object target, string method) => target.GetType().GetMethod(method, PrivateInstance).Invoke(target, null);

        static void SavePreview(Texture2D before, Texture2D after)
        {
            string directory = Path.Combine(Path.GetTempPath(), "opencode");
            if (!Directory.Exists(directory)) return;
            var preview = new Texture2D(512, 256, TextureFormat.RGBA32, false);
            preview.SetPixels(0, 0, 256, 256, before.GetPixels());
            preview.SetPixels(256, 0, 256, 256, after.GetPixels());
            preview.Apply();
            string path = Path.Combine(directory, "body-glass-preview.png");
            File.WriteAllBytes(path, preview.EncodeToPNG());
            Object.DestroyImmediate(preview);
            Debug.Log("[BodyGlassValidation] Preview (before / glass): " + path);
        }

        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("[BodyGlassValidation] FAIL: " + message);
        }
    }
}
