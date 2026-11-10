using System;
using System.Collections.Generic;
using SurviveI95.Data;
using SurviveI95.World;
using UnityEngine;

namespace SurviveI95.Sim
{
    public enum Grievance { None, CutOff, BrakeCheck, Tailgated, Blocked, Hit }

    /// <summary>
    /// A driver who has had a run-in with the player. They stay in the run for good: off screen they keep driving,
    /// so the one you cut off ten miles back can catch up with you in the next slowdown.
    /// </summary>
    public class Regular
    {
        public int Id;
        public string Name, Plate;
        public VehicleDefinition Def;
        public DriverKind Kind;
        public DriverTraits Traits;
        public int Paint;
        public bool Signals;
        public float BaseDesired;
        /// <summary>The highest level this driver's anger can reach. See <see cref="TrafficCar.Temper"/>.</summary>
        public int Temper;
        /// <summary>0 calm, 1 out for blood. Cools down slowly.</summary>
        public float Anger;
        public Grievance Last;
        public int Grievances, Returns;
        /// <summary>The car on the road, or null while out of sight.</summary>
        public TrafficCar Car;
        // Where they are while out of sight.
        public float S, Speed;
        public int Lane;
        public bool Wrecked;
        public float WreckedFor;
        internal int AnnouncedLevel;
        internal bool Announce;

        /// <summary>0 calm, 1 annoyed (horn, lights), 2 angry (follows you), 3 raging (alongside, cuts in), 4 out for blood (rams).</summary>
        public int Level => Anger < .15f ? 0 : Anger < .4f ? 1 : Anger < .65f ? 2 : Anger < .85f ? 3 : 4;
        public bool OnScreen => Car != null;
    }

    public partial class TrafficSim
    {
        public const int MaxRegulars = 12;
        const float AngerCooling = .0006f; // per second: a big grudge takes ten minutes or more to fade

        /// <summary>Paint names in the same order as the paint colors TrafficManager draws.</summary>
        public static readonly string[] PaintNames = { "silver", "silver", "gray", "charcoal", "black", "black", "red", "blue", "navy", "tan", "green", "silver" };

        public readonly List<Regular> Regulars = new List<Regular>();
        /// <summary>Short lines for the screen, such as "The red pickup truck is back."</summary>
        public Action<string> Notice;
        public Action<TrafficCar, bool> Honk;
        public int Honks, Grudges, Returns, BrakeChecks, Rams, Shrugs;

        float lastDt = .02f, playerDecel, playerPrevSpeed, playerLaneChangedAt = -99f;
        int playerLane = -1;
        bool scanLeadPlayer, scanBackPlayer, playerHasTrafficAhead;

        /// <summary>Notes when the player changes lanes, and whether they have traffic right in front, which excuses braking.</summary>
        void WatchPlayer()
        {
            if (!player.Present) { playerLane = -1; return; }
            int lane = LaneAt(player.S, player.Lateral);
            if (lane != playerLane) { if (playerLane >= 0) playerLaneChangedAt = clock; playerLane = lane; }
            playerHasTrafficAhead = false;
            float room = Mathf.Max(12f, player.SpeedS * 1.5f);
            foreach (var o in Cars)
            {
                float gap = o.S - player.S - o.Length / 2f - player.HalfLength;
                if (gap > -1f && gap < room && Mathf.Abs(o.Lateral - player.Lateral) < player.HalfWidth + o.Width / 2f + .3f) { playerHasTrafficAhead = true; break; }
            }
        }

        public static string Describe(VehicleDefinition def, int paint)
        {
            string n = def.name.ToLowerInvariant().Replace(" (rv)", "");
            return def.id == "taxi" ? "taxi" : PaintNames[paint % PaintNames.Length] + " " + n;
        }

        static string WithArticle(string name) => ("aeiou".IndexOf(name[0]) >= 0 ? "an " : "a ") + name;

        string MakePlate()
        {
            const string letters = "ABCDEFGHJKLMNPQRSTVWXYZ";
            char L() => letters[rng.Next(letters.Length)];
            return $"{L()}{L()}{L()} {L()}{rng.Next(10)}{rng.Next(10)}";
        }

        static bool Chases(Regular r) => r.Kind == DriverKind.Normal || r.Kind == DriverKind.Tailgater || r.Kind == DriverKind.Weaver;
        static float ChaseSpeed(Regular r, float target) => Mathf.Min(MphToMs(r.Def.cruiseMph[1] + 18f), Mathf.Max(r.BaseDesired, target + 3f));

