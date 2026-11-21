using System;
using System.Collections.Generic;
using SurviveI95.Sim;
using SurviveI95.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SurviveI95.Game
{
    /// <summary>
    /// A thin first trip (5b.3): a reason to go, a time you're expected, a partner who says a few lines when something
    /// happens, a destination exit, and an arrival card. A thin wallet: a budget, gas burned by the car's real tank and
    /// mileage, and what your speeding would have cost in Florida fines (police come in M8, so for now it's "if a trooper
    /// had clocked you"). The full economy is M9. Trips and the partner's lines are in Resources/Data/trips.json.
    /// </summary>
    public class Trip : MonoBehaviour
    {
        [Serializable] public class Lines { public string on; public string[] say; }
        [Serializable] public class Def
        {
            public string id, kind, goal, to, exit, place, where, partner, leave;
            public float dueMinutes, budget, gasPrice, startFuel;
            public Lines[] lines;
        }
        [Serializable] class File { public Def[] trips; }

        public Def Plan { get; private set; }
        PlayerCar car;
        RouteJunction dest; bool missed;
        float leaveMinutes, elapsed, startS;

        // ---- what the HUD shows
        public float Gallons { get; private set; }
        public float GallonsUsed { get; private set; }
        public float TankGal => car.Sim.Car.tankGal > 0f ? car.Sim.Car.tankGal : 15f;
        public float FuelFraction => Mathf.Clamp01(Gallons / TankGal);
        public float Money => Plan.budget - GallonsUsed * Plan.gasPrice;
        public bool Arrived { get; private set; }
        public float ArriveMinutes { get; private set; }
        public string Line { get; private set; } public float LineUntil { get; private set; }
        public float Miles { get; private set; }
        public float TopMph { get; private set; }
        public int Crashes { get; private set; }
        public float WorstOver { get; private set; } float worstMph, worstLimit, overFor;
        public bool Missed => missed;
        public float Elapsed => elapsed;
        public float LeaveMinutes => leaveMinutes;

        public string Clock(float minutesAfterLeaving) => ClockText(leaveMinutes + DayNight.StartShift + minutesAfterLeaving);
        public string Now => Clock(elapsed / 60f);
        public string Due => Clock(Plan.dueMinutes);
        public float MinutesLate => (Arrived ? ArriveMinutes : elapsed / 60f) - Plan.dueMinutes;

        static string ClockText(float m)
        {
            int t = Mathf.FloorToInt(m) % (24 * 60), h = t / 60, mm = t % 60;
            return $"{(h % 12 == 0 ? 12 : h % 12)}:{mm:00} {(h < 12 ? "AM" : "PM")}";
        }

        void Start()
        {
            car = GetComponent<PlayerCar>();
            var asset = Resources.Load<TextAsset>("Data/trips");
            if (asset == null) { enabled = false; return; }
            Plan = JsonUtility.FromJson<File>(asset.text).trips[0];
            var hm = Plan.leave.Split(':'); leaveMinutes = int.Parse(hm[0]) * 60 + int.Parse(hm[1]);
            Begin();
        }

        /// <summary>Starts the trip from the beginning: back at the start, tank as it was, the clock at leaving time.</summary>
        public void Begin()
        {
            startS = car.startS;
            if (Arrived || elapsed > 0f) car.Place(startS);
            Arrived = missed = false; elapsed = 0f; Miles = TopMph = WorstOver = 0f; Crashes = 0; overFor = 0f;
            Gallons = TankGal * Plan.startFuel; GallonsUsed = 0f; car.OutOfGas = false;
            said.Clear(); lastSaid = -99f; Line = null; lastImpacts = car.Sim.ImpactCount; lastS = car.Sim.S;
            dest = FindExit(Plan.exit, 0f);
            Aim();
        }

        // The ramp built at a numbered exit (or the first ramp after a point, once you've missed yours).
        RouteJunction FindExit(string number, float after)
        {
            RouteJunction best = null; float at = -1f;
            if (number != null && car.Path.File.exits != null)
                foreach (var e in car.Path.File.exits) if (e.@ref == number) { at = e.s; break; }
            foreach (var j in car.Path.Junctions)
            {
                if (number != null && at >= 0f) { if (Mathf.Abs(j.divergeS - at) < 400f && (best == null || Mathf.Abs(j.divergeS - at) < Mathf.Abs(best.divergeS - at))) best = j; }
                else if (j.divergeS > after && (best == null || j.divergeS < best.divergeS)) best = j;
            }
            return best;
        }

        void Aim()
        {
            if (car.Hud == null) return;
            car.Hud.DestinationName = Plan.place;
            car.Hud.DestinationS = dest != null ? dest.divergeS : car.Path.Length;
        }

        float lastS; int lastImpacts; bool countyDone, exitSoonDone, lowFuelDone, lateDone;

        void Update()
        {
            if (Plan == null) return;
            var k = Keyboard.current;
            if (Arrived)
            {
                if (k != null && (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame)) { countyDone = exitSoonDone = lowFuelDone = lateDone = false; Begin(); }
                return;
            }
            float dt = Time.deltaTime; var s = car.Sim;
            elapsed += dt;
            float v = Mathf.Abs(s.Speed), mph = v * DriveSim.MetersPerSecondToMph;
            float routeS = s.Current == null ? s.S : s.CurrentJunction.divergeS + s.S;
            float moved = Mathf.Abs(s.S - lastS); if (moved < 60f) Miles += moved / 1609.34f; lastS = s.S;
            TopMph = Mathf.Max(TopMph, mph);
            BurnFuel(dt, v);

            // The partner's lines, when something happens.
            if (elapsed > 6f) Say("start");
            float limit = car.LimitMph, over = limit > 0f ? mph - limit : 0f;
            overFor = over >= 6f ? overFor + dt : 0f;
            if (overFor > 3f && over > WorstOver) { WorstOver = over; worstMph = mph; worstLimit = limit; }
            if (over >= 25f && overFor > 3f) Say("wayover", 45f);
            else if (over >= 12f && overFor > 4f) Say("speeding", 60f);
            if (s.ImpactCount != lastImpacts)
            {
                lastImpacts = s.ImpactCount;
                if (s.LastImpact > 3f) { Crashes++; Say("crash", 20f, true); }
            }
            else if (s.LongAccel < -7f && v > 10f) Say("hardbrake", 40f);
            if (!countyDone && routeS > 25300f) { countyDone = true; Say("county", 0f, true); }
            if (dest != null && !exitSoonDone && s.Current == null && dest.divergeS - s.S < 3200f && dest.divergeS > s.S) { exitSoonDone = true; Say("exitsoon", 0f, true); }
            if (!lowFuelDone && FuelFraction < .125f) { lowFuelDone = true; Say("lowfuel", 0f, true); }
            if (!lateDone && elapsed / 60f > Plan.dueMinutes) { lateDone = true; Say("late", 0f, true); }

            // Missed the exit: the next one will do, a little late.
            if (dest != null && s.Current == null && s.S > dest.mergeS)
            {
                var next = FindExit(null, dest.mergeS);
                if (next != null) { missed = true; dest = next; Aim(); Say("missed", 0f, true); }
            }
            // Arrived: well along the destination's ramp.
            if (dest != null && s.Current != null && s.CurrentJunction == dest && s.S > 300f)
            {
                Arrived = true; ArriveMinutes = elapsed / 60f;
                float late = MinutesLate;
                Say(late < -2f ? "early" : late <= 2f ? "ontime" : "lateArrive", 0f, true);
                LineUntil = float.MaxValue;
            }
        }

        // Fuel from the power the engine makes: what it takes to push the car along (air and rolling drag) and to speed it
        // up, at an efficiency set so a steady 55 mph gives the EPA highway figure. Coasting cuts fuel, as modern engines do;
        // stopped, it idles. So speed and hard launches cost gas, just as on a real trip.
        void BurnFuel(float dt, float v)
        {
            var c = car.Sim.Car;
            float hwy = c.mpgHwy > 0f ? c.mpgHwy : 30f, idle = c.engine == "v8" ? .55f : .28f; // gallons an hour
            float v55 = 55f / DriveSim.MetersPerSecondToMph, drag55 = .35f + .00042f * v55 * v55;
            float k = (55f / hwy - idle) / (c.mass * drag55 * v55);
            float drag = .35f + .00042f * v * v;
            float throttle = car.LastInput.Throttle;
            float power = throttle > .02f ? c.mass * Mathf.Max(0f, Mathf.Max(car.Sim.LongAccel, 0f) + drag) * v : 0f;
            float gph = (v < 4f || throttle > .02f ? idle : 0f) + k * power;
            float used = Mathf.Min(Gallons, gph * dt / 3600f);
            Gallons -= used; GallonsUsed += used;
            if (Gallons <= 0f && !car.OutOfGas) { car.OutOfGas = true; car.Flash("Out of gas", 4f); }
        }

        readonly Dictionary<string, float> said = new Dictionary<string, float>();
        float lastSaid;

        /// <summary>The partner says one of the lines for a moment. Each moment once (or again after its cooldown); never two lines on top of each other unless it matters.</summary>
        void Say(string on, float cooldown = -1f, bool urgent = false)
        {
            float now = Time.time;
            if (said.TryGetValue(on, out var at) && (cooldown < 0f || now - at < cooldown)) return;
            if (!urgent && now - lastSaid < 8f) return;
            Lines pick = null; foreach (var l in Plan.lines) if (l.on == on) pick = l;
            if (pick == null || pick.say.Length == 0) return;
            said[on] = now; lastSaid = now;
            Line = pick.say[UnityEngine.Random.Range(0, pick.say.Length)].Replace("{due}", Due);
            LineUntil = now + 4.5f;
        }

        /// <summary>What Florida would charge for the worst speeding you held for 3 seconds: the state's base fine by how far
        /// over (s. 318.18) plus typical Broward County court costs; 50 over or 100 mph is a crime since July 2025 (court date,
        /// up to $500 and 30 days in jail the first time).</summary>
        public string Ticket(out float dollars)
        {
            dollars = 0f;
            float o = WorstOver;
            if (o < 6f) return "No ticket-worthy speeding";
            string what = $"{worstMph:0} in a {worstLimit:0}";
            if (o >= 50f || worstMph >= 100f) { dollars = 500f; return $"{what}: a court date, up to $500 and 30 days in jail"; }
            dollars = o >= 30f ? 354f : o >= 20f ? 279f : o >= 15f ? 254f : o >= 10f ? 204f : 129f;
            return $"{what}: ${dollars:0} fine";
        }

        public string Status() => Plan == null ? "no trip" :
            FormattableString.Invariant($"trip={Plan.id} now={Now} due={Due} elapsedMin={elapsed / 60f:F1} miles={Miles:F1} fuel={Gallons:F2}/{TankGal:F1}gal used={GallonsUsed:F3} money={Money:F2} top={TopMph:F0} crashes={Crashes} worstOver={WorstOver:F0} arrived={Arrived} missed={missed} dest={(dest != null ? dest.divergeS : -1f):F0} line=\"{Line}\"");
    }
}
