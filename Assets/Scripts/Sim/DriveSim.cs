using SurviveI95.World;
using UnityEngine;

namespace SurviveI95.Sim
{
    public struct DriveInput { public bool Throttle, Brake; public float Steer; }

    /// <summary>Fixed-step driving rules in road space. No scene objects, so it can run on a server later.</summary>
    public class DriveSim
    {
        public const float MetersPerSecondToMph = 2.23694f;
        public float S, Lateral, Speed, LateralVelocity, SteerSmooth;
        public float TopSpeed = 124f / MetersPerSecondToMph, Accel = 8.5f, BrakeForce = 30f, SteerRate = 8.5f;

        public void Step(RoutePath path, DriveInput input, float dt)
        {
            float half = path.HalfWidth(S);
            bool offRoad = Mathf.Abs(Lateral) > half + 3.4f;
            float top = offRoad ? Mathf.Min(TopSpeed, 17f) : TopSpeed;
            float ratio = Mathf.Clamp01(Speed / TopSpeed);

            if (input.Throttle && Speed < top) Speed += Accel * (1 - ratio * ratio * 0.72f) * dt;
            if (input.Brake) Speed -= BrakeForce * dt;
            if (!input.Throttle && !input.Brake) Speed -= 2.4f * dt;
            if (Speed > top) Speed -= 20f * dt;
            Speed = Mathf.Max(0, Speed);

            SteerSmooth += (input.Steer - SteerSmooth) * Mathf.Min(1, dt * 9f);
            float authority = Mathf.Min(1, Speed / 12f) * (1 - 0.3f * ratio);
            float target = SteerSmooth * SteerRate * authority;
            LateralVelocity += (target - LateralVelocity) * Mathf.Min(1, dt * 9f);
            Lateral += LateralVelocity * dt;
            Lateral -= path.Curvature(S) * Speed * Speed * 0.5f * dt;

            float limit = half + 16f;
            if (Mathf.Abs(Lateral) > limit) { Lateral = Mathf.Sign(Lateral) * limit; LateralVelocity = 0; }

            S += Speed * dt;
            if (S > path.Length - 60f) { S = 150f; Speed = 0; Lateral = path.HalfWidth(150f) - RoutePath.LaneWidth / 2f; }
        }
    }
}
