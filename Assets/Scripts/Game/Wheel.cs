using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace SurviveI95.Game
{
    /// <summary>
    /// Steering wheels and pedals. Every model reports itself differently (which axis is the wheel, which the pedals,
    /// whether a pedal reads 1 or -1 when it's up, gas and brake on one axis or two), so instead of a list of known
    /// wheels the game asks once: F4 walks you through turning the wheel left, right and center, pressing each pedal,
    /// and choosing a wheel button for each action. The answers are kept per wheel model, so it's asked only once.
    /// The wheel steers in proportion (full lock of the wheel is full lock of the car), and the pedals are analog.
    /// Force feedback needs the wheel maker's own kit and is not here yet.
    /// </summary>
    public static class Wheel
    {
        [Serializable]
        public class Setup
        {
            public string steer, gas, brake;
            public float left, right, center, gasUp, gasDown, brakeUp, brakeDown;
            public string[] buttons = new string[Actions.Length];
        }

        public static readonly string[] Actions = { "Handbrake", "Horn", "Camera", "Signal left", "Signal right", "Look back", "High beams", "Hazards" };
        public const int Handbrake = 0, Horn = 1, Camera = 2, SignalLeft = 3, SignalRight = 4, LookBack = 5, HighBeams = 6, Hazards = 7;

        public static Joystick Device { get; private set; }
        public static Setup Map { get; private set; }
        public static bool Ready => Device != null && Map != null && Map.steer != null && Device.added;
        /// <summary>The setup step being asked (-1 when not setting up), and what it says.</summary>
        public static int Step { get; private set; } = -1;
        public static string Prompt { get; private set; }

        static AxisControl steerAxis, gasAxis, brakeAxis;
        static ButtonControl[] buttons = new ButtonControl[Actions.Length];
        static Dictionary<string, float> snapLeft, snapRight, snapCenter, snapGas;
        static Setup draft;

        static string Key(InputDevice d) => "wheel/" + (string.IsNullOrEmpty(d.description.product) ? d.displayName : d.description.product);

        /// <summary>Once a frame: notices a wheel plugged in or pulled out, and runs the setup when it's on.</summary>
        public static void Update(PlayerCar car)
        {
            var found = InputSystem.devices.OfType<Joystick>().FirstOrDefault(d => d.added && d.enabled);
            if (found != Device)
            {
                Device = found; Map = null; Step = -1;
                if (Device != null)
                {
                    var saved = PlayerPrefs.GetString(Key(Device), "");
                    if (saved.Length > 0) Use(JsonUtility.FromJson<Setup>(saved));
                    car.Flash(Ready ? $"Steering wheel ready: {Device.displayName}" : $"Steering wheel found: press F4 to set it up", 4f);
                }
            }
            var k = Keyboard.current;
            if (k != null && k.f4Key.wasPressedThisFrame)
            {
                if (Device == null) car.Flash("No steering wheel found", 3f);
                else if (Step >= 0) { Step = -1; car.Flash("Wheel setup cancelled", 2f); }
                else Begin();
            }
            if (Step >= 0) RunSetup(car, k);
            // What the game read this frame, for the test status (the editor keeps its own copy of device input).
            if (Map != null)
            {
                seen[0] = steerAxis.ReadUnprocessedValue(); seen[1] = gasAxis?.ReadUnprocessedValue() ?? 0f; seen[2] = brakeAxis?.ReadUnprocessedValue() ?? 0f;
                seen[3] = Steer; seen[4] = Gas; seen[5] = Brake; seenHeld = 0;
                for (int i = 0; i < Actions.Length; i++) if (Held(i)) seenHeld |= 1 << i;
            }
        }
        static readonly float[] seen = new float[6]; static int seenHeld;
        static int doneFrame = -1;

        public static void Begin() { if (Device == null) return; draft = new Setup(); Step = 0; }

        // ---- the setup, one question at a time; a wheel button or Enter answers, Backspace skips a button
        static readonly string[] Questions =
        {
            "Turn the wheel all the way LEFT and hold it there, then press any button on the wheel (or Enter)",
            "Now all the way RIGHT and hold it, then press a button",
            "Let the wheel go back to the middle, feet off the pedals, then press a button",
            "Hold the GAS pedal all the way down, then press a button",
            "Hold the BRAKE pedal all the way down, then press a button",
        };

        static void RunSetup(PlayerCar car, Keyboard k)
        {
            int buttonStep = Step - Questions.Length;
            Prompt = Step < Questions.Length ? $"Wheel setup {Step + 1}/{Questions.Length + Actions.Length}: {Questions[Step]}"
                : $"Wheel setup {Step + 1}/{Questions.Length + Actions.Length}: press the wheel button for {Actions[buttonStep].ToUpper()} (Backspace: none)";
            car.Flash(Prompt, .5f);
            var pressed = Device.allControls.OfType<ButtonControl>().FirstOrDefault(b => !(b.parent is StickControl) && b.wasPressedThisFrame);
            bool enter = k != null && (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame);
            bool skip = k != null && k.backspaceKey.wasPressedThisFrame;
            if (Step < Questions.Length)
            {
                if (pressed == null && !enter) return;
                var snap = Snapshot();
                if (Step == 0) snapLeft = snap;
                else if (Step == 1) snapRight = snap;
                else if (Step == 2)
                {
                    snapCenter = snap;
                    var best = Widest(snapLeft, snapRight, null);
                    if (best == null) { car.Flash("The wheel didn't move: try again", 3f); Step = 0; return; }
                    draft.steer = best; draft.left = snapLeft[best]; draft.right = snapRight[best]; draft.center = snap[best];
                }
                else if (Step == 3)
                {
                    snapGas = snap;
                    draft.gas = Widest(snapCenter, snap, draft.steer);
                    if (draft.gas == null) { car.Flash("No pedal moved: try again", 3f); return; }
                    draft.gasUp = snapCenter[draft.gas]; draft.gasDown = snap[draft.gas];
                }
                else
                {
                    // On wheels with gas and brake on one axis the brake moves it the other way.
                    draft.brake = Widest(snapCenter, snap, draft.steer, draft.gas);
                    float gasMove = snap[draft.gas] - snapCenter[draft.gas];
                    if (draft.brake == null || Mathf.Abs(gasMove) > .3f && Mathf.Sign(gasMove) != Mathf.Sign(draft.gasDown - draft.gasUp)) draft.brake = draft.gas;
                    draft.brakeUp = snapCenter[draft.brake]; draft.brakeDown = snap[draft.brake];
                    if (Mathf.Abs(draft.brakeDown - draft.brakeUp) < .2f) { car.Flash("The brake didn't move: try again", 3f); return; }
                }
                Step++; return;
            }
            if (pressed == null && !enter && !skip) return;
            draft.buttons[buttonStep] = pressed != null ? Path(pressed) : null;
            if (++Step >= Questions.Length + Actions.Length)
            {
                Step = -1; Prompt = null; doneFrame = Time.frameCount; // the press that answered the last question isn't an action
                PlayerPrefs.SetString(Key(Device), JsonUtility.ToJson(draft)); PlayerPrefs.Save();
                Use(draft);
                car.Flash("Steering wheel set up. F4 to do it again", 4f);
            }
        }

        static string Path(InputControl c) => c.path.Substring(Device.path.Length + 1);

        static IEnumerable<AxisControl> Axes() => Device.allControls.OfType<AxisControl>().Where(a => !(a is ButtonControl));

        static Dictionary<string, float> Snapshot() => Axes().ToDictionary(a => Path(a), a => a.ReadUnprocessedValue());

        /// <summary>The axis that moved most between two snapshots, leaving out the ones already taken.</summary>
        static string Widest(Dictionary<string, float> a, Dictionary<string, float> b, params string[] not)
        {
            string best = null; float most = .3f;
            foreach (var kv in a)
            {
                if (not != null && not.Contains(kv.Key) || !b.TryGetValue(kv.Key, out var v)) continue;
                // A stick's own x also drives derived controls; prefer the plain axis by taking the first that wins.
                float d = Mathf.Abs(v - kv.Value);
                if (d > most + .01f) { most = d; best = kv.Key; }
            }
            return best;
        }

        static void Use(Setup s)
        {
            Map = s;
            steerAxis = s.steer != null ? Device.TryGetChildControl<AxisControl>(s.steer) : null;
            gasAxis = s.gas != null ? Device.TryGetChildControl<AxisControl>(s.gas) : null;
            brakeAxis = s.brake != null ? Device.TryGetChildControl<AxisControl>(s.brake) : null;
            buttons = new ButtonControl[Actions.Length];
            for (int i = 0; i < Actions.Length && s.buttons != null && i < s.buttons.Length; i++)
                if (!string.IsNullOrEmpty(s.buttons[i])) buttons[i] = Device.TryGetChildControl<ButtonControl>(s.buttons[i]);
            if (steerAxis == null) Map = null;
        }

        /// <summary>The wheel's steering, -1 (full left lock) to 1, straight through: a wheel is already analog.</summary>
        public static float Steer
        {
            get
            {
                if (!Ready) return 0f;
                float v = steerAxis.ReadUnprocessedValue(), c = Map.center;
                float x = v < c == Map.left < c ? -(v - c) / (Map.left - c) : (v - c) / (Map.right - c);
                x = Mathf.Clamp(x, -1f, 1f);
                return Mathf.Abs(x) < .01f ? 0f : x;
            }
        }

        // Axes are read unprocessed: the input system puts a dead zone on joystick sticks, a loose patch a wheel must not have.
        static float Pedal(AxisControl a, float up, float down)
        {
            if (a == null || Mathf.Abs(down - up) < .05f) return 0f;
            float t = Mathf.InverseLerp(up, down, a.ReadUnprocessedValue());
            return Mathf.Clamp01((t - .04f) / .92f); // a little play at each end, as pedals never rest exactly at zero
        }

        public static float Gas => Ready ? Pedal(gasAxis, Map.gasUp, Map.gasDown) : 0f;
        public static float Brake => Ready ? Pedal(brakeAxis, Map.brakeUp, Map.brakeDown) : 0f;
        public static bool Held(int action) => Ready && Step < 0 && Time.frameCount > doneFrame && buttons[action] != null && buttons[action].isPressed;
        public static bool Pressed(int action) => Ready && Step < 0 && Time.frameCount > doneFrame && buttons[action] != null && buttons[action].wasPressedThisFrame;

        public static string Status() => Device == null ? "no wheel" :
            $"wheel={Device.displayName} ready={Ready} step={Step}" +
            (Map != null ? $" raw={seen[0]:F2}/{seen[1]:F2}/{seen[2]:F2} steer={seen[3]:F2} gas={seen[4]:F2} brake={seen[5]:F2} held={string.Concat(Enumerable.Range(0, Actions.Length).Select(i => (seenHeld >> i & 1).ToString()))}" : "") +
            (Map != null ? $" cal={Map.left:F2},{Map.center:F2},{Map.right:F2} gas {Map.gasUp:F2}>{Map.gasDown:F2} brake {Map.brakeUp:F2}>{Map.brakeDown:F2} axes={Map.steer}/{Map.gas}/{Map.brake} buttons={string.Join(",", Map.buttons.Select(b => b ?? "-"))}" : "");
    }
}