        /// <summary>The speed this driver wants: an angry one behind the player keeps up with them.</summary>
        float CruiseFor(Regular r, float s) => r.Level >= 2 && Chases(r) && player.Present && s < player.S ? ChaseSpeed(r, player.SpeedS) : r.BaseDesired;

        int LaneAt(float s, float lat) => Mathf.Clamp(Mathf.FloorToInt((path.HalfWidth(s) - lat) / RoutePath.LaneWidth), 0, LaneCount(s) - 1);

        int NextTo(int lane, TrafficCar c)
        {
            int max = (int)MaxLaneFor(c.Kind, LaneCount(c.S));
            int left = lane + 1, right = lane - 1;
            bool l = left <= max, r = right >= 0;
            if (l && r) return Mathf.Abs(left - c.Lane) <= Mathf.Abs(right - c.Lane) ? left : right;
            return l ? left : r ? right : lane;
        }

        // --------------------------------------------------------------- temper

        // Most people who get cut off mutter and drive on. AAA Foundation's 2014 survey of 2,705 US drivers: in a whole year
        // about half had honked or tailgated in anger, a quarter had tried to block someone, and under 4% had confronted
        // or rammed anyone. Those are drivers who did it at least once among many provocations, so per run-in it is rarer still.
        // Cumulative odds for an average driver: shrugs 35%, honks 35%, follows and blocks 20%, yells and cuts in 8.5%, rams 1.5%.
        static readonly float[] TemperOdds = { .35f, .70f, .90f, .985f };
        // How much harder each kind of driver takes the same offense.
        static readonly float[] TemperGain = { 0f, .8f, 1f, 1.15f, 1.3f };
        // The most anger each temper can reach, just under the next level.
        static readonly float[] TemperCeiling = { .14f, .39f, .64f, .84f, 1f };

        int PickTemper(VehicleDefinition def, DriverKind kind, DriverTraits t)
        {
            // Aggressive drivers lean angrier; families and people towing a camper lean calmer.
            float u = (float)rng.NextDouble() + (t.Aggression - .4f) * .2f;
            if (def.id == "minivan" || def.category == "recreation") u -= .12f;
            int temper = 0;
            while (temper < TemperOdds.Length && u >= TemperOdds[temper]) temper++;
            // Truck drivers and left-lane campers dig in, but they don't chase anyone down.
            if (kind == DriverKind.Truck || kind == DriverKind.Camper) temper = Mathf.Min(temper, 2);
            return temper;
        }

        /// <summary>How many drivers of each temper there are on the road right now, for tests.</summary>
        public int[] TemperCounts()
        {
            var n = new int[5];
            foreach (var c in Cars) n[c.Temper]++;
            return n;
        }

        // --------------------------------------------------------------- memory

        Regular Promote(TrafficCar c)
        {
            if (c.Regular != null) return c.Regular;
            if (Regulars.Count >= MaxRegulars)
            {
                // Forget whoever cares least and is out of sight.
                Regular drop = null;
                foreach (var r in Regulars) if (r.Car == null && (drop == null || r.Anger < drop.Anger)) drop = r;
                if (drop == null) return null;
                Regulars.Remove(drop);
            }
            var reg = new Regular
            {
                Id = c.Id, Def = c.Def, Kind = c.Kind, Traits = c.Traits, Paint = c.Paint, Signals = c.Signals, Temper = c.Temper,
                BaseDesired = c.Desired, Car = c, Plate = MakePlate(), Name = Describe(c.Def, c.Paint)
            };
            Regulars.Add(reg);
            c.Regular = reg;
            return reg;
        }

