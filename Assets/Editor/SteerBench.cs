using System;
using System.Globalization;
using System.Text;
using SurviveI95.Sim;
using SurviveI95.World;
using UnityEngine;

namespace SurviveI95.EditorTools
{
    /// <summary>
    /// Measures keyboard steering without rendering: how fast a driver on A and D can dodge one lane over, and how
    /// gentle a tap is. Compares steering laws side by side, so a change in feel shows up as numbers before anyone drives it.
    /// </summary>
    public static class SteerBench
    {
        const float Dt = 0.02f, Mph = DriveSim.MetersPerSecondToMph, Lane = RoutePath.LaneWidth;

        // "old" is full lock on any key press (before M4 4.8c), "m4" is the 4.8c softening, "new" is DriveSim.KeySteer.
        static float Law(string law, DriveSim s, float held)
        {
            if (law == "old") return 1f;
            if (law == "m4") return Mathf.Max(s.SteerFor(s.GripAccel * Mathf.Lerp(.5f, .8f, Mathf.InverseLerp(.5f, 1.8f, held))), .12f);
            return s.KeySteer(held);
        }

        static DriveSim Start(RoutePath path, string carId, float mph, string law)
        {
            var s = new DriveSim(CarHandling.Load(carId));
            if (law != "new") s.Car.stability = 0f; // the earlier laws had no stability control
            s.Reset(path, 12000f); s.Lateral = 0f; s.Speed = mph / Mph; s.Gear = 'D';
            return s;
        }

        public static bool Verbose; static string LastDebug = "";
        struct Result { public float time, peakG, peakSlip, overshoot, settle; public bool ok; }

        // A dodge the way a person does it on keys: hold toward the gap, then hold the other way until the car points
        // straight again, then let go. switchAt is how far over the car is when the driver swaps keys.
        static Result Dodge(RoutePath path, string carId, string law, float mph, float switchAt)
        {
            var s = Start(path, carId, mph, law);
            var r = new Result { time = -1f };
            int phase = 0; float held = 0f, t = 0f, start = s.Lateral, maxTheta = 0f, doneAt = 99f;
            while (t < doneAt + 1f && t < 8f) // one more second after letting go, to see where the car lands and whether it's still turning
            {
                float moved = s.Lateral - start;
                if (phase == 0 && moved >= switchAt) { phase = 1; held = 0f; }
                else if (phase == 1 && s.Theta + s.YawRate * 0.15f <= 0f) { phase = 2; held = 0f; doneAt = t; } // lets go a moment early, as a driver who sees the car coming straight does
                int dir = phase == 0 ? 1 : phase == 1 ? -1 : 0;
                held += Dt;
                var input = new DriveInput { Steer = dir * Law(law, s, held), Throttle = s.Speed * Mph < mph ? 1f : 0f };
                s.Step(path, input, Dt); t += Dt;
                moved = s.Lateral - start;
                if (r.time < 0f && moved >= Lane * 0.8f) r.time = t; // far enough over to clear a car ahead
                r.peakG = Mathf.Max(r.peakG, Mathf.Abs(s.LateralAccel) / 9.81f);
                r.peakSlip = Mathf.Max(r.peakSlip, Mathf.Abs(s.Beta) * Mathf.Rad2Deg);
                r.overshoot = Mathf.Max(r.overshoot, moved - Lane);
                maxTheta = Mathf.Max(maxTheta, Mathf.Abs(s.Theta) * Mathf.Rad2Deg);
            }
            r.settle = s.Lateral - start;
            LastDebug = FormattableString.Invariant($"maxTheta{maxTheta:F0} endTheta{s.Theta * Mathf.Rad2Deg:F1} endYaw{s.YawRate:F2} phase{phase}");
            // A usable dodge ends in the next lane, pointing straight, without having spun or left the lane beyond it.
            r.ok = r.time > 0f && phase == 2 && maxTheta < 25f && Mathf.Abs(s.Theta) < 0.09f && Mathf.Abs(s.YawRate) < 0.08f && r.settle > Lane * 0.7f && r.settle < Lane * 1.5f;
            return r;
        }

