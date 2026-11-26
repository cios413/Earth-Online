using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using EarthOL.ARCore;
using EarthOL.BodyThrow;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using Debug = UnityEngine.Debug;

namespace EarthOL.EditorTools
{
    /// <summary>Behavioral regressions for the tracking/frozen-model lifecycle. Older validation entry points remain in the project.</summary>
    public static class PersonSystemValidation
    {
        const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        public static void Validate()
        {
            Pose2DTrackingValidation();
            PersonFacingValidation();
            PersonHeadJellyValidation();
            PersonShowcaseGeometryValidation();
            SegmentedShowcaseGeometryValidation();
            ReshapedShowcaseValidation();
            PillowCrossSectionValidation();
            Cartoon2DRigValidation();
            Person3DThrowValidation();
            BootstrapMigrationValidation();
            PersonDetectionPerformanceValidation();
            ValidateModelScheduling();
            Debug.Log("[PersonValidation] ALL PASS: 2D tracking/flow, sticker activation, inflated photo model, four-mode bootstrap and stop/reset lifecycle.");
        }

        public static void ValidateAndBuild()
        {
            Validate();
            UnityEditor.PlayerSettings.enableFrameTimingStats = true;
            AndroidBuild.BuildDevelopmentApk();
        }

        static void ReshapedShowcaseValidation()
        {
            const int size = 160;
            var photo = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            var alpha = new byte[pixels.Length];
            var pose = PersonPoseData.FromDetection(FullPose(), 0f);
            var limbs = new[,] { { 5, 7, 9 }, { 6, 8, 10 }, { 11, 13, 15 }, { 12, 14, 16 } };
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Vector2 uv = new Vector2(x + .5f, y + .5f) / size;
                bool body = uv.x > .405f && uv.x < .595f && uv.y > .43f && uv.y < .735f;
                bool head = Vector2.Distance(uv, new Vector2(.5f, .83f)) < .08f ||
                    (Mathf.Abs(uv.x - .5f) < .034f && uv.y > .72f && uv.y < .82f);
                for (int i = 0; i < 4; i++)
                for (int j = 0; j < 2; j++)
                {
                    Vector2 a = pose.keypoints[limbs[i, j]], b = pose.keypoints[limbs[i, j + 1]];
                    Vector2 segment = b - a;
                    float t = Mathf.Clamp01(Vector2.Dot(uv - a, segment) / segment.sqrMagnitude);
                    body |= Vector2.Distance(uv, a + t * segment) < .026f;
                }
                int index = y * size + x;
                alpha[index] = (byte)(body || head ? 255 : 0);
                if (Vector2.Distance(uv, new Vector2(.5f, .30f)) < .009f) alpha[index] = 255;
                pixels[index] = new Color32((byte)x, (byte)y, 120, 255);
            }
            photo.SetPixels32(pixels);
            photo.Apply();
            Texture2D composed = null;
            Mesh mesh = null;
            try
            {
                byte[] reshapedMask = null;
                PersonPoseData reshapedPose = null;
                var type = typeof(PersonShowcaseController).Assembly.GetType("EarthOL.BodyThrow.PersonShowcasePose");
                var compose = type.GetMethod("Compose", BindingFlags.NonPublic | BindingFlags.Static);
                Vector2[] limbDepth = null;
                Action<Texture2D, byte[], PersonPoseData, Vector2[]> complete = (texture, mask, result, depth) =>
                {
                    composed = texture;
                    reshapedMask = mask;
                    reshapedPose = result;
                    limbDepth = depth;
                };
                var crop = new Rect(0f, 0f, 1f, 1f);
                var operation = (IEnumerator)compose.Invoke(null,
                    new object[] { photo, alpha, pose, crop, .85f, .065f, 712, complete });
                while (operation.MoveNext()) { }
                Check(composed != null && reshapedPose != null, "A visible pose must produce a reshaped photo");
                var sizeInPixels = new Vector2(composed.width, composed.height);
                float sourceArm = Vector2.Distance(pose.keypoints[5], pose.keypoints[9]) * size;
                float reshapedArm = ((reshapedPose.keypoints[5] - reshapedPose.keypoints[9]) * sizeInPixels).magnitude;
                Check(reshapedArm > sourceArm * 1.10f, "A clear arm must lengthen");
                mesh = PersonPhotoSilhouetteBuilder.BuildFromMask(composed, reshapedMask, 1.45f, reshapedPose, crop, limbDepth);
                Check(mesh != null && mesh.subMeshCount == 2 && mesh.bounds.size.z > .01f,
                    "Reshaped photos must generate both inflated mesh surfaces");
                Check(CountAlphaComponents(composed.GetPixels32(), composed.width, composed.height) == 1,
                    "Reshaping must keep the hips attached and remove floating fragments");
                Debug.Log("[PersonValidation] ReshapedShowcaseValidation PASS");
            }
            finally
            {
                Object.DestroyImmediate(photo);
                if (composed != null) Object.DestroyImmediate(composed);
                if (mesh != null) Object.DestroyImmediate(mesh);
            }
        }

