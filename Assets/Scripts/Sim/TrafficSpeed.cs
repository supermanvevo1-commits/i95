using SurviveI95.Data;
using UnityEngine;

namespace SurviveI95.Sim
{
    /// <summary>
    /// How fast each driver goes: the posted limit plus a personal habit picked once.
    /// On US freeways, measured speeds average about 5 over the limit, the 85th percentile sits about 9-10 over,
    /// and only a sliver drive 20+ over. Those few are ordinary drivers, in any lane.
    /// A habit is the speed on an open road. Traffic holds people back, so habits sit about 3 mph above
    /// what the road measures. Checked with the bridge's "speeds" command (meanOver, p85).
    /// </summary>
    public partial class TrafficSim
    {
        // Cumulative odds for an average driver, then the open-road mph-over range for each band.
        static readonly float[] HabitOdds = { .12f, .35f, .82f, .95f, .99f };
        static readonly Vector2[] HabitBands =
        {
            new Vector2(-4f, 2f),   // at or under the limit
            new Vector2(2f, 7f),    // a little over
            new Vector2(7f, 12f),   // most people
            new Vector2(12f, 16f),  // brisk
            new Vector2(16f, 22f),  // fast
            new Vector2(22f, 38f),  // far over: the one the police will find
        };

        float PickHabit(VehicleDefinition def, DriverKind kind, DriverTraits t)
        {
            // A few over and sure that's fast enough, in the fast lane.
            if (kind == DriverKind.Camper) return Rand(0f, 5f);
            // Trucks mostly run near the limit, and many are governed.
            if (kind == DriverKind.Truck) return Rand(-4f, 5f);
            float u = (float)rng.NextDouble() + (t.Aggression - .4f) * .25f;
            int band = 0;
            while (band < HabitOdds.Length && u >= HabitOdds[band]) band++;
            var r = HabitBands[band];
            return Rand(r.x, r.y);
        }

        /// <summary>The most this vehicle will do whatever the limit: towing and freight top out, cars barely do.</summary>
        static float TopMph(VehicleDefinition def) => def.category == "freight" || def.category == "recreation" ? def.cruiseMph[1] : 110f;

        public float LimitMph(float s) => path.LimitMph(s);

        /// <summary>Off only to compare against the old behavior in tests.</summary>
        public static bool LaneSorting = true;

        /// <summary>
        /// The lane a driver settles in, counted from the right: about the limit or under keeps right,
        /// 12+ over lives in the left lane, everyone else spreads across the middle.
        /// </summary>
        static int PreferredLane(TrafficCar c, int lanes) => Mathf.Clamp(Mathf.RoundToInt(Mathf.InverseLerp(-1f, 13f, c.SpeedHabit) * (lanes - 1)), 0, lanes - 1);

        readonly int[] laneSamples = new int[8];
        readonly float[] laneOver = new float[8], laneTop = new float[8];

        /// <summary>Adds one sample of every car's speed by lane, for tests. Lanes count from the right; the left lane is reported as "left".</summary>
        public void SampleLanes()
        {
            foreach (var c in Cars)
            {
                if (c.Speed < 4f || c.Changing) continue;
                int lanes = LaneCount(c.S);
                // Index from the left, so "left lane" means the same thing on 3, 4 and 5 lane stretches.
                int fromLeft = Mathf.Clamp(lanes - 1 - c.Lane, 0, 7);
                float over = c.Speed * DriveSim.MetersPerSecondToMph - path.LimitMph(c.S);
                laneSamples[fromLeft]++; laneOver[fromLeft] += over; laneTop[fromLeft] = Mathf.Max(laneTop[fromLeft], over);
            }
        }

        /// <summary>Average and fastest speed over the limit per lane, left lane first.</summary>
        public int CamperYields;

        public string LaneSpeeds()
        {
            var sb = new System.Text.StringBuilder($"camperYields={CamperYields} ");
            for (int i = 0; i < 8; i++)
                if (laneSamples[i] > 0)
                    sb.Append(System.FormattableString.Invariant($"{(i == 0 ? "left" : "left+" + i)}: share={100f * laneSamples[i] / Mathf.Max(1, SumSamples()):F0}% avg={laneOver[i] / laneSamples[i]:+0.0;-0.0} top={laneTop[i]:+0} | "));
            return sb.ToString();
        }

        int SumSamples() { int t = 0; foreach (int x in laneSamples) t += x; return t; }

        float CruiseAt(VehicleDefinition def, float habit, float s) => MphToMs(Mathf.Clamp(path.LimitMph(s) + habit, 25f, TopMph(def)));
        float CruiseAt(TrafficCar c, float s) => CruiseAt(c.Def, c.SpeedHabit, s);
        float CruiseAt(Regular r, float s) => CruiseAt(r.Def, r.SpeedHabit, s);

        /// <summary>The shares of drivers by how far over the limit they're going right now, for tests.</summary>
        public string SpeedSpread()
        {
            int under = 0, little = 0, most = 0, brisk = 0, fast = 0, far = 0, n = 0;
            foreach (var c in Cars)
            {
                if (c.Kind == DriverKind.Truck) continue;
                float over = c.SpeedHabit; n++;
                if (over < 0f) under++; else if (over < 5f) little++; else if (over < 10f) most++; else if (over < 15f) brisk++; else if (over < 20f) fast++; else far++;
            }
            n = Mathf.Max(1, n);
            return $"cars={n} under={100 * under / n}% 0-4={100 * little / n}% 5-9={100 * most / n}% 10-14={100 * brisk / n}% 15-19={100 * fast / n}% 20+={100 * far / n}%";
        }
    }
}
