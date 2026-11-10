using SurviveI95.Data;
using UnityEngine;

namespace SurviveI95.Sim
{
    public enum Season { Winter, Spring, Summer, Fall }

    /// <summary>
    /// Who's on the road depends on where and when: the season (chosen per run), city or countryside,
    /// and the license plate, which says where the driver is from and hints at how they drive.
    /// </summary>
    public partial class TrafficSim
    {
        /// <summary>Chosen at the start of a run. Winter is snowbird season in Florida.</summary>
        public Season Season = Season.Winter;

        /// <summary>The Miami test road is all city. Rural stretches come with the full Florida route (M12).</summary>
        static bool Urban(float s) => true;

        /// <summary>How the season and the city shift the vehicle mix.</summary>
        float VehicleFactor(VehicleDefinition d, float s)
        {
            float f = 1f;
            switch (Season)
            {
                case Season.Winter: if (d.id == "motorhome" || d.id == "fifth_wheel") f *= 1.6f; else if (d.id == "convertible") f *= 1.2f; break;
                case Season.Spring: if (d.id == "convertible") f *= 1.4f; else if (d.id == "coupe") f *= 1.1f; break;
                case Season.Summer: if (d.id == "suv_boat") f *= 1.6f; else if (d.id == "minivan") f *= 1.3f; else if (d.id == "convertible") f *= 1.1f; break;
            }
            if (Urban(s))
            {
                if (d.id == "taxi") f *= 2f;
                else if (d.id == "delivery_van") f *= 1.5f;
                else if (d.id == "box_truck") f *= 1.2f;
                else if (d.id == "motorhome" || d.id == "suv_boat") f *= .7f;
                else if (d.id == "fifth_wheel") f *= .5f;
                else if (d.id == "semi") f *= .8f;
            }
            return f;
        }

        /// <summary>How the season shifts who's driving: snowbirds come down for winter, families travel in summer, spring break brings the young.</summary>
        float ProfileFactor(string id)
        {
            switch (Season)
            {
                case Season.Winter: return id == "snowbird" ? 2.5f : id == "leisure" ? 1.3f : id == "rental" ? 1.3f : 1f;
                case Season.Spring: return id == "rental" ? 1.4f : id == "sporty" ? 1.2f : 1f;
                case Season.Summer: return id == "snowbird" ? .2f : id == "family" ? 1.4f : id == "leisure" ? 1.4f : id == "rental" ? 1.2f : 1f;
                default: return id == "snowbird" ? .8f : 1f;
            }
        }

        // Plates on I-95 in South Florida. Most are local; through traffic and tourists bring the rest of the East Coast;
        // in winter the snowbirds bring Quebec, Ontario and the Northeast.
        static readonly (string code, string name, float weight)[] PlateMix =
        {
            ("FL", "Florida", 70), ("GA", "Georgia", 4), ("NY", "New York", 5), ("NJ", "New Jersey", 3), ("NC", "North Carolina", 3),
            ("SC", "South Carolina", 2), ("PA", "Pennsylvania", 2), ("OH", "Ohio", 2), ("MA", "Massachusetts", 1), ("TX", "Texas", 2),
            ("QC", "Quebec", 2), ("ON", "Ontario", 3)
        };
        static readonly (string code, string name, float weight)[] SnowbirdPlates =
        {
            ("FL", "Florida", 15), ("QC", "Quebec", 25), ("ON", "Ontario", 25), ("NY", "New York", 15), ("NJ", "New Jersey", 5),
            ("PA", "Pennsylvania", 5), ("OH", "Ohio", 5), ("MA", "Massachusetts", 5)
        };
        static readonly (string code, string name, float weight)[] FreightPlates =
        {
            ("FL", "Florida", 35), ("GA", "Georgia", 10), ("TX", "Texas", 8), ("NC", "North Carolina", 7), ("SC", "South Carolina", 5),
            ("TN", "Tennessee", 8), ("IN", "Indiana", 8), ("IL", "Illinois", 7), ("OH", "Ohio", 7), ("PA", "Pennsylvania", 5)
        };

