using UnityEngine;

namespace EarthOL.BodyThrow
{
    public static class PersonWorldPositionCalculator
    {
        public static bool TryCalculate(Camera camera, Vector2 viewport, float distance, out Pose pose)
        {
            pose = default;
            if (camera == null || distance <= 0f || viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f)
                return false;
            Vector3 position = camera.ViewportToWorldPoint(new Vector3(viewport.x, viewport.y, distance));
            pose = new Pose(position, camera.transform.rotation);
            return IsFinite(position);
        }

        public static Vector3 ViewportPointToWorld(Camera camera, Vector2 viewport, float distance) =>
            camera.ViewportToWorldPoint(new Vector3(viewport.x, viewport.y, distance));

        static bool IsFinite(Vector3 v) => !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) &&
            !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
    }
}
