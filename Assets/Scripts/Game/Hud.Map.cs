using SurviveI95.World;
using UnityEngine;

namespace SurviveI95.Game
{
    /// <summary>The minimap panel: the map picture, your arrow, north, the destination and the next exit.</summary>
    public partial class Hud
    {
        static readonly Color ExitGreen = new Color(0f, .42f, .22f), DestRed = new Color(1f, .3f, .22f);
        Minimap map;
        Texture2D carArrow, ring;
        /// <summary>Where this trip ends: a name and a distance along the route. Scenarios set it (5b.3); until then it's the end of the test road.</summary>
        public string DestinationName { get; set; }
        public float DestinationS { get; set; } = -1f;

        void StartMap()
        {
            map = new Minimap(car);
            carArrow = MakeCarArrow(); ring = MakeDisc(64, Color.white, Color.white);
            if (DestinationS < 0f) DestinationS = car.Path.Length;
            if (DestinationName == null) DestinationName = places.Length > 0 ? places[places.Length - 1].name : "END OF ROAD";
        }

        void UpdateMap()
        {
            if (map == null) return;
            map.Follow(car.transform, car.Sim.Speed, Time.deltaTime);
        }

        /// <summary>Draws the minimap in the given corner (it takes the corner across from the speedometer).</summary>
        void DrawMap(bool right, bool bottom)
        {
            if (map == null) return;
            float s = scale, M = 250f * s, margin = 34f * s;
            float x = right ? Screen.width - margin - M : margin, y = bottom ? Screen.height - margin - M : margin;
            var r = new Rect(x, y, M, M); float radius = 18f * s;
            Panel(new Rect(r.x + 2f * s, r.y + 4f * s, M, M), new Color(0f, 0f, 0f, .35f), radius);
            GUI.DrawTexture(r, map.Texture, ScaleMode.StretchToFill, false, 0f, new Color(1f, 1f, 1f, .94f), 0f, radius);
            GUI.DrawTexture(r, white, ScaleMode.StretchToFill, true, 0f, new Color(1f, 1f, 1f, .16f), 1.5f * s, radius); // a thin edge
            var c = r.center; float half = M / 2f;
            Vector2 Px(Vector2 m) => c + m * half;

            // North: a small compass in the top corner away from the dial, its red point turning as you do.
            float cr = 14f * s;
            var cc = new Vector2(right ? r.x + 20f * s : r.xMax - 20f * s, r.y + 20f * s);
            Panel(new Rect(cc.x - cr, cc.y - cr, cr * 2f, cr * 2f), new Color(.04f, .05f, .06f, .88f), cr);
            var nDir = new Vector2(Mathf.Sin(-map.Yaw * Mathf.Deg2Rad), -Mathf.Cos(-map.Yaw * Mathf.Deg2Rad));
            float mw = 7f * s, mh = 7f * s; var mp = cc + nDir * (cr + mh * .5f);
            Rotated(mark, new Rect(mp.x - mw / 2f, mp.y - mh / 2f, mw, mh), mp, 180f - map.Yaw, DestRed);
            Text(new Rect(cc.x - cr, cc.y - cr + 1f * s, cr * 2f, cr * 2f), "N", tiny, Ink, false);

            // The destination: a red dot on the map, or on the edge pointing the way when it's farther than the map shows.
            var dest = car.Path.Center(DestinationS);
            var dm = map.ToMap(dest);
            bool inside = Mathf.Abs(dm.x) < .9f && Mathf.Abs(dm.y) < .9f;
            var dp = inside ? Px(dm) : c + EdgeClamp(dm.normalized, half - 14f * s);
            float dr = 9f * s;
            // Off the map, the dot rides the edge; it steps aside rather than cover the compass.
            if (!inside && Vector2.Distance(dp, cc) < cr + dr + 6f * s)
            {
                float away = cr + dr + 8f * s;
                if (Mathf.Abs(dp.y - cc.y) < Mathf.Abs(dp.x - cc.x)) dp.y = cc.y + away; else dp.x = cc.x + (cc.x < c.x ? away : -away);
            }
            var keep = GUI.color;
            GUI.color = Color.white; GUI.DrawTexture(new Rect(dp.x - dr - 2f * s, dp.y - dr - 2f * s, (dr + 2f * s) * 2f, (dr + 2f * s) * 2f), ring);
            GUI.color = DestRed; GUI.DrawTexture(new Rect(dp.x - dr, dp.y - dr, dr * 2f, dr * 2f), ring);
            GUI.color = keep;

            // You: an arrow below the middle, pointing the way the car faces.
            var you = Px(map.ToMap(car.transform.position));
            var f = car.transform.forward; float carYaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            float aw = 26f * s, ah = 30f * s;
            Rotated(carArrow, new Rect(you.x - aw / 2f, you.y - ah / 2f, aw, ah), you, carYaw - map.Yaw, Color.white);

            // The destination and how far, in a band along the bottom of the map.
            float milesLeft = Mathf.Max(0f, DestinationS - RouteS()) / 1609.34f;
            var band = new Rect(r.x, r.yMax - 40f * s, M, 40f * s); // taller than twice the corner radius, or the rounding eats its bottom edge
            GUI.DrawTexture(band, white, ScaleMode.StretchToFill, true, 0f, new Color(.03f, .04f, .05f, .78f), Vector4.zero, new Vector4(0f, 0f, radius, radius));
            GUI.color = DestRed; GUI.DrawTexture(new Rect(band.x + 12f * s, band.center.y - 5f * s, 10f * s, 10f * s), ring); GUI.color = keep;
            var left = new GUIStyle(small) { alignment = TextAnchor.MiddleLeft };
            var rightAlign = new GUIStyle(small) { alignment = TextAnchor.MiddleRight };
            Text(new Rect(band.x + 30f * s, band.y, M - 110f * s, band.height), DestinationName, left, Ink, false);
            Text(new Rect(band.x, band.y, M - 12f * s, band.height), Miles(milesLeft), rightAlign, Ink, false);

            DrawNextExit(r, bottom);
        }

