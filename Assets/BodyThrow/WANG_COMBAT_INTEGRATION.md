# Reused wang combat interactions

Source: <https://github.com/cios413/Earth-Online/tree/wang>, commit `fe4947f` (including `db636fa`, `369ffef`, `c905cba`, `75e0079`, and `fe4947f`).

The following runtime implementations originate from that branch:

| Source file | Reused behavior |
| --- | --- |
| `Assets/EarthOL/Combat/PersonCombatController.cs` | Target selection, barehand press/swipe damage, held-item sweep rules, selection boxes, settlement and continue event |
| `Assets/ARCore/Runtime/FreeModeWheel.cs` | Center-item pull-out handoff, held thumbnail hiding, target-selection hint and arrow interaction |
| `Assets/ARCore/Runtime/SwipeDeformController.cs` | Directional slap impulse, shear/tilt and spring recovery |
| `Assets/ARCore/Runtime/PersonDeformationSurface.cs` | Target focus, body mask and directional deformation integration |
| `Assets/Scripts/PokeDeform/PokeWarpDeformer.cs` | Impulse, shear and body-mask shader upload |
| `Assets/Shaders/PersonWarp.shader` | Person-masked shear rendering |

`ARCoreMainSceneBootstrap` also reuses wang's session marker policy, input policy and deferred recognition restart when continuing to another target. The current sticker skin picker cleanup and default combat skull selection remain connected. `Cartoon2DCanvas.DrawPersonMarkers`, `BodyProjectileCatalog.Entry.canHold`, the StickyHand catalog flag, and `Person3DThrowController.SelectedEntry` are the same small additions from wang, applied without replacing this project's alien skins or DreamGrenade entry.

## Smooth deformation rendering adjustment

The imported body mask previously multiplied the combined press and swipe vertex displacement. This could produce visible polygon edges around narrow limbs. Ordinary press/impulse vertices now use the original pre-import deformation formula without the body mask. Only the directional swipe image offset is masked, evaluated per pixel when sampling the camera grab; moving the contour no longer folds the coarse mesh at its boundary. Its feather is supplied from the person region's size/grid spacing and uses pixel derivatives for antialiasing. The region border retains the original smooth falloff. The imported swipe spring, direction, strength, tilt, attack detection, health and cooldown rules remain in use. Final zero amplitude and reset clear the renderer's swipe displacement and tilt.

`SwipeDeformationValidation.Validate` checks that press output is pixel-identical with/without a body mask; swipe remains visible and consistent across 48x64 and 4x4 meshes; feather width changes the actual rendered transition; outside pixels and the fully recovered image stay unchanged. Preview output is written to the temporary `opencode/SmoothPressSwipePreview.png`.

## Resulting controls

- Free mode retains ordinary person detection rectangles, deformation and sticker skins.
- Combat starts in target selection. The first person tap chooses a target without applying damage or launching a projectile. After selection, the selection boxes disappear and only the selected target receives a combat round and health display.
- With the wheel retracted, short barehand taps apply the existing `press` attack. Swipes apply the existing `swipe` attack and directional slap deformation.
- With the wheel expanded, ordinary person taps still throw the center-selected item. Pulling the center StickyHand upward instead transfers the current gesture to a held item that follows the pointer.
- Held attacks follow wang's selected-target outside-to-inside intersection and cooldown rules. Holding still does not repeatedly apply damage; releasing or canceling ends holding. The source branch enables this behavior for StickyHand, not every catalog entry.
- Settlement's continue action defers recognition stop/restart by one frame, clearing old target state and returning to target selection.

## Validation

`Tools/Earth OL/Validate wang combat integration` (`WangCombatReuseValidation.Validate`) uses a preview scene to check selection, marker policy, exact press damage, selected-target isolation, canceled input, swipe feedback, actual center-item drag handoff, stationary/cooldown/release behavior, projectile attachment accounting, stale round rejection, reset and free-mode alien skin compatibility. The ordinary combat/wheel regression assertions have been updated for wang's selected-target flow.
