using System;
using System.Collections;
using System.Reflection;
using EarthOL.ARCore;
using EarthOL.BodyThrow;
using EarthOL.Combat;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EarthOL.EditorTools
{
    /// <summary>Exercises the imported wang input flow in a preview scene, preserving the user's open scene.</summary>
    public static class WangCombatReuseValidation
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [MenuItem("Tools/Earth OL/Validate wang combat integration")]
        public static void Validate()
        {
            var preview = new PreviewRenderUtility();
            var root = new GameObject("Wang combat reuse regression");
            preview.AddSingleGO(root);
            var previousOverlay = ARCoreModeOverlay.Instance;
            try
            {
                var camera = preview.camera;
                camera.pixelRect = new Rect(0, 0, Screen.width, Screen.height);
                var tracking = root.AddComponent<PersonTrackingManager>();
                var distance = root.AddComponent<PersonDistanceEstimator>(); distance.Configure(camera);
                var detector = root.AddComponent<PoseDetectionManager>(); detector.AutoStart = false; detector.Configure(camera, tracking);
                Set(detector, "m_Enabled", true);
                var surface = root.AddComponent<PersonDeformationSurface>(); surface.Configure(camera, tracking, distance, detector);
                var swipe = root.AddComponent<SwipeDeformController>(); swipe.Configure(surface);
                var cartoons = root.AddComponent<Cartoon2DCanvas>(); cartoons.Configure(camera, tracking);
                preview.AddSingleGO(((Canvas)Get(cartoons, "m_Canvas")).gameObject);
                var thrower = root.AddComponent<Person3DThrowController>();
                var catalog = Resources.Load<BodyProjectileCatalog>("Throwables/Catalog"); thrower.Configure(camera, tracking, catalog);
                var combat = root.AddComponent<PersonCombatController>(); combat.Configure(camera, tracking, surface, thrower, swipe);
                var showcase = root.AddComponent<PersonShowcaseController>(); showcase.Configure(camera, tracking, detector, surface);
                var click = root.AddComponent<PersonClickController>(); click.Configure(camera, tracking, cartoons);
                var bootstrap = root.AddComponent<ARCoreMainSceneBootstrap>();
                Set(bootstrap, "startPersonDetection", false);
                Set(bootstrap, "<ARCamera>k__BackingField", camera);
                Set(bootstrap, "<PersonTracking>k__BackingField", tracking);
                Set(bootstrap, "<DistanceEstimator>k__BackingField", distance);
                Set(bootstrap, "<DetectionManager>k__BackingField", detector);
                Set(bootstrap, "<PersonDeformer>k__BackingField", surface);
                Set(bootstrap, "<CartoonCanvas>k__BackingField", cartoons);
                Set(bootstrap, "<ThrowController>k__BackingField", thrower);
                Set(bootstrap, "<CombatController>k__BackingField", combat);
                Set(bootstrap, "<ShowcaseController>k__BackingField", showcase);
                Set(bootstrap, "<ClickController>k__BackingField", click);
                Set(bootstrap, "<SelectedAppMode>k__BackingField", AppMode.Combat);
                var wheel = root.AddComponent<FreeModeWheel>(); wheel.Configure(bootstrap);
                Set(bootstrap, "<FreeWheel>k__BackingField", wheel);
                var overlay = root.AddComponent<ARCoreModeOverlay>(); overlay.Configure(bootstrap); Set(bootstrap, "m_Overlay", overlay);
                Invoke(bootstrap, "BeginSession");
                Seed();
                int target = tracking.Pick(new Vector2(.5f, .5f)).personId;
                int other = tracking.Pick(new Vector2(.88f, .5f)).personId;
                var damage = (AttackDamageConfig)Get(combat, "m_Damage");
                foreach (var entry in damage.AttackTypes) entry.criticalChance = 0;
                Vector2 point = ScreenPoint(.5f, .5f), otherPoint = ScreenPoint(.88f, .5f);
                Vector2 outside = ScreenPoint(.05f, .5f), farSide = ScreenPoint(.98f, .5f);
                Check(combat.IsSelecting && !cartoons.DrawPersonMarkers && !surface.ReadPointerInput,
                    "Combat begins with wang target-selection markers instead of always-on cartoon boxes");
                Check(combat.CaptureRound(target) == 0 && combat.Sweep(outside, farSide, false) == 0, "No attacks before selection");
                wheel.SetExpanded(true);
                Tap(point);
                Check(combat.SelectedPersonId == target && !combat.IsSelecting && combat.FindRound(target).History.Count == 0 &&
                    thrower.ActiveProjectileCount == 0, "First person tap selects even with wheel expanded, without throwing or damage");
                Check(!cartoons.DrawPersonMarkers && combat.FindRound(other) == null && combat.CaptureRound(other) == 0,
                    "After selection all boxes disappear and other people cannot receive a damage round");
                wheel.SetExpanded(false);
                Tap(point);
                var round = combat.FindRound(target);
                Check(round.History.Count == 1 && round.MaxHealth - round.CurrentHealth == 10 && round.History[0].Definition.AttackId == "press",
                    "Barehand click applies one 10-point wang press attack after selection");
                int records = round.History.Count;
                Tap(otherPoint);
                Check(round.History.Count == records && combat.FindRound(other) == null, "Tapping another person cannot change target or damage it");
                Dispatch(point, true, true, false); Dispatch(point, false, false, true, true);
                Check(round.History.Count == records, "Canceled barehand tap cannot damage");
                Dispatch(outside, true, true, false); Dispatch(farSide, false, true, false); Dispatch(outside, false, true, false); Dispatch(outside, false, false, true);
                Check(round.History.Count == records + 1 && round.History[records].Definition.AttackId == "swipe", "Barehand path hits selected person once per gesture");
                Check(surface.Warp.HasActivePokes, "Imported slap triggers its independent local impulse");
                Invoke(swipe, "LateUpdate");
                Check(surface.Warp.HasActiveSwipeShear, "Imported swipe controller drives directional shear");
                Invoke(surface, "UpdateBodyMask"); surface.Warp.Advance(.016f);
                var shaderBlock = new MaterialPropertyBlock(); surface.Warp.GetComponent<Renderer>().GetPropertyBlock(shaderBlock);
                Check(shaderBlock.GetFloat("_BodyPartCount") == 10 && Mathf.Abs(shaderBlock.GetFloat("_SwipeAmp")) > 0,
                    "Imported body mask and swipe amplitude reach the renderer");
                Check(shaderBlock.GetFloat("_BodyFeather") > 0, "Adaptive swipe feather reaches the renderer");
                Check(!ShaderUtil.ShaderHasError(surface.Warp.warpMaterial.shader), "Person warp shader compiles with imported shear and body mask");
                surface.ResetEffects(); surface.Warp.GetComponent<Renderer>().GetPropertyBlock(shaderBlock);
                Check(!surface.Warp.HasActiveSwipeShear && shaderBlock.GetFloat("_SwipeAmp") == 0 &&
                    (Quaternion)Get(surface, "m_SwipeTilt") == Quaternion.identity,
                    "Reset removes active directional push and tilt immediately");
                combat.RestartRound(target); round = combat.FindRound(target);
                wheel.SetExpanded(true);
                int sticky = Array.FindIndex(catalog.entries, entry => entry.key == "StickyHand");
                thrower.SelectProjectile(sticky);
                Check(combat.CanHoldSelectedItem && thrower.SelectedEntry.canHold, "Original sticky-hand resource is marked draggable");
                Vector2 center = GuiToScreen(wheel.Layout.SlotCenter(0));
                Dispatch(center, true, true, false);
                Dispatch(center + Vector2.up * Screen.width * .06f, false, true, false);
                Check(combat.Holding && !(bool)Get(wheel, "m_Pressed") && !surface.ReadPointerInput && thrower.ActiveProjectileCount == 0,
                    "Pulling the center sticky hand transfers the same pointer to wang held attacks without launching");
                Dispatch(outside, false, true, false); Dispatch(point, false, true, false);
                Check((Vector2)Get(combat, "m_HoldPoint") == point, "Held item follows the pointer position");
                Check(round.History.Count == 1 && round.History[0].Definition.AttackId == "swipe", "Dragged item damages on entering the selected person");
                for (int i = 0; i < 8; i++) Dispatch(point, false, true, false);
                Check(round.History.Count == 1, "Holding still inside the target never repeats damage");
                Dispatch(outside, false, true, false);
                var hits = (System.Collections.Generic.Dictionary<int, float>)Get(combat, "m_HoldHits"); hits[target] = Time.unscaledTime - 1;
                Dispatch(point, false, true, false);
                Check(round.History.Count == 2, "A fresh outside-to-inside swing after cooldown attacks again");
                Dispatch(point, false, false, true);
                Check(!combat.Holding && round.History.Count == 2, "Releasing returns held item without duplicate hit");
                Dispatch(center, true, true, false); Dispatch(center + Vector2.up * Screen.width * .06f, false, true, false);
                Dispatch(outside, false, true, false); Dispatch(outside, false, false, true, true);
                Check(!combat.Holding, "Canceled held gesture also releases held state");
                combat.RestartRound(target); round = combat.FindRound(target);
                wheel.SetExpanded(true);
                int nonHeld = Array.FindIndex(catalog.entries, entry => entry.key == "Brain"); thrower.SelectProjectile(nonHeld);
                Tap(point);
                Check(thrower.ActiveProjectileCount == 1 && round.History.Count == 0, "Ordinary tap-to-throw remains available and launch is not a hit");
                Invoke(thrower, "UpdateThrownItems", .5f); Invoke(thrower, "UpdateThrownItems", .5f);
                Check(round.History.Count == 1 && round.History[0].Definition.AttackId == "Brain", "Projectile counts once on actual attachment");
                int token = round.Token; combat.RestartRound(target);
                Check(!combat.Confirm(new PersonInteractionHit(target, "press", Vector2.one * .5f, PersonInteractionHit.NextId(), token)),
                    "Restart continues to reject stale round callbacks");
                int attackCount = combat.FindRound(target).History.Count;
                Set(combat, "m_Settings", true); wheel.SetExpanded(false); Tap(point);
                Check(combat.FindRound(target).History.Count == attackCount, "Settings block barehand input"); Set(combat, "m_Settings", false);
                Invoke(bootstrap, "ResetFeatureEffects"); tracking.ClearPeople();
                Invoke(combat, "SyncPeople");
                Check(combat.IsSelecting && combat.Rounds.Count == 0 && !combat.Holding && thrower.ActiveProjectileCount == 0,
                    "Recognition reset clears selected target, held item, rounds and thrown objects");
                Set(bootstrap, "<SelectedAppMode>k__BackingField", AppMode.Free); Invoke(bootstrap, "BeginSession"); Seed();
                Check(cartoons.DrawPersonMarkers && !combat.Active && surface.ReadPointerInput, "Free mode retains normal markers and deformation");
                overlay.ToggleStickerSkinPicker();
                Check(overlay.SelectStickerSkin("alien") && cartoons.SelectedSkinId == "alien", "Current alien skin sidebar remains functional");
                Debug.Log("[WangCombatReuseValidation] ALL PASS: selection/marker hiding, barehand press and swipe damage, center-item drag handoff, stationary/cooldown/release rules, selected-person isolation, real impact/stale callbacks and free skin compatibility.");

                void Seed() => tracking.UpdateDetections(new[] { PersonSystemValidation.FullPose(.5f), PersonSystemValidation.FullPose(.88f) }, 10f);
                Vector2 ScreenPoint(float x, float y) => camera.ViewportToScreenPoint(new Vector3(x, y, 0));
                void Dispatch(Vector2 position, bool down, bool held, bool released, bool canceled = false)
                {
                    Set(wheel, "m_ConsumedFrame", -1);
                    var frame = new BodyPointerFrame { id = 7, position = position, down = down, held = held, released = released, canceled = canceled };
                    if (combat.Holding) wheel.ResetInteraction(); else wheel.ProcessPointer(frame);
                    combat.ProcessPointer(frame);
                }
                void Tap(Vector2 position) { Dispatch(position, true, true, false); Dispatch(position, false, false, true); }
            }
            finally
            {
                preview.Cleanup();
                typeof(ARCoreModeOverlay).GetProperty("Instance").SetValue(null, previousOverlay);
            }
        }

        static Vector2 GuiToScreen(Vector2 point) => new Vector2(point.x, Screen.height - point.y);
        static object Get(object target, string field) => target.GetType().GetField(field, Private).GetValue(target);
        static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
        static object Invoke(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);
        static void Check(bool value, string message)
        { if (!value) throw new InvalidOperationException("[WangCombatReuseValidation] FAIL: " + message); }
    }
}
