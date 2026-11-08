# Live 2D cartoon and frozen AR person migration

## Audit and migration decisions

The pre-migration `ARCoreMainSceneBootstrap` creates the AR session/camera, a
single-person `BodyPoseTracker`, a continuously driven head/torso `BodyProxyRig`,
projectile spawner and `PersonDeformationSurface`. `BodyPoseTracker` owns camera
readback, YOLO execution, single-person matching, gesture classification, depth
estimation and live 3D updates. Its current scan interval was 0.30 seconds and
384 pixels on the longest capture side, with inference suspended during legacy
gestures. These values were read from the actual project, not the earlier design.

Reusable: Unity 2022.3.62f3c1; AR Foundation/ARCore 5.1.6; Input System; Barracuda
2.0.0; AR background display transform; letterboxing; DFL, grid-offset, sigmoid,
NMS and tensor-layout decoding; COCO-17 keypoint geometry; existing AR bootstrap.
No model retraining or package upgrade is involved.

Conflicts: the previous selected target and depth-stabilized live proxy cannot
represent frozen snapshots. Its double-tap/throw/radial interaction also conflicts
with the new single-click event. The old single-person tracker, throw catalog,
projectiles, stabilizer and validation files remain in the project, but their
runtime objects are inactive under `Legacy Projectile System`. They do not load a
second YOLO worker. The new default overlay controls detection and interaction,
not projectile launching. Legacy tests that assert the old default bootstrap/UI
need their legacy configuration; they are preserved, not used to certify this path.

The previous `PersonDeformationSurface` depends on collider bounds from BodyProxyRig.
Its original overload is retained; a new overload takes tracking data directly.
The new flow does not depend on the old invisible live collider. Pose inference
is not suspended during deformation in the new path.

Original bottlenecks: synchronous whole-model execution/readback on the render
thread; repeated managed input allocation; high-resolution capture; no intermediate
image tracking; repeated GameObject creation. The new path uses a reusable RGB
buffer and readback textures, scheduled model steps (4 ms CPU scheduling budget),
low-rate YOLO and 192-pixel grayscale patch matching. The GPU/backend can still
stall during execution/readback; scheduled CPU work does not guarantee a 4 ms GPU
budget. Device profiling is required before selecting final rates.

Implementation order: audit -> unified 2D pose -> association/prediction and image
matching -> pooled Canvas rigs -> immutable click snapshot, distance/world mapping
and ARAnchor capsules -> bootstrap/UI isolation -> regression/performance checks.

## Runtime data flow

```
AR camera background (display rotation/crop applied)
  -> reusable RGB capture (384px longest side by default)
  -> letterbox 416x416 -> scheduled BodyPoseDetector -> COCO viewport poses
  -> PersonTrackingManager (one ID/state/velocity per person)
     + high-rate grayscale SAD patch matching with forward/backward rejection
     + velocity prediction per rendered frame
     + periodic YOLO correction, overlap/location/landmark matching
  -> tracked bounding boxes (available for clicking)
  -> ONE short tap on a person's bounding box
     + activate that ID in Cartoon2DCanvas -> 10 rectangular artwork parts
     + deep-copy FrozenPersonData -> distance/world pose -> ARAnchor + 10 capsules
  -> subsequent realtime poses update only the 2D cartoon
```

The user's corrected click contract is explicit: the SAME click activates the
selected person's 2D cartoon and creates its 3D snapshot. It is not a sequential
two-mode operation. Before click, detection and tracking provide person candidates;
cartoon artwork appears for clicked IDs. Additional selected persons have their
own rigs and snapshots. `PersonClickController` debounces the same ID for 1.5 s;
`Frozen3DPersonBuilder` caps the number of snapshots (Inspector, default 10).

