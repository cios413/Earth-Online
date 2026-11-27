using UnityEngine;

namespace EarthOL.BodyThrow
{
    public struct BodyPointerFrame
    {
        static int s_LastPrimaryId;
        public int id;
        public Vector2 position;
        public bool down, held, released, canceled;

        public static bool TryRead(out BodyPointerFrame frame)
        {
            frame = default;
#if ENABLE_INPUT_SYSTEM
            var screen = UnityEngine.InputSystem.Touchscreen.current;
            if (screen != null)
            {
                var touch = screen.primaryTouch;
                bool down = touch.press.wasPressedThisFrame;
                bool held = touch.press.isPressed;
                bool released = touch.press.wasReleasedThisFrame;
                if (down || held || released)
                {
                    int id = touch.touchId.ReadValue();
                    if (down || held) s_LastPrimaryId = id;
                    if (id == 0 && released) id = s_LastPrimaryId;
                    frame = new BodyPointerFrame
                    {
                        id = id, position = touch.position.ReadValue(),
                        down = down, held = held, released = released,
                        canceled = touch.phase.ReadValue() == UnityEngine.InputSystem.TouchPhase.Canceled
                    };
                    return true;
                }
            }
            if (!Application.isMobilePlatform)
            {
                var mouse = UnityEngine.InputSystem.Mouse.current;
                if (mouse != null && (mouse.leftButton.isPressed || mouse.leftButton.wasPressedThisFrame || mouse.leftButton.wasReleasedThisFrame))
                {
                    frame = new BodyPointerFrame
                    {
                        id = -1, position = mouse.position.ReadValue(), down = mouse.leftButton.wasPressedThisFrame,
                        held = mouse.leftButton.isPressed, released = mouse.leftButton.wasReleasedThisFrame
                    };
                    return true;
                }
            }
#endif
            return false;
        }
    }
}
