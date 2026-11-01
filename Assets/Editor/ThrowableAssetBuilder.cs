using System;
using System.IO;
using EarthOL.BodyThrow;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace EarthOL.EditorTools
{
    public static class ThrowableAssetBuilder
    {
        const string Source = "Assets/BodyThrow/Models/Throwables";
        const string Destination = "Assets/Resources/Throwables";

        [Serializable] public sealed class Manifest { public Item[] items; }
        [Serializable] public sealed class Item
        {
            public string key, displayName, sourceCollection;
            public float longestSide;
            public Surface[] objects;
        }
        [Serializable] public sealed class Surface
        {
            public string objectName, texture;
            public float smoothness, metallic, alpha;
        }

        public static void BuildAndValidate()
        {
            BuildAssets();
            ThrowableValidation.Validate();
        }

        public static void BuildValidateAndApk()
        {
            BuildAndValidate();
            AndroidBuild.BuildApk();
        }

        [MenuItem("Tools/Earth OL/Build four projectile assets")]
        public static void BuildAssets()
        {
            AssetDatabase.Refresh();
            EnsureFolder(Destination);
            EnsureFolder(Destination + "/Materials");
            EnsureFolder(Destination + "/Previews");
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Source + "/throwables.json"));
            if (manifest.items == null || manifest.items.Length != 4) throw new InvalidOperationException("Expected four exported Blender collections.");
            var entries = new BodyProjectileCatalog.Entry[4];
            for (int index = 0; index < 4; index++)
            {
                Item item = manifest.items[index];
                var root = new GameObject("Projectile_" + item.key);
                try
                {
                    var model = new GameObject("Visual");
                    model.transform.SetParent(root.transform, false);
                    model.transform.localPosition = Vector3.zero;
                    // Align the authored arrow tip for +Z flight.
                    model.transform.localRotation = item.key == "Arrow"
                        ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
                    model.transform.localScale = Vector3.one * item.longestSide;
                    foreach (var surface in item.objects)
                    {
                        string path = Source + "/" + surface.objectName + ".obj";
                        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                        importer.globalScale = 1f;
                        importer.useFileScale = false;
                        importer.materialImportMode = ModelImporterMaterialImportMode.None;
                        importer.importNormals = ModelImporterNormals.Import;
                        importer.importCameras = importer.importLights = false;
                        importer.isReadable = true;
                        importer.SaveAndReimport();
                        var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                        var part = (GameObject)PrefabUtility.InstantiatePrefab(source);
                        part.name = surface.objectName;
                        part.transform.SetParent(model.transform, false);
                        part.transform.localPosition = Vector3.zero;
                        part.transform.localRotation = Quaternion.identity;
                        part.transform.localScale = Vector3.one;
                        var material = BuildMaterial(surface);
                        foreach (var renderer in part.GetComponentsInChildren<MeshRenderer>())
                        {
                            renderer.sharedMaterial = material;
                            renderer.shadowCastingMode = ShadowCastingMode.Off;
                            renderer.receiveShadows = false;
                        }
                    }
                    var bounds = BoundsOf(root);
                    // Correct any importer-specific translation and normalization.
                    model.transform.localPosition -= bounds.center;
                    bounds = BoundsOf(root);
                    float longest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                    model.transform.localScale *= item.longestSide / longest;
                    model.transform.localPosition *= item.longestSide / longest;
                    bounds = BoundsOf(root);
                    foreach (Transform child in root.GetComponentsInChildren<Transform>()) child.gameObject.layer = 8;
                    var body = root.AddComponent<Rigidbody>();
                    body.useGravity = false;
                    body.isKinematic = true;
                    body.mass = 0.02f;
                    var box = root.AddComponent<BoxCollider>();
                    box.center = bounds.center;
                    box.size = new Vector3(Mathf.Max(bounds.size.x, 0.012f), Mathf.Max(bounds.size.y, 0.012f), Mathf.Max(bounds.size.z, 0.012f));
                    root.AddComponent<BodyProjectile>();
                    root.AddComponent<ProjectileImpactBounce>();
                    string prefabPath = Destination + "/" + item.key + ".prefab";
                    var saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                    string previewPath = Destination + "/Previews/" + item.key + ".png";
                    RenderPreview(root, item.key, previewPath);
                    entries[index] = new BodyProjectileCatalog.Entry
                    {
                        key = item.key, displayName = item.displayName,
                        prefab = saved.GetComponent<BodyProjectile>(),
                        preview = AssetDatabase.LoadAssetAtPath<Texture2D>(previewPath)
                    };
                    Debug.Log($"[Throwables] Built {item.displayName}: {root.GetComponentsInChildren<MeshRenderer>().Length} meshes, bounds={bounds.size}");
                }
                finally { Object.DestroyImmediate(root); }
            }
            string catalogPath = Destination + "/Catalog.asset";
            var catalog = AssetDatabase.LoadAssetAtPath<BodyProjectileCatalog>(catalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<BodyProjectileCatalog>();
                AssetDatabase.CreateAsset(catalog, catalogPath);
            }
            catalog.entries = entries;
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log("[Throwables] Four-model catalog created; default is 1号 · 粘粘手.");
        }

        static Material BuildMaterial(Surface surface)
        {
            string texturePath = Source + "/" + surface.texture;
            var textureImporter = (TextureImporter)AssetImporter.GetAtPath(texturePath);
            textureImporter.textureType = TextureImporterType.Default;
            textureImporter.sRGBTexture = true;
            textureImporter.maxTextureSize = 512;
            textureImporter.textureCompression = TextureImporterCompression.Compressed;
            textureImporter.wrapMode = TextureWrapMode.Clamp;
            textureImporter.SaveAndReimport();
            string path = Destination + "/Materials/" + surface.objectName + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            material.color = new Color(1f, 1f, 1f, surface.alpha);
            material.SetFloat("_Metallic", Mathf.Min(surface.metallic, 0.5f));
            material.SetFloat("_Glossiness", Mathf.Clamp(surface.smoothness, 0.15f, 0.85f));
            if (surface.alpha < 0.99f)
            {
                material.SetFloat("_Mode", 2f);
                material.SetOverrideTag("RenderType", "Transparent");
                material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.EnableKeyword("_ALPHABLEND_ON");
                material.renderQueue = 3000;
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        public static Bounds BoundsOf(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) throw new InvalidOperationException("Projectile has no visible meshes.");
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        static void RenderPreview(GameObject root, string key, string path)
        {
            var preview = new PreviewRenderUtility();
            var clone = Object.Instantiate(root);
            var bounds = BoundsOf(clone);
            try
            {
                preview.AddSingleGO(clone);
                preview.camera.orthographic = true;
                preview.camera.orthographicSize = bounds.extents.magnitude * 1.15f;
                preview.camera.nearClipPlane = 0.01f;
                preview.camera.farClipPlane = 5f;
                preview.camera.clearFlags = CameraClearFlags.SolidColor;
                preview.camera.backgroundColor = new Color(0.10f, 0.13f, 0.17f);
                Vector3 direction = key == "JellyCube" ? new Vector3(0.65f, 0.45f, -1f) :
                    key == "Arrow" ? new Vector3(1f, 0.4f, -0.25f) :
                    key == "Brain" ? new Vector3(0.5f, 0.3f, -1f) : new Vector3(0.12f, 0.05f, -1f);
                preview.camera.transform.position = bounds.center + direction.normalized;
                preview.camera.transform.LookAt(bounds.center);
                preview.lights[0].intensity = 1.4f;
                preview.lights[0].transform.rotation = Quaternion.Euler(40f, 25f, 0f);
                preview.lights[1].intensity = 0.8f;
                preview.lights[1].transform.rotation = Quaternion.Euler(320f, 210f, 0f);
                preview.ambientColor = Color.gray;
                preview.BeginPreview(new Rect(0, 0, 256, 256), GUIStyle.none);
                preview.camera.Render();
                var rendered = preview.EndPreview();
                var temporary = RenderTexture.GetTemporary(256, 256, 0);
                var previous = RenderTexture.active;
                var image = new Texture2D(256, 256, TextureFormat.RGBA32, false);
                try
                {
                    Graphics.Blit(rendered, temporary);
                    RenderTexture.active = temporary;
                    image.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
                    image.Apply();
                    File.WriteAllBytes(path, image.EncodeToPNG());
                }
                finally
                {
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(temporary);
                    Object.DestroyImmediate(image);
                }
                AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
            finally { preview.Cleanup(); }
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = path.Substring(0, path.LastIndexOf('/'));
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
        }
    }
}
