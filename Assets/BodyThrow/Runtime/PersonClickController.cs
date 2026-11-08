using System;
using System.Collections.Generic;
using EarthOL.ARCore;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace EarthOL.BodyThrow
{
    /// <summary>Converts a short tap on a live tracked person into an immutable anchored snapshot.</summary>
    [DisallowMultipleComponent]
    public sealed class PersonClickController : MonoBehaviour
    {
        const float MaximumTapDuration = 0.55f;
        const float MaximumTapMotion = 42f;
        Camera m_Camera;
        PersonTrackingManager m_Tracking;
        PersonDistanceEstimator m_Distance;
        Frozen3DPersonBuilder m_Builder;
        ARAnchorManager m_AnchorManager;
        bool m_Pressed;
        int m_PointerId;
        Vector2 m_Origin;
        float m_PressTime;
        Cartoon2DCanvas m_Cartoons;
        readonly Dictionary<int, float> m_LastFrozen = new Dictionary<int, float>();
        public event Action<Frozen3DPersonController> PersonFrozen;
        public int FrozenPersonCount => m_Builder != null ? m_Builder.ModelCount : 0;

        public void Configure(Camera camera, PersonTrackingManager tracking, PersonDistanceEstimator distance,
            Frozen3DPersonBuilder builder, ARAnchorManager anchors, Cartoon2DCanvas cartoons = null)
        {
            m_Camera = camera;
            m_Tracking = tracking;
            m_Distance = distance;
            m_Builder = builder;
            m_AnchorManager = anchors;
            m_Cartoons = cartoons;
        }

        private void Update()
        {
            if (!BodyPointerFrame.TryRead(out var frame)) { m_Pressed = false; return; }
            if (ARCoreModeOverlay.BlocksWorldInput) { m_Pressed = false; return; }
            if (frame.down)
            {
                m_Pressed = !ARCoreModeOverlay.IsPointOnOverlay(frame.position);
                m_PointerId = frame.id;
                m_Origin = frame.position;
                m_PressTime = Time.unscaledTime;
            }
            if (!m_Pressed || frame.id != m_PointerId) return;
            if (frame.canceled) { m_Pressed = false; return; }
            if (!frame.released) return;
            m_Pressed = false;
            if (Time.unscaledTime - m_PressTime > MaximumTapDuration ||
                Vector2.Distance(frame.position, m_Origin) > MaximumTapMotion) return;
            // This click snapshots a fixed 3D model while the independent 2D canvas
            // continues tracking the same real person in either interaction mode.
            TryFreezeAt(frame.position);
        }

        public Frozen3DPersonController TryFreezeAt(Vector2 screenPosition)
        {
            if (m_Camera == null || m_Tracking == null || m_Distance == null || m_Builder == null ||
                !m_Tracking.isActiveAndEnabled || ARCoreModeOverlay.IsPointOnOverlay(screenPosition)) return null;
            Vector3 viewport = m_Camera.ScreenToViewportPoint(screenPosition);
            Vector2 click = new Vector2(viewport.x, viewport.y);
            PersonPoseData person = m_Tracking.Pick(click);
            if (person == null)
            {
                Debug.Log("[FrozenPerson] 点击位置不在当前人物检测框内，没有生成固定模型。");
                return null;
            }
            float now = Time.realtimeSinceStartup;
            if (m_LastFrozen.TryGetValue(person.personId, out float last) && now - last < 1.5f) return null;

            float distance = m_Distance.Estimate(person, out float confidence);
            var snapshot = new FrozenPersonData(person, click, distance, confidence);
            var frozen = m_Builder.Build(snapshot, m_Camera, m_AnchorManager);
            if (frozen != null)
            {
                m_LastFrozen[person.personId] = now;
                m_Cartoons?.ActivatePerson(person.personId);
                Debug.Log($"[FrozenPerson] 点击快照已固定：PersonID={snapshot.PersonId}, depth={distance:F2}m, " +
                    $"confidence={confidence:F2}。2D贴图继续跟踪独立实时数据。");
                PersonFrozen?.Invoke(frozen);
            }
            return frozen;
        }
    }
}
