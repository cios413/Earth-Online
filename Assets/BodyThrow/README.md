# Person interaction runtime

The ARCore scene has four screen-space features controlled by the four buttons in the upper-left corner:

1. `人物形变` uses `PersonDeformationSurface` and the spring-based camera warp around the tracked 2D person.
2. `人物贴图` activates the live jelly skeleton when the person is tapped.
3. `人物展示` creates the captured two-sided inflated photo model and opens its rotation view.
4. `3D投掷` opens a four-item thumbnail picker on a long press. A tap launches the selected authored 3D prefab toward a tracked 2D person; after a short camera-aligned flight it stays attached to the same person-relative position and follows the live bounding box.

All four features share `PoseDetectionManager` and `PersonTrackingManager`. The throw path uses no AR anchors, measured body depth, AR planes, or world-space collision targeting. It uses the authored 3D throwable prefabs in `Resources/Throwables/Catalog.asset`, rendered in a camera-aligned virtual layer and attached using the 2D pose target.

The `停止识别` button stops capture and inference, clears all person tracks, hides the deformation surface and jelly stickers, exits photo showcase, and removes every attached screen projectile. `开始识别` starts a fresh tracking session.

The detector still uses RGB camera capture, letterboxed 416x416 YOLO input, COCO 17 keypoints, and the lightweight image-flow tracker. The photo showcase is built from the tracked 2D pose and camera crop; it does not use AR depth.

The throwable catalog includes a fifth jelly alien built from the supplied three-view reference. All five 3D models rebound on impact, flatten along the camera-normal axis and spread laterally as slime appears. Slime briefly flows downward and settles after 2.2 seconds. `PersonSlimeSurface` merges all puddles on each person in one bounded distance-field draw, with albedo-derived colors blended at overlaps. Stopping recognition clears both the models and their slime.

The showcase photo material uses a fixed side light in preview-camera coordinates. AR camera/gyroscope rotation does not change its lighting; dragging the model still changes its surface orientation relative to that light.

`ProjectileSlimeValidation.BuildAndValidate` builds the alien/material assets and verifies the five projectiles, impact/settle lifecycle, slime fusion on the GPU and lighting invariance. Three-view and slime progression previews are saved in `ArtPreviews/`.
