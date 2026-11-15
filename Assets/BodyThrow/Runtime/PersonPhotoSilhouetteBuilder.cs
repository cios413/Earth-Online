using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace EarthOL.BodyThrow
{
    public static class PersonPhotoSilhouetteBuilder
    {
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
