using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using EarthOL.ARCore;
using EarthOL.BodyThrow;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using Debug = UnityEngine.Debug;

namespace EarthOL.EditorTools
{
    /// <summary>Behavioral regressions for the tracking/frozen-model lifecycle. Older validation entry points remain in the project.</summary>
    public static class PersonSystemValidation
    {
        const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        public static void Validate()
        {
            Pose2DTrackingValidation();
            PersonFacingValidation();
            PersonSnapshotValidation();
            Frozen3DPersonValidation();
            Cartoon2DRigValidation();
            BootstrapMigrationValidation();
            PersonDetectionPerformanceValidation();
            ValidateModelScheduling();
            Debug.Log("[PersonValidation] ALL PASS: pose tracking, flow, facing hysteresis, snapshot isolation, fixed capsules, click dual event, Canvas mapping/pool, bootstrap and Stop/Start lifecycle.");
        }

        public static void ValidateAndBuild()
        {
            Validate();
            UnityEditor.PlayerSettings.enableFrameTimingStats = true;
            AndroidBuild.BuildDevelopmentApk();
        }

        public static BodyPoseResult FullPose(float x = 0.5f, float shift = 0f)
        {
            var points = new[]
            {
                new Vector2(x, .86f), new Vector2(x-.025f,.88f), new Vector2(x+.025f,.88f),
                new Vector2(x-.055f,.86f), new Vector2(x+.055f,.86f),
                new Vector2(x-.10f,.72f), new Vector2(x+.10f,.72f),
                new Vector2(x-.14f,.57f), new Vector2(x+.14f,.57f),
                new Vector2(x-.17f,.43f), new Vector2(x+.17f,.43f),
                new Vector2(x-.07f,.45f), new Vector2(x+.07f,.45f),
                new Vector2(x-.075f,.27f), new Vector2(x+.075f,.27f),
                new Vector2(x-.08f,.08f), new Vector2(x+.08f,.08f)
            };
            var scores = new float[17];
            for (int i = 0; i < 17; i++) { scores[i] = .95f; points[i].x += shift; }
            return new BodyPoseResult { screenRect = new Rect(x-.21f+shift,.04f,.42f,.90f), keypoints = points, keypointScores = scores, confidence = .95f };
        }

        public static void Pose2DTrackingValidation()
        {
            var go = new GameObject("TrackingRegression");
            try
            {
                var tracker = go.AddComponent<PersonTrackingManager>();
                tracker.UpdateDetections(new[] { FullPose(.27f), FullPose(.73f) }, 10f);
                Check(tracker.Count == 2 && tracker.Persons[0].personId != tracker.Persons[1].personId, "Two independent IDs");
                int left = tracker.Persons[0].personId, right = tracker.Persons[1].personId;
                tracker.UpdateDetections(new[] { FullPose(.73f, -.02f), FullPose(.27f,.02f) }, 10.2f);
                Check(tracker.Find(left).screenRect.center.x < .4f && tracker.Find(right).screenRect.center.x > .6f,
                    "Detection ordering must not swap person IDs");
                float before = tracker.Find(left).keypoints[9].x;
                tracker.Tick(10.25f); tracker.Tick(10.3f);
                Check(tracker.Find(left).keypoints[9].x > before, "Keypoints must advance between detections");
                tracker.UpdateDetections(new[] { FullPose(.73f,-.03f) }, 10.4f);
                Check(tracker.Find(left) != null, "Briefly missing person must retain ID");
                tracker.UpdateDetections(new[] { FullPose(.27f,.04f), FullPose(.73f,-.04f) }, 10.6f);
                Check(tracker.Count == 2 && tracker.Find(left) != null, "Returning person must recover ID");
                tracker.Tick(12f);
                Check(tracker.Count == 0, "Stale tracks must expire");
                const int size = 64;
                var previous = new byte[size*size]; var current = new byte[size*size];
                var random = new System.Random(1234);
                for (int i=0;i<previous.Length;i++) previous[i]=(byte)random.Next(256);
                for(int y=0;y<size;y++) for(int x=0;x<size;x++)
                    current[y*size+x]=previous[Mathf.Clamp(y-1,0,size-1)*size+Mathf.Clamp(x-3,0,size-1)];
                Check(PersonKeypointFlow.TryTrack(previous,current,size,size,new Vector2(30f/63f,30f/63f),out var move,out float quality),
                    "Image patch tracking must recover textured motion");
                Check(Mathf.Abs(move.x-3f/63f)<.001f && Mathf.Abs(move.y-1f/63f)<.001f && quality>.8f,"Flow displacement in viewport units");
                Debug.Log("[PersonValidation] Pose2DTrackingValidation PASS");
            }
            finally { Object.DestroyImmediate(go); }
        }

