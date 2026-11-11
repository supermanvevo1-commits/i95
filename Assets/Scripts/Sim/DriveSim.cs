using System;
using System.Collections.Generic;
using SurviveI95.World;
using UnityEngine;

namespace SurviveI95.Sim
{
    /// <summary>Throttle and brake are 0..1 so controllers get analog control; keyboards send 0 or 1.</summary>
    public struct DriveInput { public float Throttle, Brake, Steer; public bool Handbrake; }

    [Serializable]
    public class CarHandling
    {
        public string id;
        public float topMph, accel, brake, gripG, wheelbase, steerIn, steerOut, mass, frontWeight;
        /// <summary>Stability control, 0 (none, as on an old muscle car) to 1 (a modern car). Catches the tail when it steps out.</summary>
        public float stability;

        [Serializable] class Table { public List<CarHandling> cars = new List<CarHandling>(); }

        public static CarHandling Load(string id)
        {
            var asset = Resources.Load<TextAsset>("Data/car_handling");
            if (asset == null) throw new InvalidOperationException("Missing Data/car_handling");
            var car = JsonUtility.FromJson<Table>(asset.text).cars.Find(c => c.id == id);
            if (car == null) throw new InvalidOperationException("Unknown handling profile: " + id);
            return car;
        }
    }

    /// <summary>
    /// Fixed-step two-axle tire model in road coordinates. Nothing limits the driver: too much steering
    /// overloads the tires, and the car pushes wide, slides or spins. No scene objects, so it can run on a server.
    /// </summary>
    public class DriveSim
    {
        public const float MetersPerSecondToMph = 2.23694f;
        const float G = 9.81f, RightShoulder = 3.05f, LeftShoulder = 1.3f, WorldEdge = 640f;
        const float TireB = 13f, TireC = 1.45f, MaxWheelAngle = 0.6f, SteerSpeedRef = 13f;
        const int Substeps = 4;

        public readonly CarHandling Car;
        /// <summary>Distance along road, offset right of center, heading relative to road (radians, + = right).</summary>
        public float S, Lateral, Theta;
        /// <summary>Speed is forward velocity in the car's frame; SideSpeed is its sideways velocity.</summary>
        public float Speed, SideSpeed, YawRate, Steer, LateralAccel, LongAccel;
        public bool OffRoad, HitWall, Sliding, OnRumble;
        /// <summary>Automatic transmission selector: P, R, N or D.</summary>
        /// <summary>Most recent solid hit: closing speed in m/s and what was struck.</summary>
        public float LastImpact; public string LastHitKind = "";
        /// <summary>0 is undamaged, 1 is disabled. FrontCrush is how far the nose has been pushed in, in meters.</summary>
        public float Damage, FrontCrush;
        public int ImpactCount, RepairCount;
        public bool Disabled => Damage >= 1f;
        float pullDirection = 1f;
        bool wallContact, sideContact;
        /// <summary>The exit road the car is on, or null when on the main highway.</summary>
        public RoutePath Current; public RouteJunction CurrentJunction;
        public int RoadSwitches;
        public char Gear = 'P';
        public float Beta => Mathf.Atan2(SideSpeed, Mathf.Max(1f, Mathf.Abs(Speed)));

        public DriveSim(CarHandling car) { Car = car; }
        public float TopSpeed => Car.topMph / MetersPerSecondToMph * (1f - 0.45f * Damage);

        public void Reset(RoutePath path, float s)
        {
            S = s; Lateral = path.HalfWidth(s) - RoutePath.LaneWidth / 2f;
            Current = null; CurrentJunction = null;
            Theta = Speed = SideSpeed = YawRate = Steer = LateralAccel = LongAccel = 0;
            Gear = 'P';
            Damage = FrontCrush = 0f; crashDecel = crashTurn = stunned = 0f; pinS = float.PositiveInfinity; wallContact = sideContact = false;
            RepairCount++;
        }

        void TakeDamage(float amount, float impact, float pull)
        {
            Damage = Mathf.Clamp01(Damage + amount);
            LastImpact = impact; ImpactCount++;
            if (Mathf.Abs(pull) > 0.05f) pullDirection = Mathf.Sign(pull);
        }

