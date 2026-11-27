using System;
using System.Collections;
using UnityEngine;

namespace EarthOL.BodyThrow
{
    internal static class PersonShowcasePose
    {
        const int WorkSliceMilliseconds = 8;
        static readonly int[,] Joints = { { 5, 7, 9 }, { 6, 8, 10 }, { 11, 13, 15 }, { 12, 14, 16 } };

        sealed class Limb
        {
            public int index;
            public Vector2 root, joint, tip, axis, normal;
            public Vector2 targetAxis, targetNormal;
            public float length, radius;
            public float stretch = 1f, bend, sweep, cap, inset;

            float Curve(float t) => t * t * sweep + Mathf.Sin(2f * Mathf.PI * t) * t * (1f - t) * bend * 4f;

            public Vector2 Center(float t) => root + targetAxis * (t * length * stretch) +
                targetNormal * Curve(Mathf.Clamp01(t));

            public byte Coverage(Vector2 point, out float t, out float across, out float halfDepth)
            {
                Vector2 offset = point - root;
                float along = Vector2.Dot(offset, targetAxis);
                float end = length * stretch;
                t = Mathf.Clamp01(along / end);
                float side = Vector2.Dot(offset, targetNormal) - Curve(t);
                float rootRadius = index < 2 ? .98f : .78f;
                float taper = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, .78f, t));
                float tubeRadius = radius * Mathf.Lerp(rootRadius, .46f, taper);
                float beyond = along < 0f ? -along / inset : Mathf.Max(0f, along - end) / cap;
                across = side / tubeRadius;
                float distance = Mathf.Sqrt(across * across + beyond * beyond);
                halfDepth = tubeRadius * .84f * Mathf.Sqrt(Mathf.Max(0f, 1f - distance * distance));
                return (byte)Mathf.RoundToInt(Mathf.Clamp01((1f - distance) * tubeRadius + .5f) * 255f);
            }

            public Vector2 Sample(float t, float across)
            {
                float split = JointFraction;
                Vector2 direction = t <= split ? joint - root : tip - joint;
                Vector2 center = t <= split ? Vector2.Lerp(root, joint, t / split) :
                    Vector2.Lerp(joint, tip, (t - split) / (1f - split));
                Vector2 normal = new Vector2(-direction.y, direction.x).normalized;
                return center + normal * (across * radius * .48f);
            }

