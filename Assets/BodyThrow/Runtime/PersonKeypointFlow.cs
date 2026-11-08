using UnityEngine;

namespace EarthOL.BodyThrow
{
    /// <summary>Small grayscale SAD patch search with forward/backward rejection. No neural inference.</summary>
    public static class PersonKeypointFlow
    {
        const int PatchRadius = 2;
        const int SearchRadius = 5;

        public static bool TryTrack(byte[] previous, byte[] current, int width, int height,
            Vector2 viewport, out Vector2 displacement, out float confidence)
        {
            displacement = Vector2.zero;
            confidence = 0f;
            int x = Mathf.RoundToInt(viewport.x * (width - 1)), y = Mathf.RoundToInt(viewport.y * (height - 1));
            if (!Inside(x, y, width, height, SearchRadius + PatchRadius)) return false;
            int min = 255, max = 0;
            for (int dy = -PatchRadius; dy <= PatchRadius; dy++) for (int dx = -PatchRadius; dx <= PatchRadius; dx++)
            {
                int value = previous[(y + dy) * width + x + dx];
                min = Mathf.Min(min, value); max = Mathf.Max(max, value);
            }
            if (max - min < 12) return false;
            Search(previous, current, width, height, x, y, out int tx, out int ty, out float error);
            if (error > 28f) return false;
            Search(current, previous, width, height, tx, ty, out int bx, out int by, out float reverseError);
            if (Mathf.Abs(bx - x) + Mathf.Abs(by - y) > 2 || reverseError > 28f) return false;
            displacement = new Vector2((tx - x) / (float)(width - 1), (ty - y) / (float)(height - 1));
            confidence = 1f - error / 32f;
            return true;
        }

        static void Search(byte[] source, byte[] destination, int width, int height, int x, int y,
            out int targetX, out int targetY, out float error)
        {
            targetX = x; targetY = y; float best = float.MaxValue; float raw = float.MaxValue;
            for (int dy = -SearchRadius; dy <= SearchRadius; dy++) for (int dx = -SearchRadius; dx <= SearchRadius; dx++)
            {
                int tx = x + dx, ty = y + dy;
                if (!Inside(tx, ty, width, height, PatchRadius)) continue;
                int difference = 0;
                for (int py = -PatchRadius; py <= PatchRadius; py++) for (int px = -PatchRadius; px <= PatchRadius; px++)
                    difference += Mathf.Abs(source[(y + py) * width + x + px] - destination[(ty + py) * width + tx + px]);
                float sad = difference / 25f;
                float cost = sad + (dx * dx + dy * dy) * 0.08f;
                if (cost < best) { best = cost; raw = sad; targetX = tx; targetY = ty; }
            }
            error = raw;
        }

        static bool Inside(int x, int y, int width, int height, int margin) =>
            x >= margin && y >= margin && x < width - margin && y < height - margin;
    }
}
