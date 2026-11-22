using System;
using System.Collections;
using System.Reflection;
using EarthOL.ARCore;
using EarthOL.BodyThrow;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EarthOL.EditorTools
{
    public static class FreeWheelValidation
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        public static void Validate()
        {
            ValidateGeometry();
            CombatIntegrationValidation.Validate();
            ValidateInteraction(AppMode.Free);
            ValidateInteraction(AppMode.Combat);
            ValidateBattleWheel();
            Debug.Log("[FreeWheelValidation] ALL PASS: retractable shared wheel, directional handles, cyclic selection, tap-position original throwing, collapsed deformation only, no aim guide, actual impact damage and lifecycle cleanup.");
        }

        static void ValidateGeometry()
        {
            var reference = new FreeWheelLayout(1260, 2732);
            Check(Mathf.Abs(reference.PeakHeight - 720) < .01f && Mathf.Abs(reference.ArcHeight(0) - 517) < .01f &&
                Mathf.Abs(reference.ArcHeight(1260) - 517) < .01f, "Original expanded arc geometry is retained");
            foreach (var layout in new[] { reference, new FreeWheelLayout(1080, 2400), new FreeWheelLayout(1260, 2800), new FreeWheelLayout(800, 1280) })
            {
                Check(Mathf.Abs(layout.PeakHeight / layout.height - 720f / 2732) < .0001f &&
                    layout.SlotRadius(0) > layout.SlotRadius(1) && layout.SlotRadius(1) > layout.SlotRadius(2),
                    "Expanded arc and selection circles retain screen proportions");
                var collapsed = new FreeWheelLayout(layout.width, layout.height, 0);
                Check(Mathf.Abs(layout.height - collapsed.PanelRect.y - collapsed.PeekHeight) < .01f &&
                    collapsed.Contains(new Vector2(layout.width * .5f, layout.height - collapsed.PeekHeight * .5f)) &&
                    !collapsed.Contains(layout.SlotCenter(0)), "Collapsed wheel leaves only a narrow arc edge");
                Check(collapsed.HandleRect.yMin >= 0 && collapsed.HandleRect.yMax <= layout.height &&
                    collapsed.HandleRect.center.y < collapsed.PanelRect.y, "Up-arrow handle remains visible above the collapsed edge");
            }
            Check(FreeModeWheel.Wrap(-1, 5) == 4 && FreeModeWheel.Wrap(5, 5) == 0, "Selection wraps in both directions");
            var mask = FreeModeWheel.CreatePanelTexture(reference);
            var previous = RenderTexture.active;
            var target = RenderTexture.GetTemporary(mask.width, mask.height, 0, RenderTextureFormat.ARGB32);
            var readback = new Texture2D(mask.width, mask.height, TextureFormat.RGBA32, false);
            try
            {
                Graphics.Blit(mask, target); RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); readback.Apply();
                for (int slot = -2; slot <= 2; slot++)
                {
                    Vector2 center = reference.SlotCenter(slot);
                    Check(readback.GetPixelBilinear(center.x / reference.width, (reference.height - center.y) / reference.PeakHeight).a < .01f,
                        "All five selection apertures cut through panel alpha");
                }
                Check(readback.GetPixelBilinear(.5f, .1f).a > .98f, "Panel beneath apertures remains solid");
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); Object.DestroyImmediate(mask); Object.DestroyImmediate(readback); }
            Debug.Log("[FreeWheelValidation] expanded/collapsed geometry, visible arrow handle, circular apertures and wrap PASS");
        }

        static void ValidateInteraction(AppMode mode)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try
            {
                var bootstrap = new GameObject("Retractable wheel " + mode).AddComponent<ARCoreMainSceneBootstrap>();
                Set(bootstrap, "startPersonDetection", false);
                var startup = (IEnumerator)Invoke(bootstrap, "Start"); startup.MoveNext(); bootstrap.EnterApp(mode);
                while (startup.MoveNext()) { }
                var wheel = bootstrap.FreeWheel; var thrower = bootstrap.ThrowController;
                var surface = bootstrap.PersonDeformer; var tracking = bootstrap.PersonTracking;
                var camera = bootstrap.ARCamera; var combat = bootstrap.CombatController;
                camera.pixelRect = new Rect(0, 0, Screen.width, Screen.height);
                Set(bootstrap.DetectionManager, "m_Enabled", true);
                tracking.UpdateDetections(new[] { PersonSystemValidation.FullPose() }, Time.realtimeSinceStartup);
                Vector2 tap = camera.ViewportToScreenPoint(new Vector3(.55f, .58f, 0));
                int personId = tracking.Pick(camera.ScreenToViewportPoint(tap)).personId;
                if (mode == AppMode.Combat) combat.CaptureRound(personId);
                Check(!wheel.Expanded && wheel.ShowUpArrow && !wheel.ShowDownArrow && surface.ReadPointerInput,
                    "Sessions start collapsed with visible up arrow and deformation input");
                Tap(wheel, tap);
                combat.ProcessPointer(new BodyPointerFrame { id = 3, position = tap, down = true, held = true });
                combat.ProcessPointer(new BodyPointerFrame { id = 3, position = tap, released = true });
                Check(thrower.ActiveProjectileCount == 0 && (mode != AppMode.Combat || combat.FindRound(personId).History.Count == 0),
                    "Collapsed person taps never throw or report barehand damage");
                Set(wheel, "m_ConsumedFrame", -1);
                Check(surface.RefreshRegion(0f) && surface.TryBegin(3, tap) && surface.Warp.HasActivePokes,
                    "Collapsed wheel permits the existing person deformation");
                surface.EndPointer(); surface.ResetEffects();

                Vector2 handle = ScreenPoint(wheel.Layout.HandleRect.center);
                Frame(wheel, handle, true, true, false);
                Frame(wheel, handle + Vector2.up * wheel.Layout.Travel, false, true, false);
                Check(wheel.Reveal > .99f && wheel.ShowDownArrow, "Upward drag follows the finger and exposes down arrow");
                Frame(wheel, handle + Vector2.up * wheel.Layout.Travel, false, false, true);
                Check(wheel.Expanded && wheel.Reveal == 1 && wheel.TapToThrowEnabled && !wheel.ShowUpArrow,
                    "Releasing pulled-out wheel enables person-tap throwing");
                Vector2 center = ScreenPoint(wheel.Layout.SlotCenter(0));
                Frame(wheel, center, true, true, false);
                Check(wheel.ShowDownArrow && wheel.OwnsPointer, "Pressing expanded arc shows down arrow and captures gesture");
                Frame(wheel, center, false, false, true);
                Check(thrower.ActiveProjectileCount == 0 && !wheel.ShowDownArrow, "Releasing selection UI does not launch");
                int initial = wheel.SelectedIndex;
                Frame(wheel, center, true, true, false);
                Frame(wheel, center + Vector2.left * wheel.Layout.Step * 1.05f, false, true, false);
                Frame(wheel, center + Vector2.left * wheel.Layout.Step * 1.05f, false, false, true);
                Check(wheel.SelectedIndex == FreeModeWheel.Wrap(initial + 1, thrower.ProjectileCount) && wheel.Expanded &&
                    thrower.ActiveProjectileCount == 0, "Horizontal drag retains cyclic selection without collapsing or firing");
                Frame(wheel, tap, true, true, false);
                Check(thrower.ActiveProjectileCount == 0 && wheel.OwnsPointer, "Person press does not launch before release");
                Frame(wheel, tap, false, false, true);
                Check(thrower.ActiveProjectileCount == 1 && thrower.AttachedProjectileCount == 0,
                    "Person tap launches the middle selected item exactly once");
                var item = ((IList)Get(thrower, "m_Items"))[0];
                var itemType = item.GetType();
                Vector2 targetPoint = (Vector2)itemType.GetField("targetViewport").GetValue(item);
                Check(Vector2.Distance(targetPoint, (Vector2)camera.ScreenToViewportPoint(tap)) < .0001f &&
                    !(bool)itemType.GetField("wheelFlight").GetValue(item), "Restored flight targets the exact tap using original throw logic");
                if (mode == AppMode.Combat) Check(combat.FindRound(personId).History.Count == 0, "Launching never deducts health");
                Invoke(thrower, "UpdateThrownItems", .5f);
                Check(thrower.AttachedProjectileCount == 1 && thrower.SlimeSurfaceCount == 1 && surface.ActiveImpactCount == 1,
                    "Original flight reuses attachment, slime and local impact deformation");
                if (mode == AppMode.Combat) Check(combat.FindRound(personId).History.Count == 1, "Damage occurs once at actual attachment");
                thrower.ClearEffects(); surface.ResetEffects();

                Frame(wheel, tap, true, true, false);
                Frame(wheel, tap + Vector2.right * Screen.width * .1f, false, true, false);
                Frame(wheel, tap, false, false, true);
                Check(thrower.ActiveProjectileCount == 0, "Moving away then returning is a drag, never a tap");
                Frame(wheel, tap, true, true, false); Frame(wheel, tap, false, false, true, true);
                Tap(wheel, camera.ViewportToScreenPoint(new Vector3(.02f, .5f, 0)));
                Check(thrower.ActiveProjectileCount == 0, "Canceled touches and background clicks do not throw");
                Frame(wheel, tap, true, true, false);
                Set(wheel, "m_WorldPressTime", Time.unscaledTime - 1f);
                Frame(wheel, tap, false, false, true);
                Check(thrower.ActiveProjectileCount == 0, "Long holds cannot become tap launches");
                float smallStep = Screen.width * .004f;
                Vector2 controlPoint = tap + Vector2.right * smallStep * 3;
                Set(ARCoreModeOverlay.Instance, "m_ButtonRects", new[] {
                    new Rect(controlPoint.x - smallStep, Screen.height - controlPoint.y - smallStep, smallStep * 2, smallStep * 2) });
                Frame(wheel, tap, true, true, false);
                Frame(wheel, controlPoint, false, false, true);
                Check(thrower.ActiveProjectileCount == 0, "Releasing onto another UI control cancels even a short movement");
                Set(ARCoreModeOverlay.Instance, "m_ButtonRects", new Rect[0]);

                Frame(wheel, center, true, true, false);
                Frame(wheel, center - Vector2.up * wheel.Layout.Travel * .3f, false, true, false);
                Frame(wheel, center - Vector2.up * wheel.Layout.Travel * .3f, false, false, true, true);
                Check(wheel.Expanded && wheel.Reveal == 1, "Canceled vertical drag restores its settled state");
                Frame(wheel, center, true, true, false);
                Frame(wheel, center - Vector2.up * wheel.Layout.Travel, false, true, false);
                Frame(wheel, center - Vector2.up * wheel.Layout.Travel, false, false, true);
                Check(!wheel.Expanded && wheel.Reveal == 0 && wheel.ShowUpArrow && !wheel.TapToThrowEnabled,
                    "Downward drag retracts wheel and restores up arrow and deformation-only behavior");
                Tap(wheel, tap);
                Check(thrower.ActiveProjectileCount == 0, "Person taps after retraction cannot fire");
                wheel.SetExpanded(true);
                Frame(wheel, tap, true, true, false);
                bootstrap.DetectionManager.StopDetection();
                Frame(wheel, tap, false, false, true);
                Check(!wheel.CanInteract && thrower.ActiveProjectileCount == 0 && !wheel.OwnsPointer,
                    "Stopping detection cancels a pending person tap and clears effects");
                bootstrap.ReturnToSelection(); bootstrap.EnterApp(mode);
                Check(!wheel.Expanded && wheel.Visible && !thrower.ReadPointerInput,
                    "New session reuses wheel and starts collapsed without legacy picker input");
                Debug.Log("[FreeWheelValidation] " + mode + " PASS: pull/push, arrows, selection, original tap-position flight, collapsed deformation, canceled gestures and stop/restart.");
            }
            finally { foreach (var root in scene.GetRootGameObjects()) if (root != null) Object.DestroyImmediate(root); }
        }

        static void ValidateBattleWheel()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try
            {
                var bootstrap = new GameObject("Shared battle tap regression").AddComponent<ARCoreMainSceneBootstrap>();
                Set(bootstrap, "startPersonDetection", false);
                var startup = (IEnumerator)Invoke(bootstrap, "Start"); startup.MoveNext(); bootstrap.EnterApp(AppMode.Combat);
                while (startup.MoveNext()) { }
                var wheel = bootstrap.FreeWheel; var thrower = bootstrap.ThrowController;
                var combat = bootstrap.CombatController; var tracking = bootstrap.PersonTracking; var stickers = bootstrap.CartoonCanvas;
                var camera = bootstrap.ARCamera; camera.pixelRect = new Rect(0, 0, Screen.width, Screen.height);
                Set(bootstrap.DetectionManager, "m_Enabled", true);
                tracking.UpdateDetections(new[] { PersonSystemValidation.FullPose(.5f), PersonSystemValidation.FullPose(.88f) }, Time.realtimeSinceStartup);
                int target = tracking.Pick(Vector2.one * .5f).personId;
                int other = tracking.Pick(new Vector2(.88f, .5f)).personId;
                combat.CaptureRound(target); combat.CaptureRound(other);
                var damage = (EarthOL.Combat.AttackDamageConfig)Get(combat, "m_Damage");
                foreach (var entry in damage.AttackTypes) entry.criticalChance = 0;
                wheel.SetExpanded(true);
                Vector2 tap = camera.ViewportToScreenPoint(new Vector3(.5f, .5f, 0));
                int defeats = 0; combat.Defeated += round => defeats++;
                for (int i = 0; i < 20 && !combat.FindRound(target).Defeated; i++)
                { Tap(wheel, tap); Invoke(thrower, "UpdateThrownItems", .5f); }
                var roundResult = combat.FindRound(target);
                Check(roundResult.Defeated && roundResult.CurrentHealth == 0 && defeats == 1 && stickers.IsPersonActivated(target) &&
                    !stickers.IsPersonActivated(other) && combat.FindRound(other).CurrentHealth == combat.FindRound(other).MaxHealth,
                    "Tap projectiles retain per-person damage, defeat and isolated stickers");
                int records = roundResult.History.Count;
                Tap(wheel, tap); Invoke(thrower, "UpdateThrownItems", .5f);
                Check(defeats == 1 && roundResult.History.Count == records, "Defeated target cannot report duplicate damage");
                combat.RestartRound(target);
                Check(!stickers.IsPersonActivated(target) && !combat.FindRound(target).Defeated, "Restart clears only target defeat state");
                int count = thrower.ActiveProjectileCount;
                Set(combat, "m_Settings", true); Tap(wheel, tap);
                Check(thrower.ActiveProjectileCount == count, "Combat settings prevent person-tap launches");
                Set(combat, "m_Settings", false);
                bootstrap.SetMode(GameMode.Showcase);
                Check(!wheel.TapToThrowEnabled && wheel.Visible, "Showcase subject selection retains its own gesture");
                Set(bootstrap.ShowcaseController, "m_Presenting", true);
                Check(!wheel.Visible, "Fullscreen showcase hides selector");
                Set(bootstrap.ShowcaseController, "m_Presenting", false);
                bootstrap.DetectionManager.StopDetection();
                Check(combat.Rounds.Count == 0 && stickers.SelectedPersonCount == 0 && thrower.ActiveProjectileCount == 0,
                    "Stop cleans shared effects and combat state");
                Debug.Log("[FreeWheelValidation] battle tap damage, defeat identity, restart and modal/showcase lifecycle PASS");
            }
            finally { foreach (var root in scene.GetRootGameObjects()) if (root != null) Object.DestroyImmediate(root); }
        }

        static Vector2 ScreenPoint(Vector2 guiPoint) => new Vector2(guiPoint.x, Screen.height - guiPoint.y);
        static void Tap(FreeModeWheel wheel, Vector2 point)
        { Frame(wheel, point, true, true, false); Frame(wheel, point, false, false, true); }
        static void Frame(FreeModeWheel wheel, Vector2 point, bool down, bool held, bool released, bool canceled = false)
        {
            // Editor invocations simulate separate frames without advancing Unity's frameCount.
            Set(wheel, "m_ConsumedFrame", -1);
            wheel.ProcessPointer(new BodyPointerFrame { id = 3, position = point, down = down, held = held, released = released, canceled = canceled });
        }
        static object Get(object target, string field) => target.GetType().GetField(field, Private).GetValue(target);
        static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
        static object Invoke(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);
        static void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("[FreeWheelValidation] FAIL: " + message); }
    }
}