        /// <summary>Applies contact with another vehicle. Separation and velocity change are along the road and across it.</summary>
        public void ApplyVehicleHit(float sepS, float sepLat, float dvS, float dvLat, float damage, float crush, float yawKick, float impact, float pull)
        {
            S += sepS; Lateral += sepLat;
            float c = Mathf.Cos(Theta), s = Mathf.Sin(Theta);
            float along = Speed * c - SideSpeed * s + dvS, across = Speed * s + SideSpeed * c + dvLat;
            Speed = along * c + across * s;
            SideSpeed = -along * s + across * c;
            YawRate += yawKick;
            LastHitKind = "vehicle";
            if (damage <= 0f) return;
            TakeDamage(damage, impact, pull);
            FrontCrush = Mathf.Min(0.55f, FrontCrush + crush);
            if (impact > 7f) stunned = 0.5f;
        }

        public void Step(RoutePath path, DriveInput input, float dt)
        {
            lastDt = dt;
            var mainRoad = path;
            if (Current != null) path = Current;
            if (stunned > 0f) { stunned -= dt; input.Throttle = 0f; input.Brake = 0f; }
            if (Disabled) input.Throttle = 0f;
            // Bent suspension pulls the car to one side.
            if (Mathf.Abs(Speed) > 2f) input.Steer = Mathf.Clamp(input.Steer + pullDirection * Damage * 0.07f, -1f, 1f);
            float half = path.HalfWidth(S);
            OffRoad = Lateral > half + RightShoulder || Lateral < -half - LeftShoulder;
            float edge = Mathf.Abs(Lateral) - half + 0.9f;
            OnRumble = !OffRoad && edge > 0.4f && edge < 2.1f;
            float mu = Car.gripG * (OffRoad ? 0.5f : 1f) * (1f - 0.2f * Damage);
            float before = Speed;

            UpdateSpeed(input, mu * G, dt);
            if (Speed < -0.25f) Gear = 'R';
            else if (input.Throttle > 0.02f && Speed > -0.25f) Gear = 'D';

            float wanted = Mathf.Clamp(input.Steer, -1f, 1f);
            bool returning = Mathf.Abs(wanted) < Mathf.Abs(Steer) || wanted * Steer < 0;
            Steer = Mathf.MoveTowards(Steer, wanted, (returning ? Car.steerOut : Car.steerIn) * dt);

            float h = dt / Substeps;
            for (int i = 0; i < Substeps; i++) Integrate(path, input, mu, h);

            LongAccel = (Speed - before) / dt;
            Sliding = Mathf.Abs(Speed) > 6f && Mathf.Abs(Beta) > 0.07f;
            ApplyLimits(path);
            CheckJunctions(mainRoad);
        }

        void UpdateSpeed(DriveInput input, float grip, float dt)
        {
            float top = OffRoad ? Mathf.Min(TopSpeed, 22f) : TopSpeed;
            float ratio = Mathf.Clamp01(Mathf.Abs(Speed) / TopSpeed);
            float drag = 0.35f + 0.00042f * Speed * Speed + (OffRoad ? 3f + Mathf.Abs(Speed) * 0.1f : 0f);
            bool throttle = input.Throttle > 0.02f, brake = input.Brake > 0.02f;
            if (Mathf.Abs(Speed) < 0.08f && !throttle && !brake) { Speed = 0; return; }

            float a = -Mathf.Sign(Speed) * drag;
            if (brake && Speed > 0.25f) a = -Mathf.Min(Car.brake, grip) * input.Brake - drag;
            else if (brake) a = Speed > -11f ? -4.5f * input.Brake : 0f; // reverse once stopped
            else if (throttle && Speed < -0.25f) a = Mathf.Min(Car.brake, grip) * input.Throttle;
            else if (throttle && Speed < top) a = Car.accel * (1f - 0.4f * Damage) * input.Throttle * (1f - Mathf.Pow(ratio, 2.4f)) - drag * (1f - input.Throttle);
            else if (Speed > top) a = -14f;
            if (input.Handbrake) a -= Mathf.Sign(Speed) * 5f;

            float next = Speed + a * dt;
            if ((!throttle && !brake || input.Handbrake) && Mathf.Sign(next) != Mathf.Sign(Speed)) next = 0;
            Speed = next;
        }

        /// <summary>
        /// The steering input that asks for a given cornering force at the current speed.
        /// Used to make a keyboard key (all or nothing) ask for a turn the tires can hold, instead of full lock.
        /// </summary>
        public float SteerFor(float lateralAccel)
        {
            float v = Mathf.Max(1f, Mathf.Abs(Speed));
            float delta = lateralAccel * Car.wheelbase / (v * v);
            float shaped = delta * (1f + (v * v) / (SteerSpeedRef * SteerSpeedRef)) / MaxWheelAngle;
            return Mathf.Clamp01(Mathf.Pow(Mathf.Clamp01(shaped), 1f / 1.35f));
        }

