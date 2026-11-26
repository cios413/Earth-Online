using System;
using System.Collections.Generic;
using EarthOL.BodyThrow;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace EarthOL.EditorTools
{
    public static class DreamGrenadeAssets
    {
        const string Folder = "Assets/Resources/Throwables";

        public static void BuildAndValidate()
        {
            Build();
            DreamGrenadeValidation.Validate();
            PersonSystemValidation.Person3DThrowValidation();
            FreeWheelValidation.Validate();
        }

        [MenuItem("Tools/Earth OL/Build dream grenade assets")]
        public static void Build()
        {
            AssetDatabase.Refresh();
            string imagePath = Folder + "/Previews/DreamGrenade.png";
            var importer = AssetImporter.GetAtPath(imagePath) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Run Tools/generate_dream_grenade_art.py first.");
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true; importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp; importer.maxTextureSize = 1024;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(imagePath);
            var sprite = Material("DreamGrenade", "Assets/Shaders/DreamGrenade.shader");
            sprite.mainTexture = texture;
            var mist = Material("DreamMist", "Assets/Shaders/DreamBloom.shader");
            mist.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mist.SetFloat("_Intensity", 1.05f); mist.SetFloat("_Grain", .045f); mist.renderQueue = 3100;
            var glow = Material("DreamGlow", "Assets/Shaders/DreamBloom.shader");
            glow.SetFloat("_DstBlend", (float)BlendMode.One);
            glow.SetFloat("_Intensity", 1.65f); glow.SetFloat("_Grain", .025f); glow.renderQueue = 3110;
            var catalog = Resources.Load<BodyProjectileCatalog>("Throwables/Catalog");
            if (catalog == null) throw new InvalidOperationException("Missing throwable catalog.");
            var root = new GameObject("Projectile_DreamGrenade");
            try
            {
                root.layer = 8;
                var visual = GameObject.CreatePrimitive(PrimitiveType.Quad);
                visual.name = "Visual"; visual.layer = 8;
                visual.transform.SetParent(root.transform, false);
                visual.transform.localScale = Vector3.one * .42f;
                Object.DestroyImmediate(visual.GetComponent<Collider>());
                var renderer = visual.GetComponent<Renderer>(); renderer.sharedMaterial = sprite;
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                var body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
                var collider = root.AddComponent<BoxCollider>(); collider.size = new Vector3(.30f,.35f,.012f);
                root.AddComponent<BodyProjectile>();
                var dream = root.AddComponent<DreamGrenadeProjectile>();
                var serialized = new SerializedObject(dream);
                serialized.FindProperty("grenadeRenderer").objectReferenceValue = renderer;
                serialized.FindProperty("mistMaterial").objectReferenceValue = mist;
                serialized.FindProperty("glowMaterial").objectReferenceValue = glow;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var saved = PrefabUtility.SaveAsPrefabAsset(root, Folder + "/DreamGrenade.prefab");
                var entries = new List<BodyProjectileCatalog.Entry>(catalog.entries);
                var entry = entries.Find(value => value != null && value.key == "DreamGrenade");
                if (entry == null) { entry = new BodyProjectileCatalog.Entry { key = "DreamGrenade" }; entries.Add(entry); }
                entry.displayName = "6号 · 梦境手雷";
                entry.prefab = saved.GetComponent<BodyProjectile>(); entry.preview = texture;
                entry.slimeColor = new Color(1f,.62f,.83f);
                catalog.entries = entries.ToArray();
                foreach (var value in new Object[] { sprite, mist, glow, catalog }) EditorUtility.SetDirty(value);
                AssetDatabase.SaveAssets();
                Debug.Log("[DreamGrenadeAssets] Built flat pearlescent grenade, soft mist and additive bloom; catalog entry added.");
            }
            finally { Object.DestroyImmediate(root); }
        }

        static Material Material(string name, string shaderPath)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            if (shader == null || ShaderUtil.ShaderHasError(shader))
                throw new InvalidOperationException("Dream shader must compile: " + shaderPath);
            string path = Folder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material,path); }
            material.shader = shader;
            return material;
        }
    }
}
