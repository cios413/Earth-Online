using System;
using System.Collections.Generic;
using UnityEngine;

namespace YoloSegmentation
{
    /// <summary>
    /// One decoded instance. The box and mask are expressed in source camera frame
    /// pixel space (x: 0..width-1 left to right, y: 0..height-1 bottom to top, the same
    /// convention as Texture2D.GetPixels32). Stage D maps these to screen space.
    /// </summary>
    public struct DetectedInstance
    {
        public int classId;
        public float confidence;
        public float boxMinX;
        public float boxMinY;
        public float boxMaxX;
        public float boxMaxY;
        public byte[] mask;
        public int maskWidth;
        public int maskHeight;
    }

    /// <summary>
    /// Phase C: decodes YOLOv8/YOLO11 segmentation ONNX outputs into per-instance masks.
    /// Output0 layout is [1, 4 + nc + nm, A]: box (cx, cy, w, h in input pixels), class
    /// scores (already sigmoided), mask coefficients (raw). Output1 is the mask prototype
    /// [1, nm, mh, mw]. The structural sizes (A, nm, nc, mh, mw) are derived from the
    /// actual array lengths and the model input size, not hardcoded.
    /// </summary>
    public static class SegDecoder
    {
        struct Candidate
        {
            public float cx, cy, w, h;
            public int classId;
            public float score;
            public float[] coeffs;
        }

        public static bool TryDecode(
            float[] output0,
            float[] output1,
            LetterboxInfo letterbox,
            float confidenceThreshold,
            float iouThreshold,
            float maskThreshold,
            List<DetectedInstance> results,
            out string error)
        {
            error = null;
            results.Clear();

            if (output0 == null || output1 == null)
            {
                error = "Output tensors are null.";
                return false;
            }

            var inputW = letterbox.inputWidth;
            var inputH = letterbox.inputHeight;

            // YOLO anchors: three detection scales at strides 8, 16 and 32.
            var A = (inputW / 8) * (inputH / 8) + (inputW / 16) * (inputH / 16) + (inputW / 32) * (inputH / 32);
            if (A <= 0 || output0.Length % A != 0)
            {
                error = "output0 length=" + output0.Length + " does not match anchor count A=" + A +
                        " (input " + inputW + "x" + inputH + "), output1 length=" + output1.Length;
                return false;
            }

            var C = output0.Length / A;
            var mw = inputW / 4;
            var mh = inputH / 4;
            var protoPixels = mw * mh;
            if (protoPixels <= 0 || output1.Length % protoPixels != 0)
            {
                error = "output1 length does not match the expected prototype size " + mw + "x" + mh;
                return false;
            }

            var nm = output1.Length / protoPixels;
            var nc = C - 4 - nm;
            if (nc < 1)
            {
                error = "Derived class count is invalid (nc=" + nc + ").";
                return false;
            }

            Debug.Log("SegDecoder: A=" + A + " C=" + C + " nc=" + nc + " nm=" + nm +
                      " proto=" + mw + "x" + mh);

            var candidates = new List<Candidate>();

            for (var a = 0; a < A; a++)
            {
                var bestClass = 0;
                var bestScore = float.NegativeInfinity;
                for (var c = 0; c < nc; c++)
                {
                    var score = output0[(4 + c) * A + a];
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestClass = c;
                    }
                }

                if (bestScore < confidenceThreshold)
                    continue;

                var coeffs = new float[nm];
                for (var k = 0; k < nm; k++)
                    coeffs[k] = output0[(4 + nc + k) * A + a];

                candidates.Add(new Candidate
                {
                    cx = output0[0 * A + a],
                    cy = output0[1 * A + a],
                    w = output0[2 * A + a],
                    h = output0[3 * A + a],
                    classId = bestClass,
                    score = bestScore,
                    coeffs = coeffs
                });
            }

            candidates.Sort((x, y) => y.score.CompareTo(x.score));

            var kept = new List<Candidate>();
            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                var suppressed = false;
                foreach (var selected in kept)
                {
                    if (selected.classId == candidate.classId && IoU(candidate, selected) > iouThreshold)
                    {
                        suppressed = true;
                        break;
                    }
                }

                if (!suppressed)
                    kept.Add(candidate);
            }

            foreach (var candidate in kept)
            {
                var instance = BuildMask(candidate, output1, nm, mw, mh, letterbox, maskThreshold);
                if (instance.HasValue)
                    results.Add(instance.Value);
            }

