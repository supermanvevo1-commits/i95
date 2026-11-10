using System;
using System.Collections.Generic;
using SurviveI95.Data;
using SurviveI95.World;
using UnityEngine;

namespace SurviveI95.Sim
{
    public enum DriverKind { Normal, Camper, Tailgater, Truck, Weaver }

    /// <summary>All 0..1. Attention: how quickly they react. Aggression: speed and gaps. LaneDiscipline: keeping right and signaling.</summary>
    public struct DriverTraits { public float Attention, Aggression, LaneDiscipline; }

    public class TrafficCar
    {
        public int Id;
        public VehicleDefinition Def;
        public DriverKind Kind;
        public DriverTraits Traits;
        public float S, Lateral, Speed, Accel, Desired, Length, Width, Mass, Yaw;
        public float PrevS, PrevLateral, PrevYaw;
        /// <summary>Lane counted from the right edge, 0 being the rightmost.</summary>
        public int Lane;
        /// <summary>-1 left, +1 right, 0 off.</summary>
        public int Signal;
        public bool Braking, Wrecked, Hazards, FlashingHighBeams;
        /// <summary>True once this car has been hit, or hit a wreck, so a crash always has a traceable cause.</summary>
        public bool Involved;
        /// <summary>Set once this driver has had a run-in with the player. They then stay in the run, on screen or not.</summary>
        public Regular Regular;
        /// <summary>The furthest this driver will ever take a grudge: 0 shrugs it off, 1 honks, 2 follows and blocks, 3 yells and cuts in, 4 rams.</summary>
        public int Temper;
        /// <summary>How many mph over the posted limit this driver likes to go. Negative is under.</summary>
        public float SpeedHabit;
        /// <summary>Keeps Desired as set, ignoring the limit. For tests that place a car at a fixed speed.</summary>
        public bool HoldDesired;
        public object View;

        internal int PendingLane, Paint;
        internal bool Signals;
        internal float SignalLeft, ChangeFrom, ChangeTime, ChangeDuration, Cooldown, ReactionLeft, SeenLeadSpeed = -1f;
        internal float Offset, OffsetVelocity, YawRate, Phase, StartledLeft, FlashTimer, BehindTimer, LastPlayerHit = -10f;
        // Run-ins with the player
        internal float PlayerLeadTime, TailTime, BlockTime, GrudgeCooldown, HonkCooldown, RageTimer, BrakeCheckLeft, AlongsideTime, RamCooldown;
        internal float RamAt = -10f, RamAccel = float.NaN, SinceChange = 99f, BlockSpeed, BlockLeft, ShoveLeft, ShoveDir;
        internal int WantLane = -1;
        internal bool AngryFlash, CutIn, FlippedOff, RamNoticed, TightFollow;

        public bool Changing => ChangeDuration > 0f && ChangeTime < ChangeDuration;
        public int PaintIndex => Paint;
    }

    /// <summary>
    /// Highway traffic in road coordinates. Holds no scene objects, so hours of driving can be tested without rendering.
    /// Cars exist only in a window around the player and follow the Intelligent Driver Model, with lane changes decided per driver.
    /// </summary>
    public partial class TrafficSim
    {
        public const float Ahead = 1250f, Behind = 600f, FarDistance = 350f;
        const int FarThinkEvery = 6;
        const float RouteMargin = 170f, RouteStart = 25f, LaneEndLook = 900f;

        readonly RoutePath path;
        readonly List<VehicleDefinition> pool = new List<VehicleDefinition>();
        readonly List<float> weights = new List<float>();
        readonly System.Random rng;
        float totalWeight, lastAnchor = float.NaN, clock;
        int nextId, tick;

        struct Body { public bool Present; public float S, Lateral, SpeedS, SpeedLat, HalfLength, HalfWidth; }
        Body player;

        public readonly List<TrafficCar> Cars = new List<TrafficCar>();
        public Action<TrafficCar> Removed;
        public int Target = 40;
        public int CrashesWithPlayer, ChainCrashes, UnprovokedCrashes, LaneChanges, LaneChangesSignaled, Spawned;

        public TrafficSim(RoutePath path, VehicleCatalog catalog, ChapterTraffic chapter, int seed)
        {
            this.path = path;
            rng = new System.Random(seed);
            foreach (var row in chapter.mix)
            {
                var def = catalog.vehicles.Find(v => v.id == row.id);
                if (def == null) throw new InvalidOperationException("Traffic mix names unknown vehicle: " + row.id);
                pool.Add(def); weights.Add(row.weight); totalWeight += row.weight;
            }
        }

