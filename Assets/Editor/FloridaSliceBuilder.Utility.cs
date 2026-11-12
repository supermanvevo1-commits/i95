using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SurviveI95.World;
using UnityEditor;
using UnityEngine;

namespace SurviveI95.EditorTools
{
    /// <summary>
    /// Water, power and rail from the land-use data. Ponds, lakes and canals sit just above the grass with a darker bank;
    /// South Florida is laced with them (canals drain the whole county). Power: wooden distribution poles with a
    /// crossarm and three sagging wires along the avenues, as nearly every Miami street has, and tall poles on the
    /// transmission lines the map knows. Rail: ballast and two rails on every mapped line, saved for the trains.
    /// </summary>
    public static partial class FloridaSliceBuilder
    {
        // ---------- ear clipping, for ponds and lakes that aren't convex ----------
        static List<int> EarClip(List<Vector2> pts)
        {
            var idx = Enumerable.Range(0, pts.Count).ToList(); var tris = new List<int>();
            if (PolyArea(pts) < 0) idx.Reverse();
            int guard = 0;
            while (idx.Count > 3 && guard++ < 10000)
            {
                bool cut = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int a = idx[(i + idx.Count - 1) % idx.Count], b = idx[i], c = idx[(i + 1) % idx.Count];
                    var A = pts[a]; var B = pts[b]; var C = pts[c];
                    if ((B.x - A.x) * (C.y - A.y) - (B.y - A.y) * (C.x - A.x) <= 0f) continue; // reflex
                    bool inside = false;
                    foreach (var j in idx)
                    {
                        if (j == a || j == b || j == c) continue;
                        var P = pts[j];
                        float d1 = (B.x - A.x) * (P.y - A.y) - (B.y - A.y) * (P.x - A.x), d2 = (C.x - B.x) * (P.y - B.y) - (C.y - B.y) * (P.x - B.x), d3 = (A.x - C.x) * (P.y - C.y) - (A.y - C.y) * (P.x - C.x);
                        if (d1 > 0 && d2 > 0 && d3 > 0) { inside = true; break; }
                    }
                    if (inside) continue;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    idx.RemoveAt(i); cut = true; break;
                }
                if (!cut) break;
            }
            if (idx.Count == 3) { tris.Add(idx[0]); tris.Add(idx[2]); tris.Add(idx[1]); }
            return tris;
        }

        static void FlatPoly(Bucket bk, int slot, List<Vector2> pts, float y, float uvScale)
        {
            var v = bk.v[slot]; int n = v.Count;
            foreach (var q in pts) { v.Add(new Vector3(q.x, y, q.y)); bk.uv[slot].Add(q / uvScale); }
            foreach (var i in EarClip(pts)) bk.t[slot].Add(n + i);
        }

        static List<Vector2> Inset(List<Vector2> pts, float d)
        {
            var mid = new Vector2(pts.Average(q => q.x), pts.Average(q => q.y));
            float rad = pts.Average(q => (q - mid).magnitude);
            return pts.Select(q => mid + (q - mid) * Mathf.Max(.2f, 1f - d / Mathf.Max(1f, rad))).ToList();
        }

        static bool InWater(Vector2 q)
        {
            if (!zoneGrid.TryGetValue(((int)Mathf.Floor(q.x / ZoneCell), (int)Mathf.Floor(q.y / ZoneCell)), out var l)) return false;
            foreach (var z in l) if (z.k == "water" && q.x >= z.min.x && q.x <= z.max.x && q.y >= z.min.y && q.y <= z.max.y && Inside(z.pts, q)) return true;
            return false;
        }

