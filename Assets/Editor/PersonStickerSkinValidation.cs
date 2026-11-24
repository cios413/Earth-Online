using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using EarthOL.ARCore;
using EarthOL.BodyThrow;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace EarthOL.EditorTools
{
    /// <summary>Uses a preview scene so skin checks never replace an open project scene.</summary>
    public static class PersonStickerSkinValidation
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [MenuItem("Tools/Earth OL/Validate person sticker skins")]
        public static void Validate()
        {
            var preview = new PreviewRenderUtility();
            var root = new GameObject("Sticker skin validation");
            preview.AddSingleGO(root);
            var originalOverlay = ARCoreModeOverlay.Instance;
            Texture2D alternativeImage = null, before = null, after = null;
            Sprite alternativeSprite = null;
            PersonStickerSkinCatalog testCatalog = null;
            try
            {
                var camera = preview.camera;
                camera.cameraType = CameraType.Game;
                camera.orthographic = true; camera.nearClipPlane = .01f; camera.farClipPlane = 100;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.13f, .12f, .20f, 1);
                camera.allowHDR = camera.allowMSAA = false;
                preview.BeginPreview(new Rect(0, 0, 640, 960), GUIStyle.none);
                int width = camera.targetTexture.width, height = camera.targetTexture.height;
                camera.aspect = width / (float)height; camera.orthographicSize = height * .5f;
                camera.transform.SetPositionAndRotation(new Vector3(width * .5f, height * .5f, -10), Quaternion.identity);

                var tracking = root.AddComponent<PersonTrackingManager>();
                const float trackingTime = 10f;
                tracking.UpdateDetections(new[] { PersonSystemValidation.FullPose() }, trackingTime);
                int firstId = tracking.Persons[0].personId;
                var cartoons = root.AddComponent<Cartoon2DCanvas>(); cartoons.Configure(camera, tracking);
                var canvas = (Canvas)Get(cartoons, "m_Canvas"); preview.AddSingleGO(canvas.gameObject);
                canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera;
                canvas.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                var canvasRect = (RectTransform)canvas.transform;
                canvasRect.sizeDelta = new Vector2(width, height); canvasRect.position = new Vector3(width * .5f, height * .5f, 0);
                canvasRect.rotation = Quaternion.identity; canvasRect.localScale = Vector3.one;
                Canvas.ForceUpdateCanvases();

                var catalog = cartoons.SkinCatalog;
                Check(catalog != null && catalog.entries.Length == 2, "Production catalog contains skull and alien skins");
                var skeleton = catalog.Find(PersonStickerSkinCatalog.DefaultSkinId);
                Check(skeleton != null && skeleton.Available && skeleton.headArtwork == Resources.Load<Sprite>("CartoonJelly/Skull"),
                    "Default skin references the original skull artwork");
                var alien = catalog.Find("alien");
                Check(alien.Available && alien.jellyAntennae && !skeleton.jellyAntennae &&
                    alien.headArtwork == Resources.Load<Sprite>("CartoonJelly/AlienFace"), "Alien art and jelly antennae are enabled");
                Check(!cartoons.SelectSkin("missing"), "Missing artwork cannot be selected");
                cartoons.ActivatePerson(firstId); Canvas.ForceUpdateCanvases();
                for (int settle = 0; settle < 20; settle++) cartoons.Refresh();
                var rigs = (Dictionary<int, Cartoon2DRig>)Get(cartoons, "m_Rigs");
                var first = rigs[firstId];
                Check(Head(first).sprite == skeleton.headArtwork, "New default sticker renders the original skull");
                var palette = cartoons.PaletteFor(firstId);
                var shell = first.JellyShell;
                var shellMaterial = shell.RuntimeMaterial;
                Check(shellMaterial.shader.isSupported && !ShaderUtil.ShaderHasError(shellMaterial.shader),
                    "Extended jelly shader compiles for the active GPU");
                var segments = shellMaterial.GetVectorArray("_JellySegments");
                var radii = shellMaterial.GetVectorArray("_JellyRadii");
                var body = shellMaterial.GetColor("_BodyColor"); var limbs = shellMaterial.GetColor("_LimbColor");
                float opacity = shellMaterial.GetFloat("_Opacity");
                before = Capture();

                // An artwork-only skin also checks the generic path without head geometry additions.
                alternativeImage = new Texture2D(16, 16, TextureFormat.RGBA32, false);
                var pixels = new Color[256]; for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.magenta;
                alternativeImage.SetPixels(pixels); alternativeImage.Apply();
                alternativeSprite = Sprite.Create(alternativeImage, new Rect(0, 0, 16, 16), Vector2.one * .5f);
                testCatalog = Object.Instantiate(catalog);
                testCatalog.entries = new[] { skeleton, new PersonStickerSkin { id = "test-alien", displayName = "Test alien", headArtwork = alternativeSprite } };
                Set(cartoons, "<SkinCatalog>k__BackingField", testCatalog);
                Check(cartoons.SelectSkin("test-alien"), "Future skin becomes selectable when artwork is supplied");
                cartoons.Refresh(); Canvas.ForceUpdateCanvases(); after = Capture();
                Check(Head(first).sprite == skeleton.headArtwork && cartoons.SkinFor(firstId) == skeleton.id &&
                    cartoons.PaletteFor(firstId).body == palette.body && DifferentPixels(before, after) == 0,
                    "Changing selection alone leaves existing stickers and their GPU output unchanged");
                Object.DestroyImmediate(after); after = null;

                cartoons.ActivatePerson(firstId); first.SetPalette(palette); cartoons.Refresh(); Canvas.ForceUpdateCanvases();
                Check(Head(first).sprite == alternativeSprite && cartoons.SkinFor(firstId) == "test-alien",
                    "Regenerating uses the selected head artwork");
                Check(shellMaterial == first.JellyShell.RuntimeMaterial && shellMaterial.GetColor("_BodyColor") == body &&
                    shellMaterial.GetColor("_LimbColor") == limbs && shellMaterial.GetFloat("_Opacity") == opacity,
                    "Skin changes retain the exact jelly material, colors and opacity");
                Check(Equal(segments, shellMaterial.GetVectorArray("_JellySegments")) && Equal(radii, shellMaterial.GetVectorArray("_JellyRadii")),
                    "Skin changes retain connected jelly geometry");
                Check(first.GetComponentsInChildren<Image>().Length == 10 && Mathf.Abs(Head(first).color.a - .16f * (.25f + tracking.Persons[0].trackingConfidence * .75f)) < .001f,
                    "Head artwork retains existing transparency and other body parts");
                after = Capture();
                int changed = DifferentPixels(before, after);
                Check(changed > 20, "A regenerated skin visibly changes the rendered center artwork");
                var corners = new Vector3[4]; Head(first).rectTransform.GetWorldCorners(corners);
                Vector3 low = camera.WorldToScreenPoint(corners[0]), high = camera.WorldToScreenPoint(corners[2]);
                var allowed = Rect.MinMaxRect(low.x - 3, low.y - 3, high.x + 3, high.y + 3);
                var a = before.GetPixels32(); var b = after.GetPixels32();
                for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                    if (!allowed.Contains(new Vector2(x, y)))
                        Check(Difference(a[y * width + x], b[y * width + x]) < 5, "Skin changes affect only the center head artwork on the GPU");

                tracking.UpdateDetections(new[] { PersonSystemValidation.FullPose(), PersonSystemValidation.FullPose(.85f) }, trackingTime + .1f);
                int secondId = tracking.Persons[1].personId;
                cartoons.SelectSkin(skeleton.id); cartoons.ActivatePerson(secondId);
                Check(Head(rigs[firstId]).sprite == alternativeSprite && Head(rigs[secondId]).sprite == skeleton.headArtwork,
                    "Two people retain independent skin choices");
                tracking.Find(firstId).trackingConfidence = 0; cartoons.Refresh();
                tracking.Find(firstId).trackingConfidence = 1; cartoons.Refresh();
                Check(rigs[firstId] == first && Head(first).sprite == alternativeSprite,
                    "Temporary tracking loss and pool recovery preserve the original person's skin");
                cartoons.DeactivatePerson(firstId);
                cartoons.ActivatePerson(firstId, PersonStickerSkinCatalog.DefaultSkinId);
                Check(rigs[firstId] == first && Head(first).sprite == skeleton.headArtwork,
                    "Reused pooled rig switches back to the requested default artwork");

                // Leave room above the synthetic head for both antenna tips in the GPU preview.
                var person = tracking.Find(firstId);
                for (int i = 0; i < person.keypoints.Length; i++) person.keypoints[i].y *= .72f;
                person.screenRect = new Rect(person.screenRect.x, person.screenRect.y * .72f,
                    person.screenRect.width, person.screenRect.height * .72f);
                cartoons.DeactivatePerson(secondId);
                Set(cartoons, "<SkinCatalog>k__BackingField", catalog);
                palette = new JellySkeletonPalette { body = new Color(.52f, .86f, .30f), limbs = new Color(.20f, .85f, .76f) };
                cartoons.ActivatePerson(firstId, skeleton.id); first.SetPalette(palette);
                Canvas.ForceUpdateCanvases(); for (int settle = 0; settle < 20; settle++) cartoons.Refresh();
                Object.DestroyImmediate(before); before = Capture();
                var originalBounds = shell.LocalBounds;
                var originalSegments = shellMaterial.GetVectorArray("_JellySegments");
                var originalRadii = shellMaterial.GetVectorArray("_JellyRadii");
                Check(shell.SegmentCount == 11 && !first.HasJellyAntennae, "Skull keeps the original 11-part envelope");
                cartoons.SelectSkin(alien.id); cartoons.Refresh();
                Check(!first.HasJellyAntennae && Head(first).sprite == skeleton.headArtwork,
                    "Selecting alien does not change already generated skulls");
                cartoons.ActivatePerson(firstId); first.SetPalette(palette); cartoons.Refresh(); Canvas.ForceUpdateCanvases();
                Check(first.HasJellyAntennae && Head(first).sprite == alien.headArtwork && shell.SegmentCount == 15,
                    "Alien regeneration adds two jelly stems and two round tips to the same envelope");
                Check(shell.RuntimeMaterial == shellMaterial && shellMaterial.GetColor("_BodyColor") == palette.body &&
                    shellMaterial.GetColor("_LimbColor") == palette.limbs && shellMaterial.GetFloat("_Opacity") == opacity,
                    "Alien antennae reuse the original jelly material and palette");
                var alienSegments = shellMaterial.GetVectorArray("_JellySegments");
                var alienRadii = shellMaterial.GetVectorArray("_JellyRadii");
                Vector2 offset = shell.LocalBounds.min - originalBounds.min;
                for (int i = 0; i < 11; i++)
                {
                    Vector4 restored = alienSegments[i] + new Vector4(offset.x, offset.y, offset.x, offset.y);
                    Check(Vector4.Distance(restored, originalSegments[i]) < .01f && originalRadii[i] == alienRadii[i],
                        "Original head, torso and limb fitting stays exactly the same");
                }
                Object.DestroyImmediate(after); after = Capture();
                var alienHead = shellMaterial.GetVector("_JellyHead");
                var localCenter = shell.LocalBounds.min + new Vector2(alienHead.x, alienHead.y);
                float headRadius = alienHead.z;
                Vector3 regionLow = camera.WorldToScreenPoint(first.transform.TransformPoint(localCenter - Vector2.one * headRadius * 1.35f));
                Vector3 regionHigh = camera.WorldToScreenPoint(first.transform.TransformPoint(localCenter + new Vector2(1.35f, 2f) * headRadius));
                var headRegion = Rect.MinMaxRect(regionLow.x, regionLow.y, regionHigh.x, regionHigh.y);
                var skullPixels = before.GetPixels32(); var alienPixels = after.GetPixels32();
                int headChanges = 0;
                for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                {
                    int delta = Difference(skullPixels[y * width + x], alienPixels[y * width + x]);
                    if (!headRegion.Contains(new Vector2(x, y))) Check(delta < 7, "Alien changes are confined to the head and its antennae");
                    else if (delta > 10) headChanges++;
                }
                Check(headChanges > 100, "Alien artwork and antennae are visible on the GPU");
                foreach (int segment in new[] { 11, 13 })
                {
                    var stem = alienSegments[segment];
                    for (int sample = 0; sample <= 8; sample++)
                    {
                        Vector2 local = shell.LocalBounds.min + Vector2.Lerp(new Vector2(stem.x, stem.y), new Vector2(stem.z, stem.w), sample / 8f);
                        Vector3 pixel = camera.WorldToScreenPoint(first.transform.TransformPoint(local));
                        int px = Mathf.RoundToInt(pixel.x), py = Mathf.RoundToInt(pixel.y);
                        Check(px >= 0 && px < width && py >= 0 && py < height &&
                            Difference(alienPixels[py * width + px], (Color32)camera.backgroundColor) > 30,
                            "Each antenna is connected continuously to the head with no transparent gaps");
                    }
                }
                File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "opencode", "AlienStickerPreview.png"), after.EncodeToPNG());
                int cropX = Mathf.Clamp(Mathf.FloorToInt(headRegion.xMin), 0, width - 1);
                int cropY = Mathf.Clamp(Mathf.FloorToInt(headRegion.yMin), 0, height - 1);
                int cropWidth = Mathf.Clamp(Mathf.CeilToInt(headRegion.xMax) - cropX, 1, width - cropX);
                int cropHeight = Mathf.Clamp(Mathf.CeilToInt(headRegion.yMax) - cropY, 1, height - cropY);
                var headPreview = new Texture2D(cropWidth, cropHeight, TextureFormat.RGBA32, false);
                try
                {
                    headPreview.SetPixels(after.GetPixels(cropX, cropY, cropWidth, cropHeight)); headPreview.Apply();
                    File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "opencode", "AlienStickerHeadPreview.png"), headPreview.EncodeToPNG());
                }
                finally { Object.DestroyImmediate(headPreview); }
                Vector3 oldHeadWorld = first.transform.TransformPoint(localCenter);
                for (int i = 0; i < person.keypoints.Length; i++) person.keypoints[i].x += .025f;
                var movedBounds = person.screenRect; movedBounds.center += Vector2.right * .025f; person.screenRect = movedBounds;
                person.facing = PersonFacing.Left;
                for (int settle = 0; settle < 20; settle++) cartoons.Refresh();
                var movedHead = shellMaterial.GetVector("_JellyHead");
                Vector2 movedHeadLocal = shell.LocalBounds.min + new Vector2(movedHead.x, movedHead.y);
                Vector3 newHeadWorld = first.transform.TransformPoint(movedHeadLocal);
                Check(first.transform.localScale.x < 0 && newHeadWorld.x > oldHeadWorld.x && shell.SegmentCount == 15,
                    "Mirrored moving person keeps artwork and antennae on the fitted head");
                var mirroredSegments = shellMaterial.GetVectorArray("_JellySegments");
                foreach (int segment in new[] { 11, 13 })
                {
                    Vector2 tip = shell.LocalBounds.min + new Vector2(mirroredSegments[segment].z, mirroredSegments[segment].w);
                    Vector3 worldTip = first.transform.TransformPoint(tip);
                    Check(Mathf.Abs((worldTip.y - newHeadWorld.y) - headRadius * 1.55f) < .02f &&
                        Mathf.Abs(Mathf.Abs(worldTip.x - newHeadWorld.x) - headRadius * .72f) < .02f,
                        "Both jelly tips retain their head-relative size and position through mirroring and motion");
                }
                tracking.Find(firstId).trackingConfidence = 0; cartoons.Refresh();
                tracking.Find(firstId).trackingConfidence = 1; cartoons.Refresh();
                Check(rigs[firstId].HasJellyAntennae, "Alien antennae survive temporary tracking loss and pool recovery");
                cartoons.ActivatePerson(firstId, skeleton.id);
                Check(!rigs[firstId].HasJellyAntennae && shell.SegmentCount == 11, "Switching back removes all antenna geometry");

                var bootstrap = root.AddComponent<ARCoreMainSceneBootstrap>();
                var click = root.AddComponent<PersonClickController>(); click.Configure(camera, tracking, cartoons);
                var detector = root.AddComponent<PoseDetectionManager>(); detector.AutoStart = false; detector.Configure(camera, tracking);
                Set(bootstrap, "<SessionActive>k__BackingField", true);
                Set(bootstrap, "<SelectedAppMode>k__BackingField", AppMode.Free);
                Set(bootstrap, "<CartoonCanvas>k__BackingField", cartoons);
                Set(bootstrap, "<ClickController>k__BackingField", click);
                Set(bootstrap, "<DetectionManager>k__BackingField", detector);
                var overlay = root.AddComponent<ARCoreModeOverlay>(); overlay.Configure(bootstrap);
                Set(bootstrap, "m_Overlay", overlay);
                overlay.ToggleStickerSkinPicker();
                Check(overlay.IsStickerSkinPickerOpen && bootstrap.CurrentMode == GameMode.Sticker,
                    "Person sticker button opens the sidebar and enables person sticker input");
                Vector2 panelPoint = overlay.StickerSkinPanelRect.center;
                Check(ARCoreModeOverlay.IsPointOnOverlay(new Vector2(panelPoint.x, Screen.height - panelPoint.y)),
                    "Sidebar intercepts taps before person/world interaction");
                Check(!overlay.SelectStickerSkin("missing") && overlay.IsStickerSkinPickerOpen, "Unavailable choice leaves picker open");
                Check(overlay.SelectStickerSkin(alien.id) && cartoons.SelectedSkinId == alien.id &&
                    !overlay.IsStickerSkinPickerOpen && bootstrap.CurrentMode == GameMode.Sticker,
                    "Selecting artwork closes sidebar and keeps person sticker generation enabled");
                overlay.ToggleStickerSkinPicker(); overlay.SelectFeature(GameMode.Showcase);
                Check(!overlay.IsStickerSkinPickerOpen, "Switching to showcase closes sidebar");
                overlay.ToggleStickerSkinPicker();
                typeof(ARCoreMainSceneBootstrap).GetMethod("ResetFeatureEffects", Private).Invoke(bootstrap, null);
                Check(!overlay.IsStickerSkinPickerOpen && cartoons.SelectedPersonCount == 0, "Stop reset closes picker and clears generated skin records");
                Set(bootstrap, "<SelectedAppMode>k__BackingField", AppMode.Combat);
                overlay.ToggleStickerSkinPicker();
                Check(!overlay.IsStickerSkinPickerOpen, "Combat cannot open the free-mode skin picker");
                Debug.Log("[PersonStickerSkinValidation] ALL PASS: skull/alien catalog, sidebar selection, independent skins, connected jelly antennae, original fitting/shading, GPU head isolation, pool recovery and cleanup.");
            }
            finally
            {
                preview.EndPreview();
                preview.Cleanup();
                typeof(ARCoreModeOverlay).GetProperty("Instance").SetValue(null, originalOverlay);
                if (before != null) Object.DestroyImmediate(before);
                if (after != null) Object.DestroyImmediate(after);
                if (alternativeSprite != null) Object.DestroyImmediate(alternativeSprite);
                if (alternativeImage != null) Object.DestroyImmediate(alternativeImage);
                if (testCatalog != null) Object.DestroyImmediate(testCatalog);
            }

            Texture2D Capture()
            {
                var camera = preview.camera;
                camera.Render(); var previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = camera.targetTexture;
                    var image = new Texture2D(camera.targetTexture.width, camera.targetTexture.height, TextureFormat.RGBA32, false);
                    image.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0); image.Apply(); return image;
                }
                finally { RenderTexture.active = previous; }
            }
        }

        static Image Head(Cartoon2DRig rig) => rig.transform.Find("Head/Artwork").GetComponent<Image>();
        static object Get(object target, string field) => target.GetType().GetField(field, Private).GetValue(target);
        static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
        static bool Equal(Vector4[] a, Vector4[] b)
        { if (a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
        static int Difference(Color32 a, Color32 b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) + Mathf.Abs(a.a - b.a);
        static int DifferentPixels(Texture2D a, Texture2D b)
        {
            var first = a.GetPixels32(); var second = b.GetPixels32(); int count = 0;
            for (int i = 0; i < first.Length; i++) if (Difference(first[i], second[i]) > 5) count++;
            return count;
        }
        static void Check(bool value, string message)
        { if (!value) throw new InvalidOperationException("[PersonStickerSkinValidation] FAIL: " + message); }
    }
}