        public static void PersonFacingValidation()
        {
            var front=PersonPoseData.FromDetection(FullPose(),1f);
            Check(PersonFacingDetector.Estimate(front)==PersonFacing.Front,"Frontal face classification");
            var left=PersonPoseData.FromDetection(FullPose(),1f); left.keypoints[0].x-=.08f;
            Check(PersonFacingDetector.Estimate(left)==PersonFacing.Left,"Side face classification");
            var go=new GameObject("FacingRegression");
            try
            {
                var tracker=go.AddComponent<PersonTrackingManager>(); tracker.UpdateDetections(new[]{FullPose()},0f);
                var side=FullPose(); side.keypoints[0].x-=.12f;
                tracker.UpdateDetections(new[]{side},.2f);
                Check(tracker.Persons[0].facing==PersonFacing.Front,"One head turn must not flip the rig");
                for(int i=2;i<7;i++) tracker.UpdateDetections(new[]{side},i*.2f);
                Check(tracker.Persons[0].facing==PersonFacing.Left,"Sustained side view must change facing");
                Debug.Log("[PersonValidation] PersonFacingValidation PASS");
            }
            finally { Object.DestroyImmediate(go); }
        }

        public static void PersonSnapshotValidation()
        {
            var live=PersonPoseData.FromDetection(FullPose(),4f); live.personId=22;
            var snapshot=new FrozenPersonData(live,new Vector2(.5f,.5f),2.5f,.8f);
            Vector2 original=snapshot.Keypoints[0]; live.keypoints[0]=Vector2.zero;
            Check(snapshot.Keypoints[0]==original,"Snapshot must deep-copy live keypoints");
            var exposed=snapshot.Keypoints; exposed[0]=Vector2.one;
            Check(snapshot.Keypoints[0]==original,"Snapshot keypoint getters must not expose mutable storage");
            live.screenRect=new Rect(0,0,1,1);
            Check(snapshot.ViewportBounds.height==.9f && snapshot.PersonId==22,"Bounding box and identity are frozen values");
            Debug.Log("[PersonValidation] PersonSnapshotValidation PASS");
        }

        public static void Frozen3DPersonValidation()
        {
            var root=new GameObject("FrozenRegression");
            var frozenObjects=new List<GameObject>();
            try
            {
                var camera=root.AddComponent<Camera>(); camera.enabled=false;
                camera.pixelRect=new Rect(0,0,1000,1000); camera.fieldOfView=60f;
                camera.transform.SetPositionAndRotation(new Vector3(2,1,-3),Quaternion.Euler(0,35,0));
                var estimator=root.AddComponent<PersonDistanceEstimator>(); estimator.Configure(camera);
                var live=PersonPoseData.FromDetection(FullPose(),1f); live.personId=1;
                float distance=estimator.Estimate(live,out float confidence);
                float expected=1.7f*Mathf.Abs(camera.projectionMatrix.m11)*.5f/.9f;
                Check(Mathf.Abs(distance-expected)<.01f && confidence>0f,"Pinhole distance from projection matrix");
                Check(PersonWorldPositionCalculator.TryCalculate(camera,new Vector2(.65f,.5f),distance,out var world),"Off-center world pose");
                Check(Vector3.Distance(world.position,camera.transform.position+Vector3.forward*distance)>.5f,"Position must respect camera rotation and off-center ray");
                var snapshot=new FrozenPersonData(live,new Vector2(.5f,.5f),distance,confidence);
                var builder=root.AddComponent<Frozen3DPersonBuilder>();
                var frozen=builder.Build(snapshot,camera,null); frozenObjects.Add(frozen.gameObject);
                Check(frozen!=null && frozen.GetComponentsInChildren<CapsuleCollider>().Length==10,"Full-pose snapshot must build ten capsule primitives");
                Check(frozen.Anchor!=null && frozen.GetComponent<BodyProxyRig>()==null && frozen.GetComponent<BodyPoseTracker>()==null,
                    "Frozen model has its own ARAnchor, no realtime rig");
                Vector3 position=frozen.transform.position;
                var parts=frozen.GetComponentsInChildren<Transform>(); var locals=new Vector3[parts.Length];
                for(int i=0;i<parts.Length;i++) locals[i]=parts[i].localPosition;
                live.keypoints[0]=Vector2.one; camera.transform.position+=Vector3.right*3f;
                Check(frozen.transform.position==position,"Camera/person motion must not move a frozen root");
                for(int i=1;i<parts.Length;i++) Check(parts[i].localPosition==locals[i],"Frozen local capsule transforms must remain unchanged");
                Check(frozen.Snapshot.Keypoints[0]!=live.keypoints[0],"Frozen capsule data independent from live person");
                Debug.Log("[PersonValidation] Frozen3DPersonValidation PASS (native ARCore registration requires device)");
            }
            finally { foreach(var frozen in frozenObjects) if(frozen!=null)Object.DestroyImmediate(frozen); Object.DestroyImmediate(root); }
        }