        /// <summary>How much of the full turn a key asks for the instant it's pressed, and the seconds of holding it takes to reach all of it.</summary>
        public static float KeyTap = 0.2f, KeyBuild = 0.6f;
        /// <summary>Stability control: rear slip (radians) where it steps in, the range over which it reaches full strength, its strength (rad/s²) and the speed it costs.</summary>
        public static float EscSlip = 0.09f, EscRange = 0.1f, EscYaw = 4f, EscDrag = 0.4f;
        // The slip angle (radians) where the tire curve above gives its most grip. Steering past it turns the car less, not more.
        static readonly float PeakSlip = Mathf.Tan(Mathf.PI / (2f * TireC)) / TireB;

        /// <summary>
        /// Keyboard steering, 0..1, by how long the key has been held. A key is all or nothing, so time stands in for
        /// how far a hand would turn the wheel: a tap is a nudge, and a held key builds in about half a second to the
        /// hardest turn the front tires can make at this speed. It never asks for more than that, because more steering
        /// past the tires' peak only makes the car push wide.
        /// </summary>
        public float KeySteer(float held)
        {
            float v = Mathf.Max(1f, Mathf.Abs(Speed));
            float limit = GripAccel * Car.wheelbase / (v * v) + PeakSlip;
            float shaped = limit * (1f + (v * v) / (SteerSpeedRef * SteerSpeedRef)) / MaxWheelAngle;
            float full = Mathf.Pow(Mathf.Clamp01(shaped), 1f / 1.35f);
            float build = Mathf.Lerp(KeyTap, 1f, Mathf.SmoothStep(0f, 1f, held / KeyBuild));
            return Mathf.Max(full * build, .12f);
        }

        /// <summary>Most cornering force the tires give on dry pavement, in m/s².</summary>
        public float GripAccel => Car.gripG * G;

        static float Tire(float slip, float peak) => -peak * Mathf.Sin(TireC * Mathf.Atan(TireB * slip));

        void Integrate(RoutePath path, DriveInput input, float mu, float h)
        {
            float v = Mathf.Abs(Speed);
            // Road-wheel angle available from the input shrinks with speed like a person's hands do, but never to a safe limit.
            float shaped = Mathf.Sign(Steer) * Mathf.Pow(Mathf.Abs(Steer), 1.35f);
            float delta = shaped * MaxWheelAngle / (1f + (v * v) / (SteerSpeedRef * SteerSpeedRef));

            float L = Car.wheelbase, a = L * (1f - Car.frontWeight), b = L * Car.frontWeight;
            float inertia = Car.mass * 1.65f;
            float kinematicYaw = Speed / L * Mathf.Tan(delta);
            float blend = Mathf.Clamp01((Speed - 3f) / 5f);

            float brakeUse = Speed > 0.25f ? Mathf.Clamp01(input.Brake) : 0f;
            float lateralShare = Mathf.Sqrt(1f - 0.6f * brakeUse * brakeUse);
            float frontPeak = mu * G * Car.mass * Car.frontWeight * lateralShare;
            float rearPeak = mu * 1.08f * G * Car.mass * (1f - Car.frontWeight) * lateralShare * (input.Handbrake ? 0.3f : 1f);

            float vx = Mathf.Max(3f, Speed);
            float slipF = Mathf.Atan2(SideSpeed + a * YawRate, vx) - delta;
            float slipR = Mathf.Atan2(SideSpeed - b * YawRate, vx);
            float fyF = Tire(slipF, frontPeak), fyR = Tire(slipR, rearPeak);

            float sideAccel = (fyF * Mathf.Cos(delta) + fyR) / Car.mass;
            float yawAccel = (a * fyF * Mathf.Cos(delta) - b * fyR) / inertia;

            // Stability control, standard on US cars since 2012: when the rear starts to let go, it brakes one front wheel
            // to pull the car straight, which costs a little speed. The handbrake overrides it, and a crash is too much for it.
            float over = Mathf.Abs(slipR) - EscSlip;
            if (over > 0f && Car.stability > 0f && !input.Handbrake && stunned <= 0f && v > 8f)
            {
                float help = Mathf.Min(1f, over / EscRange) * EscYaw * Car.stability * (1f - Damage);
                yawAccel += Mathf.Sign(slipR) * help;
                Speed -= Mathf.Sign(Speed) * help * EscDrag * h;
            }

            float dynYaw = YawRate + yawAccel * h;
            float dynSide = SideSpeed + (sideAccel - Speed * YawRate) * h;
            YawRate = Mathf.Lerp(kinematicYaw, dynYaw, blend);
            SideSpeed = Mathf.Lerp(0f, dynSide, blend);
            Speed += blend * (-fyF * Mathf.Sin(delta) / Car.mass + YawRate * SideSpeed) * h;
            LateralAccel = blend * sideAccel + (1f - blend) * YawRate * Speed;

            float k = path.Curvature(S);
            float cos = Mathf.Cos(Theta), sin = Mathf.Sin(Theta);
            float sDot = (Speed * cos - SideSpeed * sin) / Mathf.Max(0.3f, 1f - k * Lateral);
            S += sDot * h;
            Lateral += (Speed * sin + SideSpeed * cos) * h;
            Theta = Mathf.Repeat(Theta + (YawRate - k * sDot) * h + Mathf.PI, Mathf.PI * 2f) - Mathf.PI;
        }