            return true;
        }

        static DetectedInstance? BuildMask(
            Candidate candidate,
            float[] proto,
            int nm,
            int mw,
            int mh,
            LetterboxInfo info,
            float maskThreshold)
        {
            // Prototype layout: [1, nm, mh, mw] row-major, plane k offset = k * (mh*mw).
            var protoPixels = mh * mw;
            var maskRes = new float[protoPixels];
            for (var p = 0; p < protoPixels; p++)
            {
                var dot = 0f;
                for (var k = 0; k < nm; k++)
                    dot += candidate.coeffs[k] * proto[k * protoPixels + p];
                maskRes[p] = 1f / (1f + Mathf.Exp(-dot));
            }

            var x1 = Mathf.Clamp(candidate.cx - candidate.w * 0.5f, 0f, info.inputWidth - 1f);
            var x2 = Mathf.Clamp(candidate.cx + candidate.w * 0.5f, 0f, info.inputWidth - 1f);
            var y1 = Mathf.Clamp(candidate.cy - candidate.h * 0.5f, 0f, info.inputHeight - 1f);
            var y2 = Mathf.Clamp(candidate.cy + candidate.h * 0.5f, 0f, info.inputHeight - 1f);

            var scaleX = (float)mw / info.inputWidth;
            var scaleY = (float)mh / info.inputHeight;
            var mx1 = x1 * scaleX;
            var mx2 = x2 * scaleX;
            var my1 = y1 * scaleY;
            var my2 = y2 * scaleY;

            var sourceWidth = info.sourceWidth;
            var sourceHeight = info.sourceHeight;
            var mask = new byte[sourceWidth * sourceHeight];

            for (var y = 0; y < sourceHeight; y++)
            {
                var yIn = y * info.scale + info.padY;
                var yMask = yIn * scaleY;

                for (var x = 0; x < sourceWidth; x++)
                {
                    var xIn = x * info.scale + info.padX;
                    var xMask = xIn * scaleX;

                    if (xMask < mx1 || xMask > mx2 || yMask < my1 || yMask > my2)
                        continue; // outside the detection box, stays zero

                    var value = SampleMaskRes(maskRes, mw, mh, xMask, yMask);
                    mask[y * sourceWidth + x] = value > maskThreshold ? (byte)255 : (byte)0;
                }
            }

            return new DetectedInstance
            {
                classId = candidate.classId,
                confidence = candidate.score,
                boxMinX = (x1 - info.padX) / info.scale,
                boxMinY = (y1 - info.padY) / info.scale,
                boxMaxX = (x2 - info.padX) / info.scale,
                boxMaxY = (y2 - info.padY) / info.scale,
                mask = mask,
                maskWidth = sourceWidth,
                maskHeight = sourceHeight
            };
        }

        static float SampleMaskRes(float[] maskRes, int mw, int mh, float x, float y)
        {
            var x0 = Mathf.Clamp(Mathf.FloorToInt(x), 0, mw - 1);
            var y0 = Mathf.Clamp(Mathf.FloorToInt(y), 0, mh - 1);
            var x1 = Mathf.Min(x0 + 1, mw - 1);
            var y1 = Mathf.Min(y0 + 1, mh - 1);
            var tx = x - x0;
            var ty = y - y0;

            var top = Mathf.Lerp(maskRes[y0 * mw + x0], maskRes[y0 * mw + x1], tx);
            var bottom = Mathf.Lerp(maskRes[y1 * mw + x0], maskRes[y1 * mw + x1], tx);
            return Mathf.Lerp(top, bottom, ty);
        }

        static float IoU(Candidate a, Candidate b)
        {
            var ax1 = a.cx - a.w * 0.5f;
            var ay1 = a.cy - a.h * 0.5f;
            var ax2 = a.cx + a.w * 0.5f;
            var ay2 = a.cy + a.h * 0.5f;

            var bx1 = b.cx - b.w * 0.5f;
            var by1 = b.cy - b.h * 0.5f;
            var bx2 = b.cx + b.w * 0.5f;
            var by2 = b.cy + b.h * 0.5f;

            var interX1 = Mathf.Max(ax1, bx1);
            var interY1 = Mathf.Max(ay1, by1);
            var interX2 = Mathf.Min(ax2, bx2);
            var interY2 = Mathf.Min(ay2, by2);
            var interW = Mathf.Max(0f, interX2 - interX1);
            var interH = Mathf.Max(0f, interY2 - interY1);
            var inter = interW * interH;

            var areaA = Mathf.Max(0f, ax2 - ax1) * Mathf.Max(0f, ay2 - ay1);
            var areaB = Mathf.Max(0f, bx2 - bx1) * Mathf.Max(0f, by2 - by1);
            var union = areaA + areaB - inter;
            return union <= 0f ? 0f : inter / union;
        }
    }
}