        /// <summary>The player did something to this driver. They remember it.</summary>
        public Regular Grudge(TrafficCar c, Grievance why, float amount)
        {
            // Even the calmest driver honks at someone who hits them.
            if (why == Grievance.Hit) c.Temper = Mathf.Max(c.Temper, 1);
            if (c.Temper == 0 && amount > 0f) { c.GrudgeCooldown = 4f; Shrugs++; return null; }
            var r = Promote(c);
            if (r == null) return null;
            r.Temper = c.Temper;
            int before = r.Level;
            bool first = r.Grievances == 0;
            r.Anger = Mathf.Min(TemperCeiling[r.Temper], Mathf.Clamp01(r.Anger + amount * TemperGain[r.Temper]));
            r.Last = why; r.Grievances++; Grudges++;
            c.GrudgeCooldown = 4f;
            if (first && amount > 0f)
            {
                // The first time, say what the player did, so they know who to watch for.
                r.AnnouncedLevel = Mathf.Max(1, r.Level);
                Notice?.Invoke(WhatYouDid(r) + (r.Level >= 2 ? " They didn't like that one bit." : ""));
            }
            else if (r.Level > before && r.Level > r.AnnouncedLevel)
            {
                r.AnnouncedLevel = r.Level;
                Notice?.Invoke(LevelLine(r));
            }
            return r;
        }

        static string LevelLine(Regular r)
        {
            switch (r.Level)
            {
                case 1: return WhatYouDid(r);
                case 2: return $"The {r.Name} isn't letting that go.";
                case 3: return $"The {r.Name} is coming for you.";
                default: return $"The {r.Name} wants you off the road.";
            }
        }

        static string WhatYouDid(Regular r)
        {
            switch (r.Last)
            {
                case Grievance.CutOff: return $"You cut off {WithArticle(r.Name)}.";
                case Grievance.BrakeCheck: return $"You brake-checked {WithArticle(r.Name)}.";
                case Grievance.Tailgated: return $"You're riding the bumper of {WithArticle(r.Name)}.";
                case Grievance.Blocked: return $"You've been holding up {WithArticle(r.Name)}.";
                default: return $"You hit {WithArticle(r.Name)}.";
            }
        }

        /// <summary>Clears every grudge, for tests.</summary>
        public void ForgetRegulars()
        {
            foreach (var r in Regulars) if (r.Car != null) r.Car.Regular = null;
            Regulars.Clear();
        }

        // --------------------------------------------------------------- out of sight

        void Detach(TrafficCar c)
        {
            var r = c.Regular;
            if (r == null) return;
            r.Car = null; r.S = c.S; r.Speed = c.Speed; r.Lane = c.Lane; r.Wrecked = c.Wrecked; r.WreckedFor = 0f;
        }

        void StepRegulars(float dt, float anchor, float anchorSpeed)
        {
            for (int k = Regulars.Count - 1; k >= 0; k--)
            {
                var r = Regulars[k];
                r.Anger = Mathf.Max(0f, r.Anger - AngerCooling * dt);
                if (r.Car != null) continue;
                if (r.Wrecked)
                {
                    // Out of sight, a wreck gets itself going again: bent, and in no better mood.
                    r.WreckedFor += dt; r.Speed = 0f;
                    if (r.WreckedFor > 75f) r.Wrecked = false;
                    continue;
                }
                bool chasing = r.Level >= 2 && Chases(r) && r.S < anchor;
                float want = chasing ? ChaseSpeed(r, anchorSpeed) : r.BaseDesired;
                r.Speed = Mathf.MoveTowards(r.Speed, want, 1.5f * dt);
                r.S += r.Speed * dt;
                if (r.S > path.Length - RouteMargin) Regulars.RemoveAt(k);
            }
        }

        void BringBackRegulars(float anchor)
        {
            foreach (var r in Regulars)
            {
                if (r.Car != null) continue;
                float d = r.S - anchor;
                if (d < -Behind + 15f || d > Ahead - 15f || r.S < RouteStart) continue;
                // After a jump down the road, nobody should appear out of thin air next to the player.
                if (d > -250f && d < 120f) { r.S = anchor + (d < 0f ? -Behind + 20f : Ahead - 20f); continue; }
                int lanes = LaneCount(r.S);
                int lane = Mathf.Clamp(r.Lane, 0, (int)MaxLaneFor(r.Kind, lanes));
                while (lane > 0 && !float.IsInfinity(LaneEnd(r.S, lane, LaneEndLook + 300f))) lane--;
                int tries = 0;
                while (!SpotIsClear(r.S, lane, r.Def.lengthM) && tries++ < lanes) lane = (lane + 1) % Mathf.Max(1, (int)MaxLaneFor(r.Kind, lanes) + 1);
                if (!SpotIsClear(r.S, lane, r.Def.lengthM)) continue;
                var c = Create(r.Def, r.Kind, r.S, lane, r.Speed);
                Spawned--; // the same driver, not new traffic
                c.Traits = r.Traits; c.Paint = r.Paint; c.Signals = r.Signals; c.Desired = r.BaseDesired; c.Temper = r.Temper;
                c.Regular = r; r.Car = c;
                if (r.Wrecked) { c.Wrecked = true; c.Involved = true; c.Hazards = true; c.Speed = 0f; }
                r.Announce = true;
            }
        }