        void PickPlate(TrafficCar c)
        {
            var p = c.Profile;
            string id = p != null ? p.id : "everyday";
            // Rentals and work vehicles carry local plates; freight comes from everywhere; snowbirds from up north.
            if (id == "rental" || id == "work") { c.PlateState = "FL"; return; }
            var table = id == "snowbird" ? SnowbirdPlates : id == "freight" ? FreightPlates : PlateMix;
            float total = 0f;
            foreach (var t in table) total += Weight(t);
            float r = Rand(0f, total);
            foreach (var t in table) { r -= Weight(t); if (r <= 0f) { c.PlateState = t.code; return; } }
            c.PlateState = "FL";

            float Weight((string code, string name, float weight) t)
            {
                bool north = t.code == "QC" || t.code == "ON" || t.code == "NY" || t.code == "NJ" || t.code == "PA" || t.code == "OH" || t.code == "MA";
                return t.weight * (Season == Season.Winter && north ? (t.code == "QC" || t.code == "ON" ? 3f : 1.8f) : 1f);
            }
        }

        /// <summary>Where they're from nudges how they drive: New York and New Jersey are in a hurry, out-of-towners are unsure of the road.</summary>
        void ApplyPlate(TrafficCar c)
        {
            var t = c.Traits;
            float aggr = 0f, attn = 0f, habit = 0f;
            if (c.PlateState == "NY" || c.PlateState == "NJ" || c.PlateState == "MA") { aggr = .06f; habit = 2f; }
            else if (c.PlateState != "FL" && (c.Profile == null || (c.Profile.id != "freight" && c.Profile.id != "snowbird"))) attn = -.05f;
            c.Traits = new DriverTraits { Aggression = Mathf.Clamp01(t.Aggression + aggr), Attention = Mathf.Clamp01(t.Attention + attn), LaneDiscipline = t.LaneDiscipline };
            if (c.Kind != DriverKind.Truck && c.Kind != DriverKind.Camper) c.SpeedHabit += habit;
        }

        public static string PlateName(string code)
        {
            foreach (var t in PlateMix) if (t.code == code) return t.name;
            foreach (var t in FreightPlates) if (t.code == code) return t.name;
            return code;
        }

        /// <summary>" with New York plates" for out-of-state drivers, nothing for Florida. Helps you recognize who's back.</summary>
        public static string PlateNote(string code) => string.IsNullOrEmpty(code) || code == "FL" ? "" : $" with {PlateName(code)} plates";

        readonly System.Collections.Generic.SortedDictionary<string, int> plateLog = new System.Collections.Generic.SortedDictionary<string, int>();
        readonly System.Collections.Generic.SortedDictionary<string, int> profileCount = new System.Collections.Generic.SortedDictionary<string, int>();

        void LogSpawn(TrafficCar c)
        {
            plateLog.TryGetValue(c.PlateState, out int k); plateLog[c.PlateState] = k + 1;
            string id = c.Profile != null ? c.Profile.id : "none";
            profileCount.TryGetValue(id, out int q); profileCount[id] = q + 1;
        }

        /// <summary>Plate shares and driver kinds among every car spawned so far, in percent, for tests.</summary>
        public string PlateSpread()
        {
            int total = 0; foreach (var v in plateLog.Values) total += v;
            var sb = new System.Text.StringBuilder($"season={Season} cars={total} plates: ");
            foreach (var kv in plateLog) sb.Append($"{kv.Key}={100f * kv.Value / Mathf.Max(1, total):F0}% ");
            sb.Append("| drivers: ");
            foreach (var kv in profileCount) sb.Append($"{kv.Key}={100f * kv.Value / Mathf.Max(1, total):F0}% ");
            return sb.ToString();
        }
    }
}