        public const float CarHalfLength = 2.3f, CarHalfWidth = 0.9f;
        readonly HashSet<int> broken = new HashSet<int>();
        float crashDecel, crashTurn, stunned, lastDt = 1f / 60f, pinS = float.PositiveInfinity;
        public int BrokenCount; public float LastBrokenS, LastBrokenLat;

        // How far the front of the car crushes against each kind of object, per unit of the base distance.
        static float CrushFactor(string kind) => kind == "tree" ? 1.25f : kind == "pole" ? 1.1f : 1f;

        void HitObstacles(RoutePath path, float dt)
        {
            if (crashDecel > 0f)
            {
                Speed = Mathf.MoveTowards(Speed, 0f, crashDecel * dt);
                Theta += crashTurn * dt;
                if (Speed <= 0f) { crashDecel = 0f; crashTurn = 0f; }
            }
            var items = path.Obstacles;
            int lo = 0, hi = items.Length;
            while (lo < hi) { int mid = (lo + hi) / 2; if (items[mid].s < S - 8f - path.MaxObstacleHalfS) lo = mid + 1; else hi = mid; }
            bool touching = false;
            for (int i = lo; i < items.Length && items[i].s < S + 8f + path.MaxObstacleHalfS; i++)
            {
                if (broken.Contains(i)) continue;
                var o = items[i];
                float ds = S - o.s, dl = Lateral - o.lat;
                float overS = CarHalfLength + o.halfS - Mathf.Abs(ds), overL = CarHalfWidth + o.halfLat - Mathf.Abs(dl);
                if (overS <= 0 || overL <= 0) continue;
                HitWall = true; LastHitKind = o.kind;

                if (o.kind == "post" && Mathf.Abs(Speed) > 2f)
                {
                    // Light sign posts shear off; the car loses a little speed and carries on.
                    for (int j = lo; j < items.Length && items[j].s < S + 8f; j++)
                        if (Mathf.Abs(items[j].s - o.s) < 0.01f) broken.Add(j);
                    TakeDamage(0.03f, Mathf.Abs(Speed), 0f);
                    Speed *= 0.88f;
                    BrokenCount++; LastBrokenS = o.s; LastBrokenLat = o.lat;
                    continue;
                }

                touching = true;
                if (overS < overL && ds < 0)
                {
                    if (float.IsPositiveInfinity(pinS))
                    {
                        float v = Mathf.Max(0f, Speed * Mathf.Cos(Theta));
                        float crush = Mathf.Clamp(0.25f + v * 0.026f, 0.25f, 1.1f) * CrushFactor(o.kind);
                        pinS = S + crush;
                        if (v > 2f)
                        {
                            float severity = o.kind == "pier" ? 1f : o.kind == "pole" ? 0.8f : 0.85f;
                            TakeDamage(Mathf.Pow(v / 31f, 2f) * 0.7f * severity, v, -dl);
                            FrontCrush = Mathf.Min(0.55f, FrontCrush + crush * 0.55f);
                            crashDecel = v * v / (2f * crush);
                            // Off-center hits swing the car around the object.
                            float offset = Mathf.Clamp(-dl / (CarHalfWidth + o.halfLat), -1f, 1f);
                            crashTurn = offset * Mathf.Min(v, 30f) * 0.075f / Mathf.Max(0.05f, crush / v);
                            crashTurn = Mathf.Clamp(crashTurn, -9f, 9f);
                            stunned = v > 7f ? 0.8f : 0.2f;
                        }
                    }
                    if (crashDecel <= 0f && Speed > 0f) Speed = 0f;
                    S = Mathf.Min(S, pinS);
                }
                else if (overS < overL)
                {
                    S += overS; if (Speed < 0f) Speed = 0f;
                }
                else
                {
                    // Side contact scrapes along the object; only motion into it is removed, so the car is not straightened.
                    float toward = SideSpeed * Mathf.Cos(Theta) + Speed * Mathf.Sin(Theta);
                    Lateral += Mathf.Sign(dl) * overL;
                    if (toward * dl < 0f)
                    {
                        if (!sideContact && Mathf.Abs(toward) > 2f) TakeDamage(0.2f * Mathf.Pow(Mathf.Abs(toward) / 15f, 2f), Mathf.Abs(toward), -dl);
                        sideContact = true;
                        SideSpeed = 0f; Speed *= 0.985f;
                    }
                }
            }
            if (!touching) { pinS = float.PositiveInfinity; sideContact = false; }
        }