        static void BuildWater(RoutePath p)
        {
            var old = GameObject.Find("Water"); if (old != null) UnityEngine.Object.DestroyImmediate(old);
            if (land == null) return;
            var water = Lit("Water", new Color(.1f, .22f, .22f), .93f);
            var bank = Lit("WaterBank", new Color(.34f, .38f, .22f), .05f, "grass_ground");
            var map = new Dictionary<(int, int), Bucket>(); int ponds = 0, canals = 0;
            foreach (var z in zones)
            {
                if (z.k != "water" || z.pts.Count < 3) continue;
                if (z.pts.Any(q => OnRoad(q, 4f))) continue;
                var bk = Get(map, z.pts[0], 900f, 2);
                FlatPoly(bk, 1, z.pts, -.215f, 6f);
                FlatPoly(bk, 0, Inset(z.pts, 2.5f), -.2f, 30f);
                ponds++;
            }
            foreach (var w in land.ways)
            {
                if (w.k != "canal" && w.k != "ditch") continue;
                var pts = Pts(w.p); float width = Mathf.Max(3f, w.w);
                for (int i = 0; i + 1 < pts.Count; i++)
                {
                    var a = pts[i]; var b = pts[i + 1]; var mid = (a + b) / 2f;
                    if (OnRoad(mid, 2f)) continue; // a culvert carries it under the highway
                    var dir = (b - a).normalized; var n = new Vector2(-dir.y, dir.x);
                    var bk = Get(map, mid, 900f, 2);
                    Strip(bk, 1, a - dir * .5f, b + dir * .5f, n * (width / 2f + 2.5f), -.215f, width + 5f);
                    Strip(bk, 0, a - dir * .5f, b + dir * .5f, n * (width / 2f), -.2f, width);
                }
                canals++;
            }
            Flush(map, "Water", new[] { water, bank }, 2, false);
            Debug.Log($"[FloridaSlice] water: ponds and lakes {ponds}, canals and ditches {canals}");
        }

        // ---------- power ----------
        static void Wire(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, float sag)
        {
            const int seg = 8; var side = Vector3.Cross(b - a, Vector3.up).normalized * .02f;
            int n = v.Count;
            for (int i = 0; i <= seg; i++)
            {
                float k = i / (float)seg; var c = Vector3.Lerp(a, b, k) + Vector3.down * sag * 4f * k * (1f - k);
                v.Add(c - side + Vector3.down * .02f); v.Add(c + side + Vector3.up * .02f);
                if (i > 0) { int q = n + (i - 1) * 2; t.AddRange(new[] { q, q + 1, q + 2, q + 2, q + 1, q + 3 }); } // one face only: front and back on shared points leave no normal, and that blows up the glow
            }
        }

