using SurviveI95.Data;
using UnityEngine;

namespace SurviveI95.Sim
{
    /// <summary>
    /// Getting out of the way, and what the player signals to other drivers: turn signals, horn and high beams.
    /// </summary>
    public partial class TrafficSim
    {
        /// <summary>The player's turn signal (-1 left, +1 right), horn and high beams, set by the game each step.</summary>
        public int PlayerSignal;
        public bool PlayerHorn, PlayerFlash;
        public int Yields, YieldRefusals;

        int signalDir;
        float signalOnSince = -99f, signalSeenAt = -99f;
        bool playerChangeSignaled;

        /// <summary>Remembers the player's signal for a moment after it cancels, so a lane change that just finished still counts as signaled.</summary>
        void TrackPlayerSignal()
        {
            if (PlayerSignal != 0)
            {
                if (PlayerSignal != signalDir) { signalDir = PlayerSignal; signalOnSince = clock; }
                signalSeenAt = clock;
            }
            else if (clock - signalSeenAt > 1.5f) signalDir = 0;
        }

        /// <summary>True when the player had the right blinker on for at least 0.8 s before moving from one lane to the other.</summary>
        bool SignaledFor(int fromLane, int toLane)
        {
            int wanted = toLane > fromLane ? -1 : 1; // lanes count from the right, so a higher lane is to the left
            return signalDir == wanted && signalSeenAt - signalOnSince >= .8f;
        }

        /// <summary>How well a driver sees what's behind them: a boat, a camper or a box body blocks the mirror.</summary>
        static float MirrorView(VehicleDefinition d) =>
            d.id == "suv_boat" || d.id == "fifth_wheel" || d.id == "motorhome" || d.id == "box_truck" || d.id == "delivery_van" ? .35f : d.category == "motorcycle" ? .7f : 1f;

        /// <summary>
        /// A driver who notices a faster car closing in, or one riding their bumper, moves over to let it by.
        /// How soon depends on attention and skill. The player's horn and high beams make them notice faster.
        /// A hot-tempered driver pushed by the player takes it as an insult instead.
        /// </summary>
        void Yielding(TrafficCar c, float backGap, float backV, bool backPlayer, float dt)
        {
            if (c.Kind == DriverKind.Truck || c.Lane == 0 || c.Wrecked || (c.Regular != null && c.Regular.Level >= 2)) { c.Noticed = 0f; return; }
            bool closing = backGap < 70f && backV > c.Speed + 2.2f;
            bool riding = backGap < Mathf.Max(7f, c.Speed * .5f) && backV > c.Speed - 1f;
            if (!closing && !riding) { c.Noticed = Mathf.Max(0f, c.Noticed - dt); return; }
            float rate = Mathf.Lerp(.25f, 1.1f, c.Traits.Attention * c.Skill) * MirrorView(c.Def);
            if (backPlayer) rate += (PlayerHorn ? 1.2f : 0f) + (PlayerFlash ? .8f : 0f);
            c.Noticed += rate * dt;
            if (c.Noticed < 2f) return;
            c.Noticed = 0f;
            if (backPlayer && c.Temper >= 3 && c.Traits.Aggression > .55f)
            {
                // "Go around me." The grudge brings the brake-check that follows.
                YieldRefusals++;
                Grudge(c, Grievance.Tailgated, .25f + (PlayerHorn || PlayerFlash ? .15f : 0f));
                return;
            }
            // Pushed from right behind (not just a faster car coming up), plenty of drivers won't be bullied out of their lane:
            // the more assertive, the likelier they hold it and answer with the brakes instead (Tailgated).
            if (backPlayer && riding && rng.NextDouble() < Mathf.Lerp(.15f, .8f, c.Traits.Aggression)) { YieldRefusals++; c.Noticed = -6f; return; }
            c.YieldFor = Rand(6f, 12f); Yields++;
            c.Cooldown = Mathf.Min(c.Cooldown, .3f);
        }

        public int TailTapsGiven, TailBrakeChecks, TailSlowdowns, BackPlayerSeen, RiddenSeen;
        public float MaxRidden;

        /// <summary>
        /// The player riding this driver's bumper (closer than about 0.6 s). Drivers notice in a second or a few, by how
        /// attentive they are and how well they see out the back, and answer as people do: a tap of the brakes to say "back
        /// off" (brake lights and a little slowing), then, if it goes on, the easygoing ones ease off to make the point or move
        /// over (Yielding), and the hotheads brake-check, hard. So tailgating stops being free: the car ahead can slow down on
        /// you at any moment, and at half a second behind there's no room to react.
        /// </summary>
        void Tailgated(TrafficCar c, float backGap, bool backPlayer, float dt)
        {
            bool ridden = backPlayer && !c.Wrecked && !c.PullingOver && c.Speed > 10f && backGap > 0f && backGap < Mathf.Max(5f, c.Speed * .6f);
            c.TailRidden = ridden ? c.TailRidden + dt : Mathf.Max(0f, c.TailRidden - dt * .5f);
            if (backPlayer) { BackPlayerSeen++; if (ridden) RiddenSeen++; }
            MaxRidden = Mathf.Max(MaxRidden, c.TailRidden);
            if (!ridden) { if (c.TailRidden <= 0f) { c.TailTaps = 0; c.TailNext = 0f; } return; }
            if (c.Kind == DriverKind.Truck) return; // a trucker watches you in the mirror and carries on
            c.TailNext -= dt;
            float notice = Mathf.Lerp(3.5f, 1.2f, c.Traits.Attention) / MirrorView(c.Def);
            if (c.TailRidden < notice || c.TailNext > 0f) return;
            bool hot = c.Traits.Aggression > .75f || c.Temper >= 3;
            float checkOdds = hot ? .55f : c.Traits.Aggression > .55f ? .22f : 0f; // a brake-check is a hothead's answer, now and then an assertive driver's
            if (c.TailTaps >= 1 && clock - c.LastPlayerHit > 6f && rng.NextDouble() < checkOdds)
            {
                c.BrakeCheckLeft = Rand(.5f, .9f); BrakeChecks++; TailBrakeChecks++;
                Notice?.Invoke($"The {c.Def.name.ToLower()} you're tailgating brake-checks you.");
            }
            else
            {
                c.TailTapLeft = Rand(.3f, .55f); TailTapsGiven++;
                // After a couple of taps an easygoing driver slows down to make the point (or moves over, if they can).
                if (c.TailTaps >= 2 && !hot && rng.NextDouble() < .5) { c.BlockSpeed = Mathf.Max(9f, c.Speed - MphToMs(Rand(5f, 10f))); c.BlockLeft = Rand(8f, 16f); TailSlowdowns++; }
            }
            c.TailTaps++;
            c.TailNext = Rand(3.5f, 7f);
        }
    }
}
