using System;
using System.Collections.Generic;
using System.IO;
using EarthOL.Deform;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace EarthOL.EditorTools
{
    public static class SwipeDeformationValidation
    {
        [MenuItem("Tools/Earth OL/Validate smooth press and swipe rendering")]
        public static void Validate()
        {
            var preview = new PreviewRenderUtility();
            var root = new GameObject("Press and swipe GPU regression"); preview.AddSingleGO(root);
            var pattern = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            var commands = new CommandBuffer { name = "Synthetic camera feed for smooth warp" };
            var images = new List<Texture2D>();
            bool began = false;
            try
            {
                var camera = preview.camera;
                camera.cameraType = CameraType.Game;
                camera.transform.SetPositionAndRotation(new Vector3(0, 0, -2), Quaternion.identity);
                camera.orthographic = true; camera.orthographicSize = 1;
                camera.nearClipPlane = .01f; camera.farClipPlane = 5;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.cullingMask = 1 << 30; camera.allowHDR = camera.allowMSAA = false;
                preview.BeginPreview(new Rect(0, 0, 384, 384), GUIStyle.none); began = true;
                camera.aspect = 1;
                var pixels = new Color32[256 * 256];
                for (int y = 0; y < 256; y++) for (int x = 0; x < 256; x++)
                    pixels[y * 256 + x] = new Color((x / 8 + y / 8) % 2 == 0 ? .8f : .2f, y / 300f, .2f + x / 512f);
                pattern.SetPixels32(pixels); pattern.Apply();
                commands.Blit(pattern, BuiltinRenderTextureType.CameraTarget);
                commands.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
                camera.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, commands);
                var baseline = Capture();
                var surface = new GameObject("Warp surface"); surface.transform.SetParent(root.transform, false);
                surface.layer = 30; surface.SetActive(false); surface.transform.rotation = Quaternion.Euler(0, 180, 0);
                var warp = surface.AddComponent<PokeWarpDeformer>();
                warp.planeInXZ = false; warp.planeWidth = warp.planeHeight = 1.6f;
                warp.gridX = 48; warp.gridY = 64; warp.createCollider = false; warp.lockPokesToSurface = true;
                warp.stretchAmount = .45f; warp.bulgeAmount = .02f;
                warp.warpMaterial = Resources.Load<Material>("PersonWarp"); warp.logPokes = false;
                warp.Initialize(); surface.SetActive(true);
                Check(warp.warpMaterial.shader.isSupported && !ShaderUtil.ShaderHasError(warp.warpMaterial.shader), "Warp shader compiles");
                Check(DifferentPixels(baseline, Capture()) < 20, "Idle surface preserves camera pixels");
                warp.BeginAt(3, surface.transform.TransformPoint(new Vector3(.22f, .05f, 0)));
                var unmaskedPress = Capture();
                var segments = new[] { new PokeWarpDeformer.BodySegment(new Vector2(-.16f, -.45f), new Vector2(.13f, .42f), .13f) };
                warp.SetBodyMask(segments, .035f); warp.Advance(.016f);
                var maskedPress = Capture();
                Check(DifferentPixels(unmaskedPress, maskedPress) == 0,
                    "Ordinary press is pixel-identical with or without a body mask, restoring the original smooth path");
                Check(DifferentPixels(baseline, maskedPress) > 500, "Press remains visibly deforming beyond the narrow body mask");
                warp.OnPokeEnd(3);
                for (int i = 0; i < 600; i++) warp.Advance(1f / 120f);
                Check(DifferentPixels(baseline, Capture()) < 20, "Press spring restores camera image");
                warp.SetBodyMask(segments, .10f); warp.SetSwipeShear(Vector2.right, .24f);
                var swipe = Capture();
                Check(DifferentPixels(baseline, swipe) > 500, "Swipe retains a visible directional push");
                var block = new MaterialPropertyBlock(); surface.GetComponent<Renderer>().GetPropertyBlock(block);
                Check(Mathf.Abs(block.GetFloat("_BodyFeather") - .10f) < .0001f, "Requested feather reaches GPU properties");
                warp.gridX = warp.gridY = 4;
                // Rebuild through the existing resize path at the final original size.
                warp.SetPlaneSize(1.61f, 1.61f); warp.SetPlaneSize(1.6f, 1.6f);
                var coarseSwipe = Capture();
                Check(DifferentPixels(swipe, coarseSwipe) < 50,
                    "Swipe contour remains visually identical across 48x64 and 4x4 grids; its boundary is evaluated per pixel");
                warp.SetBodyMask(segments, .23f); warp.Advance(.016f);
                var feathered = Capture();
                Check(DifferentPixels(coarseSwipe, feathered) > 100, "Feather width actually controls the rendered swipe boundary");
                // No effect can leak outside the region, even with the broader contour feather.
                var original = baseline.GetPixels32(); var swiped = feathered.GetPixels32();
                for (int y = 0; y < baseline.height; y++) for (int x = 0; x < baseline.width; x++)
                    if (x < baseline.width * .08f || x > baseline.width * .92f || y < baseline.height * .08f || y > baseline.height * .92f)
                        Check(Difference(original[y * baseline.width + x], swiped[y * baseline.width + x]) < 5, "Outer camera background stays unchanged");
                warp.SetSwipeShear(Vector2.right, 0);
                Check(DifferentPixels(baseline, Capture()) < 20, "Last zero swipe amplitude clears GPU displacement immediately");
                string folder = Path.Combine(Path.GetTempPath(), "opencode");
                var sheet = new Texture2D(baseline.width * 3, baseline.height, TextureFormat.RGBA32, false);
                try
                {
                    sheet.SetPixels(0, 0, baseline.width, baseline.height, baseline.GetPixels());
                    sheet.SetPixels(baseline.width, 0, baseline.width, baseline.height, maskedPress.GetPixels());
                    sheet.SetPixels(baseline.width * 2, 0, baseline.width, baseline.height, swipe.GetPixels());
                    sheet.Apply(); File.WriteAllBytes(Path.Combine(folder, "SmoothPressSwipePreview.png"), sheet.EncodeToPNG());
                }
                finally { Object.DestroyImmediate(sheet); }
                Debug.Log("[SwipeDeformationValidation] ALL PASS: press unaffected by body mask, spring recovery, visible pixel-masked swipe, grid-independent contour, real feather control, background preservation and final-zero upload.");

                Texture2D Capture()
                {
                    camera.Render(); var previous = RenderTexture.active;
                    try
                    {
                        var target = camera.targetTexture; RenderTexture.active = target;
                        var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
                        image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply(); images.Add(image); return image;
                    }
                    finally { RenderTexture.active = previous; }
                }
            }
            finally
            {
                preview.camera.RemoveCommandBuffer(CameraEvent.BeforeForwardOpaque, commands);
                commands.Release(); if (began) preview.EndPreview(); preview.Cleanup();
                Object.DestroyImmediate(pattern); foreach (var image in images) Object.DestroyImmediate(image);
            }
        }
        static int DifferentPixels(Texture2D a, Texture2D b)
        {
            var aa = a.GetPixels32(); var bb = b.GetPixels32(); int count = 0;
            for (int i = 0; i < aa.Length; i++) if (Difference(aa[i], bb[i]) > 5) count++;
            return count;
        }
        static int Difference(Color32 a, Color32 b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);
        static void Check(bool value, string message)
        { if (!value) throw new InvalidOperationException("[SwipeDeformationValidation] FAIL: " + message); }
    }
}
