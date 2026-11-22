using System;
using System.Collections;
using System.Reflection;
using EarthOL.ARCore;
using EarthOL.BodyThrow;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EarthOL.EditorTools
{
    public static class MultiPersonInteractionValidation
    {
        const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        public static void Validate()
        {
            var root = new GameObject("Automatic multi-person interaction regression");
            try
            {
                var camera = root.AddComponent<Camera>(); camera.enabled = false;
                camera.pixelRect = new Rect(0,0,1000,1000);
                var tracking = root.AddComponent<PersonTrackingManager>();
                float now = Time.realtimeSinceStartup;
                tracking.UpdateDetections(new[] { PersonSystemValidation.FullPose(.27f), PersonSystemValidation.FullPose(.73f) },now);
                Check(tracking.Count == 2,"Detection automatically starts two tracks without a selection gesture");
                Vector2 leftTap = camera.ViewportToScreenPoint(new Vector3(.27f,.5f,0));
                Vector2 rightTap = camera.ViewportToScreenPoint(new Vector3(.73f,.5f,0));
                int left = tracking.Pick(new Vector2(.27f,.5f)).personId;
                int right = tracking.Pick(new Vector2(.73f,.5f)).personId;

                var cartoons = root.AddComponent<Cartoon2DCanvas>();cartoons.Configure(camera,tracking);
                var click = root.AddComponent<PersonClickController>();click.Configure(camera,tracking,cartoons);
                Check(click.TryActivateAt(leftTap) && click.TryActivateAt(rightTap) && cartoons.RigCount == 2,
                    "One tap per person activates two independent stickers without clearing the first");

                var thrower = root.AddComponent<Person3DThrowController>();
                thrower.Configure(camera,tracking,Resources.Load<BodyProjectileCatalog>("Throwables/Catalog"));
                thrower.SetInteractionEnabled(true);
                Check(thrower.TryThrowAt(leftTap) && thrower.TryThrowAt(rightTap),"Both people accept 3D projectiles directly");
                typeof(Person3DThrowController).GetMethod("UpdateThrownItems",Private).Invoke(thrower,new object[]{.5f});
                Check(thrower.AttachedProjectileCount == 2 && thrower.SlimeSurfaceCount == 2,
                    "Each person keeps its own attached model and merged slime surface");
                var items = (IList)typeof(Person3DThrowController).GetField("m_Items",Private).GetValue(thrower);
                object leftItem = items[0], rightItem = items[1];
                var leftModel = (GameObject)leftItem.GetType().GetField("gameObject").GetValue(leftItem);
                var rightModel = (GameObject)rightItem.GetType().GetField("gameObject").GetValue(rightItem);
                float oldLeft = leftModel.transform.localPosition.x;
                float oldRight = rightModel.transform.localPosition.x;
                tracking.UpdateDetections(new[] { PersonSystemValidation.FullPose(.73f), PersonSystemValidation.FullPose(.27f,.04f) },now+.2f);
                typeof(Person3DThrowController).GetMethod("UpdateThrownItems",Private).Invoke(thrower,new object[]{.05f});
                Check(leftModel.transform.localPosition.x > oldLeft && Mathf.Abs(rightModel.transform.localPosition.x-oldRight)<.0001f,
                    "Each attached effect follows its own ID after detection reordering");
                Check(tracking.Find(left)!=null && tracking.Find(right)!=null && cartoons.SelectedPersonCount==2,
                    "Interacting with another person does not replace or clear previous person effects");

                var distance=root.AddComponent<PersonDistanceEstimator>();distance.Configure(camera);
                var surface=root.AddComponent<PersonDeformationSurface>();surface.Configure(camera,tracking,distance,null);
                Check(surface.RefreshRegion(1f),"Deformation automatically obtains a tracked person region");
                Check(surface.TryBegin(1,leftTap),"Left person accepts a press directly");
                surface.ResetEffects();surface.RefreshRegion(1f);
                Check(surface.TryBegin(2,rightTap),"Right person also accepts a press without global reselection");
                surface.ResetEffects();

                var showcase=root.AddComponent<PersonShowcaseController>();showcase.Configure(camera,tracking,null,surface);
                var begin=typeof(PersonShowcaseController).GetMethod("BeginPress",Private);
                var personId=typeof(PersonShowcaseController).GetField("m_PersonId",Private);
                begin.Invoke(showcase,new object[]{new BodyPointerFrame{id=1,position=leftTap,down=true,held=true}});
                Check((int)personId.GetValue(showcase)==left,"Showcase hold can pick the left tracked person directly");
                begin.Invoke(showcase,new object[]{new BodyPointerFrame{id=2,position=rightTap,down=true,held=true}});
                Check((int)personId.GetValue(showcase)==right,"Showcase hold can pick the right tracked person directly");
                cartoons.ClearSelection();thrower.ClearEffects();tracking.ClearPeople();
                Check(tracking.Count==0&&cartoons.RigCount==0&&thrower.ActiveProjectileCount==0&&thrower.SlimeSurfaceCount==0,
                    "Stop reset removes all multi-person effects");
                tracking.UpdateDetections(new[]{PersonSystemValidation.FullPose()},now+.3f);
                Check(tracking.Count==1,"Restart detects people automatically without another double tap");
                Debug.Log("[MultiPersonValidation] PASS: automatic two-person tracks, direct sticker taps, independent model/slime attachments, per-person deformation/showcase holds and stop/restart.");
            }
            finally{Object.DestroyImmediate(root);}
        }

        static void Check(bool condition,string message)
        {if(!condition)throw new InvalidOperationException("[MultiPersonValidation] FAIL: "+message);}
    }
}
