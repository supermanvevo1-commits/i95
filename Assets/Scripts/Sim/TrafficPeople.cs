using SurviveI95.Data;
using UnityEngine;

namespace SurviveI95.Sim
{
    public enum AgeBand { Teen, Young, Adult, Senior, Elderly }
    public enum LicenseStatus { Valid, Points, Suspended, None }

    /// <summary>
    /// Who is behind the wheel: age, experience and license. Skill comes from experience, not from the car.
    /// Real numbers behind it: per mile, drivers 16-19 have nearly 3 times the fatal crash rate of drivers 20+ (IIHS).
    /// Drivers 70+ rise again per mile, much of it from fragility rather than mistakes. About 1 in 5 fatal crashes
    /// involves an unlicensed or improperly licensed driver (AAA Foundation), with suspended or revoked licenses
    /// about 5-7% of drivers in them. Those shares are of fatal crashes, so in everyday traffic they're smaller.
    /// </summary>
    public partial class TrafficSim
    {
        // Skill range by age band. Experience is what builds it.
        static readonly Vector2[] SkillRange = { new Vector2(.15f, .5f), new Vector2(.35f, .75f), new Vector2(.5f, .95f), new Vector2(.5f, .9f), new Vector2(.25f, .65f) };

        void PickPerson(TrafficCar c, DriverProfile p)
        {
            float u = (float)rng.NextDouble();
            float young = p.youngShare, old = p.oldShare;
            if (u < young * .3f) { c.AgeBand = AgeBand.Teen; c.AgeYears = rng.Next(16, 20); }
            else if (u < young) { c.AgeBand = AgeBand.Young; c.AgeYears = rng.Next(20, 25); }
            else if (u < 1f - old) { c.AgeBand = AgeBand.Adult; c.AgeYears = rng.Next(25, 65); }
            else if (u < 1f - old * .3f) { c.AgeBand = AgeBand.Senior; c.AgeYears = rng.Next(65, 80); }
            else { c.AgeBand = AgeBand.Elderly; c.AgeYears = rng.Next(80, 93); }
            var r = SkillRange[(int)c.AgeBand];
            c.Skill = Rand(r.x, r.y);

            // License: most are valid. Points on the record are common; suspended or never licensed is rare, and leans young.
            float youth = c.AgeBand <= AgeBand.Young ? 2f : 1f;
            float l = (float)rng.NextDouble();
            c.License = l < .015f * youth ? LicenseStatus.None : l < .04f * youth ? LicenseStatus.Suspended : l < .16f ? LicenseStatus.Points : LicenseStatus.Valid;
        }

        /// <summary>Age and record shift the traits on top of the vehicle profile.</summary>
        void ApplyPerson(TrafficCar c)
        {
            var t = c.Traits;
            float aggr = 0f, attn = 0f, disc = 0f, habit = 0f;
            switch (c.AgeBand)
            {
                case AgeBand.Teen: aggr = .15f; attn = -.12f; disc = -.15f; habit = 4f; break;
                case AgeBand.Young: aggr = .08f; attn = -.05f; disc = -.08f; habit = 2.5f; break;
                case AgeBand.Senior: aggr = -.08f; disc = .05f; habit = -2f; break;
                case AgeBand.Elderly: aggr = -.12f; attn = -.15f; habit = -4f; break;
            }
            // Driving on a suspended license, or without one, goes with taking chances. Points mean a history of speeding.
            if (c.License == LicenseStatus.Suspended || c.License == LicenseStatus.None) { aggr += .1f; habit += 2f; }
            else if (c.License == LicenseStatus.Points) { aggr += .05f; habit += 2f; }
            c.Traits = new DriverTraits { Aggression = Mathf.Clamp01(t.Aggression + aggr), Attention = Mathf.Clamp01(t.Attention + attn), LaneDiscipline = Mathf.Clamp01(t.LaneDiscipline + disc) };
            if (c.Kind != DriverKind.Truck && c.Kind != DriverKind.Camper) c.SpeedHabit += habit;
        }

        public int Wanders;

        /// <summary>
        /// A low-skill driver now and then lets the car drift onto the lane line for a few seconds, then jerks it back.
        /// Only toward a side with room, for now: crashes caused by drivers come with the accident system (M6).
        /// </summary>
        void StartWander(TrafficCar c, int i, float dt)
        {
            if (c.WanderLeft > 0f || c.Changing || c.Wrecked || c.Speed < 12f) return;
            float perMinute = (1f - c.Skill) * (1f - c.Skill) * .8f + (1f - c.Traits.Attention) * .12f;
            if ((float)rng.NextDouble() >= perMinute / 60f * dt) return;
            int dir = rng.NextDouble() < .5 ? 1 : -1; // +1 drifts toward the left line
            int side = c.Lane + dir;
            if (side >= 0 && side < LaneCount(c.S))
            {
                Scan(i, LaneCenter(c.S, side), c.Width, out float gap, out _, out float backGap, out _, out _);
                if (gap < 20f || backGap < 20f) return;
            }
            c.WanderLeft = Rand(2f, 5f);
            c.WanderTarget = -dir * Rand(.7f, 1.1f); // lateral is positive to the right side of the road
            Wanders++;
        }

        /// <summary>The drift for this frame: eases out onto the line, then a quick correction back.</summary>
        static float WanderOffset(TrafficCar c, float dt)
        {
            c.WanderLeft -= dt;
            float want = c.WanderLeft > 0f ? c.WanderTarget : 0f;
            float rate = c.WanderLeft > 0f ? .35f : 1.6f; // slow drift out, sharp jerk back
            c.WanderNow = Mathf.MoveTowards(c.WanderNow, want, rate * dt);
            return c.WanderNow;
        }

        readonly float[,] ageLog = new float[5, 7];

        /// <summary>Adds a driver to the per-age tally. Called when a car leaves the road.</summary>
        void LogAge(TrafficCar c, float[,] n)
        {
            if (c.Age < 20f) return;
            int b = (int)c.AgeBand;
            n[b, 0]++; n[b, 1] += c.Speed * DriveSim.MetersPerSecondToMph - path.LimitMph(c.S);
            n[b, 2] += c.Changes; n[b, 6] += c.Age / 60f; n[b, 3] += c.GapSamples > 0 ? c.GapSum / c.GapSamples : 2f;
            n[b, 4] += c.Signals ? 1f : 0f; n[b, 5] += c.Skill;
        }

        void LogAge(TrafficCar c) => LogAge(c, ageLog);

        /// <summary>Per age band: drivers seen, skill, speed over the limit, lane changes per minute, gap kept, share who signal.</summary>
        public string AgeSpread()
        {
            var n = (float[,])ageLog.Clone();
            foreach (var c in Cars) LogAge(c, n);
            var sb = new System.Text.StringBuilder($"wanders={Wanders} ");
            for (int b = 0; b < 5; b++)
            {
                float k = Mathf.Max(1f, n[b, 0]);
                sb.Append(System.FormattableString.Invariant($"{(AgeBand)b}: n={n[b, 0]:F0} skill={n[b, 5] / k:F2} over={n[b, 1] / k:+0.0;-0.0} changes/min={n[b, 2] / Mathf.Max(.01f, n[b, 6]):F2} gap={n[b, 3] / k:F2}s signals={100f * n[b, 4] / k:F0}% | "));
            }
            return sb.ToString();
        }
    }
}
