using System;
using System.Collections;
using System.Reflection;
using EarthOL.ARCore;
using EarthOL.BodyThrow;
using EarthOL.Combat;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace EarthOL.EditorTools
{
    public static class CombatIntegrationValidation
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        public static void Validate()
        {
            ValidateRules();
            ShowcaseInteractionValidation.Validate();
            ValidateSessions();
            Debug.Log("[CombatValidation] ALL PASS: entry/free/combat reuse, independent rounds, press/swipe/hold, projectile impact, deduplication, stale events, reset and existing GPU regressions.");
        }

        static void ValidateRules()
        {
            var combo = ScriptableObject.CreateInstance<ComboConfig>();
            var damage = ScriptableObject.CreateInstance<AttackDamageConfig>();
            var attack = ScriptableObject.CreateInstance<AttackDefinition>();
            try
            {
                attack.Configure("press", "按压", "", "次", 10f, AttackType.BarehandPress);
                var left = new CombatRound(1, 11, 120f);
                var right = new CombatRound(2, 12, 120f);
                var hit = new PersonInteractionHit(1,"press",Vector2.one*.5f,1,11);
                Check(left.TryApply(hit,attack,combo,damage,1f,1f,out var first) && first.FinalDamage==10f,"First normal hit uses wang base damage");
                Check(!left.TryApply(hit,attack,combo,damage,1.1f,1f,out _),"Duplicate event cannot apply twice");
                Check(!right.TryApply(hit,attack,combo,damage,1.1f,1f,out _) && right.CurrentHealth==120f,"Damage is isolated by person and round");
                Check(left.TryApply(new PersonInteractionHit(1,"press",Vector2.zero,2,11),attack,combo,damage,1.2f,0f,out var critical) &&
                    critical.Critical && Mathf.Abs(critical.FinalDamage-18.75f)<.001f,"Second hit combines wang 1.25 combo and 1.5 critical multipliers");
                left.Tick(6f,combo);
                Check(left.ComboCount==0 && left.BestCombo==2,"Combo timeout resets the active combo, retaining round statistics");
                var replacement = new CombatRound(1,13,60);
                Check(!replacement.TryApply(hit,attack,combo,damage,6f,1f,out _),"Old round token is rejected after restart");
                attack.Configure("press","按压","","次",500f,AttackType.BarehandPress);
                Check(replacement.TryApply(new PersonInteractionHit(1,"press",Vector2.zero,3,13),attack,combo,damage,7f,1f,out var final) &&
                    replacement.Defeated && final.AppliedDamage==60f && final.FinalDamage==500f,"Lethal damage clamps applied damage and defeats only once");
                Check(!replacement.TryApply(new PersonInteractionHit(1,"press",Vector2.zero,4,13),attack,combo,damage,8f,1f,out _),"Defeated round cannot accept another hit");
                Check(PersonCombatController.SegmentRect(new Vector2(0,.5f),new Vector2(1,.5f),new Rect(.2f,.2f,.2f,.6f),out _) &&
                    !PersonCombatController.SegmentRect(Vector2.zero,Vector2.right,new Rect(.2f,.2f,.2f,.6f),out _),"Full path intersection catches fast swipes and rejects misses");
            }
            finally { Object.DestroyImmediate(combo); Object.DestroyImmediate(damage); Object.DestroyImmediate(attack); }
            Debug.Log("[CombatValidation] wang damage/combo/critical, deduplication and per-person round rules PASS");
        }

        static void ValidateSessions()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            try
            {
                var bootstrap = new GameObject("Application combat regression").AddComponent<ARCoreMainSceneBootstrap>();
                Set(bootstrap,"startPersonDetection",false);
                var startup=(IEnumerator)Invoke(bootstrap,"Start");
                startup.MoveNext();
                Check(bootstrap.ARCamera==null && bootstrap.DetectionManager==null && !bootstrap.SessionActive,"Startup menu creates no AR or recognition runtime");
                bootstrap.EnterApp(AppMode.Free);
                while(startup.MoveNext()) { }
                var tracking=bootstrap.PersonTracking;
                var detector=bootstrap.DetectionManager;
                var thrower=bootstrap.ThrowController;
                var surface=bootstrap.PersonDeformer;
                var combat=bootstrap.CombatController;
                bootstrap.ARCamera.pixelRect=new Rect(0,0,1000,1000);
                Seed(tracking);
                int left=tracking.Pick(new Vector2(.27f,.5f)).personId;
                int right=tracking.Pick(new Vector2(.73f,.5f)).personId;
                Vector2 leftPoint=bootstrap.ARCamera.ViewportToScreenPoint(new Vector3(.27f,.5f,0));
                Check(!combat.Active && surface.ReadPointerInput && !detector.AutoStart,"Free mode keeps original controllers and startup is owned by the menu");
                bootstrap.SetMode(GameMode.Sticker);
                Check(bootstrap.ClickController.TryActivateAt(leftPoint) && combat.Rounds.Count==0,"Free stickers work without combat state");
                bootstrap.SetMode(GameMode.Throw);
                Check(thrower.TryThrowAt(leftPoint),"Free projectile uses shared throw controller");
                Invoke(thrower,"UpdateThrownItems",.5f);
                Check(combat.Rounds.Count==0 && thrower.AttachedProjectileCount==1,"Free impacts never create damage rounds");
                bootstrap.ReturnToSelection();
                Check(!bootstrap.SessionActive && tracking.Count==0 && thrower.ActiveProjectileCount==0 && bootstrap.CartoonCanvas.SelectedPersonCount==0,"Returning clears effects and people");
                bootstrap.EnterApp(AppMode.Combat);
                Check(ReferenceEquals(tracking,bootstrap.PersonTracking) && ReferenceEquals(detector,bootstrap.DetectionManager) &&
                    ReferenceEquals(thrower,bootstrap.ThrowController) && ReferenceEquals(surface,bootstrap.PersonDeformer),"Both modes reuse identical runtime instances");
                Seed(tracking);
                left=tracking.Pick(new Vector2(.27f,.5f)).personId; right=tracking.Pick(new Vector2(.73f,.5f)).personId;
                int leftToken=combat.CaptureRound(left), rightToken=combat.CaptureRound(right);
                Frame(combat,leftPoint,true,true,false);
                Frame(combat,leftPoint,false,false,true);
                Check(combat.FindRound(left).History.Count==1 && combat.FindRound(right).History.Count==0,"Tap attacks its own person only");
                Frame(combat,leftPoint,true,true,false);
                Frame(combat,leftPoint,false,false,false,true);
                Check(combat.FindRound(left).History.Count==1,"Canceled press never reports damage");
                Vector2 a=bootstrap.ARCamera.ViewportToScreenPoint(new Vector3(.01f,.5f,0));
                Vector2 b=bootstrap.ARCamera.ViewportToScreenPoint(new Vector3(.99f,.5f,0));
                Frame(combat,a,true,true,false); Frame(combat,b,false,true,false); Frame(combat,a,false,true,false); Frame(combat,a,false,false,true);
                Check(combat.FindRound(left).History.Count==2 && combat.FindRound(right).History.Count==1,"Fast swipe crosses two people once each, with no duplicate on return");
                bootstrap.SetMode(GameMode.Sticker);
                int history=combat.FindRound(left).History.Count;
                bootstrap.ClickController.TryActivateAt(leftPoint);
                Check(combat.FindRound(left).History.Count==history,"Battle sticker activation is visual only");
                bootstrap.SetMode(GameMode.Throw);
                int impacts=0;
                thrower.InteractionConfirmed += hit => impacts++;
                Check(thrower.TryThrowAt(leftPoint),"Battle calls same throw implementation");
                Check(combat.FindRound(left).History.Count==history,"Launch is not a hit");
                bootstrap.SetMode(GameMode.Showcase);
                Invoke(thrower,"UpdateThrownItems",.5f); Invoke(thrower,"UpdateThrownItems",.5f);
                Check(impacts==1 && combat.FindRound(left).History.Count==history+1,"Impact counts exactly once even after feature switch");
                bootstrap.SetMode(GameMode.Throw);
                thrower.TryThrowAt(leftPoint);
                combat.RestartRound(left);
                Invoke(thrower,"UpdateThrownItems",.5f);
                Check(combat.FindRound(left).History.Count==0,"Projectile from previous round cannot damage a restarted round");
                int sticky=0;
                var catalog=Resources.Load<BodyProjectileCatalog>("Throwables/Catalog");
                for(int i=0;i<catalog.entries.Length;i++)if(catalog.entries[i].key=="StickyHand")sticky=i;
                thrower.SelectProjectile(sticky); combat.SetHolding(true);
                Check(combat.Holding && !thrower.ReadPointerInput && !surface.ReadPointerInput,"Held item owns input without duplicating throw or deformation input");
                Frame(combat,a,true,true,false); Frame(combat,b,false,true,false); Frame(combat,a,false,true,false);
                Check(combat.FindRound(left).History.Count==1 && combat.FindRound(right).History.Count==2,"Held item crosses multiple people and rate-limits repeat hits");
                Frame(combat,a,false,false,true);
                Check(!combat.Confirm(new PersonInteractionHit(left,"press",Vector2.zero,PersonInteractionHit.NextId(),leftToken)),"Stale round is rejected through controller");
                var savedRound = combat.FindRound(left);
                float savedHealth = savedRound.CurrentHealth;
                detector.StopDetection();
                Check(!combat.Holding && !thrower.ReadPointerInput && !detector.DetectionEnabled && tracking.Count==0,
                    "Stop hides tracked people and cancels active input");
                detector.StartDetection();
                Seed(tracking);
                Check(tracking.Find(left).trackingConfidence > .2f && combat.CaptureRound(left) == savedRound.Token &&
                    combat.FindRound(left).CurrentHealth == savedHealth, "Reacquisition restores the same round and health");
                bootstrap.ReturnToSelection(); bootstrap.EnterApp(AppMode.Free);
                Check(!combat.Active && combat.Rounds.Count == 0 && surface.ReadPointerInput && !thrower.ReadPointerInput &&
                    Object.FindObjectsOfType<PersonTrackingManager>().Length==1 && Object.FindObjectsOfType<PersonCombatController>().Length==1,
                    "Combat-to-free restores direct deformation and wheel throw input with no duplicate runtime");
                Check(!combat.Confirm(new PersonInteractionHit(right,"press",Vector2.zero,PersonInteractionHit.NextId(),rightToken)),"Free mode rejects late combat callbacks");
                Debug.Log("[CombatValidation] application lifecycle, free regression, multi-person input, actual impact, holding and stale callback reset PASS");
            }
            finally { foreach(var root in scene.GetRootGameObjects())if(root!=null)Object.DestroyImmediate(root); }
        }

        static void Seed(PersonTrackingManager tracking) => tracking.UpdateDetections(new[] {
            PersonSystemValidation.FullPose(.27f),PersonSystemValidation.FullPose(.73f)},Time.realtimeSinceStartup);
        static void Frame(PersonCombatController combat,Vector2 position,bool down,bool held,bool released,bool canceled=false)
            => combat.ProcessPointer(new BodyPointerFrame{id=1,position=position,down=down,held=held,released=released,canceled=canceled});
        static object Invoke(object target,string method,params object[] args)=>target.GetType().GetMethod(method,Private).Invoke(target,args);
        static void Set(object target,string field,object value)=>target.GetType().GetField(field,Private).SetValue(target,value);
        static void Check(bool condition,string message)
        { if(!condition)throw new InvalidOperationException("[CombatValidation] FAIL: "+message); }
    }
}
