using System;
using System.IO;
using System.Reflection;
using EarthOL.BodyThrow;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EarthOL.EditorTools
{
    public static class ProjectileSlimeValidation
    {
        public static void BuildAndValidate()
        {
            SlimeProjectileAssets.Build();
            PersonSystemValidation.Person3DThrowValidation();
            ValidateMotion();
            ValidateFusionRendering();
            ValidateShowcaseLight();
            PersonSystemValidation.PersonShowcaseGeometryValidation();
            PersonSystemValidation.BootstrapMigrationValidation();
        }

        public static void ValidateShowcaseLight()
        {
            var material=Resources.Load<Material>("PersonShowcase");
            Check(material!=null&&material.shader.name=="EarthOL/ShowcasePhoto"&&
                !ShaderUtil.ShaderHasError(material.shader),"Showcase must use its fixed preview-side lighting shader");
            var root=new GameObject("Showcase fixed-light GPU test");
            var target=new RenderTexture(128,128,16,RenderTextureFormat.ARGB32);
            var previous=RenderTexture.active;
            try
            {
                var camera=root.AddComponent<Camera>();camera.enabled=false;
                camera.orthographic=true;camera.orthographicSize=.6f;camera.aspect=1;
                camera.nearClipPlane=.01f;camera.farClipPlane=5;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
                camera.allowHDR=camera.allowMSAA=false;camera.targetTexture=target;camera.cullingMask=1<<30;
                var model=GameObject.CreatePrimitive(PrimitiveType.Sphere);
                model.transform.SetParent(camera.transform,false);model.transform.localPosition=Vector3.forward*2;
                model.gameObject.layer=30;model.GetComponent<Renderer>().sharedMaterial=material;
                Color32[] Capture()
                {
                    camera.Render();RenderTexture.active=target;
                    var image=new Texture2D(128,128,TextureFormat.RGBA32,false);
                    try{image.ReadPixels(new Rect(0,0,128,128),0,0);image.Apply();return image.GetPixels32();}
                    finally{Object.DestroyImmediate(image);}
                }
                var baseline=Capture();
                camera.transform.SetPositionAndRotation(new Vector3(2,1,-3),Quaternion.Euler(32,78,11));
                var rotated=Capture();
                int changed=0;
                for(int i=0;i<baseline.Length;i++)
                    if(Mathf.Abs(baseline[i].r-rotated[i].r)+Mathf.Abs(baseline[i].g-rotated[i].g)+Mathf.Abs(baseline[i].b-rotated[i].b)>9)changed++;
                Check(changed<20,"Gyroscope-like camera translation/rotation must not change showcase lighting, changedPixels="+changed);
                model.transform.localRotation=Quaternion.Euler(0,0,0);
                var left=baseline[64*128+32];var right=baseline[64*128+96];
                Check(Mathf.Abs(left.r-right.r)>10,"Preview must retain visible fixed side lighting");
                Debug.Log("[SlimeValidation] ShowcaseLight GPU PASS: fixed side lighting, AR pose change pixels="+changed);
            }
            finally{RenderTexture.active=previous;Object.DestroyImmediate(root);target.Release();Object.DestroyImmediate(target);}
        }

        static void ValidateMotion()
        {
            var root=new GameObject("Slime impact validation");
            try
            {
                var camera=root.AddComponent<Camera>();camera.enabled=false;camera.pixelRect=new Rect(0,0,1000,1000);
                var tracking=root.AddComponent<PersonTrackingManager>();
                tracking.UpdateDetections(new[]{PersonSystemValidation.FullPose()},Time.realtimeSinceStartup);
                var thrower=root.AddComponent<Person3DThrowController>();
                var catalog=Resources.Load<BodyProjectileCatalog>("Throwables/Catalog");
                thrower.Configure(camera,tracking,catalog);thrower.SetInteractionEnabled(true);
                Vector2 target=camera.ViewportToScreenPoint(new Vector3(.5f,.55f,0));
                for(int i=0;i<catalog.entries.Length;i++)
                {
                    Check(thrower.SelectProjectile(i)&&thrower.TryThrowAt(target),"Every catalog model must launch");
                    Check(catalog.entries[i].slimeColor.maxColorComponent>.15f,"Every model has a sampled dominant color");
                    var spawned=GameObject.Find("3D Thrown "+catalog.entries[i].displayName);
                    Check(spawned!=null&&Mathf.Abs(spawned.transform.localScale.x-.25f)<.001f,
                        "Every 3D throwable launches at one quarter scale");
                }
                Advance(thrower,.44f);
                Check(thrower.AttachedProjectileCount==catalog.entries.Length&&thrower.SlimeSurfaceCount==1,
                    "Impacts on the same person must share exactly one merged slime surface");
                Check(ProjectileSlimeMotion.Rebound(.12f)>0f&&ProjectileSlimeMotion.Rebound(1f)==0f,
                    "Impact rebound must be brief and finish");
                Advance(thrower,3f);
                var settled=ProjectileSlimeMotion.ModelScale(3f);
                Vector3 expectedScale=settled*.25f;
                Check(settled.z<.1f&&settled.x>1.5f&&settled.y>1.5f,"All models must flatten in camera-normal Z and spread in XY");
                var alien=GameObject.Find("3D Thrown 5号 · 果冻外星人");
                Check(alien!=null&&Vector3.Distance(alien.transform.localScale,expectedScale)<.001f,"Alien uses the same dissolve animation at quarter base size");
                Vector3 position=alien.transform.localPosition;
                var surface=camera.GetComponentInChildren<PersonSlimeSurface>();
                Vector4 blob=surface.RuntimeMaterial.GetVectorArray("_SlimeBlobs")[0];
                float personHeight=Vector3.Distance(
                    camera.ViewportToWorldPoint(new Vector3(.5f,.94f,1.20f)),
                    camera.ViewportToWorldPoint(new Vector3(.5f,.04f,1.20f)));
                float expectedSlimeRadius=Mathf.Clamp(personHeight*.085f,.025f,.14f);
                Check(Mathf.Abs(blob.z-expectedSlimeRadius)<.002f,
                    "Slime radius remains based on person height, independent of the quarter-size projectile model (actual="+blob.z+", expected="+expectedSlimeRadius+")");
                Advance(thrower,10f);
                Check(Vector3.Distance(position,alien.transform.localPosition)<.00001f&&
                    surface.RuntimeMaterial.GetVectorArray("_SlimeBlobs")[0]==blob,
                    "After settling the attached model and its downward-flowing slime must stop drifting");
                thrower.ClearEffects();
                Check(thrower.SlimeSurfaceCount==0&&thrower.ActiveProjectileCount==0,"Reset removes models and slime together");
                Debug.Log("[SlimeValidation] Motion PASS: five 3D models, rebound, normal flattening, lateral spreading, finite flow, one merged surface and reset.");
            }
            finally{Object.DestroyImmediate(root);}
        }

        static void ValidateFusionRendering()
        {
            var template=Resources.Load<Material>("Throwables/PersonSlime");
            Check(template!=null&&template.shader.isSupported&&!ShaderUtil.ShaderHasError(template.shader),"Slime shader compiles for the active GPU");
            var root=new GameObject("Slime fusion GPU validation");
            var target=new RenderTexture(256,256,16,RenderTextureFormat.ARGB32);
            var sheet=new Texture2D(256*3,256,TextureFormat.RGBA32,false);
            var previous=RenderTexture.active;
            try
            {
                var camera=root.AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=.22f;
                camera.nearClipPlane=.01f;camera.farClipPlane=3;camera.aspect=1;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;
                camera.allowHDR=camera.allowMSAA=false;camera.targetTexture=target;camera.cullingMask=1<<30;
                var surface=new GameObject("Fused puddle test").AddComponent<PersonSlimeSurface>();surface.gameObject.layer=30;
                surface.Configure(camera,template);
                Color green=new Color(.59f,.94f,.075f),pink=new Color(.95f,.25f,.48f);
                float singleAlpha=0;
                for(int stage=0;stage<3;stage++)
                {
                    surface.BeginFrame();
                    surface.Add(new Vector2(-.025f,.045f),.063f,stage==2?.10f:0f,green);
                    if(stage>0)surface.Add(new Vector2(.025f,.045f),.063f,stage==2?.07f:0f,pink);
                    surface.FinishFrame(1.2f);camera.Render();RenderTexture.active=target;
                    var image=new Texture2D(256,256,TextureFormat.RGBA32,false);
                    try
                    {
                        image.ReadPixels(new Rect(0,0,256,256),0,0);image.Apply();
                        sheet.SetPixels(stage*256,0,256,256,image.GetPixels());
                        Color center=image.GetPixel(128,154);
                        Check(center.a>.5f,"Slime must cover the intersection without a transparent hole");
                        if(stage==0)singleAlpha=center.a;
                        else Check(center.a<=singleAlpha+.08f,"Overlapping slime must union once instead of stacking translucent layers");
                        if(stage==2)Check(image.GetPixel(114,100).a>.35f,"Slime must form a downward connected trail");
                    }
                    finally{Object.DestroyImmediate(image);}
                }
                sheet.Apply();
                File.WriteAllBytes("Assets/BodyThrow/ArtPreviews/SlimeFusionStages.png",sheet.EncodeToPNG());
                Debug.Log("[SlimeValidation] GPU PASS: opaque continuous overlap, one translucent union, downward trail. Preview: SlimeFusionStages.png");
            }
            finally
            {
                RenderTexture.active=previous;Object.DestroyImmediate(root);target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(sheet);
            }
            AssetDatabase.Refresh();
        }

        static void Advance(Person3DThrowController thrower,float dt) =>
            typeof(Person3DThrowController).GetMethod("UpdateThrownItems",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(thrower,new object[]{dt});
        static void Check(bool value,string message){if(!value)throw new InvalidOperationException("[SlimeValidation] FAIL: "+message);}
    }
}
