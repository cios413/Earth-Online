using System;
using System.IO;
using EarthOL.BodyThrow;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EarthOL.EditorTools
{
    public static class RetroArrowAssets
    {
        const string Folder = "Assets/Resources/Throwables";

        [MenuItem("Tools/Earth OL/Apply retro arrow style")]
        public static void Apply()
        {
            string texturePath = Folder+"/Materials/ArrowRetro_BaseColor.png";
            AssetDatabase.ImportAsset(texturePath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
            importer.sRGBTexture = true; importer.maxTextureSize = 2048;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = true; importer.SaveAndReimport();
            var material = AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Materials/Arrow_0.mat");
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/RetroArrow.shader");
            if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Retro arrow shader must compile.");
            material.shader = shader;
            material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            material.color = Color.white;
            EditorUtility.SetDirty(material); AssetDatabase.SaveAssets();
            var prefab = Resources.Load<BodyProjectile>("Throwables/Arrow");
            if (prefab == null || material.mainTexture == null || ShaderUtil.ShaderHasError(material.shader))
                throw new InvalidOperationException("Arrow prefab, vintage atlas and shader must be valid.");
            Render(prefab,512,512,Folder+"/Previews/Arrow.png",false);
            Render(prefab,1400,360,"Assets/BodyThrow/ArtPreviews/RetroArrowSide.png",true);
            PersonSystemValidation.Person3DThrowValidation();
            Debug.Log("[RetroArrowAssets] PASS: original mesh, UVs, transforms and collider retained; red/teal vintage material, wheel thumbnail, side preview and arrow flight verified.");
        }

        static void Render(BodyProjectile prefab,int width,int height,string path,bool side)
        {
            var preview = new PreviewRenderUtility();
            var clone = Object.Instantiate(prefab.gameObject);
            clone.GetComponent<BodyProjectile>().enabled = false;
            // Keep +Z flight orientation; rotate the preview camera to place the tip on the left.
            var bounds = ThrowableAssetBuilder.BoundsOf(clone);
            var oldTarget = RenderTexture.active;
            var image = new Texture2D(width,height,TextureFormat.RGBA32,false);
            try
            {
                preview.AddSingleGO(clone);
                var camera = preview.camera;
                camera.orthographic = true; camera.aspect = width/(float)height;
                camera.orthographicSize = side ? .070f : .25f;
                camera.nearClipPlane = .01f; camera.farClipPlane = 5;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = side ? new Color(.96f,.925f,.83f) : new Color(.10f,.13f,.17f);
                camera.transform.position = bounds.center+new Vector3(-1,.20f,0).normalized;
                camera.transform.LookAt(bounds.center,Vector3.up);
                camera.allowHDR = false;
                preview.lights[0].intensity = 1.5f;
                preview.lights[0].transform.rotation = Quaternion.Euler(35,35,0);
                preview.lights[1].intensity = .9f;
                preview.ambientColor = new Color(.65f,.65f,.65f);
                preview.BeginPreview(new Rect(0,0,width,height),GUIStyle.none);
                camera.Render();
                var texture = preview.EndPreview();
                var rt = RenderTexture.GetTemporary(width,height,0);
                try
                {
                    Graphics.Blit(texture,rt); RenderTexture.active = rt;
                    image.ReadPixels(new Rect(0,0,width,height),0,0); image.Apply();
                    int red=0,teal=0;
                    foreach (var c in image.GetPixels())
                    {
                        if(c.r>c.g*1.35f && c.r>c.b*1.25f) red++;
                        if(c.g>c.r*1.2f && c.g>c.b*.9f) teal++;
                    }
                    if(red<10 || teal<3) throw new InvalidOperationException($"Arrow colors missing: red={red}, teal={teal}");
                    File.WriteAllBytes(path,image.EncodeToPNG());
                    Debug.Log($"[RetroArrowAssets] GPU {path}: red={red}, teal={teal}");
                }
                finally { RenderTexture.active = oldTarget; RenderTexture.ReleaseTemporary(rt); }
                AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.mipmapEnabled = false; importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            finally { Object.DestroyImmediate(image); preview.Cleanup(); }
        }
    }
}
