using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace EarthOL.BodyThrow
{
    public static class PersonPhotoSilhouetteBuilder
    {
        static readonly int[,] SkeletonLinks =
        {
            { 0, 1 }, { 0, 2 }, { 1, 3 }, { 2, 4 },
            { 5, 6 }, { 5, 7 }, { 7, 9 }, { 6, 8 }, { 8, 10 },
            { 5, 11 }, { 6, 12 }, { 11, 12 }, { 11, 13 }, { 13, 15 },
            { 12, 14 }, { 14, 16 }
        };

        struct SkeletonSegment
        {
            public Vector2 start, direction;
            public float inverseLengthSquared;

            public SkeletonSegment(Vector2 start, Vector2 end)
            {
                this.start = start;
                direction = end - start;
                inverseLengthSquared = 1f / Mathf.Max(.000001f, direction.sqrMagnitude);
            }
        }

        public static Mesh BuildFromMask(Texture2D photo, byte[] alpha, float modelHeight)
        {
            return BuildFromMask(photo, alpha, modelHeight, null, default);
        }

        public static Mesh BuildFromMask(Texture2D photo, byte[] alpha, float modelHeight,
            PersonPoseData pose, Rect photoBounds)
        {
            if (photo == null || alpha == null || alpha.Length != photo.width * photo.height) return null;
            Color32[] pixels = photo.GetPixels32();
            for (int i = 0; i < pixels.Length; i++) pixels[i].a = alpha[i];
            photo.SetPixels32(pixels);
            photo.Apply(false, false);

            const float contour = 0.50f;
            int gridX = Mathf.Clamp(photo.width * 2 / 3, 32, 192);
            int gridY = Mathf.Clamp(photo.height * 2 / 3, 48, 256);
            int row = gridX + 1;
            int count = row * (gridY + 1);
            float width = modelHeight * photo.width / photo.height;
            float stepX = width / gridX;
            float stepY = modelHeight / gridY;
            float diagonal = Mathf.Sqrt(stepX * stepX + stepY * stepY);
            float deepest = 0f;
            var opacity = new float[count];
            var distance = new float[count];
            var positions = new Vector3[count];
            var coordinates = new Vector2[count];
            for (int y = 0; y <= gridY; y++)
            for (int x = 0; x <= gridX; x++)
            {
                int i = y * row + x;
                float u = x / (float)gridX, v = y / (float)gridY;
                coordinates[i] = new Vector2(u, v);
                positions[i] = new Vector3((u - 0.5f) * width, (v - 0.5f) * modelHeight, 0f);
                if (x > 0 && x < gridX && y > 0 && y < gridY)
                    opacity[i] = SampleAlpha(alpha, photo.width, photo.height, u, v);
                distance[i] = opacity[i] > contour ? float.PositiveInfinity : 0f;
            }

            // Distance from the silhouette gives the torso more depth than thin limbs.
            for (int y = 1; y < gridY; y++)
            for (int x = 1; x < gridX; x++)
            {
                int i = y * row + x;
                distance[i] = Mathf.Min(distance[i], Mathf.Min(distance[i - 1] + stepX, distance[i - row] + stepY));
                distance[i] = Mathf.Min(distance[i], Mathf.Min(distance[i - row - 1], distance[i - row + 1]) + diagonal);
            }
            for (int y = gridY - 1; y > 0; y--)
            for (int x = gridX - 1; x > 0; x--)
            {
                int i = y * row + x;
                distance[i] = Mathf.Min(distance[i], Mathf.Min(distance[i + 1] + stepX, distance[i + row] + stepY));
                distance[i] = Mathf.Min(distance[i], Mathf.Min(distance[i + row - 1], distance[i + row + 1]) + diagonal);
                deepest = Mathf.Max(deepest, distance[i]);
            }

            float halfDepth = Mathf.Min(modelHeight * .056f, deepest * .56f) * .75f;
            float edgeOffset = .5f * Mathf.Min(stepX, stepY);
            var skeleton = BuildSkeleton(pose, photoBounds, width, modelHeight, alpha, photo.width, photo.height);
            float maxInset = Mathf.Max(stepY, deepest - edgeOffset);
            for (int y = 1; y < gridY; y++)
            for (int x = 1; x < gridX; x++)
            {
                int i = y * row + x;
                if (opacity[i] <= contour) continue;
                Vector2 point = positions[i];
                float skeletonDistanceSquared = float.PositiveInfinity;
                foreach (var segment in skeleton)
                {
                    Vector2 offset = point - segment.start;
                    float t = Mathf.Clamp01(Vector2.Dot(offset, segment.direction) * segment.inverseLengthSquared);
                    skeletonDistanceSquared = Mathf.Min(skeletonDistanceSquared, (offset - segment.direction * t).sqrMagnitude);
                }

                float borderDistance = Mathf.Max(0f, distance[i] - edgeOffset);
                float inset = Mathf.Clamp01(borderDistance / maxInset);
                float fullness = inset * (2f - inset);
                if (skeleton.Count > 0)
                {
                    float localWidth = borderDistance + Mathf.Sqrt(skeletonDistanceSquared);
                    float localInset = Mathf.Clamp01(borderDistance / Mathf.Max(stepY, localWidth));
                    float localDepth = .68f + .32f * Mathf.Sqrt(Mathf.Clamp01(localWidth / maxInset));
                    float guided = localInset * (2f - localInset) * localDepth;
                    fullness = Mathf.Lerp(fullness, guided, .55f);
                }
                float angle = Mathf.Atan2(point.y, point.x);
                float fold = .5f - .5f * Mathf.Cos(angle * 12f + .18f * Mathf.Sin(angle * 3f));
                float crease = .045f * fold * Mathf.Exp(-distance[i] / (stepY * 3.5f));
                positions[i].z = -halfDepth * fullness * (1f - crease);
            }

            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var photoUvs = new List<Vector2>();
            var front = new List<int>();
            var indices = new Dictionary<(int, int), int>();
            var polygon = new int[4];

            int Vertex(int a, int b)
            {
                var key = a < b ? (a, b) : (b, a);
                if (indices.TryGetValue(key, out int index)) return index;
                float t = a == b ? 0f : Mathf.InverseLerp(opacity[a], opacity[b], contour);
                Vector3 position = Vector3.Lerp(positions[a], positions[b], t);
                if (a != b) position.z = 0f;
                index = vertices.Count;
                vertices.Add(position);
                uvs.Add(Vector2.Lerp(coordinates[a], coordinates[b], t));
                // Sample just inside the geometric contour to reduce background color on the join.
                // Keep the inset small so the photo does not jump between adjacent grid vertices.
                Vector2 uv = uvs[index];
                Vector2 inside = coordinates[opacity[a] >= opacity[b] ? a : b];
                photoUvs.Add(Vector2.MoveTowards(uv, inside, .75f / Mathf.Max(photo.width, photo.height)));
                indices.Add(key, index);
                return index;
            }

            void ClipTriangle(int a, int b, int c)
            {
                int size = 0;
                void Edge(int from, int to)
                {
                    bool inside = opacity[from] > contour;
                    if (inside) polygon[size++] = Vertex(from, from);
                    if (inside != (opacity[to] > contour)) polygon[size++] = Vertex(from, to);
                }
                Edge(a, b);
                Edge(b, c);
                Edge(c, a);
                for (int i = 1; i + 1 < size; i++)
                {
                    front.Add(polygon[0]);
                    front.Add(polygon[i]);
                    front.Add(polygon[i + 1]);
                }
            }

            for (int y = 0; y < gridY; y++)
            for (int x = 0; x < gridX; x++)
            {
                int i = y * row + x;
                ClipTriangle(i, i + row + 1, i + 1);
                ClipTriangle(i, i + row, i + row + 1);
            }
            if (front.Count == 0) return null;

            int faceVertexCount = vertices.Count;
            for (int i = 0; i < faceVertexCount; i++)
            {
                Vector3 point = vertices[i];
                point.z = -point.z;
                vertices.Add(point);
                uvs.Add(uvs[i]);
                photoUvs.Add(photoUvs[i]);
            }
            var back = new List<int>(front.Count);
            int BackIndex(int index) => vertices[index].z == 0f ? index : index + faceVertexCount;
            for (int i = 0; i < front.Count; i += 3)
            {
                back.Add(BackIndex(front[i]));
                back.Add(BackIndex(front[i + 2]));
                back.Add(BackIndex(front[i + 1]));
            }
            var mesh = new Mesh { name = "Inflated Segmented Person" };
            if (vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.subMeshCount = 2;
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, photoUvs);
            mesh.SetTriangles(front, 0);
            mesh.SetTriangles(back, 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static List<SkeletonSegment> BuildSkeleton(PersonPoseData pose, Rect photoBounds, float width, float height,
            byte[] alpha, int pixelWidth, int pixelHeight)
        {
            var segments = new List<SkeletonSegment>(33);
            if (pose?.keypoints == null || pose.keypointConfidence == null ||
                pose.keypoints.Length < 17 || pose.keypointConfidence.Length < 17 ||
                photoBounds.width <= 0f || photoBounds.height <= 0f) return segments;

            var points = new Vector2[17];
            var valid = new bool[17];
            for (int i = 0; i < points.Length; i++)
            {
                Vector2 keypoint = pose.keypoints[i];
                if (pose.keypointConfidence[i] < .30f || !photoBounds.Contains(keypoint)) continue;
                float u = (keypoint.x - photoBounds.xMin) / photoBounds.width;
                float v = (keypoint.y - photoBounds.yMin) / photoBounds.height;
                if (SampleAlpha(alpha, pixelWidth, pixelHeight, u, v) < .5f) continue;
                points[i] = new Vector2((u - .5f) * width, (v - .5f) * height);
                valid[i] = true;
                segments.Add(new SkeletonSegment(points[i], points[i]));
            }

            for (int i = 0; i < SkeletonLinks.GetLength(0); i++)
            {
                int start = SkeletonLinks[i, 0], end = SkeletonLinks[i, 1];
                if (!valid[start] || !valid[end]) continue;
                AddSegment(points[start], points[end]);
            }
            if (valid[5] && valid[6] && valid[11] && valid[12])
                AddSegment((points[5] + points[6]) * .5f, (points[11] + points[12]) * .5f);
            return segments;

            void AddSegment(Vector2 start, Vector2 end)
            {
                for (int sample = 1; sample <= 3; sample++)
                {
                    Vector2 point = Vector2.Lerp(start, end, sample * .25f);
                    if (SampleAlpha(alpha, pixelWidth, pixelHeight, point.x / width + .5f, point.y / height + .5f) < .5f)
                        return;
                }
                segments.Add(new SkeletonSegment(start, end));
            }
        }

        static float SampleAlpha(byte[] alpha, int width, int height, float u, float v)
        {
            float x = Mathf.Clamp(u * width - 0.5f, 0f, width - 1);
            float y = Mathf.Clamp(v * height - 0.5f, 0f, height - 1);
            int x0 = (int)x, y0 = (int)y;
            int x1 = Mathf.Min(x0 + 1, width - 1), y1 = Mathf.Min(y0 + 1, height - 1);
            float bottom = Mathf.Lerp(alpha[y0 * width + x0], alpha[y0 * width + x1], x - x0);
            float top = Mathf.Lerp(alpha[y1 * width + x0], alpha[y1 * width + x1], x - x0);
            return Mathf.Lerp(bottom, top, y - y0) / 255f;
        }

        struct Capsule
        {
            public Vector2 a, b;
            public float radiusA, radiusB;
        }

        public static Mesh Build(PersonPoseData pose, Texture2D photo, Rect photoBounds, float modelHeight = 1.45f)
        {
            if (pose == null || photo == null || photo.width < 2 || photo.height < 2) return null;
            var pixels = photo.GetPixels32();
            var shapes = BuildShapes(pose, photoBounds, photo.width, photo.height);
            if (shapes.Count == 0) return null;

            for (int y = 0; y < photo.height; y++)
            {
                for (int x = 0; x < photo.width; x++)
                {
                    Vector2 point = new Vector2(x + 0.5f, y + 0.5f);
                    float distance = float.MaxValue;
                    for (int i = 0; i < shapes.Count; i++)
                        distance = Mathf.Min(distance, CapsuleDistance(point, shapes[i]));
                    float edge = Mathf.Clamp01((distance + 1.6f) / 3.2f);
                    float alpha = 1f - edge * edge * (3f - 2f * edge);
                    int index = y * photo.width + x;
                    var pixel = pixels[index];
                    pixel.a = (byte)Mathf.RoundToInt(alpha * 255f);
                    pixels[index] = pixel;
                }
            }
            photo.SetPixels32(pixels);
            photo.Apply(false, false);

            int gridX = Mathf.Clamp(photo.width / 8, 48, 96);
            int gridY = Mathf.Clamp(photo.height / 8, 64, 144);
            var inside = new bool[gridX, gridY];
            for (int y = 0; y < gridY; y++)
            for (int x = 0; x < gridX; x++)
            {
                int px = Mathf.Clamp((int)((x + 0.5f) * photo.width / gridX), 0, photo.width - 1);
                int py = Mathf.Clamp((int)((y + 0.5f) * photo.height / gridY), 0, photo.height - 1);
                inside[x, y] = pixels[py * photo.width + px].a >= 128;
            }

            float modelWidth = modelHeight * photo.width / photo.height;
            float thickness = Mathf.Clamp(modelWidth * 0.16f, 0.055f, 0.16f);
            float halfDepth = thickness * 0.5f;
            int row = gridX + 1;
            int faceVertexCount = row * (gridY + 1);
            var boundaryVertices = new bool[gridX + 1, gridY + 1];
            for (int y = 0; y < gridY; y++)
            for (int x = 0; x < gridX; x++)
            {
                if (!inside[x, y]) continue;
                if (y == 0 || !inside[x, y - 1]) MarkBoundary(boundaryVertices, x, y, x + 1, y);
                if (x == gridX - 1 || !inside[x + 1, y]) MarkBoundary(boundaryVertices, x + 1, y, x + 1, y + 1);
                if (y == gridY - 1 || !inside[x, y + 1]) MarkBoundary(boundaryVertices, x + 1, y + 1, x, y + 1);
                if (x == 0 || !inside[x - 1, y]) MarkBoundary(boundaryVertices, x, y + 1, x, y);
            }

            var signedDistance = new float[gridX + 1, gridY + 1];
            float maximumInteriorDistance = 0f;
            for (int y = 0; y <= gridY; y++)
            for (int x = 0; x <= gridX; x++)
            {
                Vector2 point = new Vector2(x * photo.width / (float)gridX, y * photo.height / (float)gridY);
                float distance = SignedDistance(point, shapes);
                signedDistance[x, y] = distance;
                if (!boundaryVertices[x, y] && distance < 0f)
                    maximumInteriorDistance = Mathf.Max(maximumInteriorDistance, -distance);
            }
            if (maximumInteriorDistance <= 0f) return null;

            var sharedUvs = new Vector2[gridX + 1, gridY + 1];
            for (int y = 0; y <= gridY; y++)
            for (int x = 0; x <= gridX; x++)
            {
                float u = x / (float)gridX;
                float v = y / (float)gridY;
                if (boundaryVertices[x, y])
                {
                    Vector2 point = new Vector2(x * photo.width / (float)gridX, y * photo.height / (float)gridY);
                    Vector2 edgePixel = FindOpaqueEdgePixel(point, photo.width, photo.height, pixels);
                    u = edgePixel.x / photo.width;
                    v = edgePixel.y / photo.height;
                }
                sharedUvs[x, y] = new Vector2(u, v);
            }
            photo.SetPixels32(pixels);
            photo.Apply(false, false);

            var vertices = new List<Vector3>(faceVertexCount * 2);
            var uvs = new List<Vector2>(faceVertexCount * 2);
            var frontTriangles = new List<int>(gridX * gridY * 6);
            var backTriangles = new List<int>(gridX * gridY * 6);
            for (int side = 0; side < 2; side++)
            {
                for (int y = 0; y <= gridY; y++)
                for (int x = 0; x <= gridX; x++)
                {
                    float u = x / (float)gridX;
                    float v = y / (float)gridY;
                    float distance = signedDistance[x, y];
                    float t = Mathf.Clamp01(-distance / maximumInteriorDistance);
                    float profile = Mathf.Sqrt(Mathf.Max(0f, t * (2f - t)));
                    float depth = boundaryVertices[x, y] ? 0f : halfDepth * profile;
                    float z = side == 0 ? -depth : depth;

                    vertices.Add(new Vector3((u - 0.5f) * modelWidth, (v - 0.5f) * modelHeight, z));
                    uvs.Add(sharedUvs[x, y]);
                }
            }

            for (int y = 0; y < gridY; y++)
            for (int x = 0; x < gridX; x++)
            {
                if (!inside[x, y]) continue;
                int lowerLeft = y * row + x;
                int lowerRight = lowerLeft + 1;
                int upperLeft = lowerLeft + row;
                int upperRight = upperLeft + 1;
                AddFace(frontTriangles, lowerLeft, lowerRight, upperRight, upperLeft, false);
                AddFace(backTriangles, faceVertexCount + lowerLeft, faceVertexCount + lowerRight,
                    faceVertexCount + upperRight, faceVertexCount + upperLeft, true);
            }

            if (frontTriangles.Count == 0 || backTriangles.Count == 0) return null;
            var mesh = new Mesh { name = "Inflated Photo Person Double Surface" };
            if (vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(frontTriangles, 0, false);
            mesh.SetTriangles(backTriangles, 1, false);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Texture2D CreateBackTexture(Texture2D front)
        {
            if (front == null) return null;
            int width = front.width, height = front.height;
            var source = front.GetPixels32();
            var back = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
            {
                name = "Back Photo Person",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            back.SetPixels32(source);
            back.Apply(false, false);
            return back;
        }

        static void MarkBoundary(bool[,] boundary, int x0, int y0, int x1, int y1)
        {
            boundary[x0, y0] = true;
            boundary[x1, y1] = true;
        }

        static float SignedDistance(Vector2 point, List<Capsule> shapes)
        {
            float distance = float.MaxValue;
            for (int i = 0; i < shapes.Count; i++)
                distance = Mathf.Min(distance, CapsuleDistance(point, shapes[i]));
            return distance;
        }

        static Vector2 FindOpaqueEdgePixel(Vector2 edge, int width, int height, Color32[] pixels)
        {
            int centerX = Mathf.RoundToInt(edge.x - 0.5f);
            int centerY = Mathf.RoundToInt(edge.y - 0.5f);
            float opaqueDistance = float.MaxValue, fallbackDistance = float.MaxValue;
            int fallbackAlpha = -1, opaqueX = -1, opaqueY = -1;
            int fallbackX = Mathf.Clamp(centerX, 0, width - 1), fallbackY = Mathf.Clamp(centerY, 0, height - 1);
            int bestX = Mathf.Clamp(centerX, 0, width - 1), bestY = Mathf.Clamp(centerY, 0, height - 1);
            const int searchRadius = 24;
            for (int y = Mathf.Max(0, centerY - searchRadius); y <= Mathf.Min(height - 1, centerY + searchRadius); y++)
            for (int x = Mathf.Max(0, centerX - searchRadius); x <= Mathf.Min(width - 1, centerX + searchRadius); x++)
            {
                int index = y * width + x;
                int alpha = pixels[index].a;
                if (alpha == 0) continue;
                Vector2 pixelCenter = new Vector2(x + 0.5f, y + 0.5f);
                float distance = (pixelCenter - edge).sqrMagnitude;
                if (alpha >= 250)
                {
                    if (distance < opaqueDistance) { opaqueDistance = distance; opaqueX = x; opaqueY = y; }
                }
                else if (opaqueX < 0 && (alpha > fallbackAlpha || alpha == fallbackAlpha && distance < fallbackDistance))
                {
                    fallbackDistance = distance;
                    fallbackAlpha = alpha;
                    fallbackX = x;
                    fallbackY = y;
                }
            }

            if (opaqueX >= 0) { bestX = opaqueX; bestY = opaqueY; }
            else { bestX = fallbackX; bestY = fallbackY; }
            Color32 edgeColor = pixels[bestY * width + bestX];
            edgeColor.a = 255;
            for (int y = Mathf.Max(0, bestY - 1); y <= Mathf.Min(height - 1, bestY + 1); y++)
            for (int x = Mathf.Max(0, bestX - 1); x <= Mathf.Min(width - 1, bestX + 1); x++)
                pixels[y * width + x] = edgeColor;
            return new Vector2(bestX + 0.5f, bestY + 0.5f);
        }

        static List<Capsule> BuildShapes(PersonPoseData pose, Rect crop, int width, int height)
        {
            var shapes = new List<Capsule>(18);
            Vector2[] points = new Vector2[17];
            bool[] valid = new bool[17];
            for (int i = 0; i < 17; i++)
            {
                if (pose.keypoints == null || pose.keypoints.Length <= i || pose.keypointConfidence == null ||
                    pose.keypointConfidence.Length <= i || pose.keypointConfidence[i] < 0.16f) continue;
                Vector2 point = pose.keypoints[i];
                points[i] = new Vector2((point.x - crop.xMin) / crop.width * width,
                    (point.y - crop.yMin) / crop.height * height);
                valid[i] = true;
            }

            float shoulderWidth = valid[5] && valid[6] ? Vector2.Distance(points[5], points[6]) : width * 0.42f;
            shoulderWidth = Mathf.Max(8f, shoulderWidth);
            Vector2 shoulders = valid[5] && valid[6] ? (points[5] + points[6]) * 0.5f : new Vector2(width * 0.5f, height * 0.68f);
            Vector2 hips = valid[11] && valid[12] ? (points[11] + points[12]) * 0.5f : new Vector2(width * 0.5f, height * 0.39f);

            Vector2 face = Vector2.zero;
            int faceCount = 0;
            for (int i = 0; i <= 4; i++)
                if (valid[i]) { face += points[i]; faceCount++; }
            Vector2 head = faceCount > 0 ? face / faceCount : new Vector2(width * 0.5f, height * 0.84f);
            float faceSpan = 0f;
            if (valid[3] && valid[4]) faceSpan = Vector2.Distance(points[3], points[4]);
            if (faceSpan <= 0f && faceCount > 1)
            {
                for (int i = 0; i <= 4; i++)
                    if (valid[i]) faceSpan = Mathf.Max(faceSpan, Vector2.Distance(head, points[i]) * 2f);
            }
            float headRadius = Mathf.Clamp(Mathf.Max(faceSpan * 0.58f, shoulderWidth * 0.19f),
                Mathf.Max(5f, shoulderWidth * 0.12f), Mathf.Max(8f, shoulderWidth * 0.42f));
            Add(shapes, head, head, headRadius, headRadius);

            float hipWidth = valid[11] && valid[12] ? Vector2.Distance(points[11], points[12]) : shoulderWidth * 0.72f;
            Vector2 neckTop = head + Vector2.down * headRadius * 0.80f;
            Vector2 neckBottom = shoulders + Vector2.up * shoulderWidth * 0.04f;
            Add(shapes, neckTop, neckBottom, shoulderWidth * 0.24f, shoulderWidth * 0.24f);
            Add(shapes, shoulders, hips, shoulderWidth * 0.38f, Mathf.Max(shoulderWidth * 0.32f, hipWidth * 0.42f));
            AddLimb(shapes, points, valid, 5, 7, shoulderWidth * 0.13f, shoulderWidth * 0.115f);
            AddLimb(shapes, points, valid, 7, 9, shoulderWidth * 0.11f, shoulderWidth * 0.085f);
            AddLimb(shapes, points, valid, 6, 8, shoulderWidth * 0.13f, shoulderWidth * 0.115f);
            AddLimb(shapes, points, valid, 8, 10, shoulderWidth * 0.11f, shoulderWidth * 0.085f);
            AddLimb(shapes, points, valid, 11, 13, shoulderWidth * 0.18f, shoulderWidth * 0.14f);
            AddLimb(shapes, points, valid, 13, 15, shoulderWidth * 0.14f, shoulderWidth * 0.095f);
            AddLimb(shapes, points, valid, 12, 14, shoulderWidth * 0.18f, shoulderWidth * 0.14f);
            AddLimb(shapes, points, valid, 14, 16, shoulderWidth * 0.14f, shoulderWidth * 0.095f);

            bool hasArm = valid[5] && valid[7] || valid[7] && valid[9] || valid[6] && valid[8] || valid[8] && valid[10];
            if (!hasArm)
            {
                Vector2 leftShoulder = new Vector2(width * 0.5f - shoulderWidth * 0.5f, height * 0.68f);
                Vector2 rightShoulder = new Vector2(width * 0.5f + shoulderWidth * 0.5f, height * 0.68f);
                Add(shapes, leftShoulder, leftShoulder + new Vector2(-shoulderWidth * 0.62f, -shoulderWidth * 0.36f),
                    shoulderWidth * 0.14f, shoulderWidth * 0.09f);
                Add(shapes, rightShoulder, rightShoulder + new Vector2(shoulderWidth * 0.62f, -shoulderWidth * 0.36f),
                    shoulderWidth * 0.14f, shoulderWidth * 0.09f);
            }

            bool hasLeg = valid[11] && valid[13] || valid[13] && valid[15] || valid[12] && valid[14] || valid[14] && valid[16];
            if (!hasLeg)
            {
                Add(shapes, new Vector2(width * 0.5f - shoulderWidth * 0.20f, height * 0.39f),
                    new Vector2(width * 0.5f - shoulderWidth * 0.25f, height * 0.05f), shoulderWidth * 0.15f, shoulderWidth * 0.10f);
                Add(shapes, new Vector2(width * 0.5f + shoulderWidth * 0.20f, height * 0.39f),
                    new Vector2(width * 0.5f + shoulderWidth * 0.25f, height * 0.05f), shoulderWidth * 0.15f, shoulderWidth * 0.10f);
            }
            return shapes;
        }

        static void AddLimb(List<Capsule> shapes, Vector2[] points, bool[] valid, int a, int b, float radiusA, float radiusB)
        {
            if (valid[a] && valid[b]) Add(shapes, points[a], points[b], radiusA, radiusB);
        }

        static void Add(List<Capsule> shapes, Vector2 a, Vector2 b, float radiusA, float radiusB)
        {
            shapes.Add(new Capsule { a = a, b = b, radiusA = radiusA, radiusB = radiusB });
        }

        static float CapsuleDistance(Vector2 point, Capsule capsule)
        {
            Vector2 axis = capsule.b - capsule.a;
            float t = axis.sqrMagnitude > 0.001f ? Mathf.Clamp01(Vector2.Dot(point - capsule.a, axis) / axis.sqrMagnitude) : 0f;
            return Vector2.Distance(point, Vector2.Lerp(capsule.a, capsule.b, t)) - Mathf.Lerp(capsule.radiusA, capsule.radiusB, t);
        }

        static void AddFace(List<int> triangles, int lowerLeft, int lowerRight, int upperRight, int upperLeft, bool reverse)
        {
            if (reverse)
            {
                triangles.Add(lowerLeft); triangles.Add(lowerRight); triangles.Add(upperRight);
                triangles.Add(lowerLeft); triangles.Add(upperRight); triangles.Add(upperLeft);
            }
            else
            {
                triangles.Add(lowerLeft); triangles.Add(upperRight); triangles.Add(lowerRight);
                triangles.Add(lowerLeft); triangles.Add(upperLeft); triangles.Add(upperRight);
            }
        }

    }
}
