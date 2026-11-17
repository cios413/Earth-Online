using System.Collections.Generic;
using UnityEngine;

namespace EarthOL.BodyThrow
{
    [DisallowMultipleComponent]
    public sealed class Cartoon2DRig : MonoBehaviour
    {
        sealed class Part
        {
            public int first, second;
            public float width;
            public Cartoon2DPart visual;
            public Part(int first, int second, float width, Cartoon2DPart visual)
            { this.first = first; this.second = second; this.width = width; this.visual = visual; }
        }

        readonly List<Part> m_Parts = new List<Part>(9);
        Cartoon2DPart m_Head;
        Cartoon2DPart m_Torso;
        RectTransform m_Root;
        bool m_Configured;

        public int PersonId { get; private set; }

        public void Configure(int personId, Transform parent, Sprite bodySprite, Sprite headSprite)
        {
            PersonId = personId;
            if (m_Configured)
            {
                transform.SetParent(parent, false);
                gameObject.SetActive(true);
                return;
            }
            m_Root = (RectTransform)transform;
            m_Root.SetParent(parent, false);
            m_Root.anchorMin = m_Root.anchorMax = new Vector2(0.5f, 0.5f);
            m_Root.pivot = new Vector2(0.5f, 0.5f);
            Color torso = new Color(0.11f, 0.59f, 0.54f, 1f);
            Color leftArm = new Color(0.94f, 0.42f, 0.27f, 1f);
            Color rightArm = new Color(0.95f, 0.68f, 0.19f, 1f);
            Color leftLeg = new Color(0.25f, 0.57f, 0.78f, 1f);
            Color rightLeg = new Color(0.40f, 0.48f, 0.77f, 1f);
            m_Head = NewPart("Head", headSprite, new Color(0.96f, 0.73f, 0.57f, 1f), true);
            m_Torso = NewPart("Torso", bodySprite, torso);
            Add(5, 7, 0.30f, leftArm, bodySprite); Add(7, 9, 0.24f, leftArm, bodySprite);
            Add(6, 8, 0.30f, rightArm, bodySprite); Add(8, 10, 0.24f, rightArm, bodySprite);
            Add(11, 13, 0.34f, leftLeg, bodySprite); Add(13, 15, 0.27f, leftLeg, bodySprite);
            Add(12, 14, 0.34f, rightLeg, bodySprite); Add(14, 16, 0.27f, rightLeg, bodySprite);
            m_Configured = true;
        }

        public void ReassignPerson(int personId) => PersonId = personId;

        Cartoon2DPart NewPart(string partName, Sprite sprite, Color color, bool head = false)
        {
            var go = new GameObject(partName, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            go.AddComponent<Cartoon2DPart>().Configure(transform, sprite, color, head);
            return go.GetComponent<Cartoon2DPart>();
        }

        void Add(int first, int second, float width, Color color, Sprite sprite)
        {
            m_Parts.Add(new Part(first, second, width, NewPart(first + "-" + second, sprite, color)));
        }

        public void UpdatePose(PersonPoseData pose, Camera camera, RectTransform canvasRect, float canvasScale)
        {
            if (pose == null || camera == null) { SetVisible(false); return; }
            Vector3 boxCenterScreen = camera.ViewportToScreenPoint(pose.screenRect.center);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, boxCenterScreen, null, out var origin))
                return;
            m_Root.anchoredPosition = origin;
            bool mirror = ResolveMirror(pose.facing);
            // Mirror artwork as one rig; compensate all local coordinates so the
            // reflected artwork still lands on the same measured screen joints.
            m_Root.localScale = new Vector3(mirror ? -1f : 1f, 1f, 1f);
            float alpha = Mathf.Clamp01(0.25f + pose.trackingConfidence * 0.75f);
            float aspect = (float)camera.pixelWidth / Mathf.Max(1, camera.pixelHeight);
            var poseView = pose.AsPoseView();
            bool hasHead = BodyPoseGeometry.TryFitHead(poseView, aspect, out var headCenter, out float headRadius);
            if (hasHead && TryLocal(headCenter, camera, canvasRect, out var localHead))
            {
                Vector2 upper = headCenter + Vector2.up * headRadius;
                float diameter = TryLocal(upper, camera, canvasRect, out var localUpper)
                    ? Vector2.Distance(localHead, localUpper) * 2f : 28f;
                localHead -= origin;
                if (mirror) localHead.x = -localHead.x;
                m_Head.SetHead(localHead, Vector2.zero, Mathf.Max(18f, diameter), alpha, false);
            }
            else m_Head.SetVisible(false);

            if (BodyPoseGeometry.HasPoint(poseView, 5) && BodyPoseGeometry.HasPoint(poseView, 6) &&
                BodyPoseGeometry.HasPoint(poseView, 11) && BodyPoseGeometry.HasPoint(poseView, 12) &&
                TryLocal(pose.keypoints[5], camera, canvasRect, out var ls) && TryLocal(pose.keypoints[6], camera, canvasRect, out var rs) &&
                TryLocal((pose.keypoints[11] + pose.keypoints[12]) * 0.5f, camera, canvasRect, out var hips))
            {
                Vector2 shoulderCenter = (ls + rs) * 0.5f - origin;
                hips -= origin;
                if (mirror) { shoulderCenter.x = -shoulderCenter.x; hips.x = -hips.x; }
                m_Torso.SetSegment(shoulderCenter, hips, Vector2.zero, Vector2.Distance(ls, rs), alpha);
            }
            else m_Torso.SetVisible(false);

            foreach (var part in m_Parts)
            {
                if (!BodyPoseGeometry.HasPoint(poseView, part.first) ||
                    !BodyPoseGeometry.HasPoint(poseView, part.second) ||
                    !TryLocal(pose.keypoints[part.first], camera, canvasRect, out var a) ||
                    !TryLocal(pose.keypoints[part.second], camera, canvasRect, out var b))
                {
                    part.visual.SetVisible(false);
                    continue;
                }
                a -= origin;
                b -= origin;
                if (mirror) { a.x = -a.x; b.x = -b.x; }
                float width = Mathf.Max(6f, Vector2.Distance(a, b) * part.width);
                part.visual.SetSegment(a, b, Vector2.zero, width, alpha);
            }
        }

        static bool ResolveMirror(PersonFacing facing) => facing == PersonFacing.Left || facing == PersonFacing.Back;

        static bool TryLocal(Vector2 viewport, Camera camera, RectTransform canvas, out Vector2 local)
        {
            Vector3 screen = camera.ViewportToScreenPoint(new Vector3(viewport.x, viewport.y, 0f));
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, screen, null, out local);
        }

        public void SetVisible(bool visible)
        {
            if (m_Head != null) m_Head.SetVisible(visible);
            if (m_Torso != null) m_Torso.SetVisible(visible);
            foreach (var part in m_Parts) part.visual.SetVisible(visible);
        }
    }
}
