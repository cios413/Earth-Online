# Person-system implementation and verification results

## Delivered behavior

- The existing ARCore scene, session/camera and raw YOLOv8-Pose decoder are reused.
- Automatic low-frequency person detection runs before any click and exposes
  clickable candidate boxes with separate IDs.
- A short click on a tracked person activates that person's 2D cartoon and
  deep-copies the SAME pose for one anchored 3D capsule model. The click has no
  interaction-mode restriction and does not request an extra YOLO inference.
- 2D position/rotation/length/width continue following the live track. Full poses
  render ten rectangular artwork parts; each person has its own pooled rig.
- The 3D model is built once under one ARAnchor and has no realtime pose update
  callback. With all required keypoints valid, it contains ten Capsule primitives.
- Detection and grayscale patch tracking have separate configurable rates; bounded
  velocity prediction updates the live keypoints between image corrections.
- Stop Detection cancels/disposes inference and stops capture, Tensor inputs,
  decode, flow and track updates. 2D is hidden/pooled. AR camera and frozen anchors
  remain. Restart reloads the worker; matching previous positions can restore IDs.
- The previous throw/adhesion/proxy implementation and its assets/tests remain,
  isolated in inactive Legacy runtime objects. No second worker is loaded by it.
- Person deformation retains its old proxy overload and has a separate new
  tracking-data overload; fixed models never depend on it.

## Changed files

| File | Change |
| --- | --- |
| `Assets/ARCore/Runtime/ARCoreMainSceneBootstrap.cs` | Creates new managers, Canvas, distance estimator, click builder and ARAnchorManager; isolates Legacy objects; persistent Inspector FPS/loss settings |
| `Assets/ARCore/Runtime/ARCoreModeOverlay.cs` | Detection Start/Stop, click/deform interaction controls, person count and runtime performance HUD |
| `Assets/ARCore/Runtime/PersonDeformationSurface.cs` | Adds independent tracking-data overload, preserves legacy proxy overload, never drives frozen capsules |
| `Assets/BodyThrow/Runtime/BodyPoseDetector.cs` | Reuses input float buffer and adds manually scheduled/yielded inference while preserving decoding and synchronous validation API |
| `Assets/EarthOL/Common/GameMode.cs` | Documents the new interaction meanings while preserving enum values |
| `Assets/BodyThrow/README.md` | Marks projectile instructions as Legacy and links current architecture/results |
| `ProjectSettings/ProjectSettings.asset` | Enables Frame Timing Stats via the development validation/build entry point; no Unity/package upgrade |

## New files

All runtime modules are in `Assets/BodyThrow/Runtime/` (with Unity-generated .meta
files alongside them):

| Area | Files |
| --- | --- |
| Data/tracking | `PersonPoseData.cs`, `PersonTrackingManager.cs`, `PersonFacingDetector.cs`, `PersonKeypointFlow.cs` |
| Detection/control/performance | `PoseDetectionManager.cs`, `PersonDetectionState.cs`, `PersonDetectionProfiler.cs` |
| 2D | `Cartoon2DCanvas.cs`, `Cartoon2DRig.cs`, `Cartoon2DPart.cs` |
| Frozen 3D | `PersonClickController.cs`, `FrozenPersonData.cs`, `PersonDistanceEstimator.cs`, `PersonWorldPositionCalculator.cs`, `Frozen3DPersonBuilder.cs`, `Frozen3DPersonController.cs` |
| Resources | `Assets/Resources/FrozenPerson.mat` |
| Validation | `Assets/Editor/PersonSystemValidation.cs` |
| Documentation | `Assets/BodyThrow/PERSON_SYSTEM.md`, this results file |

## Configuration and lifecycle

Open `Assets/Scenes/ARCoreMainScene.unity` as before. On the bootstrap Inspector,
`personDetectionFps=5`, `personFlowFps=15`, `personLostSeconds=1` and
`startPersonDetection=true` are the defaults. The managers expose these settings
at runtime as well. 5/10/15 detection Hz are supported caps, not guaranteed rates
on every device. The application targets a 30 FPS render loop.

Before click: AR camera, scheduled detection and multiple live track candidates
run. After click: that ID is activated in the 2D Canvas AND a separate frozen
snapshot is constructed. Live updates and ARAnchor updates have independent
lifecycles. A snapshot only contains copied viewport coordinates, scalar values
and the distance calculated at click time; subsequent keypoint or camera movement
does not rerun its builder.

Coordinate mapping and depth limitations are detailed in `PERSON_SYSTEM.md`.
The projection matrix is used as intrinsics adjusted for display rotation/crop.
The 1.7 m height assumption determines estimated depth; cropped/partial/crouched
people reduce confidence and can be misplaced. The result is an anchored 2.5D
pose silhouette, not measured 3D body geometry.

## Compilation and behavior tests

Final development build log:
`C:/Users/31290/AppData/Local/Temp/opencode/person-system-profile-build.log`.

Build result: Succeeded; C# / shader build errors: 0; Unity exit code: 0.
Unity remains 2022.3.62f3c1, AR Foundation/ARCore remain 5.1.6, Android ARM64,
minimum API 29. Output: `Builds/EarthOL-PokeDeform.apk` (Development, debug signing).

The actual Unity batch tests passed:

| Validation method | Evidence covered |
| --- | --- |
| `Pose2DTrackingValidation` | Two independent IDs, reversed detection result ordering, intervening motion prediction, loss/recovery/expiry; known textured displacement recovered by image matching |
| `PersonFacingValidation` | Front/side cues and temporal votes suppressing a one-frame flip |
| `PersonSnapshotValidation` | Deep copy of live arrays, immutable getters, frozen bounding box and identity |
| `Frozen3DPersonValidation` | Projection-based distance, translated/rotated camera and off-center placement, ten native Capsule primitives, independence from live person/camera changes |
| `Cartoon2DRigValidation` | ONE click activates the Canvas and creates 3D; ten Images, click debounce, 2D follows a changed pose while the frozen root stays put |
| `BootstrapMigrationValidation` | Independent components, inactive Legacy inference and spawner, no demo cube/sphere; interaction changes do not override Stop Detection |
| `PersonDetectionPerformanceValidation` | Configured Hz cap, Stop halts capture/Tensor/inference/tracking/flow counters, restart with candidate ID recovery; measured tracking benchmark |
| Existing `BodyPoseValidation.Validate()` | Original RGB orientation, letterboxing, stride/layout decoding, confidence filtering, geometry, stabilizer and model execution regressions |
| Actual `DetectScheduled` run | Raw model executed through yielded scheduling, blank portrait input decoded correctly |

## Measured performance and evidence limits

Final batch log measured desktop results:

- Tracking prediction loop: 1.049 ms for 1000 updates of two synthetic people;
  0 reported allocated bytes for that measured loop. This does not measure the
  entire pipeline, UI, image flow or Tensor allocations.
- Raw model scheduled blank-image execution: 122.2 ms desktop elapsed, 13 yielded
  steps. This synthetic desktop run does not establish phone inference FPS.
- Profiler markers and GC/memory recorder are present in the Development APK.
  `PersonDetectionProfiler` records an 8-second `.raw` Unity Profiler trace after
  three completed detections and writes `person-system-performance.json` beside it.
  CPU/GPU native frame-timing availability is recorded; unavailable timings are -1.

The new Development APK was installed on Vivo V2307A (`install -r`: Success),
and `am start -W` returned `Status: ok`. The USB/ADB connection recovered briefly
and the current application process was checked (PID 27800):

- Model loaded and configured detection cap was 5 Hz, grayscale flow target 15 Hz.
- A real application click produced PersonID=1, ten capsules, distance estimate
  1.50 m, distance confidence 0.91; the same handler activated 2D cartoon tracking.
- Native ARCore registration was logged for anchor
  `B400007A7D6726D0-600613A12A17C812`.
- Subsequent periodic logs continued counting detection, flow and live tracks
  after the click. Example: completed inference count 19 -> 41 -> 63, and flow
  count 46 -> 102 -> 159. This verifies that freezing did not stop the live loop.
- Unity binary Profiler capture and its JSON report were pulled successfully.

Actual 8.0169-second profiled device window (Development build; mostly one tracked
person during the sampled window):

| Measurement | Recorded value |
| --- | --- |
| Device/API | Vivo V2307A, Adreno 750, OpenGLES3 |
| Completed detections in window | 35 (234 rendered frames) |
| Actual YOLO rate | 4.366 Hz, below configured 5 Hz cap |
| Average inference elapsed time | 195.32 ms including yielded frames |
| Prediction/association CPU sample average | 0.0208 ms; image flow has its own separate profiler marker |
| Native CPU/GPU frame timing | 34.35 / 37.72 ms, 234 timing samples |
| Approximate rendered rate from window counts | 29.19 FPS |
| Peak Unity allocated memory | 136,029,597 bytes (129.73 MiB), not total system PSS |
| Total whole-frame managed GC allocation | 207,835,603 bytes over the window (nonzero; includes Barracuda, outputs, UI and profiling) |

Profiler artifacts are saved locally at:

- `C:/Users/31290/AppData/Local/Temp/opencode/person-system-device-profiler.raw`
- `C:/Users/31290/AppData/Local/Temp/opencode/person-system-performance-device.json`

This is a measured first device run, not a claim that performance tuning is
finished. The GC allocation is substantial despite reused capture/input arrays;
backend tensor upload/output buffers and UI still allocate and should be assessed
in the attached Unity Profiler trace before further optimization. Raw frame-time
samples and rendered frame counts can differ due to GPU pipelining.

The connection disappeared again before an automated device Stop/Start click test
could run. Editor counter tests passed, but real-camera alignment, sustained
multi-person intersections, visual facing accuracy, person motion versus fixed
anchor stability, device Stop/Start behavior and long-duration profiling remain
acceptance items. Startup contains existing missing-script and early XROrigin
camera-assignment warnings; the recorded native session/model/anchor path ran,
but these warnings are not claimed resolved.

## Known limitations and phone acceptance

- Motion matching plus joint similarity reduces ID swaps; it cannot reliably
  distinguish visually indistinguishable people during full overlap or long
  occlusion. There is no appearance/biometric ReID model.
- Face/shoulder heuristics cannot reliably disambiguate a back view from facial
  occlusion. Facing is conservative and vote-filtered.
- Image tracking requires textured patches and moderate motion. Fast movement,
  low light or blur requires periodic YOLO correction. Target Hz must be tuned
  with actual profiler data. Each neural layer/readback can still exceed the
  nominal per-step CPU scheduling budget.
- Low-confidence joints are omitted rather than inventing a frozen body pose.
- ARCore must reach SessionTracking before device snapshot creation. Registration
  is checked for up to 5 seconds; a failed anchor removes its provisional model
  and logs an error. Map refinement can move an anchor slightly; this is distinct
  from following realtime YOLO/person updates.
- Development profiling adds overhead. A production APK should disable automatic
  binary capture after device tuning.

Phone acceptance sequence: point the rear camera at a full standing person; wait
for a candidate box; click once; move the person while observing the live cartoon
and static 3D model; test another person, screen orientation, short/long occlusion,
Stop/Start and camera movement; retrieve logs and profiler artifacts. Native
Anchor and visual acceptance cannot be certified solely by the editor tests.