        float Rand(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        public static float MphToMs(float mph) => mph / DriveSim.MetersPerSecondToMph;

        // --------------------------------------------------------------- lanes

        public int LaneCount(float s) => Mathf.Max(1, Mathf.FloorToInt(path.Lanes(s) + 0.2f));
        public float LaneCenter(float s, int lane) => path.HalfWidth(s) - RoutePath.LaneWidth * (lane + 0.5f);

        /// <summary>Where this lane ends within the distance ahead, or infinity if it carries on.</summary>
        float LaneEnd(float s, int lane, float distance)
        {
            for (float d = 40f; d <= distance; d += 40f)
                if (lane >= LaneCount(s + d)) return s + d;
            return float.PositiveInfinity;
        }

        // --------------------------------------------------------------- creating drivers

        VehicleDefinition PickVehicle()
        {
            float r = Rand(0f, totalWeight);
            for (int i = 0; i < pool.Count; i++) { r -= weights[i]; if (r <= 0f) return pool[i]; }
            return pool[pool.Count - 1];
        }

        // Campers are memorable, not common: about one driver in ten who could be one is.
        static float Weight(string personality) => personality == "normal" ? 58f : personality == "camper" ? 9f : personality == "rager" ? 13f : personality == "weaver" ? 11f : 100f;

        DriverKind PickKind(VehicleDefinition def)
        {
            float total = 0f;
            foreach (var p in def.personalities) total += Weight(p);
            float r = Rand(0f, total);
            foreach (var p in def.personalities) { r -= Weight(p); if (r <= 0f) return ToKind(p); }
            return ToKind(def.personalities[0]);
        }

        static DriverKind ToKind(string p) => p == "camper" ? DriverKind.Camper : p == "rager" ? DriverKind.Tailgater : p == "truck" ? DriverKind.Truck : p == "weaver" ? DriverKind.Weaver : DriverKind.Normal;

        DriverTraits MakeTraits(DriverKind k)
        {
            switch (k)
            {
                case DriverKind.Camper: return new DriverTraits { Attention = Rand(.45f, .8f), Aggression = Rand(.05f, .25f), LaneDiscipline = Rand(0f, .15f) };
                case DriverKind.Tailgater: return new DriverTraits { Attention = Rand(.7f, .95f), Aggression = Rand(.8f, 1f), LaneDiscipline = Rand(.15f, .5f) };
                case DriverKind.Truck: return new DriverTraits { Attention = Rand(.75f, 1f), Aggression = Rand(.05f, .35f), LaneDiscipline = Rand(.8f, 1f) };
                case DriverKind.Weaver: return new DriverTraits { Attention = Rand(.6f, .9f), Aggression = Rand(.65f, .95f), LaneDiscipline = Rand(.05f, .4f) };
                default: return new DriverTraits { Attention = Rand(.55f, 1f), Aggression = Rand(.2f, .6f), LaneDiscipline = Rand(.55f, 1f) };
            }
        }

        static float MassOf(VehicleDefinition d)
        {
            if (d.category == "motorcycle") return 300f;
            if (d.category == "freight" || d.category == "recreation") return Mathf.Max(3200f, d.lengthM * d.lengthM * 45f);
            return 1500f * (d.lengthM / 4.8f) * (d.widthM / 1.85f);
        }

        float MaxLaneFor(DriverKind k, int lanes) => k == DriverKind.Truck ? Mathf.Min(1, lanes - 1) : lanes - 1;

        public TrafficCar Create(VehicleDefinition def, DriverKind kind, float s, int lane, float speed = -1f)
        {
            var traits = MakeTraits(kind);
            float habit = PickHabit(def, kind, traits);
            var c = new TrafficCar
            {
                Id = ++nextId, Def = def, Kind = kind, Traits = traits, S = s, Lane = lane, PendingLane = -1, SpeedHabit = habit,
                Desired = CruiseAt(def, habit, s), Length = def.lengthM, Width = def.widthM, Mass = MassOf(def),
                Phase = Rand(0f, 6.28f), Paint = rng.Next(0, 1000), Cooldown = Rand(1f, 8f),
                Signals = rng.NextDouble() < 0.25f + 0.75f * traits.LaneDiscipline
            };
            c.Temper = PickTemper(def, kind, traits);
            c.Speed = speed >= 0f ? speed : c.Desired;
            c.Lateral = LaneCenter(s, lane);
            c.PrevS = c.S; c.PrevLateral = c.Lateral;
            Insert(c);
            Spawned++;
            return c;
        }

        void Insert(TrafficCar c)
        {
            int i = Cars.Count;
            Cars.Add(c);
            while (i > 0 && Cars[i - 1].S > c.S) { Cars[i] = Cars[i - 1]; i--; }
            Cars[i] = c;
        }

        void RemoveAt(int i) { var c = Cars[i]; Cars.RemoveAt(i); Detach(c); Removed?.Invoke(c); }

        public void Clear() { for (int i = Cars.Count - 1; i >= 0; i--) RemoveAt(i); }

        bool SpotIsClear(float s, int lane, float length)
        {
            float lat = LaneCenter(s, lane);
            foreach (var o in Cars)
                if (Mathf.Abs(o.S - s) < (o.Length + length) / 2f + 45f && Mathf.Abs(o.Lateral - lat) < 3f) return false;
            if (player.Present && Mathf.Abs(player.S - s) < 60f && Mathf.Abs(player.Lateral - lat) < 3f) return false;
            return true;
        }

        bool TrySpawn(float s, float anchorSpeed, bool behindPlayer)
        {
            if (s < RouteStart || s > path.Length - RouteMargin) return false;
            var def = PickVehicle();
            var kind = PickKind(def);
            int lanes = LaneCount(s);
            int lane;
            if (kind == DriverKind.Camper && lanes >= 2) lane = lanes - 1;
            else if (kind == DriverKind.Truck) lane = rng.Next(0, Mathf.Min(2, lanes));
            else lane = Mathf.Min(lanes - 1, (int)(Mathf.Pow(Rand(0f, 1f), 1.25f) * lanes));
            // A lane that ends right here would make the driver's first move an emergency.
            while (lane > 0 && !float.IsInfinity(LaneEnd(s, lane, LaneEndLook + 300f))) lane--;
            if (!SpotIsClear(s, lane, def.lengthM)) return false;
            var c = Create(def, kind, s, lane);
            // Cars behind must be able to catch up, or they would sit out of sight forever.
            if (behindPlayer && c.Desired < anchorSpeed + 2f) { RemoveAt(Cars.IndexOf(c)); return false; }
            return true;
        }

        void Fill(float anchor)
        {
            for (int tries = 0; tries < Target * 12 && Cars.Count < Target; tries++)
            {
                float s = anchor + Rand(-Behind * .7f, Ahead * .92f);
                // Cars behind need room to see a parked player and stop.
                if (s > anchor + 70f || s < anchor - 260f) TrySpawn(s, 0f, false);
            }
        }

        void Manage(float anchor, float anchorSpeed)
        {
            for (int i = Cars.Count - 1; i >= 0; i--)
            {
                float d = Cars[i].S - anchor;
                if (d > Ahead || d < -Behind || Cars[i].S > path.Length - RouteMargin) RemoveAt(i);
            }
            BringBackRegulars(anchor);
            for (int n = 0; n < 3 && Cars.Count < Target; n++)
            {
                bool behind = rng.NextDouble() < (anchorSpeed < 24f ? .55f : .22f);
                float s = behind ? anchor - Rand(380f, Behind - 40f) : anchor + Rand(Ahead * .72f, Ahead - 30f);
                // Near the start of the road there is less room behind, but cars still have to come from somewhere.
                if (behind && s < RouteStart && anchor - RouteStart > 200f) s = RouteStart + Rand(0f, 30f);
                TrySpawn(s, anchorSpeed, behind);
            }
        }

        // --------------------------------------------------------------- reading the player

        float ReadPlayer(DriveSim car, out float anchorSpeed)
        {
            player = default;
            anchorSpeed = 0f;
            if (car == null) return lastAnchor;
            float c = Mathf.Cos(car.Theta), sn = Mathf.Sin(car.Theta);
            anchorSpeed = Mathf.Max(0f, car.Speed * c - car.SideSpeed * sn);
            float anchor = car.S;
            bool solid = true;
            float mainS = car.S, lat = car.Lateral;
            if (car.Current != null)
            {
                var j = car.CurrentJunction;
                float len = car.Current.Length;
                anchor = j.divergeS + car.S / len * (j.mergeS - j.divergeS);
                if (car.S < RoutePath.ExitOpen) mainS = j.divergeS + car.S;
                else if (car.S > len - RoutePath.ExitOpen) mainS = j.mergeS - (len - car.S);
                else solid = false;
                if (solid) lat = Vector3.Dot(car.Current.Point(car.S, car.Lateral) - path.Center(mainS), path.Right(mainS));
            }
            if (solid)
            {
                float ac = Mathf.Abs(c), as_ = Mathf.Abs(sn);
                player = new Body
                {
                    Present = true, S = mainS, Lateral = lat,
                    SpeedS = anchorSpeed, SpeedLat = car.Speed * sn + car.SideSpeed * c,
                    HalfLength = DriveSim.CarHalfLength * ac + DriveSim.CarHalfWidth * as_,
                    HalfWidth = DriveSim.CarHalfLength * as_ + DriveSim.CarHalfWidth * ac
                };
            }
            return anchor;
        }

        // --------------------------------------------------------------- one step

        /// <summary>Advances all traffic. Without a player car, the window follows anchorS instead, for automated tests.</summary>
        public void Step(float dt, DriveSim car, float anchorS = 0f, float anchorSpeedNoCar = 0f)
        {
            clock += dt; tick++;
            float anchorSpeed = anchorSpeedNoCar;
            float anchor = car != null ? ReadPlayer(car, out anchorSpeed) : anchorS;
            lastDt = dt;
            if (player.Present) { playerDecel = Mathf.Lerp(playerDecel, (playerPrevSpeed - anchorSpeed) / dt, .3f); playerPrevSpeed = anchorSpeed; }
            else playerDecel = 0f;
            StepRegulars(dt, anchor, anchorSpeed);
            WatchPlayer();
            if (float.IsNaN(lastAnchor) || Mathf.Abs(anchor - lastAnchor) > 200f) { Clear(); Fill(anchor); }
            lastAnchor = anchor;
            if (tick % 12 == 0) Manage(anchor, anchorSpeed);

            // Nearly sorted already, so insertion sort is close to free and allocates nothing.
            for (int i = 1; i < Cars.Count; i++)
            {
                var c = Cars[i]; int k = i - 1;
                while (k >= 0 && Cars[k].S > c.S) { Cars[k + 1] = Cars[k]; k--; }
                Cars[k + 1] = c;
            }

            for (int i = 0; i < Cars.Count; i++)
            {
                var c = Cars[i];
                c.PrevS = c.S; c.PrevLateral = c.Lateral; c.PrevYaw = c.Yaw;
                bool far = Mathf.Abs(c.S - anchor) > FarDistance;
                if (!far || (tick + c.Id) % FarThinkEvery == 0) Think(c, i, far ? dt * FarThinkEvery : dt, far);
            }
            foreach (var c in Cars) Move(c, dt);
            Collide();
        }

        // --------------------------------------------------------------- driving decisions

        bool Occupies(float otherLat, int otherLane, float otherWidth, float s, float lat, float width)
        {
            float reach = (otherWidth + width) / 2f + .35f;
            return Mathf.Abs(otherLat - lat) < reach || Mathf.Abs(LaneCenter(s, otherLane) - lat) < reach;
        }

        /// <summary>Finds the nearest vehicle ahead of and behind car i across the given strip of road.</summary>
        void Scan(int i, float lat, float width, out float leadGap, out float leadSpeed, out float backGap, out float backSpeed, out TrafficCar leader)
        {
            var me = Cars[i];
            leadGap = backGap = float.PositiveInfinity; leadSpeed = backSpeed = 0f; leader = null;
            scanLeadPlayer = scanBackPlayer = false;
            for (int j = i + 1; j < Cars.Count; j++)
            {
                var o = Cars[j];
                float gap = o.S - me.S - (o.Length + me.Length) / 2f;
                if (gap > 220f) break;
                if (Occupies(o.Lateral, o.Lane, o.Width, o.S, lat, width)) { leadGap = gap; leadSpeed = o.Speed; leader = o; break; }
            }
            for (int j = i - 1; j >= 0; j--)
            {
                var o = Cars[j];
                float gap = me.S - o.S - (o.Length + me.Length) / 2f;
                if (gap > 130f) break;
                if (Occupies(o.Lateral, o.Lane, o.Width, o.S, lat, width)) { backGap = gap; backSpeed = o.Speed; break; }
            }
            if (!player.Present || Mathf.Abs(player.Lateral - lat) >= player.HalfWidth + width / 2f + .35f) return;
            float d = player.S - me.S;
            float g = Mathf.Abs(d) - player.HalfLength - me.Length / 2f;
            if (d >= 0f && g < leadGap) { leadGap = g; leadSpeed = player.SpeedS; leader = null; scanLeadPlayer = true; }
            else if (d < 0f && g < backGap) { backGap = g; backSpeed = player.SpeedS; scanBackPlayer = true; }
        }

        static float Idm(float v, float v0, float a, float b, float headway, float minGap, float gap, float leadV)
        {
            float free = 1f - Mathf.Pow(Mathf.Clamp(v / Mathf.Max(1f, v0), 0f, 2.5f), 4f);
            if (float.IsInfinity(gap)) return a * free;
            float want = minGap + Mathf.Max(0f, v * headway + v * (v - leadV) / (2f * Mathf.Sqrt(a * b)));
            float ratio = want / Mathf.Max(.5f, gap);
            return a * (free - ratio * ratio);
        }

        static float MaxAccel(TrafficCar c) => c.Kind == DriverKind.Truck ? .55f + .3f * c.Traits.Aggression : 1.3f + 1.3f * c.Traits.Aggression;
        static float Headway(TrafficCar c)
        {
            if (c.Kind == DriverKind.Tailgater) return .42f + (1f - c.Traits.Aggression) * .4f;
            if (c.Kind == DriverKind.Truck) return 2.1f;
            return 1.75f - .85f * c.Traits.Aggression + (1f - c.Traits.Attention) * .35f;
        }
        static float MinGap(TrafficCar c) => c.Kind == DriverKind.Truck ? 5f : c.Kind == DriverKind.Tailgater ? 1.6f : 3f;
        float AccelFor(TrafficCar c, float gap, float leadV) => Idm(c.Speed, c.Desired, MaxAccel(c), 2.6f, Headway(c), MinGap(c), gap, leadV);

        void Think(TrafficCar c, int i, float dt, bool far)
        {
            if (c.Wrecked) { c.Accel = c.Speed > 0f ? -6.5f : 0f; c.Braking = c.Speed > .1f; c.Signal = 0; c.Hazards = true; return; }
            c.Cooldown -= dt;
            c.StartledLeft -= dt;

            float laneCenter = LaneCenter(c.S, c.Lane);
            Scan(i, laneCenter, c.Width, out float gap, out float leadV, out float backGap, out _, out var leader);
            bool leadPlayer = scanLeadPlayer, backPlayer = scanBackPlayer;
            if (Mathf.Abs(c.Lateral - laneCenter) > .4f)
            {
                Scan(i, c.Lateral, c.Width, out float g2, out float v2, out _, out _, out var l2);
                if (g2 < gap) { gap = g2; leadV = v2; leader = l2; leadPlayer = scanLeadPlayer; }
            }

            // Reactions lag by a moment, more for an inattentive driver.
            c.ReactionLeft -= dt;
            if (c.ReactionLeft <= 0f || c.SeenLeadSpeed < 0f) { c.SeenLeadSpeed = float.IsInfinity(gap) ? -1f : leadV; c.ReactionLeft = .1f + (1f - c.Traits.Attention) * .7f; }
            float seen = c.SeenLeadSpeed >= 0f ? c.SeenLeadSpeed : leadV;

            // A lane that ends behaves like a parked car at its end.
            float end = LaneEnd(c.S, c.Lane, LaneEndLook);
            float endGap = float.IsInfinity(end) ? float.PositiveInfinity : Mathf.Max(0f, end - c.S - 30f);

            // A driver with a grudge drives at the player, not just around them.
            c.SinceChange += dt; c.HonkCooldown -= dt; c.BrakeCheckLeft -= dt; c.RageTimer -= dt; c.RamCooldown -= dt; c.GrudgeCooldown -= dt;
            c.AngryFlash = false; c.TightFollow = false; c.RamAccel = float.NaN; c.WantLane = -1;
            bool near = !far && player.Present;
            float cap = float.PositiveInfinity;
            // The speed this driver wants follows the posted limit as it changes.
            if (!c.HoldDesired) c.Desired = c.Regular != null ? CruiseFor(c.Regular, c.S) : CruiseAt(c, c.S);
            if (near) cap = Personal(c, gap, leadPlayer, backGap, backPlayer, dt);

            float a = c.TightFollow && leadPlayer ? Idm(c.Speed, c.Desired, MaxAccel(c), 2.6f, .35f, 1.5f, gap, seen) : AccelFor(c, gap, seen);
            if (endGap < 160f) a = Mathf.Min(a, AccelFor(c, endGap, 0f));
            if (c.StartledLeft > 0f) a = Mathf.Min(a, -2.5f);
            a = Mathf.Min(a, cap);
            if (c.BrakeCheckLeft > 0f) a = Mathf.Min(a, -7f);
            bool ramming = !float.IsNaN(c.RamAccel);
            if (ramming) a = c.RamAccel;

            // However distracted, the car in front eventually gets through to them.
            float closing = c.Speed - leadV;
            if (!ramming && !float.IsInfinity(gap) && (gap < c.Speed * .22f + 1f || (closing > 1f && gap / closing < 1.7f))) a = Mathf.Min(a, -Mathf.Lerp(5f, 8.5f, Mathf.Clamp01(closing / 14f)));
            if (near) Judge(c, a, gap, leadPlayer, backGap, backPlayer, dt);

            c.Accel = Mathf.Clamp(a, -9f, MaxAccel(c));
            c.Braking = c.Accel < -1.1f;
            UpdateTailgate(c, gap, leadV, leader, dt);
            c.Hazards = false;
            if (!far || !float.IsInfinity(end)) UpdateLaneChange(c, i, a, dt, !float.IsInfinity(end));
        }

        void UpdateTailgate(TrafficCar c, float gap, float leadV, TrafficCar leader, float dt)
        {
            bool stuck = (c.Kind == DriverKind.Tailgater && !float.IsInfinity(gap) && gap < 26f && leadV < c.Desired - 1.5f && c.Speed > 12f) || c.AngryFlash;
            c.BehindTimer = stuck ? c.BehindTimer + dt : Mathf.Max(0f, c.BehindTimer - dt * 2f);
            c.FlashTimer -= dt;
            if (c.BehindTimer > 1.5f && c.FlashTimer < -1.6f) c.FlashTimer = .9f; // a burst of high beams every couple of seconds
            c.FlashingHighBeams = c.FlashTimer > 0f && Mathf.Repeat(clock * 5f, 1f) < .5f;
        }

        void UpdateLaneChange(TrafficCar c, int i, float currentAccel, float dt, bool laneEnding)
        {
            if (c.Changing) { if (c.ChangeTime / c.ChangeDuration > .75f) c.Signal = 0; return; }
            if (c.PendingLane >= 0)
            {
                c.SignalLeft -= dt;
                if (!Allowed(c, i, c.PendingLane, laneEnding, out _)) { CancelSignal(c); return; }
                if (c.SignalLeft <= 0f) StartChange(c, c.PendingLane);
                return;
            }
            c.Signal = 0;
            if (c.Cooldown > 0f) return;

            int lanes = LaneCount(c.S);
            int maxLane = (int)MaxLaneFor(c.Kind, lanes);
            int best = -1; float bestScore = 0f;
            // Lanes are numbered from the right, so a higher number is further left.
            for (int dir = -1; dir <= 1; dir += 2)
            {
                int to = c.Lane + dir;
                if (to < 0 || to > maxLane) continue;
                if (dir > 0 && !float.IsInfinity(LaneEnd(c.S, to, LaneEndLook))) continue;
                if (!Allowed(c, i, to, laneEnding, out float newAccel)) continue;
                float score = Score(c, dir, newAccel - currentAccel, laneEnding, lanes);
                if (score > bestScore) { bestScore = score; best = to; }
            }
            if (best < 0) { c.Cooldown = .4f; return; }
            BeginSignal(c, best, laneEnding);
        }

        float Score(TrafficCar c, int dir, float gain, bool laneEnding, int lanes)
        {
            if (laneEnding && dir < 0) return 10f + gain;
            if (c.WantLane >= 0) return c.WantLane != c.Lane && Math.Sign(c.WantLane - c.Lane) == dir ? 2.5f + gain * .2f : -1f;
            bool wantsPass = c.Speed < c.Desired - 1f;
            switch (c.Kind)
            {
                case DriverKind.Camper:
                    // Heads for the left lane and stays there, whatever is stacked up behind.
                    return dir > 0 ? 3f + gain : -1f;
                case DriverKind.Truck:
                    if (dir < 0) return c.Lane > 0 && gain > -.15f ? .4f + gain : -1f;
                    return wantsPass && gain > .45f && c.Speed > 18f ? gain - .3f : -1f;
                case DriverKind.Tailgater:
                    // Rides the bumper for a while before giving up and going around.
                    if (c.BehindTimer < 7f && gain > 0f && c.Speed > 15f) return -1f;
                    return wantsPass && gain > .1f ? gain + .3f : -1f;
                case DriverKind.Weaver:
                    return wantsPass && gain > .06f ? gain + .1f : -1f;
                default:
                    if (dir < 0) return gain > -.2f ? c.Traits.LaneDiscipline * .55f + gain : -1f;
                    // Most drivers pull out to pass once the car ahead is clearly slower than they want to go.
                    return wantsPass && gain > .22f ? gain - .05f : -1f;
            }
        }

        bool Allowed(TrafficCar c, int i, int lane, bool urgent, out float newAccel)
        {
            float lat = LaneCenter(c.S, lane);
            Scan(i, lat, c.Width, out float gap, out float leadV, out float backGap, out float backV, out _);
            newAccel = AccelFor(c, gap, leadV);
            float boldness = c.Traits.Aggression;
            float front = (urgent ? 4f : 7f) + c.Speed * Mathf.Lerp(.55f, .28f, boldness);
            // Closing in on the player is the whole point for someone with a grudge.
            if (scanLeadPlayer && c.WantLane == lane && c.Regular != null && c.Regular.Level >= 2) front = 4f + c.Speed * .12f;
            else if (leadV < c.Speed && gap / Mathf.Max(.1f, c.Speed - leadV) < 3.5f) return false;
            if (gap < front) return false;
            float rear = (urgent ? 5f : 8f) + backV * Mathf.Lerp(.5f, .25f, boldness) + Mathf.Max(0f, backV - c.Speed) * 2.4f;
            // Cutting in on the player on purpose leaves them a fraction of the room anyone else would get.
            if (c.CutIn && scanBackPlayer) rear = 3f + Mathf.Max(0f, backV - c.Speed) * .8f;
            return backGap >= rear;
        }

        void BeginSignal(TrafficCar c, int lane, bool urgent)
        {
            c.PendingLane = lane;
            c.Signal = lane > c.Lane ? -1 : 1;
            c.SignalLeft = c.Signals ? (urgent ? .7f : Mathf.Lerp(.5f, 1.8f, c.Traits.LaneDiscipline)) : 0f;
            if (!c.Signals) c.Signal = 0; // some people just don't
        }

        void CancelSignal(TrafficCar c) { c.PendingLane = -1; c.Signal = 0; c.Cooldown = Rand(.8f, 2.5f); }

        void StartChange(TrafficCar c, int lane)
        {
            c.ChangeFrom = c.Lateral - LaneCenter(c.S, lane);
            c.Lane = lane; c.PendingLane = -1; c.SinceChange = 0f;
            c.ChangeTime = 0f;
            c.ChangeDuration = c.Kind == DriverKind.Truck ? 5.5f : c.Kind == DriverKind.Weaver ? 1.9f : Mathf.Lerp(3.8f, 2.4f, c.Traits.Aggression);
            float wait = c.Regular != null && c.Regular.Level >= 2 ? 1.2f : c.Kind == DriverKind.Weaver ? 2.5f : c.Kind == DriverKind.Tailgater ? 5f : c.Kind == DriverKind.Camper ? 1.5f : Mathf.Lerp(11f, 5f, c.Traits.Aggression);
            c.Cooldown = c.ChangeDuration + Rand(wait * .6f, wait * 1.5f);
            // With another lane closing just beyond this one, there is no time to dawdle.
            if (!float.IsInfinity(LaneEnd(c.S, lane, LaneEndLook))) c.Cooldown = c.ChangeDuration + .4f;
            LaneChanges++;
            if (c.Signal != 0) LaneChangesSignaled++;
        }

        // --------------------------------------------------------------- motion

        void Move(TrafficCar c, float dt)
        {
            c.Speed = Mathf.Max(0f, c.Speed + c.Accel * dt);
            c.S += c.Speed * dt;
            if (c.Wrecked)
            {
                c.Lateral += c.OffsetVelocity * dt;
                c.OffsetVelocity = Mathf.MoveTowards(c.OffsetVelocity, 0f, 5f * dt);
                float edge = path.HalfWidth(c.S) + 2.4f;
                c.Lateral = Mathf.Clamp(c.Lateral, -path.HalfWidth(c.S) - .4f, edge);
                c.Yaw += c.YawRate * dt;
                c.YawRate = Mathf.MoveTowards(c.YawRate, 0f, 1.2f * dt);
                return;
            }
            float from = 0f;
            if (c.ChangeDuration > 0f)
            {
                c.ChangeTime += dt;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(c.ChangeTime / c.ChangeDuration));
                from = c.ChangeFrom * (1f - k);
                if (c.ChangeTime >= c.ChangeDuration) c.ChangeDuration = 0f;
            }
            if (c.ShoveLeft > 0f && Mathf.Abs(c.Offset) < 2.6f)
            {
                // Swerving into the player on purpose, not drifting back to the lane.
                c.ShoveLeft -= dt;
                c.OffsetVelocity = Mathf.MoveTowards(c.OffsetVelocity, c.ShoveDir * 4.5f, 25f * dt);
            }
            else { c.ShoveLeft = 0f; c.OffsetVelocity += (-c.Offset * 3f - c.OffsetVelocity * 3.2f) * dt; }
            c.Offset += c.OffsetVelocity * dt;
            float drift = Mathf.Sin(clock * .55f + c.Phase) * (1f - c.Traits.Attention) * .32f;
            float lat = LaneCenter(c.S, c.Lane) + from + drift + c.Offset;
            float side = (lat - c.Lateral) / Mathf.Max(.001f, dt);
            c.Lateral = lat;
            c.Yaw = Mathf.Atan2(side, Mathf.Max(4f, c.Speed));
        }