        static Result BestDodge(RoutePath path, string carId, string law, float mph)
        {
            var best = new Result { time = -1f };
            for (float at = 0.05f; at < Lane; at += 0.05f)
            {
                var r = Dodge(path, carId, law, mph, at);
                if (r.ok && (best.time < 0f || r.time < best.time)) best = r;
            }
            return best;
        }

        // One press of the key and nothing else: how hard the car reacts and how far it has drifted 1.5 s later.
        static string Tap(RoutePath path, string carId, string law, float mph, float press)
        {
            var s = Start(path, carId, mph, law);
            float held = 0f, peakG = 0f, start = s.Lateral;
            for (float t = 0f; t < 1.5f; t += Dt)
            {
                held += Dt;
                float steer = t < press ? Law(law, s, held) : 0f;
                s.Step(path, new DriveInput { Steer = steer, Throttle = s.Speed * Mph < mph ? 1f : 0f }, Dt);
                peakG = Mathf.Max(peakG, Mathf.Abs(s.LateralAccel) / 9.81f);
            }
            return FormattableString.Invariant($"{peakG:F2}g/{s.Lateral - start:F1}m");
        }

        // A key held down for 2 s, as on a ramp or a hard evasive turn: the most cornering it reaches, and whether it spun.
        static string Hold(RoutePath path, string carId, string law, float mph)
        {
            var s = Start(path, carId, mph, law);
            float held = 0f, peakG = 0f, peakSlip = 0f, at05 = 0f;
            for (float t = 0f; t < 2f; t += Dt)
            {
                held += Dt;
                s.Step(path, new DriveInput { Steer = Law(law, s, held), Throttle = s.Speed * Mph < mph ? 1f : 0f }, Dt);
                peakG = Mathf.Max(peakG, Mathf.Abs(s.LateralAccel) / 9.81f);
                peakSlip = Mathf.Max(peakSlip, Mathf.Abs(s.Beta) * Mathf.Rad2Deg);
                if (Mathf.Abs(t - 0.5f) < Dt * .5f) at05 = Mathf.Abs(s.LateralAccel) / 9.81f;
            }
            return FormattableString.Invariant($"{at05:F2}g@0.5s peak{peakG:F2}g slip{peakSlip:F0}");
        }

        public static string Run(string carId, string file)
        {
            var path = RoutePath.Load(FloridaSliceBuilder.RouteId);
            var sb = new StringBuilder();
            sb.AppendLine(FormattableString.Invariant($"car={carId} keyTap={DriveSim.KeyTap:F2} keyBuild={DriveSim.KeyBuild:F2}"));
            foreach (var law in new[] { "old", "m4", "new" })
            {
                sb.AppendLine("== " + law);
                foreach (float mph in new[] { 40f, 60f, 80f, 100f, 115f })
                {
                    var d = BestDodge(path, carId, law, mph);
                    string dodge = d.time < 0f ? "dodge: none clean" : FormattableString.Invariant($"dodge {d.time:F2}s peak{d.peakG:F2}g slip{d.peakSlip:F0} over{Mathf.Max(0f, d.overshoot):F1}m");
                    sb.AppendLine(FormattableString.Invariant($"{mph,4:F0}mph  {dodge} | tap.15 {Tap(path, carId, law, mph, .15f)} tap.3 {Tap(path, carId, law, mph, .3f)} | hold {Hold(path, carId, law, mph)}"));
                }
            }
            if (Verbose)
                foreach (var law in new[] { "old", "new" })
                    for (float at = 0.2f; at < Lane; at += 0.4f)
                    {
                        var d = Dodge(path, carId, law, 60f, at);
                        sb.AppendLine(FormattableString.Invariant($"debug {law} 60mph switch{at:F1} time{d.time:F2} settle{d.settle:F1} over{d.overshoot:F1} g{d.peakG:F2} slip{d.peakSlip:F0} ok={d.ok} {LastDebug}"));
                    }
            System.IO.File.WriteAllText(file, sb.ToString());
            return sb.ToString();
        }
    }
}