        public static BodyPoseResult FullPose(float x = 0.5f, float shift = 0f)
        {
            var points = new[]
            {
                new Vector2(x, .86f), new Vector2(x-.025f,.88f), new Vector2(x+.025f,.88f),
                new Vector2(x-.055f,.86f), new Vector2(x+.055f,.86f),
                new Vector2(x-.10f,.72f), new Vector2(x+.10f,.72f),
                new Vector2(x-.14f,.57f), new Vector2(x+.14f,.57f),
                new Vector2(x-.17f,.43f), new Vector2(x+.17f,.43f),
                new Vector2(x-.07f,.45f), new Vector2(x+.07f,.45f),
                new Vector2(x-.075f,.27f), new Vector2(x+.075f,.27f),
                new Vector2(x-.08f,.08f), new Vector2(x+.08f,.08f)
            };
            var scores = new float[17];
            for (int i = 0; i < 17; i++) { scores[i] = .95f; points[i].x += shift; }
            return new BodyPoseResult { screenRect = new Rect(x-.21f+shift,.04f,.42f,.90f), keypoints = points, keypointScores = scores, confidence = .95f };
        }

        public static void Pose2DTrackingValidation()
        {
            var go = new GameObject("TrackingRegression");
            try
            {
                var tracker = go.AddComponent<PersonTrackingManager>();
                tracker.UpdateDetections(new[] { FullPose(.27f), FullPose(.73f) }, 10f);
                Check(tracker.Count == 2 && tracker.Persons[0].personId != tracker.Persons[1].personId, "Two automatically tracked independent IDs");
                int left = tracker.Persons[0].personId, right = tracker.Persons[1].personId;
                tracker.UpdateDetections(new[] { FullPose(.73f, -.02f), FullPose(.27f,.02f) }, 10.2f);
                Check(tracker.Find(left).screenRect.center.x < .4f && tracker.Find(right).screenRect.center.x > .6f,
                    "Detection ordering must not swap person IDs");
                float before = tracker.Find(left).keypoints[9].x;
                tracker.Tick(10.25f); tracker.Tick(10.3f);
                Check(tracker.Find(left).keypoints[9].x > before, "Keypoints must advance between detections");
                tracker.UpdateDetections(new[] { FullPose(.73f,-.03f) }, 10.4f);
                Check(tracker.Find(left) != null, "Briefly missing person must retain ID");
                tracker.UpdateDetections(new[] { FullPose(.27f,.04f), FullPose(.73f,-.04f) }, 10.6f);
                Check(tracker.Count == 2 && tracker.Find(left) != null, "Returning person must recover ID");
                Check(tracker.Pick(new Vector2(.73f,.5f)) != null, "Any detected person can receive feature input");
                tracker.Tick(12f);
                Check(tracker.Count == 0 && tracker.Find(left).trackingConfidence == 0f,
                    "Temporarily missing tracks must remain available for re-identification");
                tracker.UpdateDetections(new[] { FullPose(.27f, -.01f), FullPose(.73f, .01f) }, 12.2f);
                Check(tracker.Find(left) != null && tracker.Find(left).personId == left,
                    "A returning person inside the re-identification window must retain their ID");
                tracker.PauseTracks(12.3f);
                tracker.ResumeTracks(120.3f);
                tracker.Tick(120.3f);
                Check(tracker.Find(left) != null && tracker.Find(left).personId == left,
                    "A recognition pause must not age out retained person IDs");
                tracker.Tick(181f);
                Check(tracker.Count == 0 && tracker.Find(left) == null, "Dormant tracks must expire after the re-identification window");

                var appearanceTracker = new GameObject("AppearanceReidentificationRegression").AddComponent<PersonTrackingManager>();
                try
                {
                    BodyPoseResult original = FullPose(.30f);
                    original.appearance = new float[64];
                    original.appearance[21] = 1f;
                    appearanceTracker.UpdateDetections(new[] { original }, 1f);
                    int originalPersonId = appearanceTracker.Persons[0].personId;
                    appearanceTracker.Tick(30f);
                    BodyPoseResult returning = FullPose(.79f);
                    returning.appearance = new float[64];
                    returning.appearance[21] = 1f;
                    appearanceTracker.UpdateDetections(new[] { returning }, 31f);
                    Check(appearanceTracker.Count == 1 && appearanceTracker.Persons[0].personId == originalPersonId,
                        "Matching clothing appearance must restore identity after long occlusion and large movement");
                    appearanceTracker.ClearPeople();
                    BodyPoseResult other = FullPose(.70f);
                    other.appearance = original.appearance;
                    appearanceTracker.UpdateDetections(new[] { original, other }, 40f);
                    int firstId = appearanceTracker.Persons[0].personId, secondId = appearanceTracker.Persons[1].personId;
                    BodyPoseResult ambiguous = FullPose(.50f);
                    ambiguous.appearance = original.appearance;
                    appearanceTracker.UpdateDetections(new[] { ambiguous }, 55f);
                    Check(appearanceTracker.Count == 1 && appearanceTracker.Persons[0].personId != firstId &&
                        appearanceTracker.Persons[0].personId != secondId,
                        "Ambiguous clothing matches must not transfer a retained identity");
                }
                finally { Object.DestroyImmediate(appearanceTracker.gameObject); }

                var panTracker = new GameObject("PanReidentificationRegression").AddComponent<PersonTrackingManager>();
                try
                {
                    panTracker.UpdateDetections(new[] { FullPose(.24f) }, 10f);
                    int id = panTracker.Persons[0].personId;
                    panTracker.UpdateDetections(new[] { FullPose(.77f) }, 10.4f);
                    Check(panTracker.Count == 1 && panTracker.Persons[0].personId == id,
                        "Camera pan with one recent matching pose must not spawn a duplicate");
                    panTracker.Tick(11.6f);
                    panTracker.UpdateDetections(new[] { FullPose(.65f) }, 12f);
                    Check(panTracker.Count == 1 && panTracker.Persons[0].personId == id,
                        "Briefly missing single person may return elsewhere in the view");
                }
                finally { Object.DestroyImmediate(panTracker.gameObject); }

                const int size = 64;
                var previous = new byte[size*size]; var current = new byte[size*size];
                var random = new System.Random(1234);
                for (int i=0;i<previous.Length;i++) previous[i]=(byte)random.Next(256);
                for(int y=0;y<size;y++) for(int x=0;x<size;x++)
                    current[y*size+x]=previous[Mathf.Clamp(y-1,0,size-1)*size+Mathf.Clamp(x-3,0,size-1)];
                Check(PersonKeypointFlow.TryTrack(previous,current,size,size,new Vector2(30f/63f,30f/63f),out var move,out float quality),
                    "Image patch tracking must recover textured motion");
                Check(Mathf.Abs(move.x-3f/63f)<.001f && Mathf.Abs(move.y-1f/63f)<.001f && quality>.8f,"Flow displacement in viewport units");
                for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                    current[y * size + x] = previous[y * size + Mathf.Max(0, x - 10)];
                Check(PersonKeypointFlow.TryTrack(previous, current, size, size, new Vector2(30f/63f,30f/63f),
                    out var expectedMove, out _, new Vector2(9f/63f,0f)) &&
                    Mathf.Abs(expectedMove.x-10f/63f)<.001f,
                    "Image flow search should follow the expected camera-pan displacement");
                for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                    current[y * size + x] = previous[y * size + Mathf.Max(0, x - 5)];
                Check(!PersonKeypointFlow.TryTrack(previous, current, size, size, new Vector2(30f / 63f, 30f / 63f), out _, out _),
                    "Patch matches at the search boundary must be rejected");
                var panProjection = Matrix4x4.Ortho(-1, 1, -1, 1, .1f, 10f);
                Vector2 projected = PersonTrackingManager.ReprojectPoint(panProjection.inverse * panProjection, new Vector2(.27f, .68f));
                Check(Vector2.Distance(projected, new Vector2(.27f, .68f)) < .001f,
                    "Camera view reprojection preserves the same viewport point");
                Debug.Log("[PersonValidation] Pose2DTrackingValidation PASS");
            }
            finally { Object.DestroyImmediate(go); }
        }