        void CheckJunctions(RoutePath main)
        {
            if (Current == null)
            {
                if (Speed < 1f || Lateral <= main.HalfWidth(S) + 0.6f) return;
                foreach (var j in main.Junctions)
                    if (S > j.divergeS && S < j.divergeS + RoutePath.GateLength)
                    {
                        var world = main.Point(S, Lateral); var forward = Rotate(main.Tangent(S), Theta);
                        float bestS = 0, bestD = float.MaxValue;
                        for (float q = 0; q < Mathf.Min(j.road.Length, 200f); q += 2f)
                        {
                            float d = (j.road.Center(q) - world).sqrMagnitude;
                            if (d < bestD) { bestD = d; bestS = q; }
                        }
                        // Only a car actually on the exit lane joins it; anywhere else it stays on the highway.
                        float onRamp = Vector3.Dot(world - j.road.Center(bestS), j.road.Right(bestS));
                        if (Mathf.Abs(onRamp) > j.road.HalfWidth(bestS) + 0.3f) return;
                        Current = j.road; CurrentJunction = j; S = bestS;
                        Lateral = onRamp;
                        Theta = HeadingFrom(j.road.Tangent(bestS), forward);
                        RoadSwitches++;
                        return;
                    }
                return;
            }
            bool atEnd = S >= Current.Length - 3f;
            bool inOpening = S < RoutePath.ExitOpen || S > Current.Length - RoutePath.ExitOpen;
            bool inTaper = S < RoutePath.ExitTaper || S > Current.Length - RoutePath.ExitTaper;
            if (atEnd || (inOpening && Lateral < -Current.HalfWidth(S) - 0.4f) || (inTaper && Lateral > Current.HalfWidth(S) + 0.4f))
            {
                // Hand back at the exact same world position, so joining the highway has no jump.
                var forward = Rotate(Current.Tangent(S), Theta);
                var world = Current.Point(S, Lateral);
                var j = CurrentJunction;
                float mainS = atEnd ? j.mergeS : S < RoutePath.ExitOpen ? j.divergeS + S : j.mergeS - (Current.Length - S);
                S = mainS; Lateral = Vector3.Dot(world - main.Center(S), main.Right(S));
                Theta = HeadingFrom(main.Tangent(S), forward);
                Current = null; CurrentJunction = null; RoadSwitches++;
            }
        }

        static Vector3 Rotate(Vector3 tangent, float theta) => Quaternion.AngleAxis(theta * Mathf.Rad2Deg, Vector3.up) * tangent;
        static float HeadingFrom(Vector3 tangent, Vector3 forward) => Vector3.SignedAngle(new Vector3(tangent.x, 0, tangent.z), new Vector3(forward.x, 0, forward.z), Vector3.up) * Mathf.Deg2Rad;

