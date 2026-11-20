using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using SurviveI95.World;

namespace SurviveI95.EditorTools
{
    /// <summary>
    /// Where I-95 climbs to a viaduct it rides on earth fill first, the way Florida builds its approaches: a grassy bank
    /// sloping down at about 1 in 2.5 from the edge of each shoulder to the ground. Where an exit ramp runs close
    /// alongside there's no room for a slope, so a retaining wall of square precast panels stands there instead. The
    /// fill stops at a concrete abutment where the road is about 6 m up, and the viaduct carries on from there on pillars.
    /// </summary>
    public static partial class FloridaSliceBuilder
    {
        public const float FillTop = 6f, FillSlope = 2.5f;
        const float FillGround = -.24f, FillMin = .2f;

        /// <summary>How far out from the edge of the shoulder the bank reaches at this point (0 where there's no fill).</summary>
        public static float FillRun(RoutePath p, float s)
        {
            float y = p.Center(s).y;
            return y > FillMin && y < FillTop + .5f ? FillSlope * (y - FillGround) : 0f;
        }

        static bool RampAlongside(RoutePath p, float s)
        {
            foreach (var j in p.Junctions) if (s > j.divergeS - 40f && s < j.mergeS + 40f) return true;
            return false;
        }

        static void BuildEmbankments(GameObject root, RoutePath p, Func<float, float> south, Mats m)
        {
            var old = root.transform.Find("Embankments"); if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var g = new GameObject("Embankments"); g.transform.SetParent(root.transform);
            var panels = Textured("RetainingWall", MakeConcrete("RetainingWallTex", new Color(.74f, .72f, .67f), 3), .12f);
            int runs = 0; float bankM = 0f, wallM = 0f;
            const float step = 5f;
            Func<float, float> northEdge = s => p.HalfWidth(s) + ShoulderWidth(p, s, 3.05f) + AuxLane(p, s);
            Func<float, float> southEdge = s => south(s) - p.HalfWidth(s) - 1.3f;
            float s0 = -1f;
            for (float s = 0f; s <= p.Length; s += step)
            {
                bool fill = s < p.Length - step && p.Center(s).y > FillMin && p.Center(s).y < FillTop && !p.IsBridge(s);
                if (fill && s0 < 0f) s0 = s;
                if (fill || s0 < 0f) continue;
                float a = s0, b = s; s0 = -1f;
                if (b - a < 15f) continue;
                runs++;
                var bankN = new Sheet(); var bankS = new Sheet(); var wall = new Sheet(); var top = new Sheet();
                for (float q = a; q <= b + .01f; q += step)
                {
                    float y = p.Center(q).y, run = FillSlope * (y - FillGround);
                    // North side: the bank, or the panel wall where a ramp needs the room.
                    float ne = northEdge(q);
                    if (RampAlongside(p, q)) { wall.Row(p.Point(q, ne + .05f), FillGround - .05f, y); bankN.Break(); wallM += step; }
                    else { bankN.RowSlope(p, q, ne, ne + run, y - .02f); wall.Break(); bankM += step; }
                    float se = southEdge(q);
                    bankS.RowSlope(p, q, se, se - run, y - .02f); bankM += step;
                    // The narrow gap between the two roadways is filled up to just under the pavement.
                    top.RowSlope(p, q, south(q) + p.HalfWidth(q), -p.HalfWidth(q), y - .1f, y - .1f);
                }
                Add(g, "BankNorth", bankN.Mesh(), m.grass, true);
                Add(g, "BankSouth", bankS.Mesh(), m.grass, true);
                if (wall.v.Count > 0) Add(g, "Wall", wall.Mesh(), panels, true);
                Add(g, "Median", top.Mesh(), m.grass);
                // Abutments: a concrete face across the end of the fill where the viaduct takes over (the deck's own
                // skirt covers the drop where the fill ends lower than that).
                foreach (float e in new[] { a, b })
                {
                    float y = p.Center(e).y; if (y < 1.4f) continue;
                    float run = FillSlope * (y - FillGround);
                    float nOut = RampAlongside(p, e) ? northEdge(e) + .05f : northEdge(e) + run;
                    Add(g, "Abutment", Abutment(p, e, southEdge(e) - run, southEdge(e), northEdge(e), nOut, y), m.concrete, true);
                }
            }
            Debug.Log($"[FloridaSlice] embankments: {runs} approaches, {bankM / 1000f:F1} km of bank, {wallM:F0} m of retaining wall");
        }