        public static void PersonFacingValidation()
        {
            var front=PersonPoseData.FromDetection(FullPose(),1f);
            Check(PersonFacingDetector.Estimate(front)==PersonFacing.Front,"Frontal face classification");
            var left=PersonPoseData.FromDetection(FullPose(),1f); left.keypoints[0].x-=.08f;
            Check(PersonFacingDetector.Estimate(left)==PersonFacing.Left,"Side face classification");
            var go=new GameObject("FacingRegression");
            try
            {
                var tracker=go.AddComponent<PersonTrackingManager>(); tracker.UpdateDetections(new[]{FullPose()},0f);
                var side=FullPose(); side.keypoints[0].x-=.12f;
                tracker.UpdateDetections(new[]{side},.2f);
                Check(tracker.Persons[0].facing==PersonFacing.Front,"One head turn must not flip the rig");
                for(int i=2;i<7;i++) tracker.UpdateDetections(new[]{side},i*.2f);
                Check(tracker.Persons[0].facing==PersonFacing.Left,"Sustained side view must change facing");
                Debug.Log("[PersonValidation] PersonFacingValidation PASS");
            }
            finally { Object.DestroyImmediate(go); }
        }

        public static void SegmentedShowcaseGeometryValidation()
        {
            var pose = PersonPoseData.FromDetection(FullPose(), 1f);
            pose.keypoints[9].x = pose.screenRect.xMax + pose.screenRect.width * .1f;
            Check(PersonShowcaseController.PhotoCropBounds(pose).xMax >= pose.keypoints[9].x,
                "Showcase crop includes a reliable wrist just outside the person box");
            const int size = 128;
            var photo = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Mesh mesh = null;
            try
            {
                var alpha = new byte[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(64, 64));
                    alpha[y * size + x] = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(Mathf.Min(distance - 14f, 52f - distance)));
                }

