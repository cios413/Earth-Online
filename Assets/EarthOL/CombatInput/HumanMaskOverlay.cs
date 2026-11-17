using EarthOL.BodyThrow;
using UnityEngine;

namespace EarthOL.CombatInput
{
    /// <summary>
    /// 选中人物后显示一个半透明的人形轮廓（头/躯干/四肢），
    /// 用于直观看到“可被挥扫形变”的区域，和 Li 分支的贴图类似。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HumanMaskOverlay : MonoBehaviour
    {
        [SerializeField] PersonTrackingManager tracking;
        [SerializeField] Camera arCamera;
        [SerializeField] Color maskColor = new Color(0.25f, 0.9f, 0.55f, 0.30f);

        public void Configure(PersonTrackingManager personTracking, Camera camera)
        {
            tracking = personTracking;
            arCamera = camera;
        }

        void OnGUI()
        {
            if (tracking == null || arCamera == null) return;
            var person = tracking.BestPerson();
            if (person == null) return;
            var pose = person.AsPoseView();
            float aspect = (float)arCamera.pixelWidth / Mathf.Max(1f, arCamera.pixelHeight);

            var previous = GUI.color;
            GUI.color = maskColor;

            if (BodyPoseGeometry.TryFitHead(pose, aspect, out var headCenter, out var headRadius))
            {
                Vector2 h = GuiPoint(headCenter);
                float r = ScreenRadius(headCenter, headRadius);
                GUI.DrawTexture(new Rect(h.x - r, h.y - r, r * 2f, r * 2f), Texture2D.whiteTexture);
            }

            if (BodyPoseGeometry.TryFitTorso(pose, aspect, out var shoulders, out var hips, out var torsoRadius))
            {
                float torso = ScreenRadius(shoulders, torsoRadius);
                DrawSegment(shoulders, hips, torso * 2f);
                float limb = torso * 0.55f;
                DrawSegment(pose, 5, 7, limb);
                DrawSegment(pose, 7, 9, limb * 0.8f);
                DrawSegment(pose, 6, 8, limb);
                DrawSegment(pose, 8, 10, limb * 0.8f);
                DrawSegment(pose, 11, 13, limb * 1.1f);
                DrawSegment(pose, 13, 15, limb * 0.9f);
                DrawSegment(pose, 12, 14, limb * 1.1f);
                DrawSegment(pose, 14, 16, limb * 0.9f);
            }

            GUI.color = previous;
        }

        void DrawSegment(BodyPoseResult pose, int a, int b, float thickness)
        {
            if (!BodyPoseGeometry.HasPoint(pose, a) || !BodyPoseGeometry.HasPoint(pose, b)) return;
            DrawSegment(pose.keypoints[a], pose.keypoints[b], thickness);
        }

        void DrawSegment(Vector2 viewportA, Vector2 viewportB, float thickness)
        {
            Vector2 a = GuiPoint(viewportA);
            Vector2 b = GuiPoint(viewportB);
            float length = Vector2.Distance(a, b);
            if (length < 2f || thickness < 1f) return;
            var previous = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg, a);
            GUI.DrawTexture(new Rect(a.x, a.y - thickness * 0.5f, length, thickness), Texture2D.whiteTexture);
            GUI.matrix = previous;
        }

        Vector2 GuiPoint(Vector2 viewport)
        {
            Vector3 screen = arCamera.ViewportToScreenPoint(viewport);
            return new Vector2(screen.x, Screen.height - screen.y);
        }

        float ScreenRadius(Vector2 center, float viewportRadius)
        {
            Vector2 c = GuiPoint(center);
            Vector2 r = GuiPoint(center + Vector2.up * viewportRadius);
            return Vector2.Distance(c, r);
        }
    }
}
