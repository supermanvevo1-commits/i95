using System.Text;
using SurviveI95.Data;
using SurviveI95.Sim;
using SurviveI95.World;
using UnityEngine;

namespace SurviveI95.EditorTools
{
    /// <summary>
    /// Scripted crashes between the player's car and one traffic vehicle, without rendering (6.0): a rear-end, a fast
    /// overtake that clips the other car's corner (the 99 mph hit of the first playtest), a sideswipe, a motorcycle and
    /// a semi. For each: did the other vehicle wreck, how far it spun, where it stopped, and whether the player stayed
    /// stuck to it. Bridge: crashbench.
    /// </summary>
    public static class CrashBench
    {
        const float Dt = 1f / 60f, Mph = DriveSim.MetersPerSecondToMph, Start = 42300f;

        public static string Run(string file)
        {
            var path = RoutePath.Load("miami_ftl");
            var sb = new StringBuilder();
            sb.AppendLine(Case(path, "tap 63 into 55", "sedan", 63f, 55f, 0f, 0f));
            sb.AppendLine(Case(path, "rear-end 75 into 50", "sedan", 75f, 50f, 0f, 0f));
            sb.AppendLine(Case(path, "corner clip 99 past 60", "sedan", 99f, 60f, 1.3f, 0f));
            sb.AppendLine(Case(path, "corner clip 70 past 55", "sedan", 70f, 55f, 1.3f, 0f));
            sb.AppendLine(Case(path, "sideswipe 65 beside 65", "sedan", 65f, 65f, 2.9f, -3f));
            sb.AppendLine(Case(path, "nudge 51 into motorcycle 45", "motorcycle_sport", 51f, 45f, 0f, 0f));
            sb.AppendLine(Case(path, "rear-end 40 into motorcycle 20", "motorcycle_sport", 40f, 20f, 0f, 0f));
            sb.AppendLine(Case(path, "corner clip 60 past motorcycle 45", "motorcycle_sport", 60f, 45f, 1.0f, 0f));
            sb.AppendLine(Case(path, "rear-end 60 into semi 30", "semi", 60f, 30f, 0f, 0f));
            sb.AppendLine(Case(path, "sideswipe 60 beside semi 60", "semi", 60f, 60f, 3.0f, -3f));
            System.IO.File.WriteAllText(file, sb.ToString());
            return sb.ToString();
        }

        // offset: how far right of the other vehicle the player starts (0 = straight behind it); sideSpeed: the player's
        // sideways speed toward it (m/s, negative = moving left), for sideswipes.
        static string Case(RoutePath path, string name, string vehicle, float playerMph, float otherMph, float offset, float sideSpeed)
        {
            var chapter = GameData.LoadStateTraffic().chapters.Find(c => c.id == "florida");
            var sim = new TrafficSim(path, GameData.LoadVehicles(), chapter, 7) { Target = 0 };
            sim.Clear();
            var def = GameData.LoadVehicles().vehicles.Find(v => v.id == vehicle);
            var car = new DriveSim(CarHandling.Load("sedan"));
            car.Reset(path, Start);
            sim.Step(Dt, car); // the first step clears and refills traffic, so the test vehicle comes after it
            bool side = sideSpeed != 0f;
            // Close enough that the driver ahead has no time to notice and move over (they would, otherwise: that's tested live).
            float ahead = side ? 0f : (playerMph - otherMph < 12f ? 10f : 30f);
            var other = sim.Create(def, DriverKind.Normal, Start + ahead, 1, otherMph / Mph);
            other.Desired = other.Speed; other.HoldDesired = true;
            // A calm driver, so the crash happens as scripted (a hothead would brake-check the player closing in).
            other.Traits = new DriverTraits { Attention = .2f, Aggression = .1f, LaneDiscipline = .9f }; other.Temper = 0;
            sim.Step(Dt, car);
            car.Lateral = other.Lateral + offset * (side ? 1f : 1f);
            if (offset > 0f && !side) car.Lateral = other.Lateral + (other.Width / 2f + DriveSim.CarHalfWidth) - .45f; // overlapping by 45 cm: a corner
            car.Speed = playerMph / Mph;
            // A sideswipe: steer in at an angle, so the car really moves sideways with its own momentum.
            if (side) car.Theta = Mathf.Atan2(sideSpeed, car.Speed);
            float t = 0f, contactAt = -1f, spin = 0f, startYaw = other.Yaw, before = 0f, playerBefore = 0f, startLat = other.Lateral;
            int touchingLate = 0; bool wasWrecked = false, pulled = false; float wobble = 0f, minPlayer = 999f; string firstHit = "", lastSeen = ""; int hitCount = 0;
            while (t < 9f)
            {
                var input = new DriveInput { Throttle = car.Speed * Mph < playerMph && contactAt < 0f ? .6f : 0f };
                if (side && contactAt < 0f) { car.Theta = Mathf.Atan2(sideSpeed, car.Speed); car.SideSpeed = 0f; car.YawRate = 0f; }
                if (contactAt >= 0f && t > contactAt + .4f && car.Speed > 1f) input.Brake = .7f; // after a crash the driver brakes (not into reverse)
                sim.Step(Dt, car);
                car.Step(path, input, Dt);
                int hits = sim.CrashesWithPlayer;
                before = other.Speed; playerBefore = car.Speed;
                sim.HitPlayer(car);
                if (contactAt < 0f && sim.CrashesWithPlayer > 0) { contactAt = t; firstHit = sim.LastHit; }
                if (sim.LastHit != lastSeen) { lastSeen = sim.LastHit; hitCount++; }
                if (other.Wrecked) wasWrecked = true;
                if (other.PullingOver) pulled = true;
                wobble = Mathf.Max(wobble, Mathf.Abs(other.Wobble) * Mathf.Rad2Deg);
                spin = Mathf.Max(spin, Mathf.Abs(Mathf.DeltaAngle(0f, other.Yaw * Mathf.Rad2Deg))); // turned away from the lane
                float overS = DriveSim.CarHalfLength + other.Length / 2f - Mathf.Abs(car.S - other.S);
                float overL = DriveSim.CarHalfWidth + other.Width / 2f - Mathf.Abs(car.Lateral - other.Lateral);
                if (contactAt >= 0f && t > contactAt + 1.5f && overS > -.05f && overL > -.05f && Mathf.Abs(car.Speed) > 3f) touchingLate++; // still glued together while moving
                if (contactAt >= 0f && t < contactAt + .6f) minPlayer = Mathf.Min(minPlayer, car.Speed * Mph);
                t += Dt;
            }
            string outcome = wasWrecked ? (other.DownAt >= 0f ? "DOWN" : "LOST CONTROL") : pulled ? "kept it, pulled over" : contactAt >= 0f ? "kept it" : "-";
            return FormattableString(name, contactAt, outcome, spin, wobble, other, startLat, car, touchingLate, minPlayer, playerMph) + System.Environment.NewLine + $"      first hit: {firstHit}  (hits {hitCount})";
        }

        static string FormattableString(string name, float contactAt, string outcome, float spin, float wobble, TrafficCar o, float startLat, DriveSim car, int touchingLate, float minPlayer, float playerMph) =>
            System.FormattableString.Invariant($"{name,-34} {outcome,-22} spun={spin:F0}deg wobble={wobble:F1}deg endMph={o.Speed * Mph:F0} | player: lost {(minPlayer < 900f ? playerMph - minPlayer : 0f):F0} mph in the hit, damage={car.Damage * 100f:F0}% glued={touchingLate}");
    }
}