        static void BuildPower(RoutePath p, Func<float, float> south)
        {
            var old = GameObject.Find("Power"); if (old != null) UnityEngine.Object.DestroyImmediate(old);
            var wood = Lit("UtilityPole", new Color(.33f, .27f, .21f), .08f);
            var steel = Lit("TransmissionPole", new Color(.62f, .63f, .63f), .35f);
            var wireMat = Lit("PowerWire", new Color(.09f, .09f, .09f), .3f); wireMat.SetFloat("_Cull", 0f);
            var pv = new List<Vector3>(); var pt = new List<int>(); var sv = new List<Vector3>(); var st = new List<int>(); var wv = new List<Vector3>(); var wt = new List<int>();
            var dummyUv = new List<Vector2>();
            var parent = new GameObject("Power");
            int poles = 0, towers = 0;
            void Flush3()
            {
                if (pv.Count == 0 && sv.Count == 0 && wv.Count == 0) return;
                var g = new GameObject("PowerBlock"); g.transform.SetParent(parent.transform);
                Add(g, "Poles", Finish(pv, pv.Select(_ => Vector2.zero).ToList(), pt), wood, true);
                Add(g, "Towers", Finish(sv, sv.Select(_ => Vector2.zero).ToList(), st), steel, true);
                Add(g, "Wires", Finish(wv, wv.Select(_ => Vector2.zero).ToList(), wt), wireMat, false);
                pv.Clear(); pt.Clear(); sv.Clear(); st.Clear(); wv.Clear(); wt.Clear();
            }
            // Distribution: along every avenue near the road, one side, a pole every 45 m.
            var done = new HashSet<(int, int)>();
            foreach (var (a, b, w) in streetGrid.Values.SelectMany(x => x).Distinct())
            {
                if (w < 10f) continue;
                float len = (b - a).magnitude; if (len < 20f) continue;
                var dir = (b - a) / len; var n = new Vector2(-dir.y, dir.x) * (w / 2f + 1.6f);
                Vector3? last = null;
                for (float d = 0f; d <= len; d += 45f)
                {
                    var q = a + dir * d + n;
                    var key = ((int)(q.x / 20f), (int)(q.y / 20f));
                    if (done.Contains(key) || InBuilding(q, .5f) || OnRoad(q, 6f) || NearExit(q, 6f) || InWater(q)) { last = null; continue; }
                    done.Add(key);
                    var rot = Quaternion.LookRotation(new Vector3(dir.x, 0, dir.y));
                    var foot = new Vector3(q.x, -.25f, q.y);
                    Cuboid(pv, dummyUv, pt, foot + Vector3.up * 5.5f, rot, new Vector3(.28f, 11.5f, .28f));
                    Cuboid(pv, dummyUv, pt, foot + Vector3.up * 10.6f, rot, new Vector3(2.4f, .12f, .12f));
                    // A transformer can on about one pole in four.
                    if (Hash(key.Item1, key.Item2, 61) < .25f) Cuboid(sv, dummyUv, st, foot + Vector3.up * 8.6f + rot * Vector3.left * .35f, rot, new Vector3(.5f, .9f, .5f));
                    var right = rot * Vector3.right;
                    var tops = new[] { foot + Vector3.up * 10.75f - right * 1.1f, foot + Vector3.up * 10.75f + right * 1.1f, foot + Vector3.up * 11.3f };
                    if (last.HasValue)
                    {
                        var prevFoot = last.Value;
                        var prevTops = new[] { prevFoot + Vector3.up * 10.75f - right * 1.1f, prevFoot + Vector3.up * 10.75f + right * 1.1f, prevFoot + Vector3.up * 11.3f };
                        for (int k = 0; k < 3; k++) Wire(wv, wt, prevTops[k], tops[k], .55f);
                    }
                    last = foot; poles++;
                    if (pv.Count > 50000) Flush3();
                }
            }
            // Transmission lines the map knows: tall steel poles about every 180 m.
            if (land != null)
                foreach (var w in land.ways.Where(x => x.k == "power"))
                {
                    var pts = Pts(w.p); Vector3? last = null; float carry = 0f;
                    for (int i = 0; i + 1 < pts.Count; i++)
                    {
                        var a = pts[i]; var b = pts[i + 1]; float len = (b - a).magnitude; var dir = (b - a) / Mathf.Max(.01f, len);
                        for (float d = carry; d < len; d += 180f)
                        {
                            var q = a + dir * d;
                            if (OnRoad(q, 10f) || InBuilding(q, 1f)) { last = null; continue; }
                            var rot = Quaternion.LookRotation(new Vector3(dir.x, 0, dir.y)); var foot = new Vector3(q.x, -.25f, q.y);
                            Cone(sv, st, foot, .7f, .35f, 30f, 8);
                            Cuboid(sv, dummyUv, st, foot + Vector3.up * 27f, rot, new Vector3(7f, .3f, .3f));
                            Cuboid(sv, dummyUv, st, foot + Vector3.up * 22f, rot, new Vector3(7f, .3f, .3f));
                            var right = rot * Vector3.right;
                            if (last.HasValue) foreach (var (dx, dy) in new[] { (-3.3f, 27f), (3.3f, 27f), (-3.3f, 22f), (3.3f, 22f) }) Wire(wv, wt, last.Value + right * dx + Vector3.up * dy, foot + right * dx + Vector3.up * dy, 4f);
                            last = foot; towers++;
                        }
                        carry = Mathf.Repeat(carry - len, 180f);
                    }
                }
            Flush3();
            Debug.Log($"[FloridaSlice] power: distribution poles {poles}, transmission poles {towers}");
        }

        // ---------- rail ----------
        [Serializable] public class RailLine { public float[] p; }
        [Serializable] public class RailFile { public RailLine[] lines; }

