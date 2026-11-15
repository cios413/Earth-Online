using System.Collections.Generic;
using EarthOL.ARCore;
using UnityEngine;

namespace EarthOL.BodyThrow
{
    /// <summary>Activates the live 2D sticker when a tracked person is tapped.</summary>
    [DisallowMultipleComponent]
    public sealed class PersonClickController : MonoBehaviour
    {
        const float MaximumTapDuration = 0.55f;
        const float MaximumTapMotion = 42f;
        Camera m_Camera;
        PersonTrackingManager m_Tracking;
        bool m_Pressed;
        int m_PointerId;
        Vector2 m_Origin;
        float m_PressTime;
        Cartoon2DCanvas m_Cartoons;
        readonly Dictionary<int, float> m_LastActivated = new Dictionary<int, float>();
        public int ActivatedPersonCount => m_Cartoons != null ? m_Cartoons.SelectedPersonCount : 0;

        public void Configure(Camera camera, PersonTrackingManager tracking, Cartoon2DCanvas cartoons)
        {
            m_Camera = camera;
            m_Tracking = tracking;
            m_Cartoons = cartoons;
        }

        private void Update()
        {
            if (!ARCoreModeOverlay.IsStickerMode) { m_Pressed = false; return; }
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
            TryActivateAt(frame.position);
        }

        public bool TryActivateAt(Vector2 screenPosition)
        {
            if (m_Camera == null || m_Tracking == null || m_Cartoons == null ||
                !m_Tracking.isActiveAndEnabled || ARCoreModeOverlay.IsPointOnOverlay(screenPosition)) return false;
            Vector3 viewport = m_Camera.ScreenToViewportPoint(screenPosition);
            Vector2 click = new Vector2(viewport.x, viewport.y);
            PersonPoseData person = m_Tracking.Pick(click);
            if (person == null)
            {
                return false;
            }
            float now = Time.realtimeSinceStartup;
            if (m_LastActivated.TryGetValue(person.personId, out float last) && now - last < 1.0f) return false;
            m_LastActivated[person.personId] = now;
            m_Cartoons.ActivatePerson(person.personId);
            return true;
        }

        public void ResetInput()
        {
            m_Pressed = false;
            m_LastActivated.Clear();
        }

        void OnDisable() => ResetInput();
    }
}
