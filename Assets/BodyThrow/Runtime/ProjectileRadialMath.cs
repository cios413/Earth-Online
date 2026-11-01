using UnityEngine;

namespace EarthOL.BodyThrow
{
    public static class ProjectileRadialMath
    {
        // Screen-space directions, starting at 12 o'clock and proceeding clockwise.
        public static int IndexAt(Vector2 displacement, float deadZone)
        {
            if (displacement.magnitude < deadZone) return -1;
            float degrees = Mathf.Atan2(displacement.x, displacement.y) * Mathf.Rad2Deg;
            return Mathf.FloorToInt(Mathf.Repeat(degrees + 45f, 360f) / 90f);
        }
    }
}