        /// <summary>The end face of the fill, shaped like its cross-section: flat across the top, sloping to the toes.</summary>
        static Mesh Abutment(RoutePath p, float s, float toeL, float edgeL, float edgeR, float toeR, float y)
        {
            var pts = new[] { (toeL, FillGround), (edgeL, y), (edgeR, y), (toeR, FillGround) };
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            var fwd = p.Tangent(s);
            foreach (var side in new[] { -1f, 1f })
            {
                int n = v.Count;
                foreach (var (lat, h) in pts) { var c = p.Point(s, lat) + fwd * side * .3f; c.y = h; v.Add(c); uv.Add(new Vector2(lat / 4f, h / 4f)); }
                foreach (var (lat, _) in pts) { var c = p.Point(s, lat) + fwd * side * .3f; c.y = FillGround - .05f; v.Add(c); uv.Add(new Vector2(lat / 4f, 0f)); }
                for (int i = 0; i < 3; i++)
                {
                    int a = n + i, b = n + i + 1, c2 = n + 4 + i, d = n + 5 + i;
                    if (side > 0) t.AddRange(new[] { a, b, c2, b, d, c2 }); else t.AddRange(new[] { a, c2, b, b, c2, d });
                }
            }
            return Finish(v, uv, t);
        }

        /// <summary>Rows of points along the road, joined into a strip.</summary>
        class Sheet
        {
            public readonly List<Vector3> v = new List<Vector3>(); readonly List<Vector2> uv = new List<Vector2>(); readonly List<int> t = new List<int>();
            bool fresh = true;
            /// <summary>The next row starts a new piece instead of joining the last one.</summary>
            public void Break() => fresh = true;
            void Add2(Vector3 a, Vector3 b, Vector2 ua, Vector2 ub)
            {
                v.Add(a); v.Add(b); uv.Add(ua); uv.Add(ub);
                if (!fresh) { int n = v.Count - 4; t.AddRange(new[] { n, n + 2, n + 1, n + 1, n + 2, n + 3 }); }
                fresh = false;
            }
            // Ground texture in meters from above, matching the field it runs into.
            static Vector2 G(Vector3 q) => new Vector2(q.x / 12f, q.z / 12f);

            /// <summary>From the top edge at height y out to the toe at height yToe (the ground unless given).</summary>
            public void RowSlope(RoutePath p, float s, float edge, float toe, float y, float yToe = FillGround)
            {
                var a = p.Point(s, edge); a.y = y; var b = p.Point(s, toe); b.y = yToe;
                Add2(a, b, G(a), G(b));
            }
            /// <summary>A vertical wall face at this point, panels textured 4 m to a repeat.</summary>
            public void Row(Vector3 at, float y0, float y1)
            {
                var a = at; a.y = y0; var b = at; b.y = y1;
                float u = fresh || v.Count == 0 ? 0f : uv[uv.Count - 1].x + Vector3.Distance(new Vector3(at.x, 0, at.z), new Vector3(v[v.Count - 1].x, 0, v[v.Count - 1].z)) / 4f;
                Add2(a, b, new Vector2(u, y0 / 4f), new Vector2(u, y1 / 4f));
            }
            /// <summary>Both faces, each with its own points: shared ones average opposite normals to zero and render black.</summary>
            public Mesh Mesh()
            {
                int front = v.Count, tris = t.Count;
                v.AddRange(v.ToArray()); uv.AddRange(uv.ToArray());
                for (int i = 0; i < tris; i += 3) t.AddRange(new[] { t[i] + front, t[i + 2] + front, t[i + 1] + front });
                return Finish(v, uv, t);
            }
        }
    }
}
