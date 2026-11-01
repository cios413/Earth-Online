# BodyThrow prototype

`更改投掷物` opens a four-sector radial wheel with model thumbnails. In throw mode,
holding the screen for 0.48 seconds also opens it: slide around the wheel and
release to select; return to its center before release to cancel. Up/right/down/left
select slots 1/2/3/4. A short tap is classified on release, so holding never fires
before the wheel appears. Double-tap still selects a person. Slot order:
1 Sticky Hand (default at startup), 2 Human Brain, 3 方块 (the Blender object
`立方体.001`, using Clean Jelly), 4 Arrow. Selection affects subsequent throws; existing projectiles keep
their model. The picker clears delayed taps and blocks world input while open and
briefly after closing. Expensive pose inference and its loss timers pause while
the wheel is open, keeping input responsive and preserving the current selection;
AR camera tracking continues and pose inference resumes after closing.

`Tools/export_throwables.py` evaluates the configured collections/objects from
`../投掷物.blend`, bakes their authored color graphs to 512px atlases and exports
centered meshes in Unity coordinates. Run it using Blender 4.2 background mode.
Pass `-- --only JellyCube` to update just the new cube while preserving other
exported slots. The `@` selector addresses an exact object name rather than a collection.
`EarthOL.EditorTools.ThrowableAssetBuilder.BuildAndValidate` imports those meshes,
creates materials, previews, prefabs and `Resources/Throwables/Catalog.asset`, then
runs selection, input, collision, attachment and GPU depth checks.
`BuildValidateAndApk` additionally builds Android. Projectile roots keep unit scale;
visual longest sides are 24 cm for the hand/brain, 36 cm for the cube and 44 cm for
the arrow, with a minimum 12 mm collision thickness. The cube replaces the old
road sign in slot 3, uses its authored orientation and alpha 0.8 in a Unity
Standard transparent material.
The flight/tip direction is local +Z and continues into the surface
after attachment. Spawn distance accounts for model depth and the near clip plane.
Source Blender files
are not saved by the export script. Blender transmission/SSS are approximated by
Unity Standard materials; authored colors are baked and the hand remains translucent.

On attachment, `ProjectileImpactBounce` compresses visual depth by 26%, widens it
to conserve volume, then overshoots and restores the exact original transform
within one second. The impact end is anchored and attachment roots keep unit scale.

Deformation mode now uses `PersonDeformationSurface` with the original
`PokeWarpDeformer` stretch/bulge spring (0.45 stretch, 2.6 Hz, damping 0.30).
Double-tap a person in either mode, then press/drag inside the padded region
around the visible head/torso proxies. Release starts the spring rebound. The
region freezes during touch/recovery and follows the person again when idle;
poke positions stay in surface coordinates when the camera moves. Pose inference
is deferred during held/rebounding deformation to keep drag and spring animation
responsive, then resumes with loss timers compensated. The spring
uses 1/120-second integration substeps for consistent recovery under slow frames.
The compositor
draws after 3D objects with no depth writes and anchored edges, and only displays
while deforming/rebounding. It is not a person-segmentation mask. No default soft
cube/sphere or tracked-image demo objects are generated. Person selection remains
active across mode changes.

`EarthOL.EditorTools.InteractionUpgradeValidation.BuildAndValidate` generates the
projectile assets and verifies radial gestures, tap isolation, impact recovery,
person-region bounds, image warping/recovery on the GPU and default-mode setup.
`BuildValidateAndApk` runs these checks and builds Android. Its GPU comparison is
saved as `person-deformation-preview.png` in the existing temporary opencode folder.

Head and torso use `Resources/BodyDepthOccluder.mat`. The meshes write depth but
no color (`ColorMask 0`, `ZWrite On`, queue `Geometry-10`). They render after the
AR background and before opaque projectiles: projectiles in front stay visible,
and portions behind either body proxy are hidden. Both proxy renderers remain
enabled for depth, with shadow casting/receiving disabled. Lost parts stop writing
depth when their existing tracking timeout deactivates them. The AR camera no
longer creates `BodyGlassRenderer`, so the default scene does not capture or blur
the background. Occlusion follows the fitted proxy outlines, not a body segmentation mask.