        void ApplyLimits(RoutePath path)
        {
            bool wasHit = HitWall;
            HitWall = false;
            float half = path.HalfWidth(S);
            if (!wasHit) wallContact = false;
            if (path.IsRamp)
            {
                float wall = half + 0.6f;
                if (Mathf.Abs(Lateral) > wall)
                {
                    float into = Mathf.Abs(Speed * Mathf.Sin(Theta) + SideSpeed * Mathf.Cos(Theta));
                    if (!wallContact) { if (into > 1f) TakeDamage(0.5f * Mathf.Pow(into / 20f, 2f), into, Lateral); Speed = Mathf.MoveTowards(Speed, 0f, into * 0.6f); }
                    wallContact = true;
                    Lateral = Mathf.Clamp(Lateral, -wall, wall);
                    Speed = Mathf.MoveTowards(Speed, 0f, 2f * lastDt);
                    Theta *= 0.82f; YawRate *= 0.3f; SideSpeed = 0f; HitWall = true; LastHitKind = "barrier";
                }
                if (S < 3f) { S = 3f; Speed = Mathf.Max(0, Speed); }
                return;
            }
            {
                // The concrete median runs the whole road; the right-hand barrier only exists on structures.
                float right = path.HasBarrier(S) && !path.InGate(S) ? half + RoutePath.BarrierRight - 1.2f : path.HasSoundWall(S) ? half + RoutePath.SoundWallOffset - 1.2f : float.PositiveInfinity, left = -half - RoutePath.BarrierLeft + 1.2f;
                // Only a car actually at the barrier hits it; one already beyond it stays where it is.
                float? wallAt = null;
                foreach (var j in path.Junctions)
                {
                    if (S <= j.divergeS + RoutePath.ExitTaper || S >= j.mergeS - RoutePath.ExitTaper) continue;
                    bool leftWall = S > j.divergeS + RoutePath.ExitOpen && S < j.mergeS - RoutePath.ExitOpen;
                    float c = half + RoutePath.ExitOffset(S - j.divergeS), span = RoutePath.ExitHalf + RoutePath.ExitWallGap, near = c - span, far = c + span;
                    if (leftWall && Lateral > near - 1.1f && Lateral < near + 1.5f) wallAt = near - 1.1f;
                    else if (Lateral < far + 1.1f && Lateral > far - 1.5f) wallAt = far + 1.1f;
                    break;
                }
                if (wallAt != null || (Lateral > right && Lateral < right + 4f) || (Lateral < left && Lateral > left - 2f))
                {
                    Lateral = wallAt ?? Mathf.Clamp(Lateral, left, right);
                    float into = Mathf.Abs(Speed * Mathf.Sin(Theta) + SideSpeed * Mathf.Cos(Theta));
                    if (!wallContact)
                    {
                        if (into > 1f) TakeDamage(0.5f * Mathf.Pow(into / 20f, 2f), into, Lateral);
                        Speed = Mathf.MoveTowards(Speed, 0f, into * 0.6f);
                    }
                    wallContact = true;
                    Speed = Mathf.MoveTowards(Speed, 0f, 2f * lastDt); // rubbing friction while in contact
                    Theta *= 0.82f; YawRate *= 0.3f; SideSpeed = 0f; HitWall = true;
                    LastHitKind = "barrier";
                }
            }
            HitObstacles(path, lastDt);
            Lateral = Mathf.Clamp(Lateral, -WorldEdge, WorldEdge);
            if (S < 5f) { S = 5f; Speed = Mathf.Max(0, Speed); }
            if (S > path.Length - 60f) Reset(path, 150f);
        }
    }

    /// <summary>Tap-style keyboard driver used by automated tests. Only produces -1, 0 or +1 steering, like a person on WASD.</summary>
    public class LaneBot
    {
        public int TargetLaneFromRight;
        public float SpeedMph = 200f;
        /// <summary>When set, heads for the shoulder past this distance to take an exit.</summary>
        public float ExitAtS = -1f;

        public DriveInput Think(RoutePath path, DriveSim car)
        {
            if (car.Current != null) path = car.Current;
            float half = path.HalfWidth(car.S);
            float target = path.IsRamp ? 0f : half - RoutePath.LaneWidth * (TargetLaneFromRight + 0.5f);
            if (!path.IsRamp && ExitAtS > 0f && car.S > ExitAtS) target = half + 2.2f;
            float aim = Mathf.Clamp((target - car.Lateral) * 0.055f, -0.09f, 0.09f);
            float look = path.Curvature(car.S + car.Speed * 0.4f);
            float err = aim - car.Theta - car.Beta - car.YawRate * 0.3f + look * car.Speed * 0.22f;
            float steer = Mathf.Abs(err) < 0.004f ? 0f : Mathf.Sign(err);
            float mph = car.Speed * DriveSim.MetersPerSecondToMph;
            return new DriveInput { Throttle = mph < SpeedMph ? 1f : 0f, Brake = mph > SpeedMph + 3f ? 1f : 0f, Steer = steer };
        }
    }
}
