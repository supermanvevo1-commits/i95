using System.Collections.Generic;
using SurviveI95.Data;
using UnityEngine;

namespace SurviveI95.Sim
{
    /// <summary>
    /// The car says something about the driver: minivans drive calm, sports cars race, luxury cars own the road,
    /// lifted trucks push people around, rentals brake for no reason. Every profile only shifts the odds.
    /// </summary>
    public partial class TrafficSim
    {
        static Dictionary<string, DriverProfile> profiles;
        static readonly DriverProfile Everyday = new DriverProfile { id = "everyday", name = "Everyday driver", rager = 1f };

        static DriverProfile Profile(string id)
        {
            if (profiles == null)
            {
                profiles = new Dictionary<string, DriverProfile>();
                foreach (var p in GameData.LoadDriverProfiles().profiles) profiles[p.id] = p;
            }
            return id != null && profiles.TryGetValue(id, out var found) ? found : Everyday;
        }

        public static DriverProfile FindProfile(string id) => Profile(id);

        DriverProfile PickProfile(VehicleDefinition def)
        {
            if (def.drivers == null || def.drivers.Length == 0) return Profile("everyday");
            float total = 0f;
            foreach (var d in def.drivers) total += d.weight * ProfileFactor(d.profile);
            float r = Rand(0f, total);
            foreach (var d in def.drivers) { r -= d.weight * ProfileFactor(d.profile); if (r <= 0f) return Profile(d.profile); }
            return Profile(def.drivers[0].profile);
        }

        static DriverTraits Shift(DriverTraits t, DriverProfile p) => new DriverTraits
        {
            Aggression = Mathf.Clamp01(t.Aggression + p.aggression),
            Attention = Mathf.Clamp01(t.Attention + p.attention),
            LaneDiscipline = Mathf.Clamp01(t.LaneDiscipline + p.discipline)
        };

        /// <summary>Rentals and snowbirds tap the brakes for no reason: unsure of the exit, reading the GPS.</summary>
        float BrakeTaps(TrafficCar c, float a, float dt)
        {
            var p = c.Profile;
            if (p == null || p.brakeTaps <= 0f || c.Wrecked || c.Speed < 10f) return a;
            c.TapLeft -= dt;
            if (c.TapLeft <= 0f && (float)rng.NextDouble() < p.brakeTaps / 60f * dt) c.TapLeft = Rand(.4f, .9f);
            return c.TapLeft > 0f ? Mathf.Min(a, -2.2f) : a;
        }

        public int Races;
        // A racer is flat out: far past the cruising cap of ordinary traffic, and kept in the race until 500 m apart.
        const float RaceTopMph = 150f, RaceSight = 500f;

        /// <summary>A sporty driver the player blows past may take it as a challenge and race them for a while.</summary>
        void Challenge(TrafficCar c, float dt)
        {
            var p = c.Profile;
            if (p == null) return;
            float dS = player.S - c.S;
            if (c.RaceLeft > 0f)
            {
                c.RaceLeft -= dt;
                // Chase the player down and try to edge ahead. It ends when the player backs off, one of them is out of
                // sight, or the racer has had enough. The player was already past them when it began, so they need room to catch up.
                float want = Mathf.Min(player.SpeedS + (dS > 15f ? 9f : 2.5f), MphToMs(RaceTopMph));
                bool backedOff = player.SpeedS < MphToMs(path.LimitMph(c.S));
                if (backedOff || Mathf.Abs(dS) > RaceSight) c.RaceLeft = 0f;
                else c.Desired = Mathf.Max(c.Desired, want);
                if (c.RaceLeft <= 0f)
                {
                    string who = $"the {Describe(c.Def, c.Paint)}{PlateNote(c.PlateState)}";
                    Notice?.Invoke(dS > 60f ? $"You left {who} behind." : dS < -60f ? $"You lost to {who}." : backedOff ? $"You backed off. {char.ToUpper(who[0]) + who.Substring(1)} takes it." : $"{char.ToUpper(who[0]) + who.Substring(1)} eases off.");
                }
                return;
            }
            // Remember that the player came up from behind until they are clearly gone, so the pass itself registers.
            if (dS < -4f) c.PlayerWasBehind = true;
            else if (dS > 14f) c.PlayerWasBehind = false;
            if (c.Raced || p.racer <= 0f || c.Regular != null) return;
            bool beside = Mathf.Abs(player.Lateral - c.Lateral) < RoutePathLane * 1.6f;
            // The moment the player blows past them in the next lane, well over the limit.
            if (c.PlayerWasBehind && dS > 2f && dS < 12f && beside && player.SpeedS > c.Speed + 2f && player.SpeedS > MphToMs(path.LimitMph(c.S) + 8f))
            {
                c.Raced = true;
                if ((float)rng.NextDouble() < p.racer)
                {
                    c.RaceLeft = Rand(50f, 90f); Races++;
                    Notice?.Invoke($"The {Describe(c.Def, c.Paint)}{PlateNote(c.PlateState)} wants to race.");
                }
            }
        }

        const float RoutePathLane = World.RoutePath.LaneWidth;

        readonly Dictionary<string, float[]> profileLog = new Dictionary<string, float[]>();

        /// <summary>Adds what a driver did to a tally per profile. Called when a car leaves the road.</summary>
        void LogProfile(TrafficCar c) => LogProfile(c, profileLog);

        void LogProfile(TrafficCar c, Dictionary<string, float[]> log)
        {
            if (c.Profile == null || c.Age < 20f) return;
            string id = c.Profile.id.Replace("_test", "");
            if (!log.TryGetValue(id, out var v)) log[id] = v = new float[7];
            v[0]++; v[1] += c.Temper; v[2] += c.Speed * DriveSim.MetersPerSecondToMph - path.LimitMph(c.S);
            v[3] += c.Changes; v[4] += c.Age / 60f; v[5] += c.GapSamples > 0 ? c.GapSum / c.GapSamples : 0f; v[6] += c.GapSamples > 0 ? 1 : 0;
        }

        /// <summary>Per profile: drivers seen, average temper, speed over the limit, lane changes per minute and following gap in seconds.</summary>
        public string ProfileSpread()
        {
            // Drivers who left are in the tally; the ones still on the road are added to a copy for this report.
            var all = new SortedDictionary<string, float[]>();
            foreach (var kv in profileLog) all[kv.Key] = (float[])kv.Value.Clone();
            var live = new Dictionary<string, float[]>();
            foreach (var c in Cars) LogProfile(c, live);
            foreach (var kv in live)
            {
                if (!all.TryGetValue(kv.Key, out var v)) { all[kv.Key] = kv.Value; continue; }
                for (int i = 0; i < v.Length; i++) v[i] += kv.Value[i];
            }
            var sb = new System.Text.StringBuilder();
            foreach (var kv in all)
            {
                var v = kv.Value;
                sb.Append(System.FormattableString.Invariant($"{kv.Key}: n={v[0]:F0} temper={v[1] / v[0]:F2} over={v[2] / v[0]:F1}mph changes/min={v[3] / Mathf.Max(.01f, v[4]):F2} gap={(v[6] > 0 ? v[5] / v[6] : 0f):F2}s | "));
            }
            return sb.ToString();
        }
    }
}
