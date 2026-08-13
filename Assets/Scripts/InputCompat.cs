using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Thin input abstraction — Steam Deck / gamepad players use the same
// prompts as keyboard players without rewriting fifty Input.GetKeyDown
// call sites. The gameplay code keeps calling Input.GetKeyDown /
// Input.GetKey; this helper adds a parallel gamepad path that
// interested call sites can opt into with a one-liner.
//
// Mapping (Xbox layout, PS/Switch translate 1:1 via Steam Input):
//   A / South    -> Space / E / Return       (Submit / interact)
//   B / East     -> Escape / Cancel          (Back / close panel)
//   X / West     -> F                        (Contextual / execute)
//   Y / North    -> R                        (Reload / secondary)
//   Start        -> Escape                   (Pause menu)
//   RB / R1      -> Q                        (Grenade / secondary)
//   LT / L2      -> Left Shift               (Dash / sprint)
//   D-Pad        -> WASD                     (Menus)
//
// Steam Input handles remapping to any controller layout via the
// Steamworks web dashboard — this file is just the runtime bridge.
public static class InputCompat
{
    // ---- Buttons ----
    public static bool SubmitDown()  => Input.GetKeyDown(KeyCode.Space)  || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.E) || PadDown(PadButton.South);
    public static bool CancelDown()  => Input.GetKeyDown(KeyCode.Escape) || PadDown(PadButton.East);
    public static bool InteractDown()=> Input.GetKeyDown(KeyCode.F)      || PadDown(PadButton.West);
    public static bool SecondaryDown()=>Input.GetKeyDown(KeyCode.R)      || PadDown(PadButton.North);
    public static bool PauseDown()   => Input.GetKeyDown(KeyCode.Escape) || PadDown(PadButton.Start);
    public static bool DashDown()    => Input.GetKeyDown(KeyCode.LeftShift) || PadDown(PadButton.RightTrigger);
    public static bool GrenadeHeld() => Input.GetKey(KeyCode.G)          || PadHeld(PadButton.RightShoulder);
    public static bool MapDown()     => Input.GetKeyDown(KeyCode.M)      || PadDown(PadButton.Select);

    // ---- Movement + look ----
    // Reads keyboard WASD + gamepad left stick, returns whichever has
    // a larger magnitude — feels natural on both.
    public static Vector2 MoveAxis()
    {
        Vector2 kbd = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#if ENABLE_INPUT_SYSTEM
        if (Gamepad.current != null)
        {
            Vector2 pad = Gamepad.current.leftStick.ReadValue();
            if (pad.sqrMagnitude > kbd.sqrMagnitude) return pad;
        }
#endif
        return kbd;
    }

    public static Vector2 LookAxis()
    {
        Vector2 mouse = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
#if ENABLE_INPUT_SYSTEM
        if (Gamepad.current != null && mouse.sqrMagnitude < 0.001f)
        {
            // Right stick with a small deadzone; Steam Input sensitivity
            // curve handles the "feel".
            Vector2 pad = Gamepad.current.rightStick.ReadValue();
            if (pad.sqrMagnitude > 0.01f) return pad * 3.5f;
        }
#endif
        return mouse;
    }

    public static bool AnyControllerConnected
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            return Gamepad.current != null;
#else
            return false;
#endif
        }
    }

    // ---- Rumble ----
    // Fire a gamepad rumble pulse. Respects the Controller Vibration
    // accessibility toggle — a no-op when it's off or no pad is present.
    // A tiny MonoBehaviour bootstrap auto-stops the motors after
    // `duration` so callers stay one-liners.
    public static void Rumble(float lowFreq, float highFreq, float duration)
    {
#if ENABLE_INPUT_SYSTEM
        if (!GameplaySettings.ControllerVibration) return;
        var gp = Gamepad.current;
        if (gp == null) return;
        gp.SetMotorSpeeds(Mathf.Clamp01(lowFreq), Mathf.Clamp01(highFreq));
        RumbleStopper.Schedule(gp, duration);
#endif
    }

#if ENABLE_INPUT_SYSTEM
    // Persistent helper that zeroes the motors after a delay (real-time,
    // so a pause/hit-stop doesn't leave the pad buzzing).
    private class RumbleStopper : MonoBehaviour
    {
        private static RumbleStopper s_inst;
        private Gamepad pad;
        private float stopAt;

        public static void Schedule(Gamepad gp, float duration)
        {
            if (s_inst == null)
            {
                var go = new GameObject("[RumbleStopper]");
                DontDestroyOnLoad(go);
                s_inst = go.AddComponent<RumbleStopper>();
            }
            s_inst.pad = gp;
            s_inst.stopAt = Time.realtimeSinceStartup + duration;
            s_inst.enabled = true;
        }

        private void Update()
        {
            if (Time.realtimeSinceStartup < stopAt) return;
            if (pad != null) pad.SetMotorSpeeds(0f, 0f);
            enabled = false;
        }

        private void OnDisable() { if (pad != null) pad.SetMotorSpeeds(0f, 0f); }
    }
#endif

    // ---- Gamepad button abstraction ----
    public enum PadButton { South, East, West, North, Start, Select, LeftShoulder, RightShoulder, LeftTrigger, RightTrigger }

    private static bool PadDown(PadButton b)
    {
#if ENABLE_INPUT_SYSTEM
        var gp = Gamepad.current;
        if (gp == null) return false;
        return b switch
        {
            PadButton.South         => gp.buttonSouth.wasPressedThisFrame,
            PadButton.East          => gp.buttonEast.wasPressedThisFrame,
            PadButton.West          => gp.buttonWest.wasPressedThisFrame,
            PadButton.North         => gp.buttonNorth.wasPressedThisFrame,
            PadButton.Start         => gp.startButton.wasPressedThisFrame,
            PadButton.Select        => gp.selectButton.wasPressedThisFrame,
            PadButton.LeftShoulder  => gp.leftShoulder.wasPressedThisFrame,
            PadButton.RightShoulder => gp.rightShoulder.wasPressedThisFrame,
            PadButton.LeftTrigger   => gp.leftTrigger.wasPressedThisFrame,
            PadButton.RightTrigger  => gp.rightTrigger.wasPressedThisFrame,
            _ => false,
        };
#else
        return false;
#endif
    }

    private static bool PadHeld(PadButton b)
    {
#if ENABLE_INPUT_SYSTEM
        var gp = Gamepad.current;
        if (gp == null) return false;
        return b switch
        {
            PadButton.South         => gp.buttonSouth.isPressed,
            PadButton.East          => gp.buttonEast.isPressed,
            PadButton.West          => gp.buttonWest.isPressed,
            PadButton.North         => gp.buttonNorth.isPressed,
            PadButton.Start         => gp.startButton.isPressed,
            PadButton.Select        => gp.selectButton.isPressed,
            PadButton.LeftShoulder  => gp.leftShoulder.isPressed,
            PadButton.RightShoulder => gp.rightShoulder.isPressed,
            PadButton.LeftTrigger   => gp.leftTrigger.isPressed,
            PadButton.RightTrigger  => gp.rightTrigger.isPressed,
            _ => false,
        };
#else
        return false;
#endif
    }
}