        // --------------------------------------------------------------- contact

        void Collide()
        {
            for (int i = 0; i < Cars.Count; i++)
            {
                var a = Cars[i];
                for (int j = i + 1; j < Cars.Count; j++)
                {
                    var b = Cars[j];
                    if (b.S - a.S >= (a.Length + b.Length) / 2f) break;
                    if (Mathf.Abs(a.Lateral - b.Lateral) >= (a.Width + b.Width) / 2f) continue;
                    if (!a.Wrecked && !b.Wrecked && !a.Involved && !b.Involved) UnprovokedCrashes++;
                    else if (!(a.Wrecked && b.Wrecked)) ChainCrashes++;
                    Pair(a, b);
                }
            }
        }

        static void Wreck(TrafficCar c, float yawRate)
        {
            if (!c.Wrecked) c.YawRate = yawRate;
            c.Wrecked = true; c.Involved = true; c.Signal = 0; c.PendingLane = -1; c.ChangeDuration = 0f;
        }

        void Pair(TrafficCar back, TrafficCar front)
        {
            float gap = (back.Length + front.Length) / 2f - (front.S - back.S);
            float closing = back.Speed - front.Speed;
            float total = back.Mass + front.Mass;
            back.S -= gap * front.Mass / total;
            front.S += gap * back.Mass / total;
            if (closing <= 0f) return;
            back.Speed -= 1.15f * closing * front.Mass / total;
            front.Speed += 1.15f * closing * back.Mass / total;
            back.Speed = Mathf.Max(0f, back.Speed);
            float side = front.Lateral >= back.Lateral ? 1f : -1f;
            Wreck(back, -side * Mathf.Min(1.8f, closing * .12f));
            Wreck(front, side * Mathf.Min(1.8f, closing * .12f));
        }

