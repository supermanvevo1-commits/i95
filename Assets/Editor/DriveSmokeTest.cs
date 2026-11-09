using SurviveI95.Sim;
using SurviveI95.World;
using UnityEditor;
using UnityEngine;

namespace SurviveI95.EditorTools
{
    public static class DriveSmokeTest
    {
        /// <summary>Holds the throttle down the whole route with light lane-keeping and reports whether it stays on the road.</summary>
        public static void Run()
        {
            var path = RoutePath.Load("florida_jupiter");
            var sim = new DriveSim { S = 300f };
            sim.Lateral = path.HalfWidth(300f) - RoutePath.LaneWidth / 2f;
            float maxOff = 0, maxMph = 0, maxCurve = 0;
            int ticks = 0;
            while (sim.S < path.Length - 80f && ticks < 60 * 600)
            {
                float target = path.HalfWidth(sim.S) - RoutePath.LaneWidth / 2f;
                sim.Step(path, new DriveInput { Throttle = true, Steer = Mathf.Clamp((target - sim.Lateral) * 0.5f, -1, 1) }, 1f / 60f);
                maxOff = Mathf.Max(maxOff, Mathf.Abs(sim.Lateral) - path.HalfWidth(sim.S));
                maxMph = Mathf.Max(maxMph, sim.Speed * DriveSim.MetersPerSecondToMph);
                maxCurve = Mathf.Max(maxCurve, Mathf.Abs(path.Curvature(sim.S)));
                ticks++;
            }
            Debug.Log($"[DriveTest] {sim.S / 1000f:F1} km in {ticks / 60f:F0}s, top {maxMph:F0} mph, max past lane edge {maxOff:F2} m, min radius {1f / maxCurve:F0} m");
            if (sim.S < path.Length - 100f || maxOff > 3f) EditorApplication.Exit(1);
        }
    }
}