        public static void Cartoon2DRigValidation()
        {
            var root=new GameObject("CartoonRegression");
            var frozenObjects=new List<GameObject>();
            try
            {
                var camera=root.AddComponent<Camera>(); camera.enabled=false; camera.pixelRect=new Rect(0,0,1000,1000);
                var tracking=root.AddComponent<PersonTrackingManager>(); tracking.UpdateDetections(new[]{FullPose()},Time.realtimeSinceStartup);
                var cartoons=root.AddComponent<Cartoon2DCanvas>(); cartoons.Configure(camera,tracking); Canvas.ForceUpdateCanvases();
                cartoons.Refresh(); Check(cartoons.RigCount==0,"Click activates the selected cartoon");
                var distance=root.AddComponent<PersonDistanceEstimator>(); distance.Configure(camera);
                var builder=root.AddComponent<Frozen3DPersonBuilder>();
                var click=root.AddComponent<PersonClickController>(); click.Configure(camera,tracking,distance,builder,null,cartoons);
                int inferBefore=root.GetComponents<PoseDetectionManager>().Length;
                var frozen=click.TryFreezeAt(camera.ViewportToScreenPoint(new Vector3(.5f,.5f,0)));
                Check(frozen!=null && cartoons.RigCount==1 && cartoons.IsPersonActivated(tracking.Persons[0].personId),
                    "ONE click must activate 2D tracking and create fixed 3D simultaneously");
                frozenObjects.Add(frozen.gameObject);
                Check(click.TryFreezeAt(camera.ViewportToScreenPoint(new Vector3(.5f,.5f,0)))==null,"Rapid repeated click must be debounced");
                var canvas=Object.FindObjectsOfType<Canvas>(); Canvas overlay=null;
                foreach(var item in canvas)if(item.name=="Cartoon2DCanvas")overlay=item;
                Check(overlay!=null && overlay.renderMode==RenderMode.ScreenSpaceOverlay,"Canvas must be Screen Space Overlay");
                var rig=overlay.GetComponentInChildren<Cartoon2DRig>();
                Check(rig.GetComponentsInChildren<Image>().Length==10,"Ten cartoon image body parts");
                var before=rig.transform.position; var frozenPosition=frozen.transform.position;
                tracking.UpdateDetections(new[]{FullPose(.5f,.04f)},Time.realtimeSinceStartup+.2f); cartoons.Refresh();
                Check(rig.transform.position.x>before.x && frozen.transform.position==frozenPosition,
                    "Live cartoon must follow while frozen 3D remains at the click pose");
                Check(root.GetComponents<PoseDetectionManager>().Length==inferBefore,"Click and capsule creation must not instantiate inference managers");
                tracking.ClearPeople(); cartoons.Refresh(); Check(cartoons.RigCount==0,"Lost/stopped tracks must hide cartoons");
                Debug.Log("[PersonValidation] Cartoon2DRigValidation/click dual event PASS");
            }
            finally { foreach(var frozen in frozenObjects)if(frozen!=null)Object.DestroyImmediate(frozen); Object.DestroyImmediate(root); }
        }

        public static void BootstrapMigrationValidation()
        {
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            try
            {
                var bootstrap=new GameObject("BootstrapMigrationTest").AddComponent<ARCoreMainSceneBootstrap>();
                var start=(IEnumerator)Invoke(bootstrap,"Start"); start.MoveNext();
                Check(bootstrap.DetectionManager!=null && bootstrap.PersonTracking!=null && bootstrap.CartoonCanvas!=null &&
                    bootstrap.AnchorManager!=null && bootstrap.ClickController!=null,"Bootstrap has independent detection, tracking, Canvas, anchor and click modules");
                Check(!bootstrap.PoseTracker.gameObject.activeInHierarchy && !bootstrap.Spawner.gameObject.activeInHierarchy,
                    "Legacy inference/throw loop must be isolated and inactive");
                Check(GameObject.Find("Soft Cube")==null && GameObject.Find("Soft Sphere")==null,"No demo geometry");
                bootstrap.DetectionManager.StopDetection();
                bootstrap.SetMode(GameMode.Throw);
                Check(!bootstrap.DetectionManager.DetectionEnabled,"Mode switching must not override user Stop Detection");
                Debug.Log("[PersonValidation] BootstrapMigrationValidation PASS");
            }
            finally { foreach(var go in scene.GetRootGameObjects())if(go!=null)Object.DestroyImmediate(go); }
        }

