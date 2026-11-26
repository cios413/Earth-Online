using System;
using System.IO;
using System.Reflection;
using EarthOL.Combat;
using EarthOL.BodyThrow;
using UnityEngine;
using UnityEditor;
using Object = UnityEngine.Object;

namespace EarthOL.EditorTools
{
    public static class CombatSnapshotValidation
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [MenuItem("Tools/Earth OL/Validate combat snapshots")]
        public static void Validate()
        {
            var root = new GameObject("Combat screenshot lifecycle validation");
            string directory = Path.Combine(Path.GetTempPath(),"earth-online-combat-snapshot-validation");
            if (Directory.Exists(directory)) Directory.Delete(directory,true);
            Directory.CreateDirectory(directory);
            try
            {
                var capture = root.AddComponent<CombatSnapshotCapture>();
                Set(capture,"m_SaveDirectory",directory);
                for (int i = 0; i < 256; i++)
                {
                    var scheduled = capture.Schedule(42,1000+i,7);
                    Check(scheduled.State==CombatSnapshotState.Waiting && scheduled.Image==null,
                        "Person selection arms a snapshot but does not capture before the sticker appears");
                    Check(ReferenceEquals(scheduled,capture.Schedule(42,1000+i,7)),
                        "A person and round can schedule only one screenshot");
                }
                var snapshots = capture.GetType().GetField("m_Snapshots",Private).GetValue(capture) as System.Collections.IDictionary;
                var snapshot = (CombatSnapshot)snapshots[1000];
                Check(snapshot.PersonId==42 && snapshot.RoundToken==1000 && snapshot.Session==7,
                    "Capture belongs to the selected person, round and session");
                Check(!capture.CaptureOnStickerDisplayed(snapshot,43,7) && !capture.CaptureOnStickerDisplayed(snapshot,42,8),
                    "Another person or stale session cannot trigger the screenshot");
                Check(capture.CaptureOnStickerDisplayed(snapshot,42,7) && snapshot.State==CombatSnapshotState.Capturing,
                    "The defeat sticker event requests the screenshot immediately");
                Check(!capture.CaptureOnStickerDisplayed(snapshot,42,7),"A sticker can trigger only one screenshot per round");
                var image = new Texture2D(4,3,TextureFormat.RGBA32,false);
                image.SetPixel(1,1,Color.green); image.Apply();
                Invoke(capture,"CompleteCapture",snapshot,image);
                Check(snapshot.State==CombatSnapshotState.Captured && snapshot.Image==image && File.Exists(snapshot.SavedPath),
                    "End-of-frame image is held for settlement and persisted as PNG");
                byte[] png = File.ReadAllBytes(snapshot.SavedPath);
                Check(png.Length>32 && png[0]==137 && png[1]==80 && png[2]==78 && png[3]==71,
                    "Saved file contains a valid PNG signature");

                var canceled = capture.Schedule(42,2000,7);
                capture.CancelPending(canceled);
                var stale = new Texture2D(2,2,TextureFormat.RGBA32,false);
                Invoke(capture,"CompleteCapture",canceled,stale);
                Check(canceled.State==CombatSnapshotState.Cancelled && canceled.Image==null && !File.Exists(canceled.SavedPath),
                    "Canceled capture cannot overwrite or save a stale frame");

                var combat = root.AddComponent<PersonCombatController>();
                var round = new CombatRound(42,1000,10);
                SetProperty(round,"Snapshot",snapshot);
                SetProperty(snapshot,"State",CombatSnapshotState.Capturing);
                var results = (System.Collections.Generic.Queue<CombatRound>)Get(combat,"m_Results");
                results.Enqueue(round); Set(combat,"m_FeedbackUntil",0f);
                Invoke(combat,"TryShowNextResult");
                Check(Get(combat,"m_Result")==null,
                    "A defeated round waits until its sticker screenshot has completed");
                SetProperty(snapshot,"State",CombatSnapshotState.Captured);
                Invoke(combat,"TryShowNextResult");
                Check(ReferenceEquals(Get(combat,"m_Result"),round) && round.Snapshot.Image==image,
                    "Settlement presents its own saved screenshot");
                capture.Release(snapshot);
                Check(round.Snapshot.Image==null && File.Exists(round.Snapshot.SavedPath),
                    "Dismissing settlement frees image memory and retains the PNG on disk");
                capture.Clear();
                ValidateSelection(root,combat,capture);
                Debug.Log("[CombatSnapshotValidation] PASS: sticker trigger, round ownership, real PNG persistence, canceled stale capture, defeat gating, settlement image and memory cleanup.");
            }
            finally
            {
                Object.DestroyImmediate(root);
                if (Directory.Exists(directory)) Directory.Delete(directory,true);
            }
        }