                mesh = PersonPhotoSilhouetteBuilder.BuildFromMask(photo, alpha, 1.45f);
                Check(mesh != null && mesh.subMeshCount == 2 && mesh.bounds.size.z > .042f,
                    "Segmented photo includes front and back volume");
                var vertices = mesh.vertices;
                var uvs = mesh.uv;
                int faceCount = vertices.Length / 2;
                int seam = 0;
                int fullDepthVertices = 0;
                float depthThreshold = mesh.bounds.size.z * .34f;
                for (int i = 0; i < faceCount; i++)
                {
                    var front = vertices[i];
                    Check(vertices[i + faceCount] == new Vector3(front.x, front.y, -front.z) && uvs[i] == uvs[i + faceCount],
                        "Segmented photo surfaces share matching geometry and UVs");
                    Check(Vector2.Distance(uvs[i], Vector2.one * .5f) > .09f, "Mask hole must remain open");
                    if (front.z == 0f) seam++;
                    if (front.z < -depthThreshold) fullDepthVertices++;
                }
                var joined = new HashSet<int>(mesh.GetTriangles(1));
                joined.RemoveWhere(i => i >= faceCount);
                Check(seam > 100 && joined.Count == seam, "Front and back share the zero-thickness rim");
                Check(fullDepthVertices > (faceCount - seam) * .08f,
                    "Pillow retains a broad filled interior around the open mask hole");
                Debug.Log($"[PersonValidation] SegmentedShowcaseGeometryValidation PASS: depth={mesh.bounds.size.z:F3}, seam={seam}");
            }
            finally
            {
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(photo);
            }
        }

        static void PillowCrossSectionValidation()
        {
            const int size = 160;
            var photo = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Mesh mesh = null;
            try
            {
                var alpha = new byte[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + .5f - 80f) / 25f;
                    float dy = (y + .5f - 80f) / 68f;
                    float coverage = Mathf.Clamp01((1f - dx * dx - dy * dy) * 5f);
                    alpha[y * size + x] = (byte)Mathf.RoundToInt(coverage * 255f);
                }
                mesh = PersonPhotoSilhouetteBuilder.BuildFromMask(photo, alpha, 1.45f);
                Check(mesh != null, "Tall person mask creates a cushion mesh");
                var vertices = mesh.vertices;
                var uvs = mesh.uv;
                int faceCount = vertices.Length / 2;
                float centerDepth = 0f, nearRimDepth = 0f, farRimDepth = 0f;
                int centerSamples = 0, nearSamples = 0, farSamples = 0;
                for (int i = 0; i < faceCount; i++)
                {
                    if (Mathf.Abs(uvs[i].x - .5f) > .035f) continue;
                    float v = uvs[i].y;
                    float depth = -vertices[i].z;
                    if (Mathf.Abs(v - .5f) < .05f) { centerDepth += depth; centerSamples++; }
                    if (Mathf.Abs(v - .20f) < .035f) { nearRimDepth += depth; nearSamples++; }
                    if (Mathf.Abs(v - .80f) < .035f) { farRimDepth += depth; farSamples++; }
                }
                Check(centerSamples > 0 && nearSamples > 0 && farSamples > 0,
                    "Tall mask samples center and both shoulder/hip cross sections");
                centerDepth /= centerSamples; nearRimDepth /= nearSamples; farRimDepth /= farSamples;
                Debug.Log($"[PersonValidation] Pillow section depth center={centerDepth:F3}, lower={nearRimDepth:F3}, upper={farRimDepth:F3}");
                Check(centerDepth > nearRimDepth * 1.10f && centerDepth > farRimDepth * 1.10f,
                    "Cushion center is fuller than its narrower upper and lower sections");
                float SampleDepth(Mesh sample, Vector2 uv)
                {
                    var points = sample.uv;
                    var geometry = sample.vertices;
                    int nearest = 0;
                    for (int i = 1; i < points.Length / 2; i++)
                        if ((points[i] - uv).sqrMagnitude < (points[nearest] - uv).sqrMagnitude) nearest = i;
                    return -geometry[nearest].z;
                }

                float middleDepth = SampleDepth(mesh, new Vector2(.5f, .5f));
                float halfwayDepth = SampleDepth(mesh, new Vector2(.425f, .5f));
                Check(halfwayDepth > middleDepth * .58f && halfwayDepth < middleDepth * .88f,
                    "Halfway from the rim, the convex section is already substantially filled");
                var pose = new PersonPoseData { keypoints = new Vector2[17], keypointConfidence = new float[17] };
                pose.keypoints[7] = new Vector2(.425f, .35f);
                pose.keypoints[9] = new Vector2(.425f, .65f);
                pose.keypointConfidence[7] = pose.keypointConfidence[9] = .9f;
                var guided = PersonPhotoSilhouetteBuilder.BuildFromMask(photo, alpha, 1.45f, pose, new Rect(0, 0, 1, 1));
                try
                {
                    Check(SampleDepth(guided, new Vector2(.425f, .5f)) > halfwayDepth * 1.12f &&
                        SampleDepth(guided, new Vector2(.425f, .5f)) > SampleDepth(guided, new Vector2(.575f, .5f)),
                        "A reliable off-center bone guides local fullness");
                }
                finally { Object.DestroyImmediate(guided); }
                Check(mesh.bounds.size.z <= .135f, "Cushion side thickness remains modest");
                Debug.Log($"[PersonValidation] PillowCrossSectionValidation PASS: center={centerDepth:F3}, shoulders={nearRimDepth:F3}, hips={farRimDepth:F3}");
            }
            finally
            {
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(photo);
            }
        }

        public static void PersonShowcaseGeometryValidation()
        {
            var template = Resources.Load<Material>("PersonShowcase");
            var shader = template != null ? template.shader : null;
            Check(shader != null && shader.isSupported && shader.name == "EarthOL/ShowcasePhoto",
                "Inflated doll surfaces use fixed side lighting in the showcase camera view");
            const int width = 128, height = 256;
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++) pixels[y * width + x] = new Color32((byte)(x * 2), (byte)y, 110, 255);
            var photo = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            photo.SetPixels32(pixels);
            photo.Apply(false, false);
            Mesh mesh = null;
            Texture2D backPhoto = null;
            try
            {
                var pose = PersonPoseData.FromDetection(FullPose(), 1f);
                mesh = PersonPhotoSilhouetteBuilder.Build(pose, photo, new Rect(.25f, .02f, .50f, .92f));
                backPhoto = PersonPhotoSilhouetteBuilder.CreateBackTexture(photo);
                int gridX = Mathf.Clamp(width / 8, 48, 96), gridY = Mathf.Clamp(height / 8, 64, 144);
                int row = gridX + 1;
                int faceVertices = row * (gridY + 1);
                Check(mesh != null && mesh.vertexCount == faceVertices * 2,
                    "First-version sampling density must create only two matching curved photo surfaces");
                Check(mesh.subMeshCount == 2 && mesh.GetTriangles(0).Length > 3000 && mesh.GetTriangles(1).Length > 3000 &&
                    mesh.uv.Length == mesh.vertexCount && mesh.normals.Length == mesh.vertexCount,
                    "Both curved photo surfaces must have triangles, UVs and normals");
                Vector3[] vertices = mesh.vertices;
                Vector2[] uvs = mesh.uv;
                var masked = photo.GetPixels32();
                byte bodyAlpha = 0;
                for (int y = 96; y < 208; y++)
                    for (int x = 24; x < 104; x++) bodyAlpha = (byte)Mathf.Max(bodyAlpha, masked[y * width + x].a);
                Check(bodyAlpha > 240, "Person torso remains opaque in the photo mask (max alpha=" + bodyAlpha + ")");
                Check(CountAlphaComponents(masked, width, height) == 1, "Photo silhouette must form one continuous rag-doll shape");
                byte cornerAlpha = masked[(height - 1) * width + (width - 1)].a;
                Check(cornerAlpha < 8, "Photo background is removed outside the body contour (corner alpha=" + cornerAlpha + ")");
                var backPixels = backPhoto.GetPixels32();
                for (int i = 0; i < masked.Length; i += 97)
                {
                    Color32 front = masked[i], back = backPixels[i];
                    Check(back.r == front.r && back.g == front.g && back.b == front.b && back.a == front.a,
                        "Back photo must be a pixel-identical copy of the front photo, without mirroring");
                }

                int seamVertices = 0;
                float maximumBulge = 0f;
                float minimumSeamAlpha = 1f;
                for (int y = 0; y <= gridY; y++)
                for (int x = 0; x <= gridX; x++)
                {
                    int index = y * row + x;
                    Vector3 front = vertices[index], back = vertices[faceVertices + index];
                    Check(Mathf.Abs(front.x - back.x) < .0001f && Mathf.Abs(front.y - back.y) < .0001f &&
                        Mathf.Abs(front.z + back.z) < .0001f, "Front/back surfaces must be symmetric and share the same outline");
                    Check(Mathf.Abs(uvs[index].x - uvs[faceVertices + index].x) < .0001f &&
                        Mathf.Abs(uvs[index].y - uvs[faceVertices + index].y) < .0001f,
                        "Both surfaces must share UVs and image orientation");
                    maximumBulge = Mathf.Max(maximumBulge, Mathf.Abs(front.z));
                    float regularU = x / (float)gridX, regularV = y / (float)gridY;
                    if (Mathf.Abs(uvs[index].x - regularU) < .0001f && Mathf.Abs(uvs[index].y - regularV) < .0001f) continue;
                    seamVertices++;
                    Check(Mathf.Abs(front.z) < .0001f && Mathf.Abs(back.z) < .0001f,
                        "The two curved faces must meet at zero thickness around their shared silhouette");
                    minimumSeamAlpha = Mathf.Min(minimumSeamAlpha, photo.GetPixelBilinear(uvs[index].x, uvs[index].y).a,
                        backPhoto.GetPixelBilinear(uvs[faceVertices + index].x, uvs[faceVertices + index].y).a);
                }
                Check(maximumBulge > .04f, "The two photo surfaces must visibly inflate away from the silhouette");
                Check(seamVertices > 100 && minimumSeamAlpha > .90f,
                    "Shared seam must stretch opaque edge pixels on both photo faces (vertices=" + seamVertices + ", minAlpha=" + minimumSeamAlpha + ")");
                Debug.Log($"[PersonValidation] PersonShowcaseGeometryValidation PASS: grid={gridX}x{gridY}, vertices={mesh.vertexCount}, triangles={mesh.triangles.Length / 3}, seamAlpha={minimumSeamAlpha:F2}");
            }
            finally
            {
                if (mesh != null) Object.DestroyImmediate(mesh);
                if (backPhoto != null) Object.DestroyImmediate(backPhoto);
                Object.DestroyImmediate(photo);
            }
        }

        public static void PersonHeadJellyValidation()
        {
            var material = Resources.Load<Material>("CartoonJelly/ConnectedJelly");
            var skull = Resources.Load<Texture2D>("CartoonJelly/Skull");
            Check(material != null && material.shader != null && material.shader.name == "EarthOL/UI/ConnectedJellySkeleton" &&
                material.shader.isSupported && !UnityEditor.ShaderUtil.ShaderHasError(material.shader),
                "Head jelly shader compiles");
            Check(skull != null, "Head jelly has a skull pattern texture");
            var root = new GameObject("Translucent Skull Artwork Validation");
            try
            {
                Sprite sprite = Resources.Load<Sprite>("CartoonJelly/Skull");
                bool ownsSprite = sprite == null;
                if (ownsSprite) sprite = Sprite.Create(skull, new Rect(0, 0, skull.width, skull.height), Vector2.one * .5f);
                var part = root.AddComponent<Cartoon2DPart>();
                part.Configure(root.transform, sprite, Color.white, true);
                part.SetHead(Vector2.zero, Vector2.zero, 128f, 1f, false);
                var image = (Image)typeof(Cartoon2DPart).GetField("m_Image", Private).GetValue(part);
                Check(image.enabled && Mathf.Abs(image.color.a - .16f) < .001f,
                    "Original skull Image remains visible with reduced alpha so the camera face shows through");
                part.SetVisible(true);
                Check(image.enabled && Mathf.Abs(image.color.a - .16f) < .001f,
                    "Rig visibility updates must not restore the skull Image to opaque");
                if (ownsSprite) Object.DestroyImmediate(sprite);
            }
            finally { Object.DestroyImmediate(root); }
            Debug.Log("[PersonValidation] PersonHeadJellyValidation PASS: enlarged head hood, 16% skull artwork overlay, transparent center and opaque rim.");
        }

        public static void Cartoon2DRigValidation()
        {
            var root=new GameObject("CartoonRegression");
            try
            {
                var camera=root.AddComponent<Camera>(); camera.enabled=false; camera.pixelRect=new Rect(0,0,1000,1000);
                var tracking=root.AddComponent<PersonTrackingManager>(); tracking.UpdateDetections(new[]{FullPose()},Time.realtimeSinceStartup);
                var cartoons=root.AddComponent<Cartoon2DCanvas>(); cartoons.Configure(camera,tracking); Canvas.ForceUpdateCanvases();
                cartoons.Refresh(); Check(cartoons.RigCount==0,"A person sticker appears only after a tap");
                var click=root.AddComponent<PersonClickController>(); click.Configure(camera,tracking,cartoons);
                int inferBefore=root.GetComponents<PoseDetectionManager>().Length;
                Vector2 tap = camera.ViewportToScreenPoint(new Vector3(.5f,.5f,0));
                Check(click.TryActivateAt(tap) && cartoons.RigCount==1 && cartoons.IsPersonActivated(tracking.Persons[0].personId),
                    "A 2D person tap activates the cartoon sticker without an AR anchor or depth estimate");
                Check(!click.TryActivateAt(tap),"Rapid repeated taps must not reroll the selected sticker");
                var overlay=(Canvas)typeof(Cartoon2DCanvas).GetField("m_Canvas",Private).GetValue(cartoons);
                Check(overlay!=null && overlay.renderMode==RenderMode.ScreenSpaceOverlay,"Canvas must be Screen Space Overlay");
                var rig=overlay.GetComponentInChildren<Cartoon2DRig>();
                Check(rig.GetComponentsInChildren<Image>().Length==10,"Ten cartoon image body parts");
                var before=rig.transform.position;
                tracking.UpdateDetections(new[]{FullPose(.5f,.04f)},Time.realtimeSinceStartup+.2f); cartoons.Refresh();
                Check(rig.transform.position.x>before.x,"Selected 2D sticker follows its live person track");
                Check(root.GetComponents<PoseDetectionManager>().Length==inferBefore,"Clicking a sticker must not instantiate another detector");
                tracking.ClearPeople(); cartoons.Refresh(); Check(cartoons.RigCount==0,"Lost/stopped tracks must hide cartoons");
                cartoons.ClearSelection();
                Check(cartoons.SelectedPersonCount==0,"Stopping recognition clears selected sticker state");
                Debug.Log("[PersonValidation] Cartoon2DRigValidation/2D sticker tap PASS");
            }
            finally { Object.DestroyImmediate(root); }
        }

        public static void Person3DThrowValidation()
        {
            var root = new GameObject("Person2DThrowValidation");
            try
            {
                var camera = root.AddComponent<Camera>();
                camera.enabled = false;
                camera.pixelRect = new Rect(0f, 0f, 1000f, 1000f);
                var tracking = root.AddComponent<PersonTrackingManager>();
                tracking.UpdateDetections(new[] { FullPose() }, Time.realtimeSinceStartup);
                var catalog = Resources.Load<BodyProjectileCatalog>("Throwables/Catalog");
                Check(catalog != null && catalog.entries.Length >= 5, "Existing throwable choices and the jelly alien must load");
                foreach (var entry in catalog.entries) Check(entry.preview != null, "Each throwable needs a 2D thumbnail");

                var thrower = root.AddComponent<Person3DThrowController>();
                thrower.Configure(camera, tracking, catalog);
                thrower.SetInteractionEnabled(true);
                Check(thrower.SelectProjectile(3) && thrower.SelectedProjectileIndex == 3, "Long-press picker can select a projectile");
                Check(!thrower.SelectProjectile(catalog.entries.Length), "Picker rejects an out-of-range choice");
                Vector2 target = camera.ViewportToScreenPoint(new Vector3(.5f, .5f, 0f));
                Check(thrower.TryThrowAt(target) && thrower.ActiveProjectileCount == 1,
                    "A tap on a detected 2D person launches the selected screen projectile");

                var thrown = GameObject.Find("3D Thrown 4号 · 箭头");
                Check(thrown != null && thrown.GetComponentInChildren<Renderer>() != null,
                    "Thrown items must instantiate the authored 3D prefab");
                Check(thrown.GetComponentInChildren<Rigidbody>() == null ||
                    thrown.GetComponentInChildren<Rigidbody>().isKinematic,
                    "Virtual screen throws do not use active world physics");

                Transform model = thrown.transform.GetChild(0);
                Quaternion launchRotation = model.localRotation;
                Vector3 targetLocal = camera.transform.InverseTransformPoint(
                    camera.ViewportToWorldPoint(new Vector3(.5f, .5f, 1.20f)));
                Vector3 flightDirection = (targetLocal - thrown.transform.localPosition).normalized;
                Check(Vector3.Dot(model.localRotation * Vector3.forward, flightDirection) > .999f,
                    "Projectile tip must initially point toward the targeted person");
                for (int step = 0; step < 5; step++)
                {
                    Invoke(thrower, "UpdateThrownItems", .1f);
                    Check(Quaternion.Angle(launchRotation, model.localRotation) < .01f,
                        "Flight must preserve launch orientation without spinning or snapping on attachment");
                }
                Check(thrower.AttachedProjectileCount == 1, "Projectile flight ends attached to the tracked person");
                float before = thrown.transform.position.x;
                tracking.UpdateDetections(new[] { FullPose(.5f, .05f) }, Time.realtimeSinceStartup + .2f);
                Invoke(thrower, "UpdateThrownItems", 1f / 60f);
                Check(thrown.transform.position.x > before + .001f, "Attached 3D projectile follows its live 2D person track");
                Check(Quaternion.Angle(launchRotation, model.localRotation) < .01f,
                    "Attached projectile must retain its impact orientation while following the person");
                thrower.ClearEffects();
                Check(thrower.ActiveProjectileCount == 0 && GameObject.Find("3D Thrown 4号 · 箭头") == null,
                    "Stopping detection can clear every attached 3D projectile");
                Check(thrower.SlimeSurfaceCount == 0, "Stopping detection clears all merged slime surfaces");
                Debug.Log("[PersonValidation] Person3DThrowValidation PASS: selection, authored 3D prefab flight, 2D-pose attachment, tracking follow and reset.");
            }
            finally { Object.DestroyImmediate(root); }
        }

        public static void BootstrapMigrationValidation()
        {
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            try
            {
                var bootstrap=new GameObject("BootstrapMigrationTest").AddComponent<ARCoreMainSceneBootstrap>();
                Check(!(bool)typeof(ARCoreMainSceneBootstrap).GetField("enableAccountSystem",Private).GetValue(bootstrap),
                    "Account server integration is retained but disabled by default for offline testing");
                var start=(IEnumerator)Invoke(bootstrap,"Start"); start.MoveNext();
                Check(bootstrap.DetectionManager==null && !bootstrap.SessionActive,"Entry menu waits before creating recognition");
                Set(bootstrap,"startPersonDetection",false);
                bootstrap.EnterApp(AppMode.Free);
                while(start.MoveNext()) { }
                Check(bootstrap.DetectionManager!=null && bootstrap.PersonTracking!=null && bootstrap.CartoonCanvas!=null &&
                    bootstrap.ClickController!=null && bootstrap.ShowcaseController!=null && bootstrap.ThrowController!=null,
                    "Bootstrap creates the four 2D features around one detector/tracker");
                Check(GameObject.Find("Soft Cube")==null && GameObject.Find("Soft Sphere")==null,"No demo geometry");
                bootstrap.PersonTracking.UpdateDetections(new[]{FullPose()},Time.realtimeSinceStartup);
                int suspendedId = bootstrap.PersonTracking.Persons[0].personId;
                bootstrap.CartoonCanvas.ActivatePerson(suspendedId);
                bootstrap.DetectionManager.StopDetection();
                Check(!bootstrap.DetectionManager.DetectionEnabled && bootstrap.PersonTracking.Count==0 &&
                    bootstrap.PersonTracking.Find(suspendedId)!=null &&
                    bootstrap.CartoonCanvas.SelectedPersonCount==0 && bootstrap.ThrowController.ActiveProjectileCount==0,
                    "Stopping recognition suspends tracks while resetting transient feature effects");
                foreach(GameMode mode in new[]{GameMode.Deform,GameMode.Sticker,GameMode.Showcase,GameMode.Throw})
                {
                    bootstrap.SetMode(mode);
                    Check(bootstrap.CurrentMode==mode,"All four modes must be independently selectable");
                }
                Check(!bootstrap.DetectionManager.DetectionEnabled,"Mode switching must not restart stopped recognition");
                Debug.Log("[PersonValidation] BootstrapMigrationValidation/four modes and stop reset PASS");
            }
            finally { foreach(var go in scene.GetRootGameObjects())if(go!=null)Object.DestroyImmediate(go); }
        }

        public static void PersonDetectionPerformanceValidation()
        {
            var root=new GameObject("PerformanceRegression");
            try
            {
                var tracking=root.AddComponent<PersonTrackingManager>(); var manager=root.AddComponent<PoseDetectionManager>();
                var camera=root.AddComponent<Camera>(); camera.enabled=false; manager.Configure(camera,tracking);
                manager.DetectionFramesPerSecond=10f;
                Check(Mathf.Abs(manager.DetectionFramesPerSecond-10)<.001f,"Inference Hz configurable");
                manager.StartDetection(); Check(manager.DetectionEnabled,"Start Detection enables algorithms");
                Set(manager,"m_NextDetection",12.2f);
                Check(!manager.ShouldStartInference(12.1f) && manager.ShouldStartInference(12.2f),"Inference cap enforced independent of render frames");
                tracking.UpdateDetections(new[]{FullPose()},Time.realtimeSinceStartup);
                int originalId=tracking.Persons[0].personId;
                manager.StopDetection();
                Check(tracking.Count==0 && tracking.Find(originalId)!=null,
                    "Stopping detection must suspend rather than discard person tracks");
                int detection=manager.DetectionCount, capture=manager.CaptureCount,tensor=manager.TensorInputCount,ticks=tracking.TrackingUpdateCount,flow=tracking.FlowUpdateCount;
                for(int i=0;i<30;i++)Invoke(manager,"Update");
                Check(manager.DetectionCount==detection && manager.CaptureCount==capture && manager.TensorInputCount==tensor &&
                    tracking.TrackingUpdateCount==ticks && tracking.FlowUpdateCount==flow,"Stop must halt capture, tensors, inference, decode, tracking and flow");
                manager.StartDetection(); Check(manager.DetectionEnabled,"Restart resumes detection");
                tracking.UpdateDetections(new[]{FullPose()},Time.realtimeSinceStartup);
                Check(tracking.Count==1 && tracking.Find(originalId)!=null,
                    "Restarting detection must resume the existing track and person ID");
                manager.StopDetection();
                // Real execution timing of the lightweight update path, not an inferred speed claim.
                tracking.UpdateDetections(new[]{FullPose(.3f),FullPose(.7f)},10f);
                long memory=GC.GetAllocatedBytesForCurrentThread(); var stopwatch=Stopwatch.StartNew();
                for(int i=0;i<1000;i++)tracking.Tick(10f+i*.0005f);
                stopwatch.Stop(); long allocated=GC.GetAllocatedBytesForCurrentThread()-memory;
                Debug.Log($"[PersonValidation] Tracking benchmark actual desktop elapsed={stopwatch.Elapsed.TotalMilliseconds:F3}ms/1000 updates allocated={allocated}B (includes Stopwatch setup).");
                Debug.Log("[PersonValidation] PersonDetectionPerformanceValidation PASS; target CPU/GPU profiling requires development APK.");
            }
            finally { Object.DestroyImmediate(root); }
        }

        static void ValidateModelScheduling()
        {
            // Reuse the existing decoder/model regressions because its scheduling and input-buffer path changed.
            BodyPoseValidation.Validate();
            using (var detector=new BodyPoseDetector())
            {
                var load=detector.Load(); while(load.MoveNext()) { }
                Check(detector.Ready,"Scheduled detector must load the existing raw model");
                bool callback=false; int steps=0;
                var pixels=new Color32[192*384];
                var schedule=detector.DetectScheduled(pixels,192,384,6,results=>
                {
                    Check(results.Length==0,"Blank portrait input must not become a tracked person"); callback=true;
                });
                var timer=Stopwatch.StartNew();
                while (schedule.MoveNext())
                {
                    steps++;
                    if (SystemInfo.supportsAsyncGPUReadback)
                        UnityEngine.Rendering.AsyncGPUReadback.WaitAllRequests();
                    Check(timer.ElapsedMilliseconds < 10000, "Scheduled pose inference timed out");
                }
                timer.Stop();
                Check(callback && steps>=1,"Inference must execute via a yielded schedule, not a hidden per-frame full Execute");
                Debug.Log($"[PersonValidation] Existing decoder tests and actual scheduled raw-model execution PASS: yields={steps} desktopWall={timer.Elapsed.TotalMilliseconds:F1}ms.");
            }
        }

        static object Invoke(object target,string method,params object[] args)=>target.GetType().GetMethod(method,Private).Invoke(target,args);
        static void Set(object target,string field,object value)=>target.GetType().GetField(field,Private).SetValue(target,value);
        static int CountAlphaComponents(Color32[] pixels, int width, int height)
        {
            var visited = new bool[pixels.Length];
            var queue = new Queue<int>();
            int components = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (visited[i] || pixels[i].a < 128) continue;
                components++;
                queue.Enqueue(i);
                visited[i] = true;
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    int x = current % width, y = current / width;
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                        int next = ny * width + nx;
                        if (visited[next] || pixels[next].a < 128) continue;
                        visited[next] = true;
                        queue.Enqueue(next);
                    }
                }
            }
            return components;
        }
        static void Check(bool passed,string message){if(!passed)throw new InvalidOperationException("[PersonValidation] FAIL: "+message);}
    }
}
