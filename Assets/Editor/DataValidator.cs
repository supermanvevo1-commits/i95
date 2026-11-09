using System.Collections.Generic;
using System.Linq;
using SurviveI95.Data;
using UnityEditor;
using UnityEngine;

namespace SurviveI95.EditorTools
{
    public static class DataValidator
    {
        [MenuItem("Survive I-95/Validate Game Data")]
        public static void Validate()
        {
            var errors = new List<string>();
            var catalog = GameData.LoadVehicles();
            var table = GameData.LoadStateTraffic();
            var ids = new HashSet<string>();
            var used = new HashSet<string>();

            foreach (var v in catalog.vehicles)
            {
                if (!ids.Add(v.id)) errors.Add("Duplicate vehicle id: " + v.id);
                if (v.lengthM <= 0 || v.widthM <= 0) errors.Add("Bad size: " + v.id);
                if (v.cruiseMph == null || v.cruiseMph.Length != 2 || v.cruiseMph[0] > v.cruiseMph[1]) errors.Add("Bad cruiseMph: " + v.id);
                if (v.personalities == null || v.personalities.Length == 0) errors.Add("No personalities: " + v.id);
            }
            foreach (var chapter in table.chapters)
            {
                foreach (var entry in chapter.mix)
                {
                    if (!ids.Contains(entry.id)) errors.Add(chapter.id + " uses unknown vehicle: " + entry.id);
                    if (entry.weight <= 0) errors.Add(chapter.id + " has non-positive weight: " + entry.id);
                    used.Add(entry.id);
                }
                if (chapter.mix.Select(e => e.id).Distinct().Count() != chapter.mix.Count) errors.Add(chapter.id + " repeats a vehicle");
            }
            foreach (var id in ids.Except(used)) errors.Add("Vehicle never spawns: " + id);

            if (errors.Count > 0)
            {
                foreach (var e in errors) Debug.LogError("[DataValidator] " + e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }
            Debug.Log("[DataValidator] OK: " + catalog.vehicles.Count + " vehicles, " + table.chapters.Count + " chapters");
        }
    }
}