`EarthOL.EditorTools.BodyOcclusionValidation.ValidateAndBuild` checks that the
background is unchanged by depth-only meshes and verifies front, back, partial
and intersecting occlusion with the actual projectile prefab before building.
It writes `body-occlusion-preview.png` to the existing temporary `opencode`
directory when available (background / projectiles / invisible occlusion).

The unified ARCore scene uses YOLO pose landmarks to drive head and torso proxies.
Only these two body parts are currently generated; limbs and joint visuals are
temporarily removed.

Open `Assets/Scenes/ARCoreMainScene.unity` or press `② 投掷模式`. Tracking requires
a live AR camera feed on a device. Proxies stay hidden until a person is selected
and the corresponding landmarks are reliable.

- Double-click/tap near a person: select the target.
- Single click/tap after selection: launch a projectile.
- `显示定位标记`: toggle the captured camera preview, raw landmark scores and fitted centers.
- Yellow points: reliable raw landmarks (0 nose, 1/2 eyes, 3/4 ears, 5/6 shoulders, 11/12 hips).
- Green outline: detected person. Cyan cross/line: fitted head center and torso axis.
- Use the ARCore mode buttons to switch back to deformation.

`HeadProxy` has a SphereCollider and `ChestProxy` has a CapsuleCollider. Each part
has a stable, unit-scale attachment transform and a `BodyPartProxy`. A projectile
validates against the rig root and attaches to the exact part hit. Pose inference
still outputs 17 landmarks; the rig uses the face, shoulders and hips.

Capture reuses the AR camera background's display transform and preserves the
viewport aspect ratio. YOLO input uses RGB, top-down rows and letterboxing to
416x416; decoding removes padding and the keypoint grid's half-cell offset.
Tensor layout is validated and logged rather than assuming a flat output order.

Only landmarks scoring at least 0.45 enter smoothing; large jumps must be observed
in two consecutive detections. Head and torso are fitted independently. A missing
part hides its renderer and disables collision after 0.4 seconds. If the selected
person is lost for 0.75 seconds, double-tap again. Matching uses overlap, position
and size gates; it is not biometric identity tracking. Changing screen orientation
also requires reselection. Mode buttons and the diagnostic button do not select
people or launch projectiles.

The head center and radius come from reliable face landmarks. The torso capsule's
outer ends meet the shoulder and hip centers. Attachment roots keep unit scale.
Depth is still approximate: shoulder-to-hip length is assumed to be 0.50 m, with
a head-radius fallback of 0.105 m; depth changes are smoothed and limited per scan.
This is screen-aligned 2.5D fitting, not measured 3D body geometry.

Head and torso share a world-space stabilizer after landmark fitting. It damps
translation and tilt together, then filters local shape and radii. Defaults hold
translation noise inside 8 mm, tilt inside 1.2 degrees, local shape inside 3 mm,
and radius changes inside 1.5 mm. Larger translations/rotations get a faster
response to follow deliberate movement. The rig advances every rendered frame
with time-based damping rather than jumping between inference results. Changing
between torso/head anchors preserves existing world geometry; target reselection
resets the history. Lost parts still expire, and attached projectile roots retain
unit scale. These filters reduce small pose/depth fluctuations; AR session drift
and rapid camera movement can still affect alignment.

`EarthOL.EditorTools.BodyPoseValidation.ValidateAndBuild` runs the decoder,
geometry, tracking and stabilizer regressions before building an APK. Stabilizer
checks include synthetic jitter RMS, deliberate translation/tilt/size changes,
30/60 FPS agreement, anchor changes, reset and attachment behavior.