All live pose/keypoint coordinates are normalized camera viewport coordinates,
origin bottom left. The model input is RGB top-down, with gray letterbox padding;
decoding removes padding and reverses model Y. The shared AR background material
applies the display transform exactly once. Canvas mapping uses
`ViewportToScreenPoint` + `RectTransformUtility.ScreenPointToLocalPointInRectangle`
on a Screen Space Overlay canvas. It never calculates sticker positions from
world-space proxies or anchors. Orientation/capture-size changes discard live
tracks and optical history; existing frozen anchors are retained. Reselect people
after rotating the display.

## Detection, tracking and stop/start

`PoseDetectionManager` Inspector settings:

| Setting | Default | Meaning |
| --- | --- | --- |
| detectionFramesPerSecond | 5 | Maximum start rate; accepts 1–15 Hz |
| flowFramesPerSecond | 15 | Grayscale matching rate; accepts 5–30 Hz |
| captureMaxSide | 384 | RGB readback resolution (longest side) |
| maxPeople | 6 | Maximum post-NMS results, adjustable up to 10 |
| startOnAwake | true | Start the algorithm pipeline on scene startup |

The configured rate is a cap, not a claim of achievable device FPS. A new
inference starts only after the prior scheduled inference completes and the
configured interval expires. Tracking runs per render frame and patch matching
runs at its own rate using at most 17 small 5x5 patches per person. Low texture,
excessive error and inconsistent forward/backward matches are rejected. Flow
failure requests the next allowed periodic YOLO correction, never an unbounded
extra inference. The current implementation uses an alpha-beta velocity filter,
not a full Kalman model or articulated 3D pose reconstruction.

For persistent configuration edit the existing scene bootstrap's Inspector fields
`personDetectionFps`, `personFlowFps`, `personLostSeconds`, `startPersonDetection`.
It assigns those values to its runtime-created managers. The manager's Inspector
also allows live tuning while playing.

`StopDetection()` returns before capture, Tensor creation, decode, flow or tracking
can execute. It cancels/disposes an in-flight iterator, releases the worker/input
and capture textures, clears live tracks; the Canvas hides/pools their rigs on
its next refresh. It does not destroy ARSession, ARCamera or frozen anchors.
`StartDetection()` reloads the worker and resumes; old IDs are retained as hidden
association candidates for up to 3 seconds, restored only by a new matching detection.
Merely changing interaction
mode does not override a user stop. `PersonSystemValidation` checks algorithm
counters across repeated stopped updates.

Tracked IDs are retained for a short configurable interval (default 1 second),
their confidence decays, and the Canvas hides them below its confidence threshold.
Brief recovery can keep an ID if association gates agree. The same/overlapping
people crossing or a long occlusion can still swap/lose IDs: this is not an
appearance/biometric ReID system. Lost tracks expire and new IDs are never reused.

## 2D artwork

`Cartoon2DCanvas` pools `Cartoon2DRig` objects. A rig has Head, Torso and eight
arm/leg segments. Limbs use the joint midpoint, exact projected joint distance,
angle and estimated width; torso uses shoulder/hip centers and shoulder width.
Head fitting reuses confidence-filtered face geometry. Parts smoothly interpolate
local position/size and use shortest-angle interpolation. One stabilized facing
decision is shared by the rig; mirroring is compensated in local joint coordinates
to keep each joint on the person. Low-confidence parts are hidden independently.
The initial sprites are generated rectangular test artwork (face features on the
head); custom artwork can replace these assets later.

Yaw is a conservative face/shoulder heuristic with three detection votes before
switching direction. Back-view detection from missing facial points is ambiguous
(occlusion can look like a back view). It is not a trained four-way facing model.
Monocular 17-point pose does not provide reliable depth or exact back/front identity.

## Frozen snapshots and anchors

`FrozenPersonData` deep-copies all arrays and returns array copies. Live tracking
does not have references to the stored points. `PersonDistanceEstimator` assumes
1.7 m and uses the displayed AR projection's vertical focal length (equivalent to
intrinsics corrected for screen rotation/crop), with sensor-intrinsic/FOV fallback:

`distance = 1.7 * verticalFocalPixels / detectedPersonPixelHeight`.