        // --------------------------------------------------------------- on the road

        /// <summary>
        /// Sets what this driver wants from the player: their speed, their lane, how close they follow.
        /// Returns the most acceleration that plan allows.
        /// </summary>
        float Personal(TrafficCar c, float gap, bool leadPlayer, float backGap, bool backPlayer, float dt)
        {
            const float Free = float.PositiveInfinity;
            var r = c.Regular;
            if (r == null || c.Wrecked) return Free;
            float dS = player.S - c.S; // positive: the player is ahead
            c.Desired = CruiseFor(r, c.S);
            if (Mathf.Abs(dS) > 300f) { c.FlippedOff = c.CutIn = c.RamNoticed = false; c.AlongsideTime = 0f; return Free; }

            if (r.Announce && Mathf.Abs(dS) < 150f)
            {
                r.Announce = false; r.Returns++; Returns++;
                if (r.Level >= 1) Notice?.Invoke(r.Level >= 2 ? $"The {r.Name} is back, and still angry." : $"The {r.Name} is back.");
            }
            int lvl = r.Level;
            if (lvl == 0) return Free;

            float side = Mathf.Abs(player.Lateral - c.Lateral);
            bool sameLane = side < player.HalfWidth + c.Width / 2f + .35f;
            bool adjacent = !sameLane && side < RoutePath.LaneWidth * 1.7f;

            // Annoyed: the horn and the high beams.
            if (Mathf.Abs(dS) < 30f && side < RoutePath.LaneWidth * 1.7f && c.HonkCooldown <= 0f)
            {
                c.HonkCooldown = Rand(6f, 11f) / lvl; Honks++;
                Honk?.Invoke(c, lvl >= 2);
            }
            c.AngryFlash = leadPlayer && gap < 45f;
            if (lvl < 2) return Free;

            bool chases = Chases(r);
            int pLane = LaneAt(player.S, player.Lateral);

            // Raging: pull up alongside and let them have it, then cut in. Out for blood: swerve into them.
            if (lvl >= 3 && chases && adjacent && Mathf.Abs(dS) < 8f && !c.CutIn)
            {
                c.AlongsideTime += dt;
                c.WantLane = c.Lane;
                if (c.AlongsideTime > .6f && !c.FlippedOff)
                {
                    c.FlippedOff = true; c.HonkCooldown = 3f; Honks++;
                    Honk?.Invoke(c, true);
                    Notice?.Invoke($"The {r.Name} pulls up beside you, yelling and flipping you off.");
                }
                if (lvl >= 4 && Mathf.Abs(dS) < 3.5f && c.RamCooldown <= 0f && c.AlongsideTime > 1.2f)
                {
                    c.ShoveDir = Mathf.Sign(player.Lateral - c.Lateral); c.ShoveLeft = .9f;
                    c.RamCooldown = Rand(3.5f, 6f); c.RamAt = clock; Rams++;
                    if (!c.RamNoticed) { c.RamNoticed = true; Notice?.Invoke($"The {r.Name} swerves into you!"); }
                }
                if (c.AlongsideTime > 3.5f) c.CutIn = true;
                // Hold position beside the player.
                c.Desired = player.SpeedS + 6f;
                return Mathf.Clamp(1.2f * dS + 1.6f * (player.SpeedS - c.Speed), -4f, MaxAccel(c));
            }
            if (!(adjacent && Mathf.Abs(dS) < 8f)) c.AlongsideTime = Mathf.Max(0f, c.AlongsideTime - dt * .5f);

            if (c.CutIn)
            {
                // Get ahead, then move over right in front of them.
                c.Desired = player.SpeedS + 7f;
                c.WantLane = dS < -(c.Length / 2f + 6f) ? pLane : c.Lane;
                if (c.Lane == pLane || Mathf.Abs(dS) > 80f) { c.CutIn = false; c.AlongsideTime = 0f; }
                return Free;
            }

            if (dS < 0f)
            {
                // Ahead of the player: get in front, hold them up, brake on them.
                if (chases) c.WantLane = pLane;
                bool inFront = backPlayer && backGap < 60f;
                // Holds the player up at a speed picked once, a bit under theirs, then eventually gives up and goes.
                if (!inFront) { c.BlockSpeed = 0f; c.BlockLeft = 0f; }
                else if (r.Kind != DriverKind.Truck && player.SpeedS > 8f)
                {
                    if (c.BlockSpeed == 0f) { c.BlockSpeed = Mathf.Max(9f, player.SpeedS - MphToMs(Rand(4f, 9f))); c.BlockLeft = Rand(15f, 30f); }
                    c.BlockLeft -= dt;
                    if (c.BlockLeft > 0f) c.Desired = Mathf.Min(c.Desired, c.BlockSpeed);
                }
                // Not while still tangled up with the player after contact: that just drags both cars to a stop.
                if (inFront && backGap > 3f && backGap < Mathf.Max(22f, player.SpeedS * 1.3f) && c.RageTimer <= 0f && c.Speed > 12f && !c.Changing
                    && clock - c.LastPlayerHit > 6f && r.Kind != DriverKind.Truck)
                {
                    c.BrakeCheckLeft = .6f + .25f * lvl; c.RageTimer = Rand(12f, 20f); BrakeChecks++;
                    Notice?.Invoke($"The {r.Name} brake-checks you.");
                }
                return Free;
            }

            // Behind the player: ride their bumper, or come up beside them.
            if (chases) c.WantLane = lvl >= 3 ? NextTo(pLane, c) : pLane;
            c.TightFollow = true;
            if (lvl >= 4 && chases && leadPlayer && gap < 25f && clock - c.LastPlayerHit > 2.5f)
            {
                c.RamAccel = Mathf.Clamp((player.SpeedS + 4f - c.Speed) * 1.5f, -3f, MaxAccel(c));
                c.RamAt = clock;
                if (!c.RamNoticed) { c.RamNoticed = true; Rams++; Notice?.Invoke($"The {r.Name} is trying to ram you!"); }
            }
            return Free;
        }

