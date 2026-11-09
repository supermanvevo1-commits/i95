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
        
        void Awake() { Time.fixedDeltaTime = 1f / 60f; Setup(); EnsureCabinLight(); mirrors = gameObject.AddComponent<MirrorRig>(); SetupDamageVisuals(); Sim.Reset(Path, startS); StorePrevious(); }

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
                input.Steer = ((k.dKey.isPressed || k.rightArrowKey.isPressed) ? 1 : 0) - ((k.aKey.isPressed || k.leftArrowKey.isPressed) ? 1 : 0);
                input.Handbrake = k.spaceKey.isPressed;
            }
            var pad = Gamepad.current;
            if (pad != null)
            {
                float stick = pad.leftStick.x.ReadValue();
                if (Mathf.Abs(stick) > 0.08f) input.Steer = stick * Mathf.Abs(stick);
                input.Throttle = Mathf.Max(input.Throttle, pad.rightTrigger.ReadValue());
                input.Brake = Mathf.Max(input.Brake, pad.leftTrigger.ReadValue());
                input.Handbrake |= pad.buttonSouth.isPressed;
            }
            return input;
        }

        void Update()
        {
            var k = Keyboard.current;
            var pad = Gamepad.current;
            if (k != null && k.rKey.wasPressedThisFrame) { Sim.Reset(Path, Sim.S); StorePrevious(); }
            if ((k != null && k.cKey.wasPressedThisFrame) || (pad != null && pad.buttonNorth.wasPressedThisFrame)) SetCockpit(!Cockpit);
            if (k != null && k.vKey.wasPressedThisFrame) { lookYaw = 0; lookPitch = 0; }
            if (k != null && k.escapeKey.wasPressedThisFrame) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            if (Cockpit) UpdateLook(pad);

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
            float fov = Cockpit ? 72f + 5f * ratio : 60f + 15f * ratio * ratio;
            chaseCamera.fieldOfView = Mathf.Lerp(chaseCamera.fieldOfView, fov, 1f - Mathf.Exp(-Time.deltaTime * 4f));
            if (shake > 0.01f) { chaseCamera.transform.position += UnityEngine.Random.insideUnitSphere * 0.22f * shake * shake; chaseCamera.transform.rotation *= Quaternion.Euler(UnityEngine.Random.insideUnitSphere * 2.5f * shake); }
            if (rumbleShake > 0.01f) chaseCamera.transform.position += UnityEngine.Random.insideUnitSphere * 0.022f * rumbleShake;
        }

        public void SetCockpit(bool on)
        {
            Cockpit = on;
            if (on) { lookYaw = 0; lookPitch = 0; mouseSettle = 0.3f; }
            if (chaseCamera != null) chaseCamera.nearClipPlane = on ? 0.04f : 0.3f;
            EnsureCabinLight();
            if (Application.isPlaying) { Cursor.lockState = on ? CursorLockMode.Locked : CursorLockMode.None; Cursor.visible = !on; }
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
            if (Cockpit)
            {
                var head = transform.TransformPoint(cockpitEye + new Vector3(Mathf.Sin(lookYaw * Mathf.Deg2Rad) * 0.1f, 0, 0));
                chaseCamera.transform.position = head;
                chaseCamera.transform.rotation = transform.rotation * Quaternion.Euler(1.5f - lookPitch, lookYaw, 0);
                return;
            }
            var dir = Quaternion.Euler(0, camYaw, 0) * Vector3.forward;
            float back = cameraBack + 0.9f * Mathf.Clamp01(Sim.Speed / Sim.TopSpeed);
            chaseCamera.transform.position = transform.position - dir * back + Vector3.up * cameraUp;
            chaseCamera.transform.rotation = Quaternion.LookRotation(transform.position + dir * 12f + Vector3.up * 1.05f - chaseCamera.transform.position);
        }

        public void SnapCamera()
        {
            if (chaseCamera == null) return;
            camYaw = transform.eulerAngles.y;
            ApplyCamera();
        }

        void OnGUI()
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = 32, fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerRight };
            GUI.Label(new Rect(Screen.width - 330, Screen.height - 70, 300, 56), Mathf.RoundToInt(Mathf.Abs(Sim.Speed) * DriveSim.MetersPerSecondToMph) + " MPH", style);
            if (Sim.Damage > 0.01f)
            {
                var small = new GUIStyle(style) { fontSize = 22 };
                GUI.Label(new Rect(Screen.width - 330, Screen.height - 108, 300, 40), "DAMAGE " + Mathf.RoundToInt(Sim.Damage * 100f) + "%", small);
                if (Sim.Disabled) { var big = new GUIStyle(style) { alignment = TextAnchor.MiddleCenter, fontSize = 34 }; GUI.Label(new Rect(0, Screen.height * 0.3f, Screen.width, 60), "CAR DISABLED - PRESS R TO RECOVER", big); }
            }
        }
    }
}
