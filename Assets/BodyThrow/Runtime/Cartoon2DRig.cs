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
        CartoonJellyShell m_Jelly;
        JellySkeletonPalette m_Palette;

        public int PersonId { get; private set; }
        public JellySkeletonPalette Palette => m_Palette;
        public CartoonJellyShell JellyShell => m_Jelly;

        public void Configure(int personId, Transform parent, Sprite bodySprite, Sprite headSprite,
            Sprite torsoSprite = null, Sprite pairedBoneSprite = null)
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
            m_Jelly = gameObject.AddComponent<CartoonJellyShell>();
            m_Jelly.Configure(transform);
            m_Head = NewPart("Head", headSprite, Color.white, true);
            m_Torso = NewPart("Torso", torsoSprite != null ? torsoSprite : bodySprite, Color.white);
            Sprite lowerBone = pairedBoneSprite != null ? pairedBoneSprite : bodySprite;
            Add(5, 7, .34f, Color.white, bodySprite); Add(7, 9, .32f, Color.white, lowerBone);
            Add(6, 8, .34f, Color.white, bodySprite); Add(8, 10, .32f, Color.white, lowerBone);
            Add(11, 13, .36f, Color.white, bodySprite); Add(13, 15, .34f, Color.white, lowerBone);
            Add(12, 14, .36f, Color.white, bodySprite); Add(14, 16, .34f, Color.white, lowerBone);
            // Bones always render over a single connected jelly layer.
            m_Head.transform.SetAsLastSibling();
            m_Configured = true;
        }

        public void SetPalette(JellySkeletonPalette palette)
        {
            m_Palette = palette;
            if (m_Jelly != null) m_Jelly.SetPalette(palette);
        }

        public void ReassignPerson(int personId) => PersonId = personId;

        Cartoon2DPart NewPart(string partName, Sprite sprite, Color color, bool head = false)
        {
            var go = new GameObject(partName, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            go.AddComponent<Cartoon2DPart>().Configure(go.transform, sprite, color, head);
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
                m_Head.SetHead(localHead, Vector2.zero, Mathf.Max(18f, diameter*1.12f), alpha, false);
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
                m_Torso.SetSegment(shoulderCenter, hips, Vector2.zero, Vector2.Distance(ls, rs)*1.18f, alpha);
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
            UpdateJelly(poseView, camera, canvasRect, origin, mirror, alpha);
        }

        void UpdateJelly(BodyPoseResult pose, Camera camera, RectTransform canvas, Vector2 origin, bool mirror, float alpha)
        {
            m_Jelly.BeginPose();
            bool leftShoulder = BodyPoseGeometry.HasPoint(pose,5) && TryLocal(pose.keypoints[5],camera,canvas,out _);
            bool rightShoulder = BodyPoseGeometry.HasPoint(pose,6) && TryLocal(pose.keypoints[6],camera,canvas,out _);
            float width = Mathf.Max(24f, m_Head.IsVisible ? m_Head.DisplaySize.x*1.5f : 48f);
            if (leftShoulder && rightShoulder)
            {
                TryLocal(pose.keypoints[5],camera,canvas,out var ls);
                TryLocal(pose.keypoints[6],camera,canvas,out var rs);
                width = Mathf.Max(24f,Vector2.Distance(ls,rs));
            }
            if(m_Torso.IsVisible)
            {
                var shoulders=m_Torso.DisplayStart;
                var hips=m_Torso.DisplayEnd;
                // Broad shoulders and belly fill the measured skeleton plus a soft outer margin.
                m_Jelly.AddSegment(shoulders, hips, width*.59f, width*.49f, false);
                if(m_Head.IsVisible)
                    m_Jelly.AddSegment(shoulders,m_Head.DisplayCenter,width*.25f,width*.25f,false);
            }
            if(m_Head.IsVisible)m_Jelly.AddHead(m_Head.DisplayCenter,m_Head.DisplaySize.x*.64f);
            foreach(var part in m_Parts)
            {
                if(!part.visual.IsVisible)continue;
                bool leg=part.first>=11;
                float radius=width*(leg?.235f:.19f);
                float taper=part.first==7||part.first==8||part.first==13||part.first==14?.9f:1f;
                m_Jelly.AddSegment(part.visual.DisplayStart,part.visual.DisplayEnd,radius,radius*taper,true);
            }
            m_Jelly.FinishPose(alpha,width*.13f);
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
            if (!visible && m_Jelly != null) m_Jelly.Hide();
        }
    }
}