            public float JointFraction => Mathf.Clamp(Vector2.Distance(root, joint) /
                Mathf.Max(1f, Vector2.Distance(root, joint) + Vector2.Distance(joint, tip)), .25f, .75f);
        }

        internal static IEnumerator Compose(Texture2D photo, byte[] alpha, PersonPoseData person, Rect crop,
            float strength, float clearance, int seed, Action<Texture2D, byte[], PersonPoseData, Vector2[]> completed)
        {
            if (strength <= 0f || crop.width <= 0f || crop.height <= 0f ||
                person?.keypoints == null || person.keypoints.Length < 17 ||
                person.keypointConfidence == null || person.keypointConfidence.Length < 17) yield break;

            int width = photo.width, height = photo.height;
            var slice = System.Diagnostics.Stopwatch.StartNew();
            var source = photo.GetPixels32();
            var points = new Vector2[17];
            for (int i = 0; i < points.Length; i++)
                points[i] = (person.keypoints[i] - crop.min) / crop.size * new Vector2(width, height) - Vector2.one * .5f;

            var limbs = new Limb[4];
            float shoulderWidth = Vector2.Distance(points[5], points[6]);
            float hipWidth = Vector2.Distance(points[11], points[12]);
            float bodyScale = Mathf.Clamp(Mathf.Max(shoulderWidth, hipWidth), width * .20f, width * .65f);
            Vector2 bodyCenter = (points[5] + points[6] + points[11] + points[12]) * .25f;
            for (int i = 0; i < limbs.Length; i++)
            {
                int a = Joints[i, 0], b = Joints[i, 1], c = Joints[i, 2];
                if (person.keypointConfidence[a] < .35f || person.keypointConfidence[b] < .30f ||
                    person.keypointConfidence[c] < .30f) continue;
                if (!NearForeground(alpha, width, height, points[b], bodyScale * .10f) ||
                    !NearForeground(alpha, width, height, points[c], bodyScale * .12f))
                    continue;
                Vector2 direction = points[c] - points[a];
                if (direction.magnitude < bodyScale * .45f) continue;
                Vector2 axis = direction.normalized;
                limbs[i] = new Limb { index = i, root = points[a], joint = points[b], tip = points[c],
                    axis = axis, normal = new Vector2(-axis.y, axis.x), length = direction.magnitude,
                    radius = Mathf.Max(3f, bodyScale * (i < 2 ? .19f : .22f)) };
            }

            var region = new byte[alpha.Length];
            var bounds = new RectInt[limbs.Length];
            var area = new int[limbs.Length];
            Vector2 shoulders = (points[5] + points[6]) * .5f;
            Vector2 hips = (points[11] + points[12]) * .5f;
            float torsoRadius = bodyScale * .37f;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    if (alpha[index] == 0) continue;
                    Vector2 point = new Vector2(x, y);
                    if (InTorso(point, shoulders, hips, torsoRadius)) continue;
                    float nearest = 1f;
                    Limb owner = null;
                    foreach (Limb limb in limbs)
                    {
                        if (limb == null) continue;
                        float along = Vector2.Dot(point - limb.root, limb.axis);
                        if (along < -limb.radius * .40f || along > limb.length + bodyScale * .33f) continue;
                        float ownershipRadius = limb.radius * (limb.index < 2 ? 1.60f : 1.83f);
                        float distance = Mathf.Min(DistanceSquared(point, limb.root, limb.joint),
                            DistanceSquared(point, limb.joint, limb.tip + limb.axis * bodyScale * .20f)) /
                            (ownershipRadius * ownershipRadius);
                        if (distance >= nearest) continue;
                        nearest = distance;
                        owner = limb;
                    }
                    if (owner == null) continue;
                    int slot = owner.index;
                    region[index] = (byte)(slot + 1);
                    RectInt rect = bounds[slot];
                    bounds[slot] = area[slot]++ == 0 ? new RectInt(x, y, 1, 1) :
                        new RectInt(Mathf.Min(rect.xMin, x), Mathf.Min(rect.yMin, y),
                            Mathf.Max(rect.xMax, x + 1) - Mathf.Min(rect.xMin, x),
                            Mathf.Max(rect.yMax, y + 1) - Mathf.Min(rect.yMin, y));
                }
                if (slice.ElapsedMilliseconds < WorkSliceMilliseconds) continue;
                yield return null;
                slice.Restart();
            }

            int padX = Mathf.CeilToInt(width * .42f), padY = Mathf.CeilToInt(height * .24f);
            int outWidth = width + 2 * padX, outHeight = height + 2 * padY;
            var pixels = new Color32[outWidth * outHeight];
            var mask = new byte[pixels.Length];
            var depth = new Vector2[pixels.Length];
            Vector2 padding = new Vector2(padX, padY);
            var random = new System.Random(seed);
            bool face = person.keypointConfidence[0] >= .45f && person.keypointConfidence[1] >= .45f &&
                person.keypointConfidence[2] >= .45f;
            Vector2 eyeCenter = (points[1] + points[2]) * .5f;
            float eyeWidth = Vector2.Distance(points[1], points[2]);
            float faceSign = random.Next(2) == 0 ? -1f : 1f;
            float headLean = faceSign * strength * eyeWidth * .74f;
            float HeadBlend(float y) => face && eyeWidth >= 3f
                ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(
                    Mathf.Max(shoulders.y, points[0].y - eyeWidth * 2.2f),
                    eyeCenter.y + eyeWidth * .75f, y))
                : 0f;
            bool changed = false;
            for (int y = 0; y < height; y++)
            {
                float headBlend = HeadBlend(y);
                int headMargin = Mathf.Min(padX, Mathf.CeilToInt(Mathf.Abs(headLean * headBlend)));
                for (int x = -headMargin; x < width + headMargin; x++)
                {
                    int from = y * width + x, to = (y + padY) * outWidth + x + padX;
                    if (headBlend <= 0f)
                    {
                        mask[to] = alpha[from];
                        pixels[to] = source[from];
                        if (alpha[from] < 128 && InTorso(new Vector2(x, y), shoulders, hips, torsoRadius * .80f))
                        {
                            float span = torsoRadius * 1.12f;
                            if (AlphaAt(alpha, width, height, new Vector2(x - span, y)) >= 160 &&
                                AlphaAt(alpha, width, height, new Vector2(x + span, y)) >= 160)
                                mask[to] = 255;
                        }
                        continue;
                    }
                    Vector2 sample = new Vector2(x - headLean * headBlend, y);
                    mask[to] = SampleHeadPixel(source, alpha, width, height, sample, out Color32 color);
                    if (mask[to] == 0) continue;
                    Vector2 relative = (sample - points[0]) / eyeWidth;
                    float falloff = Mathf.Clamp01(1f - relative.x * relative.x / 2.1f - relative.y * relative.y / 2.65f);
                    float lower = Mathf.Clamp01((eyeCenter.y - sample.y) / (eyeWidth * .8f));
                    Vector2 offset = new Vector2(.54f * lower + .22f * relative.x,
                        .48f * relative.x * (.65f + lower)) * (eyeWidth * strength * faceSign * falloff);
                    if (mask[to] >= 160 && SampleHeadPixel(source, alpha, width, height, sample + offset,
                        out Color32 distorted) >= 160) color = distorted;
                    pixels[to] = color;
                    changed = true;
                }
                if (slice.ElapsedMilliseconds < WorkSliceMilliseconds) continue;
                yield return null;
                slice.Restart();
            }

            var result = PersonPoseData.FromDetection(person.AsPoseView(), person.timestamp);
            int gap = Mathf.Clamp(Mathf.RoundToInt(bodyScale * clearance), 1, 16);
            var occupancy = new int[(outWidth + 1) * (outHeight + 1)];
            foreach (Limb limb in limbs)
            {
                if (limb == null || area[limb.index] < limb.length * 1.5f) continue;
                int part = limb.index + 1;
                bool arm = limb.index < 2;
                bool curvedArm = arm && random.NextDouble() < .55;
                RectInt rect = bounds[limb.index];
                int pitch = outWidth + 1;
                for (int y = 0; y < outHeight; y++)
                {
                    int row = 0;
                    for (int x = 0; x < outWidth; x++)
                    {
                        int sx = x - padX, sy = y - padY;
                        bool own = sx >= 0 && sx < width && sy >= 0 && sy < height && region[sy * width + sx] == part;
                        if (!own && mask[y * outWidth + x] >= 96) row++;
                        occupancy[(y + 1) * pitch + x + 1] = occupancy[y * pitch + x + 1] + row;
                    }
                    if (slice.ElapsedMilliseconds < WorkSliceMilliseconds) continue;
                    yield return null;
                    slice.Restart();
                }

                for (int attempt = 0; attempt < 7; attempt++)
                {
                    float amount = strength * (1f - attempt * .095f);
                    Vector2 towardOutside = limb.root - bodyCenter;
                    float sign = Vector2.Dot(limb.normal, towardOutside) >= 0f ? 1f : -1f;
                    float turn = sign * (.28f + amount * (.22f + .45f * (float)random.NextDouble()));
                    Vector2 poseAxis = limb.axis * Mathf.Cos(turn) + limb.normal * Mathf.Sin(turn);
                    float outward = Mathf.Sign(limb.root.x - bodyCenter.x);
                    if (arm)
                    {
                        poseAxis.x = outward * Mathf.Max(Mathf.Abs(poseAxis.x), curvedArm ? .70f : .44f);
                        if (curvedArm) poseAxis.y = Mathf.Max(-.36f, poseAxis.y);
                    }
                    else
                    {
                        float spread = Mathf.Clamp(limb.axis.x * outward, .10f, .17f);
                        // Open the stance on retries so touching trousers can clear each other.
                        // Keep the centerline straight even when the leg needs a wider angle.
                        spread += .036f * attempt + .03f * (float)random.NextDouble();
                        poseAxis = new Vector2(outward * spread, -1f);
                    }
                    limb.targetAxis = poseAxis.normalized;
                    limb.targetNormal = new Vector2(-limb.targetAxis.y, limb.targetAxis.x);
                    sign = Mathf.Sign(limb.targetNormal.x * outward);
                    limb.stretch = 1f + amount * (arm ? .48f : .40f) + amount * .16f * (float)random.NextDouble();
                    limb.sweep = curvedArm
                        ? Mathf.Sign(limb.targetNormal.y) * amount * limb.length * (.42f + .50f * (float)random.NextDouble())
                        : sign * amount * limb.length * (arm ? .025f : .012f);
                    limb.bend = curvedArm ? limb.sweep * -.36f : sign * amount * limb.length * (arm ? .025f : .008f);
                    limb.inset = limb.radius * (arm ? 1.45f : 1.15f);
                    limb.cap = limb.radius * (arm ? .73f : .98f);
                    RectInt target = DestinationBounds(limb, padX, padY, outWidth, outHeight);
                    bool safe = true;
                    bool joined = false;
                    float join = limb.radius * (arm ? 2.4f : 2.1f);
                    for (int y = target.yMin; y < target.yMax && safe; y++)
                    {
                        for (int x = target.xMin; x < target.xMax; x++)
                        {
                            Vector2 point = new Vector2(x, y) - padding;
                            byte coverage = limb.Coverage(point, out float t, out _, out _);
                            if (coverage < 96) continue;
                            if (t <= .20f && (point - limb.root).sqrMagnitude <= join * join)
                            {
                                int sx = x - padX, sy = y - padY;
                                joined |= coverage >= 192 && (uint)sx < (uint)width && (uint)sy < (uint)height &&
                                    mask[y * outWidth + x] >= 192 &&
                                    (region[sy * width + sx] != part ||
                                     (point - limb.root).sqrMagnitude <= limb.inset * limb.inset * .27f);
                                continue;
                            }
                            int left = Mathf.Max(0, x - gap), right = Mathf.Min(outWidth, x + gap + 1);
                            int bottom = Mathf.Max(0, y - gap), top = Mathf.Min(outHeight, y + gap + 1);
                            if (x <= 1 || y <= 1 || x >= outWidth - 2 || y >= outHeight - 2 ||
                                occupancy[top * pitch + right] - occupancy[top * pitch + left] -
                                occupancy[bottom * pitch + right] + occupancy[bottom * pitch + left] > 0)
                            {
                                safe = false;
                                break;
                            }
                        }
                        if (slice.ElapsedMilliseconds < WorkSliceMilliseconds) continue;
                        yield return null;
                        slice.Restart();
                    }
                    if (!safe || !joined) continue;

                    // Retain the original cloth around the joint and overlap the moved region there.
                    for (int y = rect.yMin; y < rect.yMax; y++)
                    for (int x = rect.xMin; x < rect.xMax; x++)
                    {
                        if (region[y * width + x] != part) continue;
                        int index = (y + padY) * outWidth + x + padX;
                        Vector2 point = new Vector2(x, y);
                        float retain = 1f - Mathf.SmoothStep(0f, 1f,
                            Mathf.InverseLerp(limb.inset * .52f, limb.inset, Vector2.Distance(point, limb.root)));
                        mask[index] = (byte)Mathf.RoundToInt(mask[index] * retain);
                    }
                    for (int y = target.yMin; y < target.yMax; y++)
                    {
                        for (int x = target.xMin; x < target.xMax; x++)
                        {
                            Vector2 point = new Vector2(x, y) - padding;
                            byte coverage = limb.Coverage(point, out float t, out float across, out float halfDepth);
                            if (coverage == 0) continue;
                            int index = y * outWidth + x;
                            float overlap = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, .14f, t));
                            float blend = mask[index] == 0 ? 1f : overlap * coverage / 255f;
                            Vector2 center = limb.Sample(t, 0f);
                            Vector2 sample = limb.Sample(t, across);
                            if (AlphaAt(alpha, width, height, sample) < 160) sample = center;
                            if (AlphaAt(alpha, width, height, sample) < 160)
                                sample = AlphaAt(alpha, width, height, limb.joint) >= 160 ? limb.joint : limb.root;
                            Color32 moved = ColorAt(source, width, height, sample);
                            pixels[index] = Color32.Lerp(pixels[index], moved, blend);
                            float rootBlend = mask[index] >= 128 ? overlap : 1f;
                            depth[index] = new Vector2(halfDepth * rootBlend, rootBlend);
                            mask[index] = Math.Max(mask[index], coverage);
                        }
                        if (slice.ElapsedMilliseconds < WorkSliceMilliseconds) continue;
                        yield return null;
                        slice.Restart();
                    }
                    for (int k = 1; k <= 2; k++)
                    {
                        int joint = Joints[limb.index, k];
                        points[joint] = limb.Center(k == 1 ? limb.JointFraction : 1f);
                    }
                    if (arm)
                        SmoothShoulder(pixels, mask, depth, outWidth, outHeight, limb.root + padding, limb.radius);
                    changed = true;
                    break;
                }
            }

            if (!changed) yield break;
            // Keep the connected body after moving the limbs. An isolated large region indicates
            // an uncertain separation, so retain the original portrait in that case.
            var connected = KeepBody(mask, outWidth, outHeight, (hips + shoulders) * .5f + padding);
            while (connected.MoveNext()) yield return null;
            if (!(connected.Current is bool valid) || !valid) yield break;
            float ScaleAt(float y)
            {
                float span = Mathf.Max(4f, shoulders.y - hips.y);
                float upper = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(shoulders.y + span * .18f, shoulders.y - span * .12f, y));
                float lower = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(hips.y - span * .38f, hips.y + span * .12f, y));
                return 1f - .22f * strength * upper * lower;
            }
            float centerX = bodyCenter.x + padX;
            var rowPixels = new Color32[outWidth];
            var rowMask = new byte[outWidth];
            var rowDepth = new Vector2[outWidth];
            for (int y = 0; y < outHeight; y++)
            {
                float scale = ScaleAt(y - padY);
                if (scale >= .999f) continue;
                int start = y * outWidth;
                Array.Copy(pixels, start, rowPixels, 0, outWidth);
                Array.Copy(mask, start, rowMask, 0, outWidth);
                Array.Copy(depth, start, rowDepth, 0, outWidth);
                for (int x = 0; x < outWidth; x++)
                {
                    float sample = centerX + (x - centerX) / scale;
                    int left = Mathf.FloorToInt(sample), right = left + 1;
                    if (left < 0 || right >= outWidth)
                    {
                        mask[start + x] = 0;
                        depth[start + x] = default;
                        continue;
                    }
                    float t = sample - left;
                    float a = rowMask[left] * (1f - t), b = rowMask[right] * t;
                    mask[start + x] = (byte)Mathf.RoundToInt(a + b);
                    pixels[start + x] = Color32.Lerp(rowPixels[left], rowPixels[right], a + b > 0f ? b / (a + b) : t);
                    depth[start + x] = Vector2.Lerp(rowDepth[left], rowDepth[right], t);
                }
                if (slice.ElapsedMilliseconds < WorkSliceMilliseconds) continue;
                yield return null;
                slice.Restart();
            }
            int xMin = outWidth, yMin = outHeight, xMax = -1, yMax = -1;
            for (int y = 0; y < outHeight; y++)
            for (int x = 0; x < outWidth; x++)
            {
                if (mask[y * outWidth + x] == 0) continue;
                xMin = Mathf.Min(xMin, x);
                yMin = Mathf.Min(yMin, y);
                xMax = Mathf.Max(xMax, x);
                yMax = Mathf.Max(yMax, y);
            }
            int border = Mathf.Max(4, Mathf.RoundToInt(height * .035f));
            xMin = Mathf.Max(0, xMin - border);
            yMin = Mathf.Max(0, yMin - border);
            xMax = Mathf.Min(outWidth - 1, xMax + border);
            yMax = Mathf.Min(outHeight - 1, yMax + border);
            int finalWidth = xMax - xMin + 1, finalHeight = yMax - yMin + 1;
            var finalPixels = new Color32[finalWidth * finalHeight];
            var finalMask = new byte[finalPixels.Length];
            var finalDepth = new Vector2[finalPixels.Length];
            for (int y = 0; y < finalHeight; y++)
            {
                int start = (y + yMin) * outWidth + xMin;
                Array.Copy(pixels, start, finalPixels, y * finalWidth, finalWidth);
                Array.Copy(mask, start, finalMask, y * finalWidth, finalWidth);
                Array.Copy(depth, start, finalDepth, y * finalWidth, finalWidth);
            }
            for (int i = 0; i < points.Length; i++)
            {
                if (i < 5) points[i].x += headLean * HeadBlend(points[i].y);
                points[i].x = bodyCenter.x + (points[i].x - bodyCenter.x) * ScaleAt(points[i].y);
                result.keypoints[i] = (points[i] + padding - new Vector2(xMin, yMin) + Vector2.one * .5f) /
                    new Vector2(finalWidth, finalHeight);
            }
            var composed = new Texture2D(finalWidth, finalHeight, TextureFormat.RGBA32, false)
                { name = "Reshaped Showcase Photo", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            composed.SetPixels32(finalPixels);
            completed(composed, finalMask, result, finalDepth);
        }

        static byte SampleHeadPixel(Color32[] source, byte[] mask, int width, int height, Vector2 sample,
            out Color32 color)
        {
            int left = Mathf.FloorToInt(sample.x), bottom = Mathf.FloorToInt(sample.y);
            float fx = sample.x - left, fy = sample.y - bottom;
            float coverage = 0f;
            Color sum = Color.clear;
            for (int y = 0; y <= 1; y++)
            for (int x = 0; x <= 1; x++)
            {
                int sx = left + x, sy = bottom + y;
                if ((uint)sx >= (uint)width || (uint)sy >= (uint)height) continue;
                int index = sy * width + sx;
                float weight = (x == 0 ? 1f - fx : fx) * (y == 0 ? 1f - fy : fy) * mask[index];
                sum += (Color)source[index] * weight;
                coverage += weight;
            }
            color = coverage > 0f ? (Color32)(sum / coverage) : default;
            return (byte)Mathf.RoundToInt(coverage);
        }

        static void SmoothShoulder(Color32[] pixels, byte[] mask, Vector2[] depth, int width, int height,
            Vector2 center, float radius)
        {
            float extent = radius * 2.5f;
            int step = Mathf.Max(1, Mathf.RoundToInt(radius * .45f));
            int left = Mathf.Max(step, Mathf.FloorToInt(center.x - extent));
            int right = Mathf.Min(width - step, Mathf.CeilToInt(center.x + extent));
            int bottom = Mathf.Max(step, Mathf.FloorToInt(center.y - extent));
            int top = Mathf.Min(height - step, Mathf.CeilToInt(center.y + extent));
            if (right <= left || top <= bottom) return;
            int rowWidth = right - left;
            var rounded = new byte[rowWidth * (top - bottom)];
            var colors = new Color32[rounded.Length];
            var depths = new Vector2[rounded.Length];
            for (int y = bottom; y < top; y++)
            for (int x = left; x < right; x++)
            {
                int index = y * width + x;
                int local = (y - bottom) * rowWidth + x - left;
                rounded[local] = mask[index];
                colors[local] = pixels[index];
                depths[local] = depth[index];
                float distance = Vector2.Distance(new Vector2(x, y), center);
                float blend = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(extent * .52f, extent, distance));
                if (blend <= 0f) continue;
                float coverage = 0f;
                Color sum = Color.clear;
                Vector2 profile = Vector2.zero;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int sample = index + step * (dy * width + dx);
                    float weight = (dx == 0 ? 2f : 1f) * (dy == 0 ? 2f : 1f) * mask[sample];
                    coverage += weight;
                    sum += (Color)pixels[sample] * weight;
                    profile += depth[sample] * weight;
                }
                // Narrow the blend around the silhouette so the shoulder stays opaque.
                float filtered = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(72f, 183f, coverage / 16f));
                rounded[local] = (byte)Mathf.RoundToInt(Mathf.Lerp(mask[index], filtered * 255f, blend));
                if (mask[index] < 128 && rounded[local] > mask[index] && coverage > 0f)
                    colors[local] = (Color32)(sum / coverage);
                if (coverage > 0f && rounded[local] > 0)
                    depths[local] = Vector2.Lerp(depth[index], profile / coverage, blend * .55f);
            }
            for (int y = bottom; y < top; y++)
            {
                Array.Copy(rounded, (y - bottom) * rowWidth, mask, y * width + left, rowWidth);
                Array.Copy(colors, (y - bottom) * rowWidth, pixels, y * width + left, rowWidth);
                Array.Copy(depths, (y - bottom) * rowWidth, depth, y * width + left, rowWidth);
            }
        }

        static RectInt DestinationBounds(Limb limb, int padX, int padY, int width, int height)
        {
            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = -min;
            float reach = limb.radius + Mathf.Abs(limb.sweep) + Mathf.Abs(limb.bend);
            for (int i = 0; i < 4; i++)
            {
                float along = (i & 1) == 0 ? -limb.inset : limb.length * limb.stretch + limb.cap;
                Vector2 p = limb.root + limb.targetAxis * along + new Vector2(padX, padY) +
                    limb.targetNormal * ((i & 2) == 0 ? -reach : reach);
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            int left = Mathf.Clamp(Mathf.FloorToInt(min.x) - 1, 0, width);
            int bottom = Mathf.Clamp(Mathf.FloorToInt(min.y) - 1, 0, height);
            int right = Mathf.Clamp(Mathf.CeilToInt(max.x) + 2, left, width);
            int top = Mathf.Clamp(Mathf.CeilToInt(max.y) + 2, bottom, height);
            return new RectInt(left, bottom, right - left, top - bottom);
        }

        static bool InTorso(Vector2 point, Vector2 shoulders, Vector2 hips, float radius)
        {
            return point.y >= hips.y - radius * .52f && point.y <= shoulders.y + radius * .5f &&
                DistanceSquared(point, shoulders, hips) < radius * radius;
        }

        static bool NearForeground(byte[] alpha, int width, int height, Vector2 p, float radius)
        {
            if (AlphaAt(alpha, width, height, p) >= 128) return true;
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
                if (AlphaAt(alpha, width, height, p + new Vector2(x, y) * radius) >= 128) return true;
            return false;
        }

        static IEnumerator KeepBody(byte[] alpha, int width, int height, Vector2 center)
        {
            int seed = -1;
            float closest = float.PositiveInfinity;
            var slice = System.Diagnostics.Stopwatch.StartNew();
            int foreground = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    if (alpha[index] < 128) continue;
                    foreground++;
                    float distance = (new Vector2(x, y) - center).sqrMagnitude;
                    if (distance >= closest) continue;
                    seed = index;
                    closest = distance;
                }
                if (slice.ElapsedMilliseconds < WorkSliceMilliseconds) continue;
                yield return null;
                slice.Restart();
            }
            if (seed < 0) { yield return false; yield break; }
            var keep = new bool[alpha.Length];
            var queue = new System.Collections.Generic.Queue<int>();
            keep[seed] = true;
            queue.Enqueue(seed);
            int connected = 0;
            while (queue.Count > 0)
            {
                int index = queue.Dequeue();
                int x = index % width, y = index / width;
                if (x > 0) Visit(index - 1);
                if (x + 1 < width) Visit(index + 1);
                if (y > 0) Visit(index - width);
                if (y + 1 < height) Visit(index + width);
                if ((++connected & 511) != 0 || slice.ElapsedMilliseconds < WorkSliceMilliseconds) continue;
                yield return null;
                slice.Restart();
            }
            if (connected < foreground * .93f) { yield return false; yield break; }
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    if (keep[i] || alpha[i] == 0) continue;
                    if (alpha[i] < 128 && (x > 0 && keep[i - 1] || x + 1 < width && keep[i + 1] ||
                        y > 0 && keep[i - width] || y + 1 < height && keep[i + width])) continue;
                    alpha[i] = 0;
                }
                if (slice.ElapsedMilliseconds < WorkSliceMilliseconds) continue;
                yield return null;
                slice.Restart();
            }
            yield return true;

            void Visit(int index)
            {
                if (keep[index] || alpha[index] < 128) return;
                keep[index] = true;
                queue.Enqueue(index);
            }
        }

        static byte AlphaAt(byte[] alpha, int width, int height, Vector2 p)
        {
            int x = Mathf.RoundToInt(p.x), y = Mathf.RoundToInt(p.y);
            return (uint)x < (uint)width && (uint)y < (uint)height ? alpha[y * width + x] : (byte)0;
        }

        static Color32 ColorAt(Color32[] pixels, int width, int height, Vector2 p)
        {
            float x = Mathf.Clamp(p.x, 0, width - 1), y = Mathf.Clamp(p.y, 0, height - 1);
            int left = (int)x, bottom = (int)y;
            int right = Mathf.Min(left + 1, width - 1), top = Mathf.Min(bottom + 1, height - 1);
            return Color32.Lerp(Color32.Lerp(pixels[bottom * width + left], pixels[bottom * width + right], x - left),
                Color32.Lerp(pixels[top * width + left], pixels[top * width + right], x - left), y - bottom);
        }

        static float DistanceSquared(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 direction = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(point - a, direction) / Mathf.Max(.001f, direction.sqrMagnitude));
            return (point - a - t * direction).sqrMagnitude;
        }
    }
}
