using SurviveI95.Sim;
using SurviveI95.World;
using UnityEditor;
using UnityEngine;

namespace SurviveI95.EditorTools
{
    /// <summary>Measured driving benchmarks, runnable without opening a scene.</summary>
    public static class DriveSmokeTest
    {
        const float Dt = 1f / 60f, Mph = DriveSim.MetersPerSecondToMph;

        public static void Run()
        {
            var path = RoutePath.Load("florida_jupiter");
            foreach (var id in new[] { "sedan", "muscle", "pickup" }) Bench(path, id);
        }

        static void Bench(RoutePath path, string id)
        {
            var car = CarHandling.Load(id);
            var s = new DriveSim(car); s.Reset(path, 300f);
            float t = 0, t60 = -1, t100 = -1;
            while (t < 40f) { s.Step(path, new DriveInput { Throttle = 1f }, Dt); t += Dt; float m = s.Speed * Mph; if (t60 < 0 && m >= 60) t60 = t; if (t100 < 0 && m >= 100) t100 = t; }
            float reached = s.Speed * Mph;

            s = new DriveSim(car); s.Reset(path, 300f); s.Speed = 60 / Mph;
            float start = s.S; while (s.Speed > 0.25f) s.Step(path, new DriveInput { Brake = 1f }, Dt);
            float brakeFt = (s.S - start) * 3.281f;

            // Lane change at 70 mph with tap steering.
            s = new DriveSim(car); s.Reset(path, 2000f); s.Speed = 70 / Mph;
            var bot = new LaneBot { SpeedMph = 70, TargetLaneFromRight = 1 };
            float settle = -1; t = 0; float over = 0; float target = 0;
            while (t < 12f)
            {
                s.Step(path, bot.Think(path, s), Dt); t += Dt;
                target = path.HalfWidth(s.S) - RoutePath.LaneWidth * 1.5f;
                over = Mathf.Max(over, target - s.Lateral);
                if (Mathf.Abs(target - s.Lateral) < 0.3f && settle < 0) settle = t;
                if (Mathf.Abs(target - s.Lateral) > 0.6f) settle = -1;
            }

            // Whole route at full throttle on a keyboard-style bot.
            s = new DriveSim(car); s.Reset(path, 300f);
            bot = new LaneBot { SpeedMph = 100f }; // a keyboard bot can genuinely lose it flat-out, as a person can
            float rms = 0, maxErr = 0, maxLat = 0; int n = 0, swaps = 0, last = 0, walls = 0, off = 0; t = 0;
            while (s.S < path.Length - 80f && t < 600f)
            {
                var input = bot.Think(path, s);
                int sign = (int)input.Steer; if (sign != 0 && sign != last) { swaps++; last = sign; }
                s.Step(path, input, Dt); t += Dt; n++;
                float err = path.HalfWidth(s.S) - RoutePath.LaneWidth / 2f - s.Lateral;
                if (t > 12f) { rms += err * err; maxErr = Mathf.Max(maxErr, Mathf.Abs(err)); }
                maxLat = Mathf.Max(maxLat, Mathf.Abs(s.LateralAccel));
                if (s.HitWall) walls++; if (s.OffRoad) off++;
            }
            // Steering response across the speed range: full key held for one second.
            var sweep = new System.Text.StringBuilder();
            foreach (float m in new[] { 15f, 30f, 45f, 60f, 75f, 90f, 120f })
            {
                var q = new DriveSim(car); q.Reset(path, 2000f); q.Lateral = 0; q.Speed = m / Mph;
                float peakG = 0;
                for (int i = 0; i < 60; i++) { q.Step(path, new DriveInput { Steer = 1f }, Dt); peakG = Mathf.Max(peakG, Mathf.Abs(q.LateralAccel) / 9.81f); }
                sweep.Append($" {m:F0}mph:{q.Theta * Mathf.Rad2Deg:F0}deg/{peakG:F2}g/slip{q.Beta * Mathf.Rad2Deg:F0}");
            }
            Debug.Log($"[DriveTest] {id} 1s full steer:{sweep}");

            // Handbrake turn at 60 mph must rotate the car, shed speed and recover.
            s = new DriveSim(car); s.Reset(path, 2000f); s.Lateral = 0; s.Speed = 60 / Mph;
            float peakBeta = 0;
            for (int i = 0; i < 60; i++) { s.Step(path, new DriveInput { Steer = -1, Handbrake = true }, Dt); peakBeta = Mathf.Max(peakBeta, Mathf.Abs(s.Beta)); }
            float exitMph = s.Speed * Mph;
            for (int i = 0; i < 180; i++) s.Step(path, new DriveInput(), Dt);
            float settledBeta = Mathf.Abs(s.Beta);

            Debug.Log($"[DriveTest] {id}: handbrake peak slip {peakBeta * Mathf.Rad2Deg:F0} deg, exit {exitMph:F0} mph, slip after 3s {settledBeta * Mathf.Rad2Deg:F1} deg");
            Debug.Log($"[DriveTest] {id}: 0-60 {t60:F1}s, 0-100 {t100:F1}s, 40s speed {reached:F0} mph, 60-0 {brakeFt:F0} ft, lane change settle {settle:F1}s; " +
                      $"route {t:F0}s, lane error rms {Mathf.Sqrt(rms / n):F2} m max {maxErr:F2} m, steer flips/min {swaps / (t / 60f):F0}, max lateral {maxLat / 9.81f:F2} g, wall ticks {walls}, offroad ticks {off}");
            if (t >= 600f || walls > 0 || off > 0) { Debug.LogError("[DriveTest] FAILED " + id); if (Application.isBatchMode) EditorApplication.Exit(1); }
        }
    }
}