        static void ValidateSelection(GameObject root,PersonCombatController combat,CombatSnapshotCapture capture)
        {
            var camera = root.AddComponent<Camera>(); camera.enabled = false;
            var tracking = root.AddComponent<PersonTrackingManager>();
            tracking.UpdateDetections(new[] { PersonSystemValidation.FullPose() },Time.realtimeSinceStartup);
            var thrower = root.AddComponent<Person3DThrowController>();
            thrower.Configure(camera,tracking,Resources.Load<BodyProjectileCatalog>("Throwables/Catalog"));
            combat.Configure(camera,tracking,null,thrower,null);
            int id = tracking.Persons[0].personId;
            combat.BeginSession(false,1); combat.SelectPerson(id);
            Check(combat.Rounds.Count==0,"Free mode never schedules a combat screenshot");
            combat.BeginSession(true,2);
            Check(combat.Rounds.Count==0,"Recognition alone does not start the screenshot timer");
            combat.SelectPerson(id);
            var first = combat.FindRound(id).Snapshot;
            Check(first!=null && first.State==CombatSnapshotState.Waiting && first.PersonId==id && first.Session==2,
                "Actual person selection arms its round without starting capture");
            combat.SelectPerson(id); combat.CaptureRound(id);
            Check(ReferenceEquals(combat.FindRound(id).Snapshot,first),"Repeated interaction never restarts the timer");
            var selectedRound = combat.FindRound(id);
            typeof(CombatRound).GetProperty("CurrentHealth").SetValue(selectedRound,0f);
            Check(combat.CaptureDefeatedPersonSticker(id,2) && selectedRound.Snapshot.State==CombatSnapshotState.Capturing,
                "Selected person's defeat and sticker display trigger the capture");
            Check(!combat.CaptureDefeatedPersonSticker(id,2),"Sticker display captures only once per round");
            combat.RestartRound(id);
            var replacement = combat.FindRound(id).Snapshot;
            Check(first.State==CombatSnapshotState.Cancelled && replacement.RoundToken!=first.RoundToken && replacement.State==CombatSnapshotState.Waiting,
                "Restart cancels the old screenshot and arms a new one for its sticker");
            tracking.ClearPeople(); Invoke(combat,"SyncPeople");
            Check(replacement.State==CombatSnapshotState.Cancelled,"Lost person cancels a pending screenshot");
            tracking.UpdateDetections(new[] { PersonSystemValidation.FullPose() },Time.realtimeSinceStartup);
            combat.ResetRounds(); combat.SelectPerson(tracking.Persons[0].personId);
            var stopping = combat.FindRound(tracking.Persons[0].personId).Snapshot;
            combat.ResetRounds();
            Check(stopping.State==CombatSnapshotState.Cancelled && combat.Rounds.Count==0,
                "Stopping recognition cancels the selected round capture");
            var cartoons = root.AddComponent<Cartoon2DCanvas>(); cartoons.Configure(camera,tracking);
            int eventCount = 0;
            cartoons.PersonStickerDisplayed += person =>
            {
                eventCount++;
                combat.CaptureDefeatedPersonSticker(person,combat.Session);
            };
            combat.ResetRounds(); combat.BeginSession(true,3);
            id = tracking.Persons[0].personId; combat.SelectPerson(id);
            var actualRound = combat.FindRound(id);
            typeof(CombatRound).GetProperty("CurrentHealth").SetValue(actualRound,0f);
            cartoons.ActivatePerson(id); cartoons.Refresh();
            Check(eventCount==1 && actualRound.Snapshot.State==CombatSnapshotState.Capturing,
                "The actual visible sticker triggers capture after its pose is refreshed");
            cartoons.Refresh();
            Check(eventCount==1,"Subsequent pose refreshes do not request another screenshot");
            Debug.Log("[CombatSnapshotValidation] actual sticker trigger, restart, lost person, reset and free-mode isolation PASS");
        }

        static object Get(object target,string field) => target.GetType().GetField(field,Private).GetValue(target);
        static void SetProperty(object target,string property,object value) => target.GetType().GetProperty(property).SetValue(target,value);
        static void Set(object target,string field,object value) => target.GetType().GetField(field,Private).SetValue(target,value);
        static object Invoke(object target,string method,params object[] args) =>
            target.GetType().GetMethod(method,Private).Invoke(target,args);
        static void Check(bool value,string message)
        { if(!value) throw new InvalidOperationException("[CombatSnapshotValidation] FAIL: "+message); }
    }
}
