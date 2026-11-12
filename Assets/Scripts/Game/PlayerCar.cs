using System;
using SurviveI95.Sim;
using SurviveI95.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SurviveI95.Game
{
    public class PlayerCar : MonoBehaviour
    {
        public string routeId = "florida_jupiter";
        public string handlingId = "sedan";
        public float startS = 300f;
        public Camera chaseCamera;
        public Material smokeMaterial;
        /// <summary>The HUD's dashboard font (Barlow Semi Condensed), set by the scene builder.</summary>
        public Font hudFontBold, hudFontMedium;
        public Hud Hud { get; private set; }
        /// <summary>Test view from in front of the car, for inspecting crash damage.</summary>
        public bool FrontView;
        /// <summary>Test view from high above, for inspecting the surroundings.</summary>
        public float TopView;
        public float cameraBack = 6.6f, cameraUp = 2.25f;

        /// <summary>When set, replaces the keyboard. Used by automated play tests.</summary>
        public static Func<RoutePath, DriveSim, DriveInput> AutoDriver;
        public RoutePath Path { get; private set; }
        public DriveSim Sim { get; private set; }
        public bool Cockpit { get; private set; }

        // Driver's eye relative to the steering wheel center, so it stays right wherever the model sits.
        static readonly Vector3 EyeFromSteeringWheel = new Vector3(0, 0.28f, -0.56f);
        Vector3 cockpitEye = new Vector3(-0.38f, 1.17f, 0.04f);
        bool eyeMeasured;
        /// <summary>When set, drives the cockpit look direction (yaw, pitch). Used by automated tests.</summary>
        public Vector2? ScriptedLook;
        const float WheelRadius = 0.33f;
        static readonly Vector3 SteeringAxis = new Vector3(0, 0.38f, -0.925f);

        float prevS, prevLat, prevTheta, camYaw, roll, pitch, wheelSpin, lookYaw, lookPitch, lookIdle, rumbleShake;
        Transform model, steeringPivot, fl, fr, rl, rr, flSpin, frSpin, rlSpin, rrSpin;
        Light cabinLight;
        MirrorRig mirrors;
        Transform speedNeedle, tachNeedle;
        TextMesh speedText, gearText;
        float visualSteer, tachSmooth, mouseSettle, shifterTravel;
        Transform shifter;
        Vector3 shifterHome;
        int shownMph = -1;
        string shownGear = "";

        // ---- view: field of view, mirror glances, looking back
        const float DefaultFov = 58f;
        /// <summary>Cockpit vertical field of view in degrees. The chase camera runs a little narrower. [ and ] change it.</summary>
        public float BaseFov { get; private set; } = DefaultFov;
        /// <summary>0 looking ahead, 1 eyes fully on a mirror.</summary>
        public float Glance { get; private set; }
        /// <summary>Forces a mirror glance in tests: "L", "Rear" or "R".</summary>
        [NonSerialized] public string TestGlance;
        [NonSerialized] public bool TestLookBack, TestHorn;
        /// <summary>True while the player is on the horn.</summary>
        public bool Honking { get; private set; }
        /// <summary>What the driver asked of the car on the last physics step (for sound).</summary>
        public DriveInput LastInput { get; private set; }
        AudioSource horn;
        static readonly string[] MirrorParts = { "Mirror_L", "Mirror_Rear", "Mirror_R" };
        Renderer[] mirrorParts;
        float[] mirrorHalfWidth;
        int glanceMirror = 1;
        float glanceSwitch, fovSmooth = DefaultFov, glanceFov = 30f;
        Vector3 glanceDir = Vector3.forward;
        bool glancing, lookingBack;
        string flash; float flashUntil;

        // ---- keyboard steering
        float keyHeld; int keyDir;

        /// <summary>
        /// A key is all or nothing, so how long it's held stands in for how far the wheel is turned (see DriveSim.KeySteer):
        /// a tap nudges, a hold builds to the hardest turn the tires can make. Gamepad sticks skip this: they're already analog.
        /// </summary>
        public float KeyboardSteer(int dir, bool handbrake)
        {
            if (dir != keyDir) { keyDir = dir; keyHeld = 0f; }
            if (dir == 0) return 0f;
            keyHeld += Time.fixedDeltaTime;
            if (handbrake) return dir; // the handbrake is for throwing it around on purpose
            return dir * Sim.KeySteer(keyHeld);
        }

        // ---- signals, high beams
        /// <summary>The player's turn signal: -1 left, +1 right, 0 off (same as traffic). Z and X toggle it.</summary>
        public int Signal { get; private set; }
        public bool HighBeams { get; private set; }
        /// <summary>Hazard lights, toggled with G.</summary>
        public bool Hazards { get; private set; }
        /// <summary>True while the brake lights are on: the brake pedal is down (cruise's gentle braking counts, as in a real car).</summary>
        public bool BrakeLights { get; private set; }
        CarLamps lamps;
        public string LampState => lamps == null ? "no lamps" : lamps.State() + $" signal={Signal} hazards={Hazards} brake={BrakeLights} high={HighBeams}";
        [System.NonSerialized] public bool? TestBrakeLights;
        public void TestHazards(bool on) => Hazards = on;
        float signalSince, signalLat;

        // Keyboard: Z and X signal, H flashes the high beams, G hazards. Gamepad: d-pad left and right signal,
        // X (Square) flashes the high beams, B (Circle) hazards.
        void UpdateSignals(Keyboard k, Gamepad pad)
        {
            bool left = (k != null && k.zKey.wasPressedThisFrame) || (pad != null && pad.dpad.left.wasPressedThisFrame);
            bool rightSig = (k != null && k.xKey.wasPressedThisFrame) || (pad != null && pad.dpad.right.wasPressedThisFrame);
            if (left) SetSignal(Signal == -1 ? 0 : -1);
            if (rightSig) SetSignal(Signal == 1 ? 0 : 1);
            // Cancels itself once the lane change is done, or after a while, like a real stalk.
            if (Signal != 0 && (Mathf.Abs(Sim.Lateral - signalLat) > 3.2f || Time.time - signalSince > 12f)) Signal = 0;
            HighBeams = TestHighBeams || (k != null && k.hKey.isPressed) || (pad != null && pad.buttonWest.isPressed);
            if ((k != null && k.gKey.wasPressedThisFrame) || (pad != null && pad.buttonEast.wasPressedThisFrame)) { Hazards = !Hazards; Flash(Hazards ? "Hazards on" : "Hazards off"); }
        }

        void SetSignal(int s) { Signal = s; signalSince = Time.time; signalLat = Sim.Lateral; }
        [NonSerialized] public bool TestHighBeams;
        public void TestSignal(int s) => SetSignal(s);

        // ---- cruise control on the mouse wheel
        const float CruiseStepMph = 5f;
        bool cruiseOn;
        float cruiseSet, cruiseI;
        public bool CruiseOn => cruiseOn;
        public float CruiseSetMph => cruiseSet * DriveSim.MetersPerSecondToMph;
        public bool LookingBack => lookingBack;
        /// <summary>A short confirmation on screen ("Cruise 70 mph"), or null.</summary>
        public string FlashText => flash != null && Time.unscaledTime < flashUntil ? flash : null;
        /// <summary>The gear the lever shows: P, R, N or D.</summary>
        public string GearLetter => string.IsNullOrEmpty(shownGear) ? Sim.Gear.ToString() : shownGear;
        /// <summary>The posted limit where the car is, in mph, or 0 off the highway.</summary>
        public float LimitMph => Sim.Current == null ? Path.LimitMph(Sim.S) : 0f;
        public string CruiseStatus => cruiseOn ? $"cruise {Mathf.RoundToInt(cruiseSet * DriveSim.MetersPerSecondToMph)} mph" : "cruise off";

        void Awake()
        {
            Time.fixedDeltaTime = 1f / 60f; Setup(); EnsureCabinLight(); mirrors = gameObject.AddComponent<MirrorRig>(); lamps = new CarLamps(transform, model); Hud = gameObject.AddComponent<Hud>(); Hud.Init(this); SetupDamageVisuals(); Sim.Reset(Path, startS); StorePrevious();
            BaseFov = Mathf.Clamp(PlayerPrefs.GetFloat("fov", DefaultFov), 40f, 80f); fovSmooth = BaseFov; RoadShake = PlayerPrefs.GetInt("roadShake", 1) == 1;
            FindMirrors();
        }

        void FindMirrors()
        {
            mirrorParts = new Renderer[MirrorParts.Length]; mirrorHalfWidth = new float[MirrorParts.Length];
            for (int i = 0; i < MirrorParts.Length; i++)
            {
                var t = transform.Find("Model/" + MirrorParts[i]);
                if (t == null) continue;
                mirrorParts[i] = t.GetComponent<Renderer>();
                var mf = t.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) { mirrorHalfWidth[i] = .12f; continue; }
                var e = Vector3.Scale(mf.sharedMesh.bounds.extents, t.lossyScale);
                mirrorHalfWidth[i] = Mathf.Max(Mathf.Abs(e.x), Mathf.Abs(e.y), Mathf.Abs(e.z));
            }
        }

        public void SetBaseFov(float fov)
        {
            BaseFov = Mathf.Clamp(Mathf.Round(fov), 40f, 80f);
            PlayerPrefs.SetFloat("fov", BaseFov);
            Flash($"Field of view {BaseFov:F0}°");
        }

        void Flash(string text) { flash = text; flashUntil = Time.unscaledTime + 1.6f; }

        // ---------------------------------------------------------------- cruise control

        /// <summary>Wheel up raises the set speed, wheel down lowers it. The first notch sets it from the current speed.</summary>
        public void ScrollCruise(int notches)
        {
            if (notches == 0) return;
            float step = CruiseStepMph / DriveSim.MetersPerSecondToMph;
            if (!cruiseOn)
            {
                cruiseOn = true; cruiseI = .3f;
                cruiseSet = Mathf.Round(Mathf.Max(0f, Sim.Speed) / step) * step;
            }
            cruiseSet = Mathf.Min(cruiseSet + notches * step, Sim.TopSpeed);
            if (cruiseSet < 9.9f / DriveSim.MetersPerSecondToMph) { CruiseOff(); return; }
            Flash("Cruise " + Mathf.RoundToInt(cruiseSet * DriveSim.MetersPerSecondToMph) + " mph");
        }

        void CruiseOff() { if (cruiseOn) Flash("Cruise off"); cruiseOn = false; }

        /// <summary>Works the pedals while cruise is on. Braking cancels it; the gas pedal overrides it while held.</summary>
        public DriveInput ApplyCruise(DriveInput input)
        {
            if (!cruiseOn) return input;
            if (input.Brake > .05f || input.Handbrake || Sim.Disabled) { CruiseOff(); return input; }
            if (input.Throttle > .05f) { cruiseI = Mathf.Max(cruiseI, .3f); return input; }
            float err = cruiseSet - Sim.Speed, dt = Time.fixedDeltaTime;
            cruiseI = Mathf.Clamp(cruiseI + .06f * err * dt, 0f, .9f);
            input.Throttle = Mathf.Clamp01(.35f * err + cruiseI);
            // A gentle brake when well over, so wheel-down slows the car without making anyone behind think it's a brake-check.
            if (err < -1.3f) { input.Throttle = 0f; input.Brake = Mathf.Min(.18f, (-err - 1.3f) * .1f); }
            return input;
        }

        void UpdateControls(Keyboard k, Gamepad pad)
        {
            var mouse = Mouse.current;
            if (k != null)
            {
                if (k.leftBracketKey.wasPressedThisFrame || k.minusKey.wasPressedThisFrame) SetBaseFov(BaseFov - 2f);
                if (k.rightBracketKey.wasPressedThisFrame || k.equalsKey.wasPressedThisFrame) SetBaseFov(BaseFov + 2f);
            }
            if (mouse != null)
            {
                // Windows reports 120 per notch; newer input settings report 1.
                float y = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(y) > .01f) ScrollCruise((int)Mathf.Sign(y) * Mathf.Max(1, Mathf.RoundToInt(Mathf.Abs(y) >= 10f ? Mathf.Abs(y) / 120f : Mathf.Abs(y))));
                if (mouse.middleButton.wasPressedThisFrame)
                {
                    if (cruiseOn) CruiseOff();
                    else if (cruiseSet > 4f) { cruiseOn = true; cruiseI = .3f; Flash("Cruise resumed " + Mathf.RoundToInt(cruiseSet * DriveSim.MetersPerSecondToMph) + " mph"); }
                }
            }
            if (pad != null)
            {
                if (pad.dpad.up.wasPressedThisFrame) ScrollCruise(1);
                if (pad.dpad.down.wasPressedThisFrame) ScrollCruise(-1);
            }

            bool right = mouse != null && mouse.rightButton.isPressed;
            bool wantGlance = Cockpit && (TestGlance != null || right || (pad != null && pad.leftShoulder.isPressed));
            if (wantGlance && !glancing)
            {
                // Eyes go to the mirror nearest where the driver is already looking.
                glanceMirror = TestGlance == "L" ? 0 : TestGlance == "R" ? 2 : TestGlance == "Rear" ? 1 : lookYaw < -18f ? 0 : lookYaw > 18f ? 2 : 1;
                glanceSwitch = 0f;
            }
            glancing = wantGlance && mirrorParts != null && mirrorParts[glanceMirror] != null;
            lookingBack = TestLookBack || (k != null && k.cKey.isPressed) || (pad != null && pad.rightStickButton.isPressed) || (!Cockpit && right);
            UpdateSignals(k, pad);
            Honking = TestHorn || (k != null && k.eKey.isPressed) || (pad != null && pad.leftStickButton.isPressed);
            if (Honking && horn == null)
            {
                horn = gameObject.AddComponent<AudioSource>();
                // A recorded horn held as long as the key is down; the made-up two-tone one only if the recordings are missing.
                horn.clip = Resources.Load<AudioClip>("Sound/horn_hold") ?? TrafficManager.MakeHorn(1f, 415f, 518f, loop: true); horn.pitch = 1.1f;
                horn.loop = true; horn.spatialBlend = 0f; horn.volume = .38f; horn.playOnAwake = false;
            }
            if (horn != null && Honking != horn.isPlaying) { if (Honking) horn.Play(); else horn.Stop(); }
            Glance = Mathf.MoveTowards(Glance, glancing && !lookingBack ? 1f : 0f, Time.deltaTime / .4f);
        }

        void Setup()
        {
            Path ??= RoutePath.Load(routeId);
            Sim ??= new DriveSim(CarHandling.Load(handlingId));
            if (model == null) model = transform.Find("Model");
            if (steeringPivot == null) steeringPivot = transform.Find("Model/SteeringPivot");
            if (fl == null)
            {
                fl = transform.Find("Wheel_FL"); fr = transform.Find("Wheel_FR");
                rl = transform.Find("Wheel_RL"); rr = transform.Find("Wheel_RR");
                flSpin = fl ? fl.Find("Spin") : null; frSpin = fr ? fr.Find("Spin") : null;
                rlSpin = rl ? rl.Find("Spin") : null; rrSpin = rr ? rr.Find("Spin") : null;
            }
            MeasureEye();
            if (shifter == null) { shifter = transform.Find("Model/Shifter"); if (shifter) shifterHome = shifter.localPosition; }
            if (speedNeedle == null)
            {
                speedNeedle = transform.Find("Model/Gauges/SpeedNeedle"); tachNeedle = transform.Find("Model/Gauges/TachNeedle");
                var a = transform.Find("Model/Gauges/SpeedText"); var b = transform.Find("Model/Gauges/GearText");
                speedText = a ? a.GetComponent<TextMesh>() : null; gearText = b ? b.GetComponent<TextMesh>() : null;
            }
        }

        void MeasureEye()
        {
            if (eyeMeasured) return;
            MeshRenderer rim = null;
            foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
                if (r.name.Contains("steering_wheel") && (rim == null || r.bounds.size.sqrMagnitude > rim.bounds.size.sqrMagnitude)) rim = r;
            if (rim == null) return;
            cockpitEye = transform.InverseTransformPoint(rim.bounds.center) + EyeFromSteeringWheel;
            eyeMeasured = true;
        }

        void StorePrevious() { prevS = Sim.S; prevLat = Sim.Lateral; prevTheta = Sim.Theta; }

        public void Place(float s)
        {
            Setup();
            Sim.Reset(Path, s);
            StorePrevious();
            Pose(s, Sim.Lateral, 0);
            SnapCamera();
        }

        void FixedUpdate()
        {
            var input = AutoDriver != null ? AutoDriver(Path, Sim) : ReadKeyboard();
            StorePrevious();
            int roads = Sim.RoadSwitches;
            BrakeLights = TestBrakeLights ?? (input.Brake > .02f && Sim.Speed > -.3f);
            LastInput = input;
            Sim.Step(Path, input, Time.fixedDeltaTime);
            if (Mathf.Abs(Sim.S - prevS) > 20f || roads != Sim.RoadSwitches) StorePrevious(); // respawned or changed road
        }

        DriveInput ReadKeyboard()
        {
            var input = new DriveInput();
            var k = Keyboard.current;
            if (k != null)
            {
                input.Throttle = k.wKey.isPressed || k.upArrowKey.isPressed ? 1f : 0f;
                input.Brake = k.sKey.isPressed || k.downArrowKey.isPressed ? 1f : 0f;
                int dir = ((k.dKey.isPressed || k.rightArrowKey.isPressed) ? 1 : 0) - ((k.aKey.isPressed || k.leftArrowKey.isPressed) ? 1 : 0);
                input.Handbrake = k.spaceKey.isPressed;
                input.Steer = KeyboardSteer(dir, input.Handbrake);
            }
            // Mouse side buttons: front floors it (cruise picks up again on release), back brakes.
            var m = Mouse.current;
            if (m != null && m.forwardButton.isPressed) input.Throttle = 1f;
            if (m != null && m.backButton.isPressed) { input.Brake = 1f; input.Throttle = 0f; }
            var pad = Gamepad.current;
            if (pad != null)
            {
                float stick = pad.leftStick.x.ReadValue();
                if (Mathf.Abs(stick) > 0.08f) input.Steer = stick * Mathf.Abs(stick);
                input.Throttle = Mathf.Max(input.Throttle, pad.rightTrigger.ReadValue());
                input.Brake = Mathf.Max(input.Brake, pad.leftTrigger.ReadValue());
                input.Handbrake |= pad.buttonSouth.isPressed || pad.rightShoulder.isPressed; // A, or RB as in GTA
            }
            return ApplyCruise(input);
        }

        void Update()
        {
            var k = Keyboard.current;
            var pad = Gamepad.current;
            if (k != null && k.rKey.wasPressedThisFrame) { Sim.Reset(Path, Sim.S); StorePrevious(); }
            // GTA-style layout: V changes the camera, C looks back, E is the horn.
            if ((k != null && k.vKey.wasPressedThisFrame) || (pad != null && pad.buttonNorth.wasPressedThisFrame)) SetCockpit(!Cockpit);
            if (k != null && k.escapeKey.wasPressedThisFrame) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            if (k != null && k.f3Key.wasPressedThisFrame) { RoadShake = !RoadShake; PlayerPrefs.SetInt("roadShake", RoadShake ? 1 : 0); Flash(RoadShake ? "Road feel on" : "Road feel off"); }
            // A click in the game takes the mouse back, as games do after Escape.
            var mClick = Mouse.current;
            if (mClick != null && mClick.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; mouseSettle = 0.3f; }
            UpdateControls(k, pad);
            if (Cockpit) UpdateLook(pad); else UpdateOrbit(pad);

            float t = Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
            Pose(Mathf.Lerp(prevS, Sim.S, t), Mathf.Lerp(prevLat, Sim.Lateral, t), Mathf.Lerp(prevTheta, Sim.Theta, t));

            float ease = 1f - Mathf.Exp(-Time.deltaTime * 8f);
            roll = Mathf.Lerp(roll, Mathf.Clamp(Sim.LateralAccel * 0.3f, -3f, 3f), ease);
            pitch = Mathf.Lerp(pitch, Mathf.Clamp(-Sim.LongAccel * 0.16f, -1.6f, 2f), ease);
            // Real rotation is too fast for a display and strobes on the spokes, so cap the step per frame.
            // smoothDeltaTime removes the frame-to-frame jitter that showed up as a slight stutter.
            float spinStep = Sim.Speed / WheelRadius * Mathf.Rad2Deg * Time.smoothDeltaTime;
            wheelSpin = Mathf.Repeat(wheelSpin + Mathf.Clamp(spinStep, -11f, 11f), 360f);
            visualSteer = Mathf.Lerp(visualSteer, Sim.Steer, 1f - Mathf.Exp(-Time.deltaTime * 32f));
            rumbleShake = Mathf.Lerp(rumbleShake, Sim.OnRumble && Mathf.Abs(Sim.Speed) > 5f ? 1f : 0f, 1f - Mathf.Exp(-Time.deltaTime * 18f));
            ApplyVisuals(visualSteer);
            UpdateGauges();
            UpdateDamageVisuals();
            lamps?.Update(Signal, Hazards, BrakeLights, HighBeams);
            if (Sim.BrokenCount != shownBroken) { shownBroken = Sim.BrokenCount; KnockDownSign(); }
        }

        // Staggered gate: the lever steps sideways between detents as it travels P, R, N, D.
        static readonly Vector2[] Gate =
        {
            new Vector2(0f, 0f), new Vector2(-0.015f, -0.012f), new Vector2(-0.015f, -0.042f),
            new Vector2(0f, -0.054f), new Vector2(0f, -0.078f), new Vector2(-0.015f, -0.09f), new Vector2(-0.015f, -0.112f)
        };
        static readonly int[] DetentNode = { 0, 2, 4, 6 };
        static readonly string[] DetentName = { "P", "R", "N", "D" };
        /// <summary>Overrides where the lever is sent. Used by live tests to inspect each position.</summary>
        public char? LeverOverride;
        float leverPause;

        static float NodeDistance(int node)
        {
            float d = 0;
            for (int i = 1; i <= node; i++) d += (Gate[i] - Gate[i - 1]).magnitude;
            return d;
        }

        string UpdateShifter()
        {
            char want = LeverOverride ?? Sim.Gear;
            float target = NodeDistance(DetentNode[Mathf.Max(0, Array.IndexOf(DetentName, want.ToString()))]);
            leverPause -= Time.deltaTime;
            if (leverPause <= 0f)
            {
                float next = Mathf.MoveTowards(shifterTravel, target, 0.3f * Time.deltaTime);
                // The lever briefly seats in each gear it passes through.
                foreach (int node in DetentNode)
                {
                    float d = NodeDistance(node);
                    if (d != target && ((shifterTravel < d && next >= d) || (shifterTravel > d && next <= d))) { next = d; leverPause = 0.07f; break; }
                }
                shifterTravel = next;
            }
            Vector2 pos = Gate[Gate.Length - 1]; float walked = 0;
            for (int i = 1; i < Gate.Length; i++)
            {
                float len = (Gate[i] - Gate[i - 1]).magnitude;
                if (shifterTravel <= walked + len) { pos = Vector2.Lerp(Gate[i - 1], Gate[i], (shifterTravel - walked) / len); break; }
                walked += len;
            }
            if (shifter) shifter.localPosition = shifterHome + new Vector3(pos.x, 0, pos.y);
            int best = 0;
            for (int i = 1; i < DetentNode.Length; i++) if (Mathf.Abs(NodeDistance(DetentNode[i]) - shifterTravel) < Mathf.Abs(NodeDistance(DetentNode[best]) - shifterTravel)) best = i;
            return DetentName[best];
        }

        int shownBroken, shownImpacts, shownRepairs;
        float shake, appliedCrush;
        ParticleSystem smoke, burst;
        readonly System.Collections.Generic.List<(Mesh mesh, Vector3[] rest, Matrix4x4 toModel, Matrix4x4 fromModel)> crumple = new System.Collections.Generic.List<(Mesh, Vector3[], Matrix4x4, Matrix4x4)>();

        void SetupDamageVisuals()
        {
            if (model == null) return;
            foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!r.name.Contains("_body_") && !r.name.Contains("frontlights")) continue;
                var filter = r.GetComponent<MeshFilter>();
                if (filter == null || !filter.sharedMesh.isReadable) continue;
                var m = filter.mesh;
                var toModel = model.worldToLocalMatrix * r.transform.localToWorldMatrix;
                crumple.Add((m, m.vertices, toModel, toModel.inverse));
            }
            smoke = MakeParticles("HoodSmoke", new Vector3(0, 0.95f, 1.5f), 0.9f, 1.4f, 2.4f, new Color(.25f, .25f, .25f, .55f), 0);
            burst = MakeParticles("ImpactBurst", new Vector3(0, 0.55f, 2.2f), 0.18f, 6f, 0.5f, new Color(.45f, .45f, .42f, .9f), 0);
        }

        ParticleSystem MakeParticles(string name, Vector3 at, float size, float speed, float life, Color color, float rate)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false); go.transform.localPosition = at;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.startSize = size; main.startSpeed = speed; main.startLifetime = life; main.startColor = color; main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 400; main.gravityModifier = name == "ImpactBurst" ? 1.2f : -0.05f;
            var em = ps.emission; em.rateOverTime = rate;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = name == "ImpactBurst" ? 55f : 12f; sh.radius = 0.25f; sh.rotation = name == "ImpactBurst" ? Vector3.zero : new Vector3(-90f, 0, 0);
            if (smokeMaterial != null) go.GetComponent<ParticleSystemRenderer>().sharedMaterial = smokeMaterial;
            ps.Play();
            return ps;
        }

        void ApplyCrumple(float amount)
        {
            const float zoneStart = 1.15f, nose = 2.35f;
            foreach (var part in crumple)
            {
                var v = new Vector3[part.rest.Length];
                for (int i = 0; i < v.Length; i++)
                {
                    var w = part.toModel.MultiplyPoint3x4(part.rest[i]);
                    if (w.z > zoneStart)
                    {
                        float k = (w.z - zoneStart) / (nose - zoneStart); k *= k;
                        float ripple = 1f + 0.3f * Mathf.Sin(w.x * 9f + w.y * 6f);
                        w.z -= amount * k * ripple;
                        w.y -= 0.12f * amount * k * ripple;
                    }
                    v[i] = part.fromModel.MultiplyPoint3x4(w);
                }
                part.mesh.vertices = v;
                part.mesh.RecalculateBounds();
            }
            appliedCrush = amount;
        }

        void UpdateDamageVisuals()
        {
            if (Sim.ImpactCount != shownImpacts)
            {
                shownImpacts = Sim.ImpactCount;
                float force = Mathf.Clamp01(Sim.LastImpact / 30f);
                shake = Mathf.Max(shake, force);
                if (Sim.LastImpact > 3f) CruiseOff(); // like a real car, a crash knocks cruise control off
                if (burst != null && Sim.LastImpact > 4f) burst.Emit(Mathf.RoundToInt(10f + force * 60f));
            }
            if (Sim.RepairCount != shownRepairs) { shownRepairs = Sim.RepairCount; if (smoke != null) smoke.Clear(); }
            if (!Mathf.Approximately(appliedCrush, Sim.FrontCrush) && crumple.Count > 0) ApplyCrumple(Sim.FrontCrush);
            if (smoke != null) { var em = smoke.emission; em.rateOverTime = Sim.Damage > 0.3f ? Mathf.Lerp(4f, 40f, (Sim.Damage - 0.3f) / 0.7f) : 0f; }
            shake = Mathf.MoveTowards(shake, 0f, Time.deltaTime * 2.2f);
        }

        void KnockDownSign()
        {
            var signs = GameObject.Find("Signs");
            if (signs == null) return;
            var at = Path.Point(Sim.LastBrokenS, Sim.LastBrokenLat);
            var hinge = new GameObject("BrokenSign").transform;
            hinge.position = at; hinge.rotation = Quaternion.LookRotation(Path.Tangent(Sim.LastBrokenS), Vector3.up);
            for (int i = signs.transform.childCount - 1; i >= 0; i--)
            {
                var c = signs.transform.GetChild(i);
                var flat = c.position - at; flat.y = 0;
                if (flat.magnitude < 5.5f) c.SetParent(hinge, true);
            }
            StartCoroutine(Fall(hinge));
        }

        System.Collections.IEnumerator Fall(Transform hinge)
        {
            for (float t = 0; t < 0.55f; t += Time.deltaTime)
            {
                hinge.localRotation *= Quaternion.Euler(Mathf.Lerp(40f, 230f, t / 0.55f) * Time.deltaTime, 0, 0);
                yield return null;
            }
        }

        void UpdateGauges()
        {
            float mph = Mathf.Abs(Sim.Speed) * DriveSim.MetersPerSecondToMph;
            if (speedNeedle) speedNeedle.localRotation = Quaternion.Euler(0, 0, 135f - 270f * Mathf.Clamp01(mph / 140f));
            float gearPos = Mathf.Clamp01(Mathf.Abs(Sim.Speed) / Sim.TopSpeed) * 6f;
            float rpm = mph < 1f ? 0.1f : 0.3f + 0.45f * (gearPos - Mathf.Min(5, (int)gearPos));
            // With sound on, the needle follows the same engine the player hears (8,000 RPM full scale).
            var audio = GetComponent<GameAudio>(); if (audio != null) rpm = audio.Rpm / 8000f;
            tachSmooth = Mathf.Lerp(tachSmooth, rpm, 1f - Mathf.Exp(-Time.deltaTime * 10f));
            if (tachNeedle) tachNeedle.localRotation = Quaternion.Euler(0, 0, 135f - 270f * tachSmooth);
            int rounded = Mathf.RoundToInt(mph);
            if (speedText && rounded != shownMph) { shownMph = rounded; speedText.text = rounded.ToString(); }
            string gear = UpdateShifter();
            if (gearText && gear != shownGear) { shownGear = gear; gearText.text = gear; }
        }

        void UpdateLook(Gamepad pad)
        {
            if (ScriptedLook.HasValue)
            {
                float ease = 1f - Mathf.Exp(-Time.deltaTime * 4f);
                lookYaw = Mathf.Lerp(lookYaw, ScriptedLook.Value.x, ease); lookPitch = Mathf.Lerp(lookPitch, ScriptedLook.Value.y, ease);
                return;
            }
            var mouse = Mouse.current;
            // Capturing the cursor produces one huge fake mouse movement; ignore input until it passes.
            mouseSettle -= Time.unscaledDeltaTime;
            Vector2 delta = mouse != null && mouseSettle <= 0 ? mouse.delta.ReadValue() * 0.09f : Vector2.zero;
            if (pad != null) delta += pad.rightStick.ReadValue() * 170f * Time.deltaTime;
            if (glancing)
            {
                // While on a mirror, a flick of the mouse moves the eyes to the next mirror over.
                glanceSwitch += delta.x;
                if (Mathf.Abs(glanceSwitch) > 6f)
                {
                    int next = Mathf.Clamp(glanceMirror + (int)Mathf.Sign(glanceSwitch), 0, MirrorParts.Length - 1);
                    if (mirrorParts[next] != null) glanceMirror = next;
                    glanceSwitch = 0f;
                }
                lookIdle = 0;
                return;
            }
            if (delta.sqrMagnitude > 0.0004f) lookIdle = 0;
            else lookIdle += Time.deltaTime;
            lookYaw = Mathf.Clamp(lookYaw + delta.x, -135f, 135f);
            lookPitch = Mathf.Clamp(lookPitch - delta.y, -50f, 32f);
            if (lookIdle > 2.2f && Mathf.Abs(Sim.Speed) > 13f)
            {
                float back = 1f - Mathf.Exp(-Time.deltaTime * 3f);
                lookYaw = Mathf.Lerp(lookYaw, 0, back); lookPitch = Mathf.Lerp(lookPitch, 0, back);
            }
        }

        /// <summary>Applies wheel, steering-wheel and body motion. Also used for test renders.</summary>
        public void ApplyVisuals(float steer)
        {
            float ratio = Mathf.Clamp01(Mathf.Abs(Sim.Speed) / Sim.TopSpeed);
            float front = steer * Mathf.Lerp(32f, 9f, ratio);
            var steerRot = Quaternion.Euler(0, front, 0);
            if (fl) fl.localRotation = steerRot;
            if (fr) fr.localRotation = steerRot;
            var spin = Quaternion.Euler(wheelSpin, 0, 0);
            if (flSpin) flSpin.localRotation = spin;
            if (frSpin) frSpin.localRotation = spin;
            if (rlSpin) rlSpin.localRotation = spin;
            if (rrSpin) rrSpin.localRotation = spin;
            if (steeringPivot) steeringPivot.localRotation = Quaternion.AngleAxis(steer * 140f, SteeringAxis);
            // Only the body leans; tires stay planted on the road.
            if (model) model.localRotation = Quaternion.Euler(pitch, 0, roll);
        }

        public void SetTestView(float steer, float yaw, float pitchLook)
        {
            lookYaw = yaw; lookPitch = pitchLook;
            ApplyVisuals(steer);
            SnapCamera();
        }

        // The grass sits below the pavement, so a car off the road has to settle down onto it.
        float GroundDrop(float s, float lateral)
        {
            float half = Path.HalfWidth(s);
            float southCenter = -(2f * half + RoutePath.MedianGap);
            float north = lateral > 0 ? lateral - (half + 3.05f) : -lateral - (half + 1.3f);
            float south = lateral > southCenter ? (lateral - southCenter) - (half + 3.05f) : (southCenter - lateral) - (half + 1.3f);
            float beyond = Mathf.Min(north, south);
            return 0.25f * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(beyond / 0.8f));
        }

        void Pose(float s, float lateral, float theta)
        {
            var road = Sim.Current ?? Path;
            var spot = road.Point(s, lateral);
            if (Sim.Current == null) spot.y = Mathf.Lerp(spot.y, -0.25f, GroundDrop(s, lateral) / 0.25f);
            if (Sim.Current != null) spot = road.Point(s, lateral);
            transform.position = spot;
            var tan = road.Tangent(s);
            transform.rotation = Quaternion.LookRotation(tan, Vector3.up) * Quaternion.Euler(0, theta * Mathf.Rad2Deg, 0);
        }

        void LateUpdate()
        {
            if (chaseCamera == null) return;
            float yaw = transform.eulerAngles.y;
            camYaw = Mathf.LerpAngle(camYaw, yaw, 1f - Mathf.Exp(-Time.deltaTime * 7f));
            ApplyCamera();
            float ratio = Mathf.Clamp01(Sim.Speed / Sim.TopSpeed);
            float fov = Cockpit ? BaseFov + 4f * ratio : BaseFov - 6f + 10f * ratio * ratio;
            fovSmooth = Mathf.Lerp(fovSmooth, fov, 1f - Mathf.Exp(-Time.deltaTime * 4f));
            float g = Mathf.SmoothStep(0f, 1f, Glance);
            chaseCamera.fieldOfView = lookingBack ? BaseFov : Mathf.Lerp(fovSmooth, glanceFov, g);
            RoadFeel();
            if (shake > 0.01f) { chaseCamera.transform.position += UnityEngine.Random.insideUnitSphere * 0.22f * shake * shake; chaseCamera.transform.rotation *= Quaternion.Euler(UnityEngine.Random.insideUnitSphere * 2.5f * shake); }
            if (rumbleShake > 0.01f) chaseCamera.transform.position += UnityEngine.Random.insideUnitSphere * 0.022f * rumbleShake;
        }

        public void SetCockpit(bool on)
        {
            Cockpit = on;
            if (on) { lookYaw = 0; lookPitch = 0; mouseSettle = 0.3f; }
            if (chaseCamera != null) chaseCamera.nearClipPlane = on ? 0.04f : 0.3f;
            EnsureCabinLight();
            // The mouse steers the view in both cameras now (cockpit look, chase orbit), so it's captured in both.
            orbitYaw = 0f; orbitPitch = 0f; mouseSettle = 0.3f;
            if (Application.isPlaying) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
            SnapCamera();
        }

        // Created once up front: first use of a new light type otherwise causes a visible freeze.
        void EnsureCabinLight()
        {
            if (cabinLight != null) return;
            var go = new GameObject("CabinLight", typeof(Light));
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(-0.1f, 0.82f, 0.45f);
            cabinLight = go.GetComponent<Light>();
            cabinLight.type = LightType.Point; cabinLight.range = 1.7f; cabinLight.intensity = 0.4f;
            cabinLight.color = new Color(1f, .95f, .86f); cabinLight.shadows = LightShadows.None;
        }

        void ApplyCamera()
        {
            if (TopView > 0f)
            {
                chaseCamera.transform.position = transform.position - transform.forward * TopView * .9f + Vector3.up * TopView;
                chaseCamera.transform.rotation = Quaternion.LookRotation(transform.position + transform.forward * TopView * .25f - chaseCamera.transform.position);
                return;
            }
            if (FrontView)
            {
                chaseCamera.transform.position = transform.position + transform.forward * 7.2f + transform.right * 2.6f + Vector3.up * 1.5f;
                chaseCamera.transform.rotation = Quaternion.LookRotation(transform.position + transform.forward * 1.2f + Vector3.up * 0.65f - chaseCamera.transform.position);
                return;
            }
            if (lookingBack)
            {
                // From over the hood, back over the roof at whatever is coming.
                var from = Cockpit ? transform.TransformPoint(0f, 1.9f, 1.4f) : transform.TransformPoint(0f, cameraUp, cameraBack * .85f);
                chaseCamera.transform.position = from;
                chaseCamera.transform.rotation = Quaternion.LookRotation(transform.TransformPoint(0f, 1.05f, -14f) - from, Vector3.up);
                return;
            }
            if (Cockpit)
            {
                var head = transform.TransformPoint(cockpitEye + new Vector3(Mathf.Sin(lookYaw * Mathf.Deg2Rad) * 0.1f, 0, 0));
                var rot = transform.rotation * Quaternion.Euler(1.5f - lookPitch, lookYaw, 0);
                if (Glance > 0f && mirrorParts != null && mirrorParts[glanceMirror] != null)
                {
                    var mirror = mirrorParts[glanceMirror].bounds.center;
                    // Lean a little toward the mirror, and ease the eyes from one mirror to the next.
                    float g = Mathf.SmoothStep(0f, 1f, Glance);
                    head += (mirror - head) * .1f * g;
                    var toMirror = transform.InverseTransformDirection(mirror - head);
                    float dist = toMirror.magnitude;
                    glanceDir = Glance < .02f ? toMirror.normalized : Vector3.Slerp(glanceDir, toMirror.normalized, 1f - Mathf.Exp(-Time.deltaTime * 9f));
                    // Zoom so the mirror fills most of the width of the screen.
                    float hfov = 2f * Mathf.Atan(mirrorHalfWidth[glanceMirror] / .72f / dist) * Mathf.Rad2Deg;
                    float want = Mathf.Clamp(Camera.HorizontalToVerticalFieldOfView(hfov, chaseCamera.aspect), 8f, 45f);
                    glanceFov = Glance < .02f ? want : Mathf.Lerp(glanceFov, want, 1f - Mathf.Exp(-Time.deltaTime * 9f));
                    rot = Quaternion.Slerp(rot, transform.rotation * Quaternion.LookRotation(glanceDir, Vector3.up), g);
                }
                chaseCamera.transform.position = head;
                chaseCamera.transform.rotation = rot;
                return;
            }
            // Behind the car, swung around it by the mouse (orbit), as in GTA.
            var dir = Quaternion.Euler(0, camYaw + orbitYaw, 0) * Vector3.forward;
            float back = cameraBack + 0.9f * Mathf.Clamp01(Sim.Speed / Sim.TopSpeed);
            float p = orbitPitch * Mathf.Deg2Rad;
            var pos = transform.position - dir * back * Mathf.Cos(p) + Vector3.up * (cameraUp + back * Mathf.Sin(p));
            pos.y = Mathf.Max(pos.y, transform.position.y + 0.5f);
            chaseCamera.transform.position = pos;
            // Straight behind, aim down the road ahead; swung round to the side, aim at the car itself.
            float aside = Mathf.Clamp01(Mathf.Abs(Mathf.DeltaAngle(0f, orbitYaw)) / 35f);
            var target = transform.position + Vector3.up * 1.05f + dir * Mathf.Lerp(12f, 0f, aside);
            chaseCamera.transform.rotation = Quaternion.LookRotation(target - pos);
        }

        // ---- speed feel (5b.4): the road through the camera, never shake for its own sake
        public static bool RoadShake = true;
        float lastSpeed, accelSmooth, thump, nextJoint;

        /// <summary>
        /// A fine tremor that grows with speed (tires on a real road surface), a soft thump at pavement joints and
        /// patches that comes faster the faster you go, and the camera dropping back under hard acceleration and
        /// closing in under braking, as a body squats and dives. F3 turns the tremor off.
        /// </summary>
        void RoadFeel()
        {
            if (Time.deltaTime <= 0f) return;
            float v = Mathf.Abs(Sim.Speed), mph = v * DriveSim.MetersPerSecondToMph;
            float accel = (v - lastSpeed) / Time.deltaTime; lastSpeed = v;
            accelSmooth = Mathf.Lerp(accelSmooth, Mathf.Clamp(accel, -9f, 6f), 1f - Mathf.Exp(-Time.deltaTime * 3f));
            if (!Cockpit && TopView <= 0f && !FrontView && !lookingBack)
                chaseCamera.transform.position -= chaseCamera.transform.forward * accelSmooth * .07f;
            if (!RoadShake || v < 1f) return;
            // Joints and patches: a pavement joint about every 20 m on average, uneven.
            nextJoint -= v * Time.deltaTime;
            if (nextJoint <= 0f) { thump = Mathf.Clamp01(.35f + mph / 160f); nextJoint = 9f + UnityEngine.Random.value * 25f; }
            thump = Mathf.MoveTowards(thump, 0f, Time.deltaTime * 9f);
            float k = Mathf.Clamp01((mph - 20f) / 120f); k *= k;
            float t = Time.time * (6f + mph * .12f);
            float tremor = (Mathf.PerlinNoise(t, .3f) - .5f) * .03f * k + (Mathf.PerlinNoise(t * 2.3f, 7.1f) - .5f) * .012f * k;
            float bump = Mathf.Sin(thump * Mathf.PI * 3f) * thump * .025f;
            float scale = Cockpit ? .6f : 1f;
            chaseCamera.transform.position += Vector3.up * (tremor + bump) * scale;
            // Past about 120 mph the view starts to hum sideways too, as the car gets light.
            float hum = Mathf.Clamp01((mph - 115f) / 50f);
            if (hum > 0f) chaseCamera.transform.rotation *= Quaternion.Euler(0f, 0f, (Mathf.PerlinNoise(t * 1.7f, 3.3f) - .5f) * 1.2f * hum * scale);
        }

        float orbitYaw, orbitPitch, orbitIdle;

        /// <summary>For tests: swings the chase camera as if the mouse had moved, then lets it return as a player's would.</summary>
        public void TestOrbit(float yaw, float pitchDeg) { orbitYaw = yaw; orbitPitch = pitchDeg; orbitIdle = 0f; }
        public float OrbitYaw => orbitYaw;

        /// <summary>
        /// Mouse or right stick swings the chase camera around the car. Left alone while driving, it eases back
        /// behind after a moment; parked, it stays where it was left.
        /// </summary>
        void UpdateOrbit(Gamepad pad)
        {
            var mouse = Mouse.current;
            mouseSettle -= Time.unscaledDeltaTime;
            Vector2 delta = mouse != null && mouseSettle <= 0 && Cursor.lockState == CursorLockMode.Locked ? mouse.delta.ReadValue() * 0.12f : Vector2.zero;
            if (pad != null) delta += pad.rightStick.ReadValue() * 160f * Time.deltaTime;
            if (delta.sqrMagnitude > 0.0004f) orbitIdle = 0f; else orbitIdle += Time.deltaTime;
            orbitYaw = Mathf.Repeat(orbitYaw + delta.x + 180f, 360f) - 180f;
            orbitPitch = Mathf.Clamp(orbitPitch - delta.y * .6f, -8f, 50f);
            if (orbitIdle > 1.4f && Mathf.Abs(Sim.Speed) > 3f)
            {
                float k = 1f - Mathf.Exp(-Time.deltaTime * 2.5f);
                orbitYaw = Mathf.LerpAngle(orbitYaw, 0f, k); orbitPitch = Mathf.Lerp(orbitPitch, 0f, k);
            }
        }

        public void SnapCamera()
        {
            if (chaseCamera == null) return;
            camYaw = transform.eulerAngles.y;
            ApplyCamera();
        }
    }
}
