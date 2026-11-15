# Jelly skeleton 2D art

The selected live 2D cartoon now uses original warm-ivory skull, rib cage/pelvis
and long-bone PNGs wrapped in a single glossy, translucent candy silhouette.
Design direction follows the supplied skull/skeleton and jelly/color references;
no reference-image pixels or external model/art assets are included.

## Assets and layers

Runtime sprites in `Assets/Resources/CartoonJelly`:

- `Skull.png`: rounded skull, large soft purple eye sockets, heart-like nose,
  separated jaw, simplified teeth and subtle ivory highlights.
- `RibcagePelvis.png`: three looping ribs per side, spine, clavicles and pelvis.
  The exported sprite runs left-to-right from shoulders to hips for the existing rig.
- `SingleBone.png`: upper-arm/thigh bone.
- `PairedBone.png`: forearm/shin bones.
- `ConnectedJelly.mat` and `Assets/Shaders/CartoonJellyShell.shader`: the separate
  recolorable translucent outer envelope.

The original PNG authoring script is `Tools/generate_jelly_skeleton_art.py`
(Python + Pillow). It generates supersampled transparent artwork. The asset import
validation uses FullRect sprites, no mipmaps, Clamp wrapping and shared asset
references; no PNG or texture is generated per frame or per tracked person.

The envelope has ONE bounded RawImage/material per rig. It blends capsule-like
head, neck, torso and limb fields into one signed-distance silhouette. This avoids
rectangular stickers, visible elbow/knee seams and darker alpha where segments
overlap. It does not use GrabPass, screen copying, a camera blur, extra render
textures or 3D geometry. Each person has its own material and two reusable arrays
of segment parameters, updated from the same smoothed 2D positions as the bones.

With all COCO points available, 11 fields (head, neck, torso and 8 limb segments)
form a connected outline. Missing/low-confidence body parts remain hidden rather
than inventing anatomical connections. A fully occluded or lost person still uses
the existing tracking hide/recovery policy. Shader cost scales with the person's
on-screen area and number of people; final mobile GPU time is not certified by
desktop previews alone.

## Two-group randomized palette

`JellySkeletonPalette` contains eight candy hues: pink, mint/teal, violet, orange,
lime, blue, rose and yellow. On each valid click accepted by `PersonClickController`,
`Cartoon2DCanvas.ActivatePerson` selects two different colors:

- one color for BOTH head and torso (and connecting neck);
- one color for ALL four limbs, shared at joints.

The groups blend only at shoulder/hip junctions, inside the same continuous outer
shell. Bone PNGs keep their original ivory color. Palette is retained per Person ID
between frame updates, temporary hiding and rig pool reuse; assigning a pooled rig
to another person applies the other person's palette. Re-clicking a valid target
rerolls the colors, with repeat-pair rejection. The existing 1.5-second click
debounce still prevents repeated rapid clicks from spawning many frozen models.

The shell is wider than the tracked skeleton: torso shoulder radius is about 59%
of shoulder spacing, tapering to 49% at the hips, arms about 19%, legs about 23.5%,
plus smooth shoulder/hip/neck joins. The head hood is about 1.28 times the sprite
frame diameter; the skull artwork has its own transparent margin inside that frame.
These are stylized proportions, not a precise real-person segmentation mask.

## Preview and verification

Unity menu `Tools > Earth OL > Validate jelly skeleton artwork` imports the art,
renders real UI/material GPU previews, checks randomized palettes and tracking
stability, verifies one continuous non-background silhouette via flood fill,
checks bent joints/mirroring and pool reassignment, then writes:

`Assets/BodyThrow/ArtPreviews/JellySkeletonContactSheet.png`

Individual PNG previews show a standing pink/mint character, a bent violet/orange
character and a lime/rose character. `ArtworkSheet.png` shows the source bone PNGs.
These previews use synthetic COCO poses to assess the artwork; they are not a
claim of measured live-camera overlay accuracy on a phone.

Actual Unity GPU validation passed for all three variants: each rendered silhouette
has exactly one connected component above the antialiasing-noise threshold, with
132,345 / 135,728 / 132,357 visible pixels at 640x960. The generated shader compiled
and rendered successfully in the current Unity editor. Additional checks passed
for per-click reroll, palette retention during movement, hiding both layers on
loss, matching-ID recovery, and pooled-rig reassignment. The current phone's
mobile GPU time has not been remeasured for this art change.

The existing 3D frozen-person builder/materials/Anchor path are not changed by this
art replacement. Rebuild/install an APK to see the new 2D appearance on a phone;
an already-installed APK cannot read changes made to the Unity source folder.
