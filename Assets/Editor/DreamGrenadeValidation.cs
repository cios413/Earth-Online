using System;
using System.IO;
using System.Reflection;
using EarthOL.BodyThrow;
using EarthOL.Combat;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EarthOL.EditorTools
{
    public static class DreamGrenadeValidation
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        [Serializable] sealed class AnimationTiming { public int frameMilliseconds, heroFrame; }

        [MenuItem("Tools/Earth OL/Validate dream grenade")]
        public static void Validate()
        {
            var catalog = Resources.Load<BodyProjectileCatalog>("Throwables/Catalog");
            int index = Array.FindIndex(catalog.entries, value => value.key == "DreamGrenade");
            Check(index >= 5 && Array.FindAll(catalog.entries, value => value.key == "DreamGrenade").Length == 1,
                "New grenade is appended exactly once to the existing catalog");
            Check(catalog.entries[index].preview != null && catalog.entries[index].prefab != null,
                "Wheel entry has an imported thumbnail and prefab");
            ValidateThrowing(catalog, index);
            ValidateRendering(catalog.entries[index]);
            Debug.Log("[DreamGrenadeValidation] ALL PASS: catalog, flight, billboard, one impact, combat, flash/hide/burst, hollow center, finite cleanup, cancellation, concurrency and GPU sequence.");
        }

        static void ValidateThrowing(BodyProjectileCatalog catalog, int index)
        {
            var root = new GameObject("Dream grenade lifecycle validation");
            try
            {
                var camera = root.AddComponent<Camera>(); camera.enabled = false;
                camera.pixelRect = new Rect(0,0,1000,1000);
                var tracking = root.AddComponent<PersonTrackingManager>();
                tracking.UpdateDetections(new[] { PersonSystemValidation.FullPose() }, Time.realtimeSinceStartup);
                var thrower = root.AddComponent<Person3DThrowController>();
                thrower.Configure(camera, tracking, catalog); thrower.SetInteractionEnabled(true);
                var combat = root.AddComponent<PersonCombatController>();
                combat.Configure(camera,tracking,null,thrower,null); combat.BeginSession(true,1);
                int hits = 0; thrower.InteractionConfirmed += hit => { hits++; Check(hit.actionId == "DreamGrenade", "Impact identity"); };
                Vector2 tap = camera.ViewportToScreenPoint(new Vector3(.5f,.55f,0));
                int id = tracking.Pick(new Vector2(.5f,.55f)).personId;
                combat.SelectPerson(id);
                Check(thrower.SelectProjectile(index) && thrower.TryThrowAt(tap), "New wheel choice launches");
                var dream = root.GetComponentInChildren<DreamGrenadeProjectile>();
                Check(dream != null && dream.SpriteVisible && hits == 0, "Flight shows grenade without premature damage");
                Advance(thrower,.40f);
                Check(hits == 0 && !dream.BurstStarted, "No explosion before impact");
                Advance(thrower,.03f);
                Check(hits == 1 && dream.SpriteVisible && !dream.BurstStarted && thrower.SlimeSurfaceCount == 0,
                    "Person impact starts flash without creating a slime puddle");
                Check(combat.FindRound(id).History.Count == 1 && combat.FindRound(id).CurrentHealth < combat.FindRound(id).MaxHealth,
                    "Battle mode registers exactly one actual-impact attack for the new entry");
                Advance(thrower,.20f);
                Check(!dream.SpriteVisible && dream.BurstStarted && hits == 1, "Bright grenade hides as the burst appears");
                camera.transform.rotation = Quaternion.Euler(15,37,8);
                dream.RenderImpact(camera,.7f);
                Check(Quaternion.Angle(dream.transform.rotation,camera.transform.rotation) < .01f,
                    "Flat art and volumetric billboards face the camera after AR pose changes");
                Advance(thrower,5);
                Check(thrower.ActiveProjectileCount == 0 && dream == null && combat.FindRound(id).History.Count == 1,
                    "Finite burst is recycled without duplicate attacks");
                for (int i = 0; i < 8; i++) Check(thrower.TryThrowAt(tap), "Repeated throws launch");
                Check(thrower.ActiveProjectileCount == 4, "Concurrent dream effects are bounded for mobile overdraw");
                thrower.ClearEffects();
                Check(root.GetComponentsInChildren<DreamGrenadeProjectile>().Length == 0, "Reset cancels all flying effects");
                thrower.SelectProjectile(index); thrower.TryThrowAt(tap); Advance(thrower,.8f);
                thrower.ClearEffects();
                Check(thrower.ActiveProjectileCount == 0 && root.GetComponentsInChildren<DreamGrenadeProjectile>().Length == 0,
                    "Reset also cancels already emitting effects");
                thrower.SelectProjectile(index); thrower.TryThrowAt(tap); Advance(thrower,.8f);
                tracking.ClearPeople();
                Advance(thrower,.1f);
                Check(thrower.ActiveProjectileCount == 0, "Lost person removes their effect");
                Debug.Log("[DreamGrenadeValidation] lifecycle and combat PASS");
            }
            finally { Object.DestroyImmediate(root); }
        }

        static void ValidateRendering(BodyProjectileCatalog.Entry entry)
        {
            const int size = 512;
            var root = new GameObject("Dream grenade GPU sequence");
            var target = new RenderTexture(size,size,24,RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            var sheet = new Texture2D(size*3,size*2,TextureFormat.RGBA32,false);
            try
            {
                var camera = root.AddComponent<Camera>(); camera.enabled = false;
                camera.orthographic = true; camera.orthographicSize = .53f; camera.aspect = 1;
                camera.nearClipPlane = .01f; camera.farClipPlane = 5;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.065f,.055f,.12f,1);
                camera.allowHDR = false; camera.allowMSAA = false; camera.targetTexture = target;
                camera.cullingMask = 1 << 30;
                var shot = Object.Instantiate(entry.prefab,root.transform); shot.enabled = false;
                shot.transform.localPosition = Vector3.forward*2;
                shot.transform.localScale = Vector3.one*.25f;
                foreach (var node in shot.GetComponentsInChildren<Transform>()) node.gameObject.layer = 30;
                var dream = shot.GetComponent<DreamGrenadeProjectile>();
                var frame = new Texture2D(size,size,TextureFormat.RGBA32,false);
                try
                {
                    float flightLuminance = 0;
                    float burstSeconds = dream.TotalImpactSeconds-dream.FlashSeconds;
                    float[] ages = { 0,dream.FlashSeconds*.90625f,
                        dream.FlashSeconds+Mathf.Min(.16f,burstSeconds*.15f),
                        dream.FlashSeconds+Mathf.Min(.74f,burstSeconds*.55f),
                        dream.FlashSeconds+Mathf.Min(.95f,burstSeconds*.70f),
                        dream.FlashSeconds+burstSeconds*.76f };
                    for (int stage = 0; stage < ages.Length; stage++)
                    {
                        if (stage == 0) dream.RenderFlight(camera, .5f);
                        else dream.RenderImpact(camera, ages[stage]);
                        if (stage == 2) Check(dream.VisibleStarCount == 0,"Liquid flies out before any stars flash");
                        if ((stage == 3 || stage == 4) && burstSeconds > .8f)
                            Check(dream.VisibleStarCount > 0,"Delayed stars appear around the flown liquid");
                        foreach (var renderer in shot.GetComponentsInChildren<Renderer>())
                            Check(renderer.sharedMaterial.shader.isSupported && !ShaderUtil.ShaderHasError(renderer.sharedMaterial.shader),
                                "GPU supports all grenade and bloom shaders");
                        camera.orthographicSize = stage < 2 ? .075f : .49f;
                        shot.transform.localPosition = new Vector3(0,stage < 2 ? 0 : -.30f,2);
                        camera.Render(); RenderTexture.active = target;
                        frame.ReadPixels(new Rect(0,0,size,size),0,0); frame.Apply();
                        sheet.SetPixels(stage%3*size,(1-stage/3)*size,size,size,frame.GetPixels());
                        int lit = 0, pastel = 0, border = 0; float luminance = 0;
                        foreach (var c in frame.GetPixels())
                        {
                            if (c.maxColorComponent > .3f) lit++;
                            if (c.r > .2f && c.b > .2f) pastel++;
                            luminance += c.grayscale;
                        }
                        for (int x = 0; x < size; x++)
                            if (frame.GetPixel(x,0).maxColorComponent > .2f || frame.GetPixel(x,size-1).maxColorComponent > .2f) border++;
                        Check(lit > 80, "Every visible stage renders real pixels");
                        if (stage == 0) flightLuminance = luminance;
                        if (stage == 1) Check(luminance > flightLuminance*1.2f, "Impact visibly brightens the sprite");
                        if (stage >= 2) Check(pastel > 1000 && border < 4, "Colored glow fills space with no rectangular hard edge");
                        if (stage >= 2 && dream.CenterGapRadius > 0)
                        {
                            Vector3 viewport = camera.WorldToViewportPoint(shot.transform.position);
                            Vector2 center = new Vector2(viewport.x,viewport.y)*size;
                            float radius = dream.CenterGapRadius*shot.transform.lossyScale.x/
                                (camera.orthographicSize*2)*size*.55f;
                            Color background = frame.GetPixel(0,0);
                            int samples = 0;
                            for (int y = Mathf.Max(0,Mathf.FloorToInt(center.y-radius)); y <= Mathf.Min(size-1,Mathf.CeilToInt(center.y+radius)); y++)
                            for (int x = Mathf.Max(0,Mathf.FloorToInt(center.x-radius)); x <= Mathf.Min(size-1,Mathf.CeilToInt(center.x+radius)); x++)
                            {
                                if ((new Vector2(x+.5f,y+.5f)-center).sqrMagnitude > radius*radius) continue;
                                Color pixel = frame.GetPixel(x,y);
                                Check(Mathf.Abs(pixel.r-background.r)+Mathf.Abs(pixel.g-background.g)+Mathf.Abs(pixel.b-background.b) < .025f,
                                    "Liquid, haze and bloom must leave the center visibly empty at every burst stage");
                                samples++;
                            }
                            Check(samples > 0,"Central gap has visible pixel area");
                        }
                        if (stage == 4)
                        {
                            int above = 0, below = 0;
                            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                            {
                                if (frame.GetPixel(x,y).maxColorComponent < .35f) continue;
                                float localY = ((y+.5f)/size*2-1)*camera.orthographicSize;
                                if (localY > -.30f) above++; else below++;
                            }
                            Check(above > below*5,"Splash grows upward from a bottom origin, with only a small glow below");
                        }
                        Debug.Log($"[DreamGrenadeValidation] GPU stage={stage}, lit={lit}, pastel={pastel}, luminance={luminance:F1}");
                    }
                    dream.RenderImpact(camera,dream.TotalImpactSeconds+.1f);
                    camera.Render(); RenderTexture.active = target;
                    frame.ReadPixels(new Rect(0,0,size,size),0,0); frame.Apply();
                    foreach (var pixel in frame.GetPixels()) Check(pixel.maxColorComponent < .2f, "Finished burst leaves no pixels");
                    string animationFolder = Path.Combine(Path.GetTempPath(),"opencode","dream-liquid-frames");
                    Directory.CreateDirectory(animationFolder);
                    float clipSeconds = dream.TotalImpactSeconds+.1f;
                    for (int i = 0; i < 66; i++)
                    {
                        dream.RenderImpact(camera,i/65f*clipSeconds);
                        camera.Render(); RenderTexture.active = target;
                        frame.ReadPixels(new Rect(0,0,size,size),0,0); frame.Apply();
                        File.WriteAllBytes(Path.Combine(animationFolder,$"frame-{i:D3}.png"),frame.EncodeToPNG());
                    }
                    File.WriteAllText(Path.Combine(animationFolder,"timing.json"),JsonUtility.ToJson(new AnimationTiming {
                        frameMilliseconds = Mathf.RoundToInt(clipSeconds/65f*1000),
                        heroFrame = Mathf.Clamp(Mathf.RoundToInt((dream.FlashSeconds+Mathf.Min(.95f,burstSeconds*.70f))/clipSeconds*65),0,65)
                    }));
                }
                finally { Object.DestroyImmediate(frame); }
                sheet.Apply();
                string path = "Assets/BodyThrow/ArtPreviews/DreamGrenadeSequence.png";
                File.WriteAllBytes(path,sheet.EncodeToPNG()); AssetDatabase.ImportAsset(path);
                Debug.Log("[DreamGrenadeValidation] GPU preview: " + path);
            }
            finally
            {
                RenderTexture.active = previous; Object.DestroyImmediate(root);
                target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(sheet);
            }
        }

        static void Advance(Person3DThrowController thrower, float dt)
            => typeof(Person3DThrowController).GetMethod("UpdateThrownItems",Private).Invoke(thrower,new object[] { dt });
        static void Check(bool passed, string message)
        { if (!passed) throw new InvalidOperationException("[DreamGrenadeValidation] FAIL: " + message); }
    }
}
