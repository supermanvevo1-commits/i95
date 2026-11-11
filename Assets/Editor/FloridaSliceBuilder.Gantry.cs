using System.Collections.Generic;
using System.Linq;
using SurviveI95.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SurviveI95.EditorTools
{
    /// <summary>
    /// Overhead sign bridges, the Interstate shield, lane arrows, distance signs and tenth-mile markers: the signs a
    /// driver reads on Florida's urban I-95. Sign shapes follow the US standard sign manual (MUTCD), which is public.
    /// </summary>
    public static partial class FloridaSliceBuilder
    {
        static Material shieldRed, shieldBlue, signSteel;
        const float BridgeClearance = 5.5f; // bottom of the signs above the road; the legal minimum is about 5.3 m (17.5 ft)

        static void PrepareGantries()
        {
            shieldRed = Lit("ShieldRed", new Color(.72f, .05f, .08f), .45f);
            shieldBlue = Lit("ShieldBlue", new Color(0f, .17f, .52f), .45f);
            signSteel = Lit("SignSteel", new Color(.62f, .64f, .66f), .45f); signSteel.SetFloat("_Metallic", .55f);
        }

        // ---------------------------------------------------------------- flat shapes

        /// <summary>A flat shape facing drivers, from a convex or center-visible outline listed clockwise as the driver sees it.</summary>
        static Mesh Poly(string key, IList<Vector2> pts)
        {
            if (plates.TryGetValue(key, out var have)) return have;
            var c = Vector2.zero; foreach (var q in pts) c += q; c /= pts.Count;
            var v = new List<Vector3> { c }; var t = new List<int>();
            foreach (var q in pts) v.Add(q);
            for (int i = 0; i < pts.Count; i++) t.AddRange(new[] { 0, 1 + i, 1 + (i + 1) % pts.Count });
            var mesh = new Mesh();
            mesh.SetVertices(v); mesh.SetTriangles(t, 0);
            mesh.SetNormals(v.Select(_ => Vector3.back).ToList()); mesh.SetUVs(0, v.Select(q => new Vector2(q.x, q.y)).ToList());
            mesh.RecalculateBounds();
            string file = $"{Gen}/Meshes/Plate_{key}.asset";
            AssetDatabase.DeleteAsset(file); AssetDatabase.CreateAsset(mesh, file);
            plates[key] = mesh;
            return mesh;
        }

        // The Interstate shield's outline, one unit tall: two low humps along the top, then sides curving in to a point.
        static readonly Vector2[] ShieldShape =
        {
            new Vector2(-.47f, .40f), new Vector2(-.36f, .47f), new Vector2(-.24f, .49f), new Vector2(-.12f, .47f), new Vector2(0f, .43f),
            new Vector2(.12f, .47f), new Vector2(.24f, .49f), new Vector2(.36f, .47f), new Vector2(.47f, .40f),
            new Vector2(.48f, .25f), new Vector2(.47f, .08f), new Vector2(.43f, -.10f), new Vector2(.36f, -.26f), new Vector2(.25f, -.39f), new Vector2(.12f, -.47f), new Vector2(0f, -.51f),
            new Vector2(-.12f, -.47f), new Vector2(-.25f, -.39f), new Vector2(-.36f, -.26f), new Vector2(-.43f, -.10f), new Vector2(-.47f, .08f), new Vector2(-.48f, .25f)
        };

        // Keeps the part of an outline above (or below) a level line.
        static List<Vector2> ClipY(IList<Vector2> poly, float y, bool above)
        {
            var o = new List<Vector2>();
            bool In(Vector2 q) => above ? q.y >= y : q.y <= y;
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % poly.Count];
                if (In(a)) o.Add(a);
                if (In(a) != In(b)) o.Add(Vector2.Lerp(a, b, (y - a.y) / (b.y - a.y)));
            }
            return o;
        }

        /// <summary>An Interstate shield: white edge, red band with INTERSTATE, blue field with the route number.</summary>
        static void Shield(Transform parent, Vector3 center, Quaternion rot, float h, string route)
        {
            var fwd = rot * Vector3.forward;
            var outer = ShieldShape.Select(q => q * h).ToList();
            var inner = ShieldShape.Select(q => (q - new Vector2(0f, -.02f)) * .88f * h + new Vector2(0f, -.02f) * h).ToList();
            string k = Mathf.RoundToInt(h * 100).ToString();
            Face(parent, center - fwd * .016f, rot, Poly("shield_" + k, outer), white, "ShieldEdge");
            Face(parent, center - fwd * .019f, rot, Poly("shieldRed_" + k, ClipY(inner, .23f * h, true)), shieldRed, "ShieldRed");
            Face(parent, center - fwd * .019f, rot, Poly("shieldBlue_" + k, ClipY(inner, .2f * h, false)), shieldBlue, "ShieldBlue");
            Ink(parent, center + Vector3.up * .33f * h - fwd * .026f, rot, "INTERSTATE", .055f * h, Color.white);
            Ink(parent, center + Vector3.up * -.09f * h - fwd * .026f, rot, route, .36f * h, Color.white);
        }

        static Material white; // the plain sign white, set while building signs

        /// <summary>A white arrow, h tall, turned clockwise by deg (0 points up, 180 down, 45 up and to the right).</summary>
        static void Arrow(Transform parent, Vector3 center, Quaternion rot, float h, float deg)
        {
            var fwd = rot * Vector3.forward;
            var turn = Quaternion.Euler(0f, 0f, -deg);
            Vector2 T(float x, float y) { var q = turn * new Vector3(x * h, y * h, 0f); return new Vector2(q.x, q.y); }
            string k = $"{Mathf.RoundToInt(h * 100)}_{Mathf.RoundToInt(deg)}";
            var head = new[] { T(0f, .5f), T(.3f, .08f), T(-.3f, .08f) };
            var shaft = new[] { T(-.085f, .1f), T(.085f, .1f), T(.085f, -.5f), T(-.085f, -.5f) };
            Face(parent, center - fwd * .024f, rot, Poly("arrowHead_" + k, head), white, "Arrow");
            Face(parent, center - fwd * .024f, rot, Poly("arrowShaft_" + k, shaft), white, "Arrow");
        }

        // ---------------------------------------------------------------- the sign bridge

        /// <summary>
        /// A steel truss across the northbound lanes half a mile before an exit. Over the through lanes: NORTH, the I-95
        /// shield, the control city and an arrow down to each lane. Over the right lane: the exit's tab, where it goes,
        /// and 1/2 MILE.
        /// </summary>
        static void SignBridge(Transform parent, RoutePath p, float s, Mats m, string tab, Line[] exitLines, string control)
        {
            var g = new GameObject("SignBridge"); g.transform.SetParent(parent);
            var rot = Plumb(p, s); var fwd = rot * Vector3.forward; var right = rot * Vector3.right;
            float half = p.HalfWidth(s); int lanes = Mathf.Max(2, Mathf.RoundToInt(p.Lanes(s))); float lw = 2f * half / lanes;
            float roadY = p.Point(s, 0f).y, bottomY = roadY + BridgeClearance;
            Vector3 At(float lat, float y) { var q = p.Point(s, lat); q.y = y; return q; }

            // The exit panel over the right lane, kept from overhanging the shoulder.
            var exitSize = PlateSize(exitLines);
            float exitLat = Mathf.Min(half - lw / 2f, half + 2.4f - exitSize.x / 2f);
            Plate(g.transform, At(exitLat, bottomY), rot, exitLines, exitSize, m.signGreen, m.white, Color.white);
            var tabLines = new[] { L(tab, TabCap) }; var ts = PlateSize(tabLines);
            Plate(g.transform, At(exitLat, bottomY) + Vector3.up * (exitSize.y - .03f) + right * (exitSize.x / 2f - ts.x / 2f), rot, tabLines, ts, m.signGreen, m.white, Color.white);

            // The pull-through panel over the other lanes.
            const float card = .42f, shieldH = 1.45f, arrowH = .8f; // the shield reads before the words, as on real signs
            int arrows = Mathf.Clamp(lanes - 1, 1, 3);
            float tw = Mathf.Max(Mathf.Max(Width(control, PlaceCap), Width("NORTH", card)), shieldH) + PlaceCap * 1.7f;
            tw = Mathf.Max(tw, arrows * 1.45f + .6f);
            float th = .3f + card + .22f + shieldH + .22f + PlaceCap + .3f + arrowH + .3f;
            float throughLat = Mathf.Min(-lw / 2f, exitLat - exitSize.x / 2f - .5f - tw / 2f);
            var tb = At(throughLat, bottomY);
            Blank(g.transform, tb, rot, new Vector2(tw, th), m.signGreen, m.white);
            float y = th - .3f - card / 2f;
            Ink(g.transform, tb + Vector3.up * y - fwd * .024f, rot, "NORTH", card, Color.white);
            y -= card / 2f + .22f + shieldH / 2f;
            Shield(g.transform, tb + Vector3.up * y, rot, shieldH, "95");
            y -= shieldH / 2f + .22f + PlaceCap / 2f;
            Ink(g.transform, tb + Vector3.up * y - fwd * .024f, rot, control, PlaceCap, Color.white);
            y -= PlaceCap / 2f + .3f + arrowH / 2f;
            for (int i = 0; i < arrows; i++)
            {
                float x = (i - (arrows - 1) / 2f) * Mathf.Min(1.6f, (tw - .6f) / arrows);
                Arrow(g.transform, tb + Vector3.up * y + right * x, rot, arrowH, 180f);
            }

            // The structure: a column behind the median barrier, one beyond the shoulder (behind its own guardrail),
            // and a box truss between them behind the signs.
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            float leftLat = -half - RoutePath.BarrierLeft - 1f, rightLat = half + RoutePath.BarrierRight + 1.05f;
            float lowChord = bottomY + .5f, highChord = bottomY + 2.4f, behind = .5f;
            foreach (float lat in new[] { leftLat, rightLat })
            {
                var foot = p.Point(s, lat); float top = highChord + .35f;
                Cuboid(v, uv, t, new Vector3(foot.x, (foot.y - .3f + top) / 2f, foot.z) + fwd * behind, rot, new Vector3(.5f, top - foot.y + .3f, .5f));
                Cuboid(v, uv, t, foot + fwd * behind + Vector3.up * .05f, rot, new Vector3(.9f, .1f, .9f)); // base plate
            }
            Solid(s + behind, rightLat, .3f, .3f, "pier");
            var a = At(leftLat, 0f); var b = At(rightLat, 0f); float span = Vector3.Distance(new Vector3(a.x, 0, a.z), new Vector3(b.x, 0, b.z));
            var midXZ = (a + b) / 2f;
            foreach (float cy in new[] { lowChord, highChord })
                foreach (float depth in new[] { behind - .35f, behind + .35f })
                    Cuboid(v, uv, t, new Vector3(midXZ.x, cy, midXZ.z) + fwd * depth, rot, new Vector3(span, .16f, .16f));
            // Zig-zag web members, front and back faces of the truss.
            int bays = Mathf.Max(2, Mathf.RoundToInt(span / 1.8f)); float bay = span / bays, rise = highChord - lowChord;
            float diag = Mathf.Sqrt(bay * bay + rise * rise), ang = Mathf.Atan2(rise, bay) * Mathf.Rad2Deg;
            for (int i = 0; i < bays; i++)
            {
                float x = -span / 2f + (i + .5f) * bay;
                foreach (float depth in new[] { behind - .35f, behind + .35f })
                {
                    var cpos = new Vector3(midXZ.x, (lowChord + highChord) / 2f, midXZ.z) + right * x + fwd * depth;
                    Cuboid(v, uv, t, cpos, rot * Quaternion.Euler(0f, 0f, i % 2 == 0 ? ang : -ang), new Vector3(diag, .08f, .08f));
                    Cuboid(v, uv, t, cpos - right * (bay / 2f), rot, new Vector3(.08f, rise, .08f));
                }
            }
            Add(g, "Truss", Finish(v, uv, t), signSteel, true);
        }

        // ---------------------------------------------------------------- distance signs and markers

        [System.Serializable] class PlaceRow { public float s; public string name, sub, line; }
        [System.Serializable] class PlaceRows { public PlaceRow[] places; }

        static List<PlaceRow> LoadPlaces()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>($"Assets/Resources/Data/Routes/{RouteId}_places.json");
            return asset == null ? new List<PlaceRow>() : JsonUtility.FromJson<PlaceRows>(asset.text).places.ToList();
        }

        static string SignName(string place) => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(place.ToLowerInvariant()).Replace("Fort ", "Ft ");

        /// <summary>
        /// A distance sign after an interchange: the next places ahead and the control city, with whole miles to each,
        /// names on the left and miles lined up on the right, as on real ones.
        /// </summary>
        static void DistanceSign(Transform parent, RoutePath p, float s, List<PlaceRow> places, string control, float controlS, Mats m)
        {
            var rows = places.Where(q => q.s - s > 1000f && q.s < controlS - 1600f).Take(2).Select(q => (SignName(q.name), q.s)).ToList();
            rows.Add((control, controlS));
            var lines = rows.Select(r => (name: r.Item1, miles: Mathf.Max(1, Mathf.RoundToInt((r.Item2 - s) / 1609.34f)).ToString())).ToList();
            const float cap = .5f, gap = 1.3f;
            float nameW = lines.Max(l => Width(l.name, cap)), numW = lines.Max(l => Width(l.miles, cap));
            var size = new Vector2(nameW + gap + numW + cap * 1.7f, lines.Count * cap + (lines.Count - 1) * cap * .6f + cap * 1.5f);
            RoadSign(parent, p, s, 4.2f, 2.2f, null, null, m.signGreen, m.white, Color.white, m, fixedSize: size, draw: (bottom, rot, sz) =>
            {
                Blank(parent, bottom, rot, sz, m.signGreen, m.white);
                var fwd = rot * Vector3.forward; var right = rot * Vector3.right;
                float y = sz.y - cap * .75f - cap / 2f;
                foreach (var l in lines)
                {
                    var row = bottom + Vector3.up * y - fwd * .024f;
                    Ink(parent, row - right * (sz.x / 2f - cap * .85f), rot, l.name, cap, Color.white, TextAnchor.MiddleLeft);
                    Ink(parent, row + right * (sz.x / 2f - cap * .85f), rot, l.miles, cap, Color.white, TextAnchor.MiddleRight);
                    y -= cap * 1.6f;
                }
            });
        }
    }
}
