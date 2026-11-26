using System;
using System.IO;
using System.Reflection;
using EarthOL.ARCore;
using EarthOL.BodyThrow;
using EarthOL.Deform;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EarthOL.EditorTools
{
    public static class ShowcaseInteractionValidation
    {
        const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        public static void Validate()
        {
            PersonSystemValidation.Pose2DTrackingValidation();
            PersonSystemValidation.PersonFacingValidation();
            PersonSystemValidation.Cartoon2DRigValidation();
            PersonSystemValidation.Person3DThrowValidation();
            PersonSystemValidation.PersonHeadJellyValidation();
            PersonSystemValidation.PersonShowcaseGeometryValidation();
            PersonSystemValidation.SegmentedShowcaseGeometryValidation();
            PersonSystemValidation.BootstrapMigrationValidation();
            MultiPersonInteractionValidation.Validate();
            ValidateModelInteraction();
            ValidateSegmentedSide();
            ProjectileSlimeValidation.ValidateShowcaseLight();
            ValidateProjectileSheet();
            Debug.Log("[ShowcaseInteraction] ALL PASS: automatic multi-person tracking, deformation interface, existing spring reuse, tap/drag/model crossing, rotation, GPU deformation/recovery and fixed side light.");
        }

        static void ValidateSegmentedSide()
        {
            var root = new GameObject("Segmented showcase side regression");
            Texture2D image = null;
            try
            {
                var camera = root.AddComponent<Camera>();
                camera.enabled = false;
                var tracking = root.AddComponent<PersonTrackingManager>();
                var showcase = root.AddComponent<PersonShowcaseController>();
                showcase.Configure(camera, tracking, null);
                const int size = 96;
                var photo = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var alpha = new byte[size * size];
                var pixels = new Color32[alpha.Length];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int i = y * size + x;
                    pixels[i] = new Color32(230, 160, 120, 255);
                    float edge = Mathf.Min(26f - Mathf.Abs(x - 47.5f), 39f - Mathf.Abs(y - 47.5f));
                    alpha[i] = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01((edge + 2f) / 4f));
                }
                photo.SetPixels32(pixels);
                photo.Apply();
                Set(showcase, "m_Photo", photo);
                Set(showcase, "m_ModelMesh", PersonPhotoSilhouetteBuilder.BuildFromMask(photo, alpha, 1.45f));
                Invoke(showcase, "CreateModelObject");
                Invoke(showcase, "BeginPresentation");
                var model = (GameObject)Get(showcase, "m_ModelRoot");
                var preview = (Camera)Get(showcase, "m_PreviewCamera");
                foreach (float yaw in new[] { 80f, 90f, 100f, 270f })
                {
                    model.transform.localRotation = Quaternion.Euler(0, yaw, 0);
                    image = Capture(preview);
                    for (int offset = -40; offset <= 40; offset++)
                        Check(image.GetPixel(image.width / 2, image.height / 2 + offset).a > .95f,
                            "Segmented preview has an opaque center line at yaw " + yaw);
                    Object.DestroyImmediate(image);
                    image = null;
                }
                Debug.Log("[ShowcaseInteraction] Segmented side GPU PASS at 80, 90, 100 and 270 degrees.");
            }
            finally
            {
                if (image != null) Object.DestroyImmediate(image);
                Object.DestroyImmediate(root);
            }
        }

        public static void ValidateProjectileSheet()
        {
            var root = new GameObject("Photo projectile sheet regression");
            var target = new RenderTexture(160, 256, 16, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            Material reference = null;
            try
            {
                var camera = root.AddComponent<Camera>();
                camera.enabled = false;
                camera.orthographic = true; camera.orthographicSize = .8f; camera.aspect = 160f / 256;
                camera.nearClipPlane = .01f; camera.farClipPlane = 5;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
                camera.allowHDR = camera.allowMSAA = false;
                camera.cullingMask = 1 << 30; camera.targetTexture = target;
                var tracking = root.AddComponent<PersonTrackingManager>();
                tracking.UpdateDetections(new[] { PersonSystemValidation.FullPose() }, Time.realtimeSinceStartup);
                var showcase = root.AddComponent<PersonShowcaseController>();
                showcase.Configure(camera, tracking, null);
                var photo = new Texture2D(128, 256, TextureFormat.RGBA32, false, false);
                var pixels = new Color32[128 * 256];
                for (int y = 0; y < 256; y++) for (int x = 0; x < 128; x++)
                    pixels[y * 128 + x] = new Color32((byte)(x < 64 ? 210 : 95), 155, (byte)(y < 128 ? 75 : 180), 255);
                photo.SetPixels32(pixels); photo.Apply();
                var pose = PersonPoseData.FromDetection(PersonSystemValidation.FullPose(), 1f);
                var showcaseMesh = PersonPhotoSilhouetteBuilder.Build(pose, photo, new Rect(.25f, .02f, .5f, .92f));
                Set(showcase, "m_Photo", photo); Set(showcase, "m_ModelMesh", showcaseMesh);
                Invoke(showcase, "CreateModelObject"); Invoke(showcase, "SaveProjectileSlot");
                var saved = showcase.SavedProjectileSource;
                var mesh = saved.GetComponent<MeshFilter>().sharedMesh;
                var material = saved.GetComponent<Renderer>().sharedMaterial;
                var snapshot = showcase.SavedProjectilePreview;
                Check(mesh.subMeshCount == 1 && mesh.vertexCount == showcaseMesh.vertexCount / 2 && mesh.bounds.size.z == 0f,
                    "Projectile must be a single planar silhouette surface, not the inflated showcase mesh");
                Check(snapshot != photo && material.mainTexture == snapshot &&
                    !UnityEditor.ShaderUtil.ShaderHasError(material.shader), "Projectile shader uses an independent captured photo");
                var captured = photo.GetPixels32(); var copied = snapshot.GetPixels32();
                for (int i = 0; i < captured.Length; i++)
                    Check(captured[i].Equals(copied[i]), "Saved photo keeps original captured RGB and silhouette alpha");
                var renderModel = new GameObject("GPU sheet"); renderModel.layer = 30;
                renderModel.transform.SetParent(camera.transform, false); renderModel.transform.localPosition = Vector3.forward * 2;
                renderModel.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = renderModel.AddComponent<MeshRenderer>();
                reference = new Material(Shader.Find("Unlit/Transparent")) { mainTexture = snapshot };
                renderer.sharedMaterial = reference;
                var baseline = CaptureSheet();
                renderer.sharedMaterial = material;
                var shaded = CaptureSheet();
                int unchangedInterior = 0, shadowPixels = 0;
                for (int i = 0; i < baseline.Length; i++)
                {
                    Check(Mathf.Abs(baseline[i].a - shaded[i].a) <= 1, "Contour shadow must preserve silhouette coverage");
                    if (baseline[i].a < 250) continue;
                    int difference = baseline[i].r - shaded[i].r;
                    if (Mathf.Abs(difference) <= 1) unchangedInterior++;
                    if (difference > 8) shadowPixels++;
                    Check(difference >= -1, "Contour shading must never brighten the captured photo");
                }
                Check(unchangedInterior > 1000 && shadowPixels > 30,
                    "GPU sheet keeps interior brightness and draws a visible narrow contour shadow");
                renderModel.transform.localRotation = Quaternion.Euler(0, 180, 0);
                var back = CaptureSheet();
                int backDifferences = 0;
                for (int y = 0; y < 256; y++) for (int x = 0; x < 160; x++)
                {
                    var a = shaded[y * 160 + x]; var b = back[y * 160 + 159 - x];
                    if (Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) + Mathf.Abs(a.a - b.a) > 12)
                        backDifferences++;
                }
                var sheet = new Texture2D(480, 256, TextureFormat.RGBA32, false);
                try
                {
                    sheet.SetPixels32(0, 0, 160, 256, baseline);
                    sheet.SetPixels32(160, 0, 160, 256, shaded);
                    sheet.SetPixels32(320, 0, 160, 256, back);
                    sheet.Apply();
                    File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "opencode", "PhotoProjectileSheet.png"), sheet.EncodeToPNG());
                }
                finally { Object.DestroyImmediate(sheet); }
                // A 180-degree rotation changes edge rasterization by subpixels. Allow
                // less than 0.5% of the frame while requiring matching photo interiors.
                Check(backDifferences < baseline.Length * .005f,
                    "Reverse face shows the same photo and brightness mirrored through the sheet; changed pixels=" + backDifferences);
                var thrower = root.AddComponent<Person3DThrowController>();
                thrower.Configure(camera, tracking, Resources.Load<BodyProjectileCatalog>("Throwables/Catalog"));
                thrower.SetInteractionEnabled(true); thrower.SetShowcaseProjectile(saved, snapshot);
                thrower.SelectProjectile(thrower.ShowcaseProjectileIndex);
                Vector2 center = camera.ViewportToScreenPoint(new Vector3(.5f, .5f, 0));
                Check(thrower.TryThrowFromWheel(center, center), "Saved sheet launches using the shared throw path");
                Invoke(showcase, "DestroyModel");
                Check(photo == null && snapshot != null && material.mainTexture == snapshot,
                    "Closing showcase keeps the projectile photo alive");
                showcase.ClearSavedProjectile(); thrower.ClearShowcaseProjectile();
                Check(snapshot != null && mesh != null && material != null,
                    "Clearing or replacing the slot preserves assets referenced by an in-flight sheet");
                Object.DestroyImmediate(renderModel);
                thrower.ClearEffects();
                Check(snapshot == null && mesh == null && material == null,
                    "Last projectile releases its retired photo, mesh and material");
                Debug.Log("[ShowcaseInteraction] ProjectileSheet GPU/lifecycle PASS: single plane, original RGB/alpha, unchanged interior brightness, contour shadow, matching reverse face and retained flight assets.");

                Color32[] CaptureSheet()
                {
                    camera.Render(); RenderTexture.active = target;
                    var image = new Texture2D(160, 256, TextureFormat.RGBA32, false);
                    try { image.ReadPixels(new Rect(0, 0, 160, 256), 0, 0); image.Apply(); return image.GetPixels32(); }
                    finally { Object.DestroyImmediate(image); }
                }
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(root);
                if (reference != null) Object.DestroyImmediate(reference);
                target.Release(); Object.DestroyImmediate(target);
            }
        }

        static void ValidateModelInteraction()
        {
            var root = new GameObject("Showcase model deformation regression");
            Texture2D originalImage=null,pressedImage=null,recoveredImage=null;
            PersonShowcaseController showcase=null;
            try
            {
                var camera=root.AddComponent<Camera>();camera.enabled=false;camera.nearClipPlane=.01f;
                var tracking=root.AddComponent<PersonTrackingManager>();
                var distance=root.AddComponent<PersonDistanceEstimator>();distance.Configure(camera);
                var source=root.AddComponent<PersonDeformationSurface>();source.Configure(camera,tracking,distance,null);
                showcase=root.AddComponent<PersonShowcaseController>();showcase.Configure(camera,tracking,null,source);
                const int width=128,height=256;
                var photo=new Texture2D(width,height,TextureFormat.RGBA32,false,false);
                var pixels=new Color32[width*height];
                for(int y=0;y<height;y++)for(int x=0;x<width;x++)
                    pixels[y*width+x]=new Color32((byte)((x/5%2==0)?235:35),(byte)((y/5%2==0)?210:45),100,255);
                photo.SetPixels32(pixels);photo.Apply();
                var pose=PersonPoseData.FromDetection(PersonSystemValidation.FullPose(),1f);
                Mesh mesh=PersonPhotoSilhouetteBuilder.Build(pose,photo,new Rect(.25f,.02f,.5f,.92f));
                Set(showcase,"m_ModelMesh",mesh);Set(showcase,"m_Photo",photo);
                Invoke(showcase,"CreateModelObject");Invoke(showcase,"BeginPresentation");
                var model=(GameObject)Get(showcase,"m_ModelRoot");
                var renderer=model.GetComponent<Renderer>();
                var effect=(IPersonModelDeformation)Get(showcase,"m_ModelDeformation");
                var preview=(Camera)Get(showcase,"m_PreviewCamera");
                Check(effect!=null&&model.GetComponent<MeshFilter>().sharedMesh==mesh&&mesh.vertexCount==6370,
                    "Model adapter must reuse the original low-density curved photo mesh");
                Check(renderer.sharedMaterials.Length==2&&renderer.sharedMaterials[0].mainTexture==photo,
                    "Adapter must preserve both photo materials");
                originalImage=Capture(preview);
                Vector2 touch=ScreenPoint(model.transform.TransformPoint(new Vector3(.015f,.1f,0)));
                Frame(touch,true,true,false);
                Check(effect.HasActivePokes&&!source.Warp.HasActivePokes,
                    "Clicking the photo model invokes an independent instance of the person-deformation spring");
                var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);
                Check(Mathf.Abs(block.GetFloat("_PokeAmp0")-source.Warp.stretchAmount)<.001f,
                    "Model and camera-view deformation share the same configured stretch strength");
                Check(Mathf.Abs(model.transform.localEulerAngles.y)<.01f,"A stationary tap must not rotate the model");
                pressedImage=Capture(preview);
                Check(DifferentPixels(originalImage,pressedImage)>80,"A model tap must visibly deform the photo on the GPU");
                Vector4 firstPoint=block.GetVector("_PokePos0");
                Frame(touch+Vector2.up*5f,false,true,false);
                renderer.GetPropertyBlock(block);
                Check((block.GetVector("_PokePos0")-firstPoint).sqrMagnitude>.000001f,"Dragging on the model updates the local deformation point");
                Frame(touch,false,false,true);
                bool overshot=false;
                for(int i=0;i<600;i++)
                {
                    effect.Advance(1f/120f);renderer.GetPropertyBlock(block);
                    overshot|=block.GetFloat("_PokeAmp0")<-.005f;
                }
                Check(overshot&&!effect.HasActivePokes&&block.GetFloat("_PokeAmp0")==0f,
                    "Releasing uses the existing elastic overshoot and settles to the exact original pose");
                recoveredImage=Capture(preview);
                Check(DifferentPixels(originalImage,recoveredImage)<12,"Settled model photo returns to the original GPU image");

                effect.Clear();
                Rect rect=showcase.PreviewRect;
                Vector2 outside=new Vector2(rect.x+rect.width*.04f,touch.y);
                Frame(outside,true,true,false);
                Check(!effect.HasActivePokes,"Touching empty preview space must not deform the character");
                Frame(touch,false,true,false);
                Check(Quaternion.Angle(model.transform.localRotation,Quaternion.identity)>5f&&effect.HasActivePokes,
                    "Swiping across the model simultaneously rotates it and triggers surface deformation");
                Frame(touch,false,false,true);
                effect.Clear();
                Set(showcase,"m_Yaw",180f);model.transform.localRotation=Quaternion.Euler(0,180,0);
                touch=ScreenPoint(model.transform.TransformPoint(new Vector3(.015f,.1f,0)));
                Frame(touch,true,true,false);
                Check(effect.HasActivePokes,"The rotated back photo surface must also accept deformation");
                showcase.ExitPresentation();
                Check(!showcase.IsPresenting&&Get(showcase,"m_ModelDeformation")==null,
                    "Exit clears model deformation, picking collider and preview resources");

                var sheet=new Texture2D(originalImage.width*3,originalImage.height,TextureFormat.RGBA32,false);
                try
                {
                    sheet.SetPixels(0,0,originalImage.width,originalImage.height,originalImage.GetPixels());
                    sheet.SetPixels(originalImage.width,0,pressedImage.width,pressedImage.height,pressedImage.GetPixels());
                    sheet.SetPixels(originalImage.width*2,0,recoveredImage.width,recoveredImage.height,recoveredImage.GetPixels());
                    sheet.Apply();File.WriteAllBytes("Assets/BodyThrow/ArtPreviews/ShowcaseDeformationStages.png",sheet.EncodeToPNG());
                }
                finally{Object.DestroyImmediate(sheet);}
                Debug.Log("[ShowcaseInteraction] GPU/gestures PASS: tap, drag point, rebound, background miss, swipe+rotation and rotated back face. Preview: ShowcaseDeformationStages.png");

                Vector2 ScreenPoint(Vector3 world)
                {
                    Vector3 viewport=preview.WorldToViewportPoint(world);Rect bounds=showcase.PreviewRect;
                    return new Vector2(bounds.x+viewport.x*bounds.width,Screen.height-bounds.y-(1-viewport.y)*bounds.height);
                }
                void Frame(Vector2 position,bool down,bool held,bool released) => showcase.ProcessPresentationPointer(
                    new BodyPointerFrame{id=19,position=position,down=down,held=held,released=released},true);
            }
            finally
            {
                if(showcase!=null)showcase.ResetForDetectionStop();
                Object.DestroyImmediate(root);
                if(originalImage!=null)Object.DestroyImmediate(originalImage);
                if(pressedImage!=null)Object.DestroyImmediate(pressedImage);
                if(recoveredImage!=null)Object.DestroyImmediate(recoveredImage);
            }
        }

        static Texture2D Capture(Camera camera)
        {
            camera.Render();var previous=RenderTexture.active;
            var output=new Texture2D(240,320,TextureFormat.RGBA32,false);
            var target=RenderTexture.GetTemporary(240,320,0,RenderTextureFormat.ARGB32);
            try
            {
                Graphics.Blit(camera.targetTexture,target);RenderTexture.active=target;
                output.ReadPixels(new Rect(0,0,240,320),0,0);output.Apply();return output;
            }
            finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);}
        }
        static int DifferentPixels(Texture2D a,Texture2D b)
        {
            var first=a.GetPixels32();var second=b.GetPixels32();int count=0;
            for(int i=0;i<first.Length;i++)if(Mathf.Abs(first[i].r-second[i].r)+Mathf.Abs(first[i].g-second[i].g)+
                Mathf.Abs(first[i].b-second[i].b)+Mathf.Abs(first[i].a-second[i].a)>12)count++;
            return count;
        }
        static object Get(object value,string field)=>value.GetType().GetField(field,Private).GetValue(value);
        static void Set(object value,string field,object content)=>value.GetType().GetField(field,Private).SetValue(value,content);
        static void Invoke(object value,string method)=>value.GetType().GetMethod(method,Private).Invoke(value,null);
        static void Check(bool value,string message){if(!value)throw new InvalidOperationException("[ShowcaseInteraction] FAIL: "+message);}
    }
}