        public static void PersonDetectionPerformanceValidation()
        {
            var root=new GameObject("PerformanceRegression");
            try
            {
                var tracking=root.AddComponent<PersonTrackingManager>(); var manager=root.AddComponent<PoseDetectionManager>();
                var camera=root.AddComponent<Camera>(); camera.enabled=false; manager.Configure(camera,tracking);
                manager.DetectionFramesPerSecond=10f;
                Check(Mathf.Abs(manager.DetectionFramesPerSecond-10)<.001f,"Inference Hz configurable");
                manager.StartDetection(); Check(manager.DetectionEnabled,"Start Detection enables algorithms");
                Set(manager,"m_NextDetection",12.2f);
                Check(!manager.ShouldStartInference(12.1f) && manager.ShouldStartInference(12.2f),"Inference cap enforced independent of render frames");
                tracking.UpdateDetections(new[]{FullPose()},Time.realtimeSinceStartup);
                int originalId=tracking.Persons[0].personId;
                manager.StopDetection();
                int detection=manager.DetectionCount, capture=manager.CaptureCount,tensor=manager.TensorInputCount,ticks=tracking.TrackingUpdateCount,flow=tracking.FlowUpdateCount;
                for(int i=0;i<30;i++)Invoke(manager,"Update");
                Check(manager.DetectionCount==detection && manager.CaptureCount==capture && manager.TensorInputCount==tensor &&
                    tracking.TrackingUpdateCount==ticks && tracking.FlowUpdateCount==flow,"Stop must halt capture, tensors, inference, decode, tracking and flow");
                manager.StartDetection(); Check(manager.DetectionEnabled,"Restart resumes detection");
                tracking.UpdateDetections(new[]{FullPose()},Time.realtimeSinceStartup);
                Check(tracking.Find(originalId)!=null,"Restart should restore selected IDs when periodic detection matches the previous position");
                manager.StopDetection();
                // Real execution timing of the lightweight update path, not an inferred speed claim.
                tracking.UpdateDetections(new[]{FullPose(.3f),FullPose(.7f)},10f);
                long memory=GC.GetAllocatedBytesForCurrentThread(); var stopwatch=Stopwatch.StartNew();
                for(int i=0;i<1000;i++)tracking.Tick(10f+i*.0005f);
                stopwatch.Stop(); long allocated=GC.GetAllocatedBytesForCurrentThread()-memory;
                Debug.Log($"[PersonValidation] Tracking benchmark actual desktop elapsed={stopwatch.Elapsed.TotalMilliseconds:F3}ms/1000 updates allocated={allocated}B (includes Stopwatch setup).");
                Debug.Log("[PersonValidation] PersonDetectionPerformanceValidation PASS; target CPU/GPU profiling requires development APK.");
            }
            finally { Object.DestroyImmediate(root); }
        }

        static void ValidateModelScheduling()
        {
            // Reuse the existing decoder/model regressions because its scheduling and input-buffer path changed.
            BodyPoseValidation.Validate();
            using (var detector=new BodyPoseDetector())
            {
                var load=detector.Load(); while(load.MoveNext()) { }
                Check(detector.Ready,"Scheduled detector must load the existing raw model");
                bool callback=false; int steps=0;
                var pixels=new Color32[192*384];
                var schedule=detector.DetectScheduled(pixels,192,384,6,results=>
                {
                    Check(results.Length==0,"Blank portrait input must not become a tracked person"); callback=true;
                });
                var timer=Stopwatch.StartNew();
                while(schedule.MoveNext())steps++;
                timer.Stop();
                Check(callback && steps>=1,"Inference must execute via a yielded schedule, not a hidden per-frame full Execute");
                Debug.Log($"[PersonValidation] Existing decoder tests and actual scheduled raw-model execution PASS: yields={steps} desktopWall={timer.Elapsed.TotalMilliseconds:F1}ms.");
            }
        }

        static object Invoke(object target,string method,params object[] args)=>target.GetType().GetMethod(method,Private).Invoke(target,args);
        static void Set(object target,string field,object value)=>target.GetType().GetField(field,Private).SetValue(target,value);
        static void Check(bool passed,string message){if(!passed)throw new InvalidOperationException("[PersonValidation] FAIL: "+message);}
    }
}
