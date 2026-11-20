using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;

namespace SurviveI95.EditorTools
{
    /// <summary>
    /// A pretend steering wheel for testing the wheel setup without one: it reports like many real wheels do, the
    /// wheel on the stick's x, gas and brake on two separate axes that read 1 with the pedal up and -1 floored, and
    /// twelve buttons. Driven by the live bridge (`fakewheel`).
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    public struct TestWheelState : IInputStateTypeInfo
    {
        public FourCC format => new FourCC('T', 'W', 'H', 'L');
        [InputControl(name = "stick", layout = "Stick", usage = "Primary2DMotion")] [FieldOffset(0)] public Vector2 stick;
        [InputControl(name = "z", layout = "Axis")] [FieldOffset(8)] public float z;
        [InputControl(name = "rz", layout = "Axis")] [FieldOffset(12)] public float rz;
        [InputControl(name = "slider", layout = "Axis")] [FieldOffset(16)] public float slider;
        [InputControl(name = "trigger", layout = "Button", bit = 0, usage = "PrimaryTrigger")]
        [InputControl(name = "button2", layout = "Button", bit = 1)] [InputControl(name = "button3", layout = "Button", bit = 2)]
        [InputControl(name = "button4", layout = "Button", bit = 3)] [InputControl(name = "button5", layout = "Button", bit = 4)]
        [InputControl(name = "button6", layout = "Button", bit = 5)] [InputControl(name = "button7", layout = "Button", bit = 6)]
        [InputControl(name = "button8", layout = "Button", bit = 7)] [InputControl(name = "button9", layout = "Button", bit = 8)]
        [InputControl(name = "button10", layout = "Button", bit = 9)] [InputControl(name = "button11", layout = "Button", bit = 10)]
        [InputControl(name = "button12", layout = "Button", bit = 11)]
        [FieldOffset(20)] public uint buttons;
    }

    [InputControlLayout(stateType = typeof(TestWheelState), displayName = "Test Wheel")]
    public class TestWheel : Joystick
    {
        static bool registered;
        static TestWheelState state = new TestWheelState { z = 1f, rz = 1f };

        public static string Command(string[] a)
        {
            if (!registered) { InputSystem.RegisterLayout<TestWheel>(); registered = true; } // registering again re-creates the device
            var dev = InputSystem.GetDevice<TestWheel>();
            switch (a.Length > 2 ? a[2] : "")
            {
                case "add":
                    // The editor normally drops input while the game view isn't focused; tests run unfocused (in memory only).
                    InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                    InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                    if (dev == null) InputSystem.AddDevice<TestWheel>();
                    state = new TestWheelState { z = 1f, rz = 1f };
                    return "test wheel plugged in";
                case "read":
                    if (dev == null) return "no test wheel";
                    return $"x={dev.stick.x.ReadValue():F2} z={((UnityEngine.InputSystem.Controls.AxisControl)dev["z"]).ReadValue():F2} enabled={dev.enabled} canBg={dev.canRunInBackground} devices={string.Join(",", InputSystem.devices)} same={(SurviveI95.Game.Wheel.Device == dev)} bg={InputSystem.settings.backgroundBehavior} ed={InputSystem.settings.editorInputBehaviorInPlayMode} focus={Application.isFocused} runBg={Application.runInBackground}";
                case "remove":
                    if (dev != null) InputSystem.RemoveDevice(dev);
                    PlayerPrefs.DeleteKey("wheel/Test Wheel");
                    return "test wheel removed, its setup forgotten";
                case "set": // steer -1..1, gas 0..1, brake 0..1, buttons bit mask
                    if (dev == null) return "no test wheel";
                    float F(int i) => float.Parse(a[i], System.Globalization.CultureInfo.InvariantCulture);
                    state.stick = new Vector2(F(3), 0f);
                    state.z = 1f - 2f * F(4); state.rz = 1f - 2f * F(5);
                    state.buttons = a.Length > 6 ? uint.Parse(a[6]) : 0u;
                    // Stamped just after the device's last update: an event older than that is thrown away, and the
                    // editor's clock (where the bridge runs) can lag the game's.
                    InputSystem.QueueStateEvent(dev, state, System.Math.Max(InputState.currentTime, dev.lastUpdateTime) + .001);
                    return $"test wheel set: queued {state.stick.x:F2}/{state.z:F2}, device reads {dev.stick.x.ReadValue():F2}";
            }
            return "fakewheel add|remove|set <steer> <gas> <brake> [buttons]";
        }
    }
}