        /// <summary>Watches what the player does to this driver.</summary>
        void Judge(TrafficCar c, float a, float gap, bool leadPlayer, float backGap, bool backPlayer, float dt)
        {
            c.PlayerLeadTime = leadPlayer ? c.PlayerLeadTime + dt : 0f;
            if (c.Wrecked || c.GrudgeCooldown > 0f) return;
            if (Mathf.Abs(player.S - c.S) > 150f) { c.TailTime = c.BlockTime = 0f; return; }
            float aggression = c.Traits.Aggression;

            // Made to brake hard: the player just pulled in front (cut off), or braked on them for no reason.
            if (leadPlayer && gap < 60f && a < -3.2f && clock - c.RamAt > 1.5f)
            {
                // A cut-off means the player just changed into this lane; pulling in behind the player is the driver's own doing.
                bool cut = c.PlayerLeadTime < 3.5f && clock - playerLaneChangedAt < 3.5f && c.SinceChange > 3f;
                // A brake-check is hard braking with nothing in front of the player. A crash stop is not one.
                bool check = playerDecel > 2.5f && playerDecel < 12f && !playerHasTrafficAhead;
                if (cut || check)
                {
                    Grudge(c, cut ? Grievance.CutOff : Grievance.BrakeCheck, .16f + .14f * aggression + Mathf.Min(.25f, (-a - 3.2f) * .05f));
                    return;
                }
            }

            // Stuck behind a player who won't go any faster or move over.
            float wants = c.Regular != null ? c.Regular.BaseDesired : c.Desired;
            if (leadPlayer && gap < 45f && player.SpeedS < wants - 3f && c.Speed > 10f && c.Kind != DriverKind.Truck) c.BlockTime += dt * (.4f + aggression);
            else c.BlockTime = Mathf.Max(0f, c.BlockTime - dt);
            if (c.BlockTime > 14f) { c.BlockTime = 0f; Grudge(c, Grievance.Blocked, .1f + .1f * aggression); return; }

            // The player sitting on their bumper.
            if (backPlayer && backGap < Mathf.Max(6f, c.Speed * .45f) && c.Speed > 12f) c.TailTime += dt;
            else c.TailTime = Mathf.Max(0f, c.TailTime - dt);
            if (c.TailTime > 3f) { c.TailTime = 0f; Grudge(c, Grievance.Tailgated, .08f + .1f * aggression); }
        }
    }
}