        // The next numbered exit ahead, in the green of a real exit sign, just above (or below) the map.
        void DrawNextExit(Rect map, bool bottom)
        {
            if (car.Sim.Current != null) return; // on a ramp already
            float at = RouteS(); RouteExit next = null;
            var exits = car.Path.File.exits;
            if (exits != null) foreach (var e in exits) if (!string.IsNullOrEmpty(e.@ref) && e.s > at + 10f) { next = e; break; }
            if (next == null) return;
            float s = scale, h = 34f * s;
            float y = bottom ? map.y - h - 8f * s : map.yMax + 8f * s;
            var tabStyle = new GUIStyle(small) { fontSize = Mathf.RoundToInt(20 * s) };
            string tabText = "EXIT " + next.@ref;
            float tabW = tabStyle.CalcSize(new GUIContent(tabText)).x + 20f * s;
            string name = Short(next.name);
            var nameStyle = new GUIStyle(tiny) { alignment = TextAnchor.MiddleLeft, fontSize = Mathf.RoundToInt(17 * s), clipping = TextClipping.Clip };
            var r = new Rect(map.x, y, map.width, h);
            Panel(r, ChipColor, 8f * s);
            var tab = new Rect(r.x + 4f * s, r.y + 4f * s, tabW, h - 8f * s);
            Panel(tab, ExitGreen, 5f * s);
            GUI.DrawTexture(tab, white, ScaleMode.StretchToFill, true, 0f, Color.white, 1.2f * s, 5f * s);
            Text(tab, tabText, tabStyle, Color.white, false);
            string dist = Miles((next.s - at) / 1609.34f);
            var distStyle = new GUIStyle(small) { alignment = TextAnchor.MiddleRight };
            float distW = distStyle.CalcSize(new GUIContent(dist)).x;
            Text(new Rect(tab.xMax + 8f * s, r.y, r.width - tabW - distW - 30f * s, h), name, nameStyle, Dim, false);
            Text(new Rect(r.x, r.y, r.width - 10f * s, h), dist, distStyle, Ink, false);
        }

        // Street names shortened the way Florida signs and map apps do ("Northwest 103rd Street" to "NW 103rd St").
        static readonly string[,] Shorter =
        {
            { "Northwest ", "NW " }, { "Northeast ", "NE " }, { "Southwest ", "SW " }, { "Southeast ", "SE " },
            { " Street", " St" }, { " Avenue", " Ave" }, { " Boulevard", " Blvd" }, { " Road", " Rd" }, { " Drive", " Dr" },
            { " Parkway", " Pkwy" }, { " Expressway", " Expy" }, { " Causeway", " Cswy" }, { " Highway", " Hwy" }, { "North ", "N " }, { "South ", "S " }
        };
        static string Short(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            for (int i = 0; i < Shorter.GetLength(0); i++) name = name.Replace(Shorter[i, 0], Shorter[i, 1]);
            return name;
        }

        float RouteS() => car.Sim.Current == null ? car.Sim.S : car.Sim.CurrentJunction.divergeS + car.Sim.S;

        // As US navigation apps say it: feet when close, then tenths of a mile, then whole miles.
        static string Miles(float mi)
        {
            if (mi < .19f) return Mathf.Max(100, Mathf.RoundToInt(mi * 5280f / 100f) * 100) + " FT";
            return mi < 10f ? mi.ToString("0.0") + " MI" : Mathf.RoundToInt(mi) + " MI";
        }

        // Pushes a direction out to the edge of a square (the map is square), rather than a circle.
        static Vector2 EdgeClamp(Vector2 dir, float half)
        {
            float m = Mathf.Max(Mathf.Abs(dir.x), Mathf.Abs(dir.y));
            return m < 1e-5f ? Vector2.zero : dir / m * half;
        }

        // The navigation arrow: white with a dark edge, notched at the back.
        static Texture2D MakeCarArrow()
        {
            int w = 52, h = 60; var t = New(w, h); var px = new Color[w * h];
            var poly = new[] { new Vector2(26, 57), new Vector2(48, 5), new Vector2(26, 17), new Vector2(4, 5) };
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                var p = new Vector2(x + .5f, y + .5f);
                float d = PolyDistance(p, poly); // negative inside
                float body = Edge(d + 3.2f, 1.4f), edge = Edge(d, 1.4f);
                var col = Color.Lerp(new Color(.05f, .2f, .45f), Color.white, body); col.a = edge;
                px[y * w + x] = col;
            }
            t.SetPixels(px); t.Apply(); return t;
        }

        static float PolyDistance(Vector2 p, Vector2[] poly)
        {
            float d = float.MaxValue; bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                var a = poly[j]; var b = poly[i]; var e = b - a;
                float k = Mathf.Clamp01(Vector2.Dot(p - a, e) / e.sqrMagnitude);
                d = Mathf.Min(d, (p - a - e * k).magnitude);
                if ((a.y > p.y) != (b.y > p.y) && p.x < a.x + (p.y - a.y) / (b.y - a.y) * e.x) inside = !inside;
            }
            return inside ? -d : d;
        }
    }
}
