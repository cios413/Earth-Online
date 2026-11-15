using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using EarthOL.BodyThrow;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace EarthOL.EditorTools
{
    /// <summary>Non-destructive art import, GPU previews and behavior checks, safe in an open unsaved scene.</summary>
    public static class JellySkeletonArtValidation
    {
        const string ArtPath = "Assets/Resources/CartoonJelly";
        public const string OutputFolder = "Assets/BodyThrow/ArtPreviews";
        static readonly Color Background = new Color(.13f,.12f,.20f,1f);

        [MenuItem("Tools/Earth OL/Validate jelly skeleton artwork")]
        public static void Validate()
        {
            foreach(string name in new[]{"Skull","RibcagePelvis","SingleBone","PairedBone"})
            {
                string path=ArtPath+"/"+name+".png";
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                Check(importer!=null,"Generated original PNG exists: "+name);
                var settings=new TextureImporterSettings();importer.ReadTextureSettings(settings);
                bool dirty=importer.textureType!=TextureImporterType.Sprite || importer.mipmapEnabled ||
                    !importer.alphaIsTransparency || settings.spriteMeshType!=SpriteMeshType.FullRect;
                if(dirty)
                {
                    settings.spriteMeshType=SpriteMeshType.FullRect;
                    importer.SetTextureSettings(settings);
                    importer.textureType=TextureImporterType.Sprite;
                    importer.spriteImportMode=SpriteImportMode.Single;
                    importer.alphaIsTransparency=true;
                    importer.mipmapEnabled=false;
                    importer.sRGBTexture=true;
                    importer.maxTextureSize=1024;
                    importer.textureCompression=TextureImporterCompression.Uncompressed;
                    importer.wrapMode=TextureWrapMode.Clamp;
                    importer.SaveAndReimport();
                }
                Check(Resources.Load<Sprite>("CartoonJelly/"+name)!=null,"Sprite import: "+name);
            }
            var material=Resources.Load<Material>("CartoonJelly/ConnectedJelly");
            Check(material!=null && material.shader.isSupported && !ShaderUtil.ShaderHasError(material.shader),"Connected jelly shader compiles");
            Directory.CreateDirectory(OutputFolder);
            var previews=new List<Texture2D>();
            try
            {
                for(int variant=0;variant<3;variant++)previews.Add(RenderAndCheck(variant));
                var sheet=new Texture2D(640*3,960,TextureFormat.RGBA32,false);
                for(int i=0;i<previews.Count;i++)sheet.SetPixels(i*640,0,640,960,previews[i].GetPixels());
                sheet.Apply();
                File.WriteAllBytes(OutputFolder+"/JellySkeletonContactSheet.png",sheet.EncodeToPNG());
                Object.DestroyImmediate(sheet);
            }
            finally { foreach(var preview in previews)Object.DestroyImmediate(preview); }
            AssetDatabase.Refresh();
            Debug.Log("[JellyArt] ALL PASS: imported original sprites; connected one-pass envelope; body/limb palette isolation; selection rerolls; tracking retains colors; loss/recovery; bent joints and root mirror; no changes to 3D snapshot system. GPU previews: "+OutputFolder);
        }

        static Texture2D RenderAndCheck(int variant)
        {
            var preview=new PreviewRenderUtility();
            var root=new GameObject("JellyArtValidationOwner");
            preview.AddSingleGO(root);
            var camera=preview.camera;
            camera.transform.SetPositionAndRotation(new Vector3(320,480,-10),Quaternion.identity);
            camera.orthographic=true; camera.orthographicSize=480f;
            camera.nearClipPlane=.01f; camera.farClipPlane=100f;
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Background;
            camera.allowHDR=camera.allowMSAA=false;
            camera.cullingMask=~0;
            camera.cameraType=CameraType.Game;
            Cartoon2DCanvas cartoons=null;
            Canvas canvas=null;
            Texture2D captured=null;
            preview.BeginPreview(new Rect(0,0,640,960),GUIStyle.none);
            // PreviewRenderUtility uses physical pixels on HiDPI monitors.
            int targetWidth=camera.targetTexture.width,targetHeight=camera.targetTexture.height;
            camera.aspect=(float)targetWidth/targetHeight;
            camera.orthographicSize=targetHeight*.5f;
            camera.transform.position=new Vector3(targetWidth*.5f,targetHeight*.5f,-10f);
            try
            {
                var tracking=root.AddComponent<PersonTrackingManager>();
                var pose=CreatePose(variant==1);
                float time=Time.realtimeSinceStartup;
                tracking.UpdateDetections(new[]{pose},time);
                int id=tracking.Persons[0].personId;
                cartoons=root.AddComponent<Cartoon2DCanvas>(); cartoons.Configure(camera,tracking);
                canvas=(Canvas)typeof(Cartoon2DCanvas).GetField("m_Canvas",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(cartoons);
                preview.AddSingleGO(canvas.gameObject);
                Check(canvas.renderMode==RenderMode.ScreenSpaceOverlay,"Production Canvas remains screen overlay");
                // Offscreen GPU harness uses pixel units in an orthographic world Canvas.
                // Its XY plane matches null-camera Overlay screen-to-Canvas mapping exactly.
                canvas.renderMode=RenderMode.WorldSpace; canvas.worldCamera=camera;
                canvas.GetComponent<CanvasScaler>().uiScaleMode=CanvasScaler.ScaleMode.ConstantPixelSize;
                var canvasRect=(RectTransform)canvas.transform;
                canvasRect.sizeDelta=new Vector2(targetWidth,targetHeight);
                canvasRect.position=new Vector3(targetWidth*.5f,targetHeight*.5f,0);
                canvasRect.rotation=Quaternion.identity;canvasRect.localScale=Vector3.one;
                Canvas.ForceUpdateCanvases();
                cartoons.ActivatePerson(id); cartoons.Refresh(); Canvas.ForceUpdateCanvases();
                var rig=canvas.GetComponentInChildren<Cartoon2DRig>();
                Check(rig!=null && rig.GetComponentsInChildren<Image>().Length==10,"Ten skull/rib/bone sprites");
                Check(rig.JellyShell!=null && rig.JellyShell.SegmentCount==11,"Head+neck+torso+8 limbs form one shell");
                Check(rig.GetComponentsInChildren<RawImage>().Length==1,"One envelope draw per person, no per-joint offscreen buffers");
                var chosen=cartoons.PaletteFor(id);
                Check(chosen.body!=chosen.limbs,"Body/limb colors must differ");
                for(int i=0;i<20;i++)cartoons.Refresh();
                Check(rig.Palette.body==chosen.body && rig.Palette.limbs==chosen.limbs,"Tracking must not reroll colors");
                cartoons.ActivatePerson(id);
                var rerolled=cartoons.PaletteFor(id);
                Check(chosen.body!=rerolled.body || chosen.limbs!=rerolled.limbs,"A new valid click rerolls the palette");

                var palette=variant==0?new JellySkeletonPalette{body=new Color(1f,.24f,.51f),limbs=new Color(.20f,.85f,.76f)}:
                    variant==1?new JellySkeletonPalette{body=new Color(.63f,.34f,1f),limbs=new Color(1f,.65f,.17f)}:
                    new JellySkeletonPalette{body=new Color(.71f,.92f,.16f),limbs=new Color(.98f,.33f,.66f)};
                rig.SetPalette(palette);
                if(variant==2)tracking.Persons[0].facing=PersonFacing.Left;
                cartoons.Refresh(); Canvas.ForceUpdateCanvases();
                var shell=rig.JellyShell;
                Check(shell.IsVisible && shell.RuntimeMaterial.GetColor("_BodyColor")==palette.body &&
                    shell.RuntimeMaterial.GetColor("_LimbColor")==palette.limbs,"Exactly two shell color groups");
                foreach(var image in rig.GetComponentsInChildren<Image>())
                    Check(image.color.r==1f && image.color.g==1f && image.color.b==1f,"Bones remain original warm ivory, not palette-tinted");

                camera.Render();
                var temporary=RenderTexture.GetTemporary(640,960,0,RenderTextureFormat.ARGB32);
                var previous=RenderTexture.active;
                captured=new Texture2D(640,960,TextureFormat.RGBA32,false);
                try
                {
                    // Canvas is already rendered into BeginPreview's camera target.
                    Graphics.Blit(camera.targetTexture,temporary);
                    RenderTexture.active=temporary;
                    captured.ReadPixels(new Rect(0,0,640,960),0,0);captured.Apply();
                }
                finally { RenderTexture.active=previous;RenderTexture.ReleaseTemporary(temporary); }
                int components=CountComponents(captured,out int inkPixels);
                File.WriteAllBytes(OutputFolder+"/JellySkeleton_"+variant+".png",captured.EncodeToPNG());
                Check(inkPixels>18000,"GPU must render substantial jelly/skeleton artwork");
                Check(components==1,"Entire silhouette must be connected across neck/shoulders/elbows/hips/knees; components="+components);
                Debug.Log($"[JellyArt] GPU variant={variant} connectedComponents={components} visiblePixels={inkPixels} segments={shell.SegmentCount}");

                // Pool/recover preserves the selected person's colors and does not resurrect stale geometry.
                if(variant==0)
                {
                    var previousPalette=cartoons.PaletteFor(id);
                    Vector3 previousRoot=rig.transform.position;
                    var moving=CreatePose(false);
                    for(int p=0;p<17;p++)moving.keypoints[p]+=Vector2.right*.035f;
                    var movedBox=moving.screenRect;movedBox.center+=Vector2.right*.035f;moving.screenRect=movedBox;
                    tracking.UpdateDetections(new[]{moving},time+.2f);cartoons.Refresh();
                    Check(rig.transform.position.x>previousRoot.x,"Art rig follows live bounding box movement");
                    Check(cartoons.PaletteFor(id).body==previousPalette.body && cartoons.PaletteFor(id).limbs==previousPalette.limbs,
                        "Live movement cannot reroll a selected palette");
                    tracking.SuspendPeople();cartoons.Refresh();
                    Check(cartoons.RigCount==0 && !shell.IsVisible,"Temporarily lost person hides both layers");
                    tracking.ResumeCandidates(time+.3f);tracking.UpdateDetections(new[]{moving},time+.4f);cartoons.Refresh();
                    var recovered=canvas.GetComponentInChildren<Cartoon2DRig>();
                    Check(recovered==rig && recovered.PersonId==id && recovered.Palette.body==previousPalette.body &&
                        recovered.Palette.limbs==previousPalette.limbs,"Recovery reuses original ID/palette without new material");
                    tracking.ClearPeople();cartoons.Refresh();
                    Check(cartoons.RigCount==0 && !shell.IsVisible,"Loss hides both skeleton and shell");
                    tracking.UpdateDetections(new[]{CreatePose(false)},time+.1f);
                    int next=tracking.Persons[0].personId;cartoons.ActivatePerson(next);cartoons.Refresh();
                    Check(cartoons.RigCount==1,"Rig pool supports the next person");
                    var reused=canvas.GetComponentInChildren<Cartoon2DRig>();
                    Check(reused==rig && reused.Palette.body==cartoons.PaletteFor(next).body,"Pooled shell takes new person's palette");
                }
                var result=captured;captured=null;return result;
            }
            finally
            {
                preview.EndPreview();
                if(cartoons!=null)Object.DestroyImmediate(cartoons);
                if(captured!=null)Object.DestroyImmediate(captured);
                preview.Cleanup();
            }
        }

        static BodyPoseResult CreatePose(bool bend)
        {
            var pose=PersonSystemValidation.FullPose();
            if(bend)
            {
                pose.keypoints[7]=new Vector2(.25f,.82f);pose.keypoints[9]=new Vector2(.28f,.96f);
                pose.keypoints[8]=new Vector2(.79f,.65f);pose.keypoints[10]=new Vector2(.83f,.82f);
                pose.keypoints[13]=new Vector2(.32f,.28f);pose.keypoints[15]=new Vector2(.23f,.14f);
                pose.keypoints[14]=new Vector2(.67f,.33f);pose.keypoints[16]=new Vector2(.74f,.11f);
            }
            return pose;
        }

        static int CountComponents(Texture2D image,out int inkPixels)
        {
            var pixels=image.GetPixels32();int width=image.width,height=image.height;
            var mask=new bool[pixels.Length];var visited=new bool[pixels.Length];
            // Find the real clear color from the actual render target (linear/sRGB conversion varies).
            var bg=pixels[0];inkPixels=0;
            for(int i=0;i<pixels.Length;i++)
            {
                var p=pixels[i];mask[i]=Mathf.Abs(p.r-bg.r)+Mathf.Abs(p.g-bg.g)+Mathf.Abs(p.b-bg.b)>35;
                if(mask[i])inkPixels++;
            }
            int count=0;var queue=new Queue<int>();
            for(int i=0;i<mask.Length;i++)
            {
                if(!mask[i]||visited[i])continue;
                int size=0;queue.Enqueue(i);visited[i]=true;
                while(queue.Count>0)
                {
                    int current=queue.Dequeue();size++;int x=current%width,y=current/width;
                    for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
                    {
                        if(x+dx<0||x+dx>=width||y+dy<0||y+dy>=height)continue;
                        int next=(y+dy)*width+x+dx;
                        if(!mask[next]||visited[next])continue;visited[next]=true;queue.Enqueue(next);
                    }
                }
                if(size>40)count++;
            }
            return count;
        }

        static void Check(bool passed,string message)
        {if(!passed)throw new InvalidOperationException("[JellyArt] FAIL: "+message);}
    }
}