        static void BuildRail(RoutePath p)
        {
            var old = GameObject.Find("Rail"); if (old != null) UnityEngine.Object.DestroyImmediate(old);
            if (land == null) return;
            var ballast = Lit("Ballast", new Color(.55f, .53f, .5f), .05f, "gravel_road");
            var steel = Lit("RailSteel", new Color(.45f, .4f, .36f), .5f); steel.SetFloat("_Metallic", .6f);
            var map = new Dictionary<(int, int), Bucket>(); var lines = new List<RailLine>();
            foreach (var w in land.ways.Where(x => x.k == "rail"))
            {
                var pts = Pts(w.p); if (pts.Count < 2) continue;
                lines.Add(new RailLine { p = w.p });
                for (int i = 0; i + 1 < pts.Count; i++)
                {
                    var a = pts[i]; var b = pts[i + 1]; var mid = (a + b) / 2f;
                    if (OnRoad(mid, 1f)) continue;
                    var dir = (b - a).normalized; var n = new Vector2(-dir.y, dir.x);
                    var bk = Get(map, mid, 900f, 2);
                    Strip(bk, 0, a - dir * .3f, b + dir * .3f, n * 2.4f, -.19f, 4.8f);
                    foreach (float o in new[] { -.72f, .72f })
                    {
                        var aa = a + n * o; var bb = b + n * o; var len = (bb - aa).magnitude;
                        var rot = Quaternion.LookRotation(new Vector3(dir.x, 0, dir.y));
                        Cuboid(bk.v[1], bk.uv[1], bk.t[1], new Vector3((aa.x + bb.x) / 2f, -.08f, (aa.y + bb.y) / 2f), rot, new Vector3(.08f, .15f, len + .05f));
                    }
                }
            }
            Flush(map, "Rail", new[] { ballast, steel }, 2, true);
            // The map stores track as many short pieces; join them end to end so a train can run the whole line.
            var pieces = lines.Select(l => Pts(l.p)).ToList(); var chains = new List<List<Vector2>>();
            while (pieces.Count > 0)
            {
                var chain = pieces[0]; pieces.RemoveAt(0);
                for (bool grew = true; grew;)
                {
                    grew = false;
                    for (int i = 0; i < pieces.Count; i++)
                    {
                        var o = pieces[i];
                        if ((o[0] - chain[chain.Count - 1]).sqrMagnitude < 4f) { chain.AddRange(o.Skip(1)); }
                        else if ((o[o.Count - 1] - chain[chain.Count - 1]).sqrMagnitude < 4f) { var r = new List<Vector2>(o); r.Reverse(); chain.AddRange(r.Skip(1)); }
                        else if ((o[o.Count - 1] - chain[0]).sqrMagnitude < 4f) { chain.InsertRange(0, o.Take(o.Count - 1)); }
                        else if ((o[0] - chain[0]).sqrMagnitude < 4f) { var r = new List<Vector2>(o); r.Reverse(); chain.InsertRange(0, r.Take(r.Count - 1)); }
                        else continue;
                        pieces.RemoveAt(i); grew = true; break;
                    }
                }
                chains.Add(chain);
            }
            float Length(List<Vector2> c) { float s = 0; for (int i = 1; i < c.Count; i++) s += (c[i] - c[i - 1]).magnitude; return s; }
            lines = chains.Where(c => Length(c) > 1500f).OrderByDescending(Length).Select(c => new RailLine { p = c.SelectMany(q => new[] { q.x, q.y }).ToArray() }).ToList();
            Debug.Log("[FloridaSlice] rail chains over 1.5 km: " + string.Join(", ", chains.Where(c => Length(c) > 1500f).Select(c => (Length(c) / 1000f).ToString("F1") + " km")));
            string file = $"Assets/Resources/Data/Routes/{RouteId}_rail.json";
            File.WriteAllText(file, JsonUtility.ToJson(new RailFile { lines = lines.ToArray() }));
            AssetDatabase.ImportAsset(file);
            Debug.Log($"[FloridaSlice] rail lines {lines.Count}");
        }
    }
}
