using System;
using System.Collections.Generic;
using UnityEngine;

namespace SurviveI95.Data
{
    [Serializable]
    public class VehicleDefinition
    {
        public string id;
        public string name;
        public string category;
        public float lengthM;
        public float widthM;
        public float[] cruiseMph;
        public string[] personalities;
        public bool articulated;
        public bool fuelHazard;
        /// <summary>Who tends to drive this vehicle, by weight. Profiles are in driver_profiles.json.</summary>
        public DriverShare[] drivers;
    }

    [Serializable]
    public class DriverShare
    {
        public string profile;
        public float weight;
    }

    /// <summary>How a kind of driver shifts the random traits of whoever is behind the wheel. Odds, never certainties.</summary>
    [Serializable]
    public class DriverProfile
    {
        public string id, name;
        public float habit, aggression, temper, discipline, attention, rager, racer, brakeTaps;
        /// <summary>Chances the driver is under 25, or 65 and over.</summary>
        public float youngShare, oldShare;
    }

    [Serializable]
    public class DriverProfileTable
    {
        public List<DriverProfile> profiles;
    }

    [Serializable]
    public class VehicleCatalog
    {
        public List<VehicleDefinition> vehicles;
    }

    [Serializable]
    public class TrafficWeight
    {
        public string id;
        public float weight;
    }

    [Serializable]
    public class ChapterTraffic
    {
        public string id;
        public string name;
        public string note;
        public List<TrafficWeight> mix;
    }

    [Serializable]
    public class StateTrafficTable
    {
        public List<ChapterTraffic> chapters;
    }

    public static class GameData
    {
        public static T Load<T>(string resource)
        {
            var asset = Resources.Load<TextAsset>(resource);
            if (asset == null) throw new InvalidOperationException("Missing data file: Resources/" + resource);
            return JsonUtility.FromJson<T>(asset.text);
        }

        public static VehicleCatalog LoadVehicles() => Load<VehicleCatalog>("Data/vehicles");
        public static StateTrafficTable LoadStateTraffic() => Load<StateTrafficTable>("Data/state_traffic");
        public static DriverProfileTable LoadDriverProfiles() => Load<DriverProfileTable>("Data/driver_profiles");
    }
}
