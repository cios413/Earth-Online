# Unity UI prototype sources

These sources accompany the HTML UI simulations in the parent directory. They were introduced by commit `2435ed4` and are retained here for future UI integration.

They target a different runtime API: `CombatManager`, `EarthOL.Throw.ThrowModeRuntime`, `GameMode.Attack/Free`, and `ModeOverlay.IsPlaying/SetPlaying`. Those APIs are absent from the current project. `ARCameraBootstrap` also automatically creates its own camera and AR session.

Keep this reference code outside `Assets` until it has been adapted to the current `ARCoreMainSceneBootstrap`, `AppMode`, `PersonCombatController`, and shared person interaction controllers. Unity compiles every C# source under `Assets`, so importing this prototype directly causes missing-type errors and can create a second runtime camera.

The running application's retractable item wheel is implemented in `Assets/ARCore/Runtime/FreeModeWheel.cs`. Its free/combat integration is checked by `EarthOL.EditorTools.FreeWheelValidation.Validate`.
