using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SurviveI95.Sim;
using SurviveI95.World;
using UnityEngine;

namespace SurviveI95.EditorTools
{
    /// <summary>Finds anything solid sitting in the paved road, by geometry and by driving every lane.</summary>
    public static class LaneAudit
    {
        public static string Run(string outFile)
        {
            var path = RoutePath.Load(FloridaSliceBuilder.RouteId);
            var sb = new StringBuilder();

            int bad = 0;
            foreach (var o in path.Obstacles)
            {
                float worst = 0;
                for (float s = o.s - o.halfS; s <= o.s + o.halfS + 0.1f; s += Mathf.Max(5f, o.halfS / 4f))
                {
                    float half = path.HalfWidth(s), carEdge = 0.9f;
                    float into = Mathf.Min(o.lat + o.halfLat - (-half - 1.3f - carEdge), half + 3.05f + carEdge - (o.lat - o.halfLat));
                    worst = Mathf.Max(worst, into);
                }
                if (worst > 0f) { bad++; if (bad <= 25) sb.AppendLine($"GEOMETRY {o.kind} s={o.s:F0} lat={o.lat:F1} halfS={o.halfS:F1} halfLat={o.halfLat:F1} intrudes {worst:F1} m"); }
            }
            sb.AppendLine($"objects intruding on pavement: {bad} of {path.Obstacles.Length}");

            var counts = new Dictionary<string, int>();
            for (int lane = 0; lane < 3; lane++)
            {
                var car = new DriveSim(CarHandling.Load("sedan"));
                car.Reset(path, 150f);
                var bot = new LaneBot { SpeedMph = 90f, TargetLaneFromRight = lane };
                float lastS = -999f; int steps = 0;
                while (car.S < path.Length - 120f && steps++ < 60 * 1500)
                {
                    float before = car.S;
                    car.Step(path, bot.Think(path, car), 1f / 60f);
                    if (car.S < before - 100f) break;
                    if (car.HitWall && car.S - lastS > 30f)
                    {
                        lastS = car.S;
                        string key = $"lane {lane} {car.LastHitKind}";
                        counts[key] = counts.TryGetValue(key, out var n) ? n + 1 : 1;
                        if (counts[key] <= 6) sb.AppendLine($"DRIVE {key} at s={car.S:F0} lateral={car.Lateral:F1} half={path.HalfWidth(car.S):F1}");
                    }
                    if (car.Speed < 3f && steps > 400)
                    {
                        float s = car.S + 40f;
                        car.Reset(path, s); car.Speed = 40f;
                        car.Lateral = path.HalfWidth(s) - RoutePath.LaneWidth * (lane + 0.5f);
                    }
                }
            }
            sb.AppendLine("drive hits: " + (counts.Count == 0 ? "none" : string.Join(", ", counts.Select(k => k.Key + "=" + k.Value))));
            File.WriteAllText(outFile, sb.ToString());
            return "audit done";
        }
    }
}