Border-truncated boxes or missing head/ankles reduce distance confidence. No
unexplained fixed distance is used, but partial/sitting/crouched people can have
large depth error. Position uses bbox center and `Camera.ViewportToWorldPoint`,
including camera translation/rotation and off-center screen location. No plane
raycast is used because a wall/floor hit is not a measurement of the person's
surface. Native environment depth could be an optional future estimate.

AR Foundation 5.1.6 supports `new GameObject -> set world pose -> AddComponent<ARAnchor>()`
with `ARAnchorManager` on the existing XROrigin. The code does not use AR Foundation
6.x async APIs. Native AR tracking must be ready before device creation. Each
snapshot owns one anchor. `Frozen3DPersonController` checks registration for up to
5 seconds and removes/reports a model that failed to anchor; it never follows
YOLO. Anchor refinements may change its world transform as ARCore corrects the
map, which is expected world tracking, not following the person.

Capsules use the snapshot joints projected at a single estimated depth: a 2.5D
silhouette with assumed radii/height. Reliable points produce all 10 native Capsule
primitives. Missing joints omit corresponding capsules rather than inventing a
pose. No SkinnedMesh, skeleton animation, BodyProxyRig or continuous pose update
is used. Local capsule transforms are written only once. The root is not parented
to the AR camera. Stop/Start and a moving/leaving real person do not remove it.

## Scene, validation and profiling

Continue opening `Assets/Scenes/ARCoreMainScene.unity`; no hand-placed model or Canvas
is required. Existing bootstrap creates new components. Do not add another ARSession,
XROrigin or camera. Unity/packages remain unchanged. `FrozenPerson.mat` is a resource
reference ensuring the capsule shader is included. Legacy assets remain intact.

Run `EarthOL.EditorTools.PersonSystemValidation.Validate` in batch mode. Its named
checks cover Pose2DTracking, PersonFacing, PersonSnapshot, Frozen3DPerson,
Cartoon2DRig (one-click dual event), BootstrapMigration and DetectionPerformance.
Editor tests cannot prove native ARCore anchor stability or camera-to-person
alignment on a real phone. `ValidateAndBuild` makes a Development APK, allowing a
Unity Profiler connection. All previous validation files remain in the project.

Profiler markers: `Person.CameraCapture`, `Person.YOLO.ScheduleStep`,
`Person.Tracking`, `Person.PatchFlow`. Runtime HUD/logs report actual detection
start rate, end-to-end inference wall time (including yields), actual tracking
CPU duration, readback/Tensor counters, tracked people, Mono memory and whole-frame
GC bytes via ProfilerRecorder. Whole-frame GC includes Unity/AR/UI/YOLO work;
it is not exclusively the tracking module. Desktop tracking benchmark records
measured time and allocations separately. Record CPU Timeline, GPU module, GC
Alloc, used/reserved memory and a multi-person scenario on the target phone;
configured Hz alone is not performance evidence.

`PersonDetectionProfiler` automatically records one 8-second binary Unity Profiler
sample in a Development APK after three completed inferences. It writes
`person-system-profiler.raw` and `person-system-performance.json` into
`Application.persistentDataPath`. Frame Timing Stats are enabled for development
builds via the validation/build entry point. Unsupported CPU/GPU timing is reported
as -1, never fabricated. Recording adds overhead and the report describes that
limitation. Pull the artifacts using ADB and open the .raw file in Unity's Profiler.

Default interaction is `点击人物`. A short click always activates cartoon tracking
and creates the fixed snapshot in the same handler; `人物形变` is optional and does
not disable that short-click dual event. Stop Detection must not be overridden
by switching between those interaction controls.

## Pending device acceptance

Verify portrait/landscape overlay alignment, near/far and half-body distance,
person intersections/occlusion, one click simultaneously yielding 2D+3D, real
person movement with 3D fixed, AR relocalization, Stop/Start counter stability and
sustained target CPU/GPU/memory. Device measurements and empirical results are
reported separately after compilation/build; do not treat this document as proof
that device performance or visual acceptance has passed.