        /// <summary>Resolves contact between one traffic car and the player. Player position and speed come back through ApplyVehicleHit.</summary>
        public void HitPlayer(DriveSim car)
        {
            if (car == null || !player.Present) return;
            for (int k = 0; k < Cars.Count; k++)
            {
                var c = Cars[k];
                float ds = player.S - c.S, dl = player.Lateral - c.Lateral;
                float overS = player.HalfLength + c.Length / 2f - Mathf.Abs(ds);
                float overL = player.HalfWidth + c.Width / 2f - Mathf.Abs(dl);
                if (overS <= 0f || overL <= 0f) continue;

                float mp = car.Car.mass, total = mp + c.Mass;
                bool fresh = clock - c.LastPlayerHit > .45f;
                const float Bounce = 1.18f; // 0.18 of restitution: bodywork absorbs nearly all of it
                float dvS = 0f, dvL = 0f, sepS = 0f, sepL = 0f, impact = 0f, damage = 0f, crush = 0f, yaw = 0f;
                bool playerFault;
                if (overS < overL)
                {
                    float n = ds >= 0f ? 1f : -1f; // +1: the player is in front of this car
                    float closing = Mathf.Max(0f, (c.Speed - player.SpeedS) * n);
                    playerFault = n < 0f; // the player drove into the back of them
                    sepS = n * overS;
                    dvS = n * Bounce * closing * c.Mass / total;
                    impact = closing;
                    if (closing > .3f) c.Speed = Mathf.Max(0f, c.Speed - n * Bounce * closing * mp / total);
                    float change = Mathf.Abs(dvS);
                    damage = Mathf.Pow(change / 26f, 2f) * .75f;
                    // Only the nose crumples, and only when the player drove into something.
                    if (n < 0f) crush = Mathf.Min(.5f, change * .022f);
                    yaw = Mathf.Clamp(-dl * closing * .04f, -1.6f, 1.6f);
                    if (fresh && closing * mp / total > 3f) Wreck(c, Mathf.Clamp(dl * closing * .05f, -1.8f, 1.8f));
                    else if (fresh && closing > .8f) c.StartledLeft = 1.3f;
                }
                else
                {
                    float n = dl >= 0f ? 1f : -1f; // +1: the player is to the right of this car
                    float mine = -player.SpeedLat * n, theirs = (c.Lateral - c.PrevLateral) / Mathf.Max(.001f, lastDt) * n;
                    float closing = Mathf.Max(0f, mine + Mathf.Max(0f, theirs));
                    playerFault = mine >= theirs;
                    sepL = n * overL;
                    dvL = n * 1.08f * closing * c.Mass / total;
                    float push = 1.08f * closing * mp / total;
                    c.OffsetVelocity -= n * push;
                    // Trading paint scrapes speed off both cars.
                    float rub = Mathf.Abs(player.SpeedS - c.Speed);
                    dvS = -Mathf.Sign(player.SpeedS - c.Speed) * Mathf.Min(rub, rub * .25f + 1f) * c.Mass / total;
                    impact = Mathf.Max(closing, rub * .35f);
                    damage = .28f * Mathf.Pow(closing / 15f, 2f) + rub * .0016f;
                    yaw = -n * Mathf.Clamp(closing * .08f, 0f, 1.1f);
                    if (fresh && push > 3.2f) Wreck(c, n * Mathf.Min(1.5f, push * .25f));
                    else if (fresh && (closing > .6f || rub > 2f)) c.StartledLeft = 1.3f;
                }
                // Cars pressed together are one contact, not a new crash every frame.
                if (!fresh || impact < 1f) { damage = 0f; crush = 0f; yaw = 0f; }
                if (fresh && impact >= 1f)
                {
                    c.LastPlayerHit = clock; c.Involved = true; c.ShoveLeft = 0f;
                    CrashesWithPlayer++;
                    if (playerFault && clock - c.RamAt > 1.5f) Grudge(c, Grievance.Hit, .3f + impact * .025f + (c.Wrecked ? .3f : 0f));
                }
                car.ApplyVehicleHit(sepS, sepL, dvS, dvL, damage, crush, yaw, impact, -dl);
                player.S += sepS; player.Lateral += sepL;
            }
        }
    }
}
