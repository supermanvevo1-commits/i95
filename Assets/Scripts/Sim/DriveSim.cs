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
        public char Gear = 'P';
        public float Beta => Mathf.Atan2(SideSpeed, Mathf.Max(1f, Mathf.Abs(Speed)));

        public DriveSim(CarHandling car) { Car = car; }
        public float TopSpeed => Car.topMph / MetersPerSecondToMph;

        public void Reset(RoutePath path, float s)
        {
            S = s; Lateral = path.HalfWidth(s) - RoutePath.LaneWidth / 2f;
            Theta = Speed = SideSpeed = YawRate = Steer = LateralAccel = LongAccel = 0;
            Gear = 'P';
        }

        public void Step(RoutePath path, DriveInput input, float dt)
        {
            float half = path.HalfWidth(S);
            OffRoad = Lateral > half + RightShoulder || Lateral < -half - LeftShoulder;
            float edge = Mathf.Abs(Lateral) - half + 0.9f;
            OnRumble = !OffRoad && edge > 0.4f && edge < 2.1f;
            float mu = Car.gripG * (OffRoad ? 0.5f : 1f);
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
            else if (throttle && Speed < top) a = Car.accel * input.Throttle * (1f - Mathf.Pow(ratio, 2.4f)) - drag * (1f - input.Throttle);
            else if (Speed > top) a = -14f;
            if (input.Handbrake) a -= Mathf.Sign(Speed) * 5f;

            float next = Speed + a * dt;
            if ((!throttle && !brake || input.Handbrake) && Mathf.Sign(next) != Mathf.Sign(Speed)) next = 0;
            Speed = next;
        }

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

        void ApplyLimits(RoutePath path)
        {
            HitWall = false;
            float half = path.HalfWidth(S);
            if (path.HasBarrier(S))
            {
                float right = half + RoutePath.BarrierRight - 1.2f, left = -half - RoutePath.BarrierLeft + 1.2f;
                if (Lateral > right || Lateral < left)
                {
                    Lateral = Mathf.Clamp(Lateral, left, right);
                    Theta *= -0.25f; YawRate *= 0.2f; SideSpeed *= -0.2f; Speed *= 0.985f; HitWall = true;
                }
            }
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

        public DriveInput Think(RoutePath path, DriveSim car)
        {
            float half = path.HalfWidth(car.S);
            float target = half - RoutePath.LaneWidth * (TargetLaneFromRight + 0.5f);
            float aim = Mathf.Clamp((target - car.Lateral) * 0.055f, -0.09f, 0.09f);
            float look = path.Curvature(car.S + car.Speed * 0.4f);
            float err = aim - car.Theta - car.Beta - car.YawRate * 0.3f + look * car.Speed * 0.22f;
            float steer = Mathf.Abs(err) < 0.004f ? 0f : Mathf.Sign(err);
            float mph = car.Speed * DriveSim.MetersPerSecondToMph;
            return new DriveInput { Throttle = mph < SpeedMph ? 1f : 0f, Brake = mph > SpeedMph + 3f ? 1f : 0f, Steer = steer };
        }
    }
}
