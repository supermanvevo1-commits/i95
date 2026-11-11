using System.Collections.Generic;
using System.Linq;
using SurviveI95.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SurviveI95.EditorTools
{
    /// <summary>
    /// Road signs built the way real ones are: the panel is sized to its words, never the words squeezed onto a panel.
    /// Green guide signs carry a white border, rounded corners and an exit number tab on the top right corner.
    /// </summary>
    public static partial class FloridaSliceBuilder
    {
        const string SignFontFile = "Assets/ThirdParty/Fonts/Overpass-Bold.ttf"; // open-license lookalike of the US highway typeface
        const float CapPerEm = 0.7f; // a capital letter's height as a share of the font size
        const int FontPx = 96;
        static Font signFont;
        static Material signInk, signBlack, signBack;
        static readonly Dictionary<string, Mesh> plates = new Dictionary<string, Mesh>();
        static readonly Color SignInk = new Color(.06f, .06f, .05f);
        // Capital letter heights in meters. Real freeway signs use 0.4 to 0.5 m; these are about a third larger, because a
        // screen shows far less detail than an eye does and the words have to be readable in time at speed.
        const float PlaceCap = .58f, ActionCap = .48f, TabCap = .4f;

        /// <summary>One line of a sign: its words and the height of its capital letters in meters.</summary>
        struct Line { public string text; public float cap; }
        static Line L(string text, float cap) => new Line { text = text, cap = cap };

        static void PrepareSigns()
        {
            // The letters are baked into a fixed sheet at import. A font that builds its sheet while the game runs can
            // repack it, and text drawn before the repack goes blank (a whole billboard line went missing that way).
            if (AssetImporter.GetAtPath(SignFontFile) is TrueTypeFontImporter imp && (imp.fontTextureCase != FontTextureCase.ASCII || imp.fontSize != FontPx))
            { imp.fontTextureCase = FontTextureCase.ASCII; imp.fontSize = FontPx; imp.SaveAndReimport(); }
            signFont = AssetDatabase.LoadAssetAtPath<Font>(SignFontFile);
            if (signFont == null)
            {
                Debug.LogWarning("[FloridaSlice] highway font missing, using the built-in one (run node Tools/fetch_assets.js environment)");
                signFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            signInk = new Material(Shader.Find("SurviveI95/SignText")) { mainTexture = signFont.material.mainTexture };
            Save(signInk, "SignInk");
            signBlack = Lit("SignBlack", new Color(.05f, .05f, .05f), .3f);
            signBack = Lit("SignBack", new Color(.6f, .62f, .63f), .5f);
            plates.Clear();
        }

        static float Width(string text, float cap)
        {
            if (signFont.dynamic) signFont.RequestCharactersInTexture(text, FontPx);
            float px = 0f;
            foreach (char ch in text) if (signFont.dynamic ? signFont.GetCharacterInfo(ch, out var info, FontPx) : signFont.GetCharacterInfo(ch, out info)) px += info.advance;
            return px * cap / CapPerEm / FontPx;
        }

        static void Ink(Transform parent, Vector3 pos, Quaternion rot, string text, float cap, Color color)
        {
            var go = new GameObject("Text", typeof(TextMesh));
            go.transform.SetParent(parent); go.transform.SetPositionAndRotation(pos, rot);
            var tm = go.GetComponent<TextMesh>();
            tm.font = signFont; tm.text = text; tm.fontSize = signFont.dynamic ? FontPx : 0; tm.characterSize = cap / CapPerEm / (FontPx * .1f);
            tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center; tm.color = color;
            go.GetComponent<MeshRenderer>().sharedMaterial = signInk;
        }

        // A flat panel with rounded corners, facing drivers coming up the road (the -Z side).
        static Mesh RoundRect(float w, float h, float r)
        {
            string key = $"{Mathf.RoundToInt(w * 100)}_{Mathf.RoundToInt(h * 100)}_{Mathf.RoundToInt(r * 100)}";
            if (plates.TryGetValue(key, out var have)) return have;
            var v = new List<Vector3> { Vector3.zero }; var t = new List<int>();
            const int steps = 5;
            for (int c = 0; c < 4; c++) // clockwise from the top left corner, as the driver sees it
            {
                float cx = (c == 0 || c == 3 ? -1 : 1) * (w / 2 - r), cy = (c < 2 ? 1 : -1) * (h / 2 - r);
                for (int i = 0; i <= steps; i++)
                {
                    float a = (180f - 90f * c - 90f * i / steps) * Mathf.Deg2Rad;
                    v.Add(new Vector3(cx + Mathf.Cos(a) * r, cy + Mathf.Sin(a) * r, 0));
                }
            }
            int n = v.Count - 1;
            for (int i = 0; i < n; i++) t.AddRange(new[] { 0, 1 + i, 1 + (i + 1) % n });
            var mesh = new Mesh();
            mesh.SetVertices(v); mesh.SetTriangles(t, 0);
            mesh.SetNormals(v.Select(_ => Vector3.back).ToList());
            mesh.SetUVs(0, v.Select(q => new Vector2(q.x / w + .5f, q.y / h + .5f)).ToList());
            mesh.RecalculateBounds();
            string file = $"{Gen}/Meshes/Plate_{key}.asset";
            AssetDatabase.DeleteAsset(file); AssetDatabase.CreateAsset(mesh, file);
            plates[key] = mesh;
            return mesh;
        }

        static void Face(Transform parent, Vector3 pos, Quaternion rot, Mesh mesh, Material mat, string name)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent); go.transform.SetPositionAndRotation(pos, rot);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off;
        }

        static Vector2 PlateSize(Line[] lines, float minWidth = 0f)
        {
            float w = 0f, h = 0f, big = 0f;
            foreach (var l in lines) { w = Mathf.Max(w, Width(l.text, l.cap)); h += l.cap; big = Mathf.Max(big, l.cap); }
            h += big * .55f * (lines.Length - 1) + big * 1.5f; // space between lines, and above and below them
            return new Vector2(Mathf.Max(minWidth, w + big * 1.7f), h);
        }

        /// <summary>One sign face. bottom is the middle of its lower edge.</summary>
        static void Plate(Transform parent, Vector3 bottom, Quaternion rot, Line[] lines, Vector2 size, Material face, Material border, Color ink)
        {
            var fwd = rot * Vector3.forward;
            float big = lines.Max(l => l.cap);
            float b = Mathf.Clamp(size.y * .035f, .025f, .09f), r = Mathf.Clamp(size.y * .1f, .05f, .3f);
            var mid = bottom + Vector3.up * (size.y / 2f);
            // The aluminum back, kept just inside the rounded corners.
            Box(parent, mid + fwd * .03f, rot, new Vector3(size.x - r * .6f, size.y - r * .6f, .05f), signBack, "Back").GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.On;
            Face(parent, mid, rot, RoundRect(size.x, size.y, r), border, "Border");
            Face(parent, mid - fwd * .012f, rot, RoundRect(size.x - 2 * b, size.y - 2 * b, Mathf.Max(.02f, r - b)), face, "Panel");
            float y = size.y - big * .75f;
            foreach (var l in lines)
            {
                y -= l.cap / 2f;
                Ink(parent, bottom + Vector3.up * y - fwd * .024f, rot, l.text, l.cap, ink);
                y -= l.cap / 2f + big * .55f;
            }
        }

        /// <summary>
        /// A roadside sign on posts. clear is the gap from the road edge to the sign's near edge, so a wider sign
        /// grows away from the road. tab is the exit number plaque on the top right corner, or null.
        /// </summary>
        static void RoadSign(Transform parent, RoutePath p, float s, float clear, float elevation, string tab, Line[] lines, Material face, Material border, Color ink, Mats m, bool solid = true, bool placedForExit = false, float minWidth = 0f)
        {
            if (!placedForExit) foreach (var j in p.Junctions) if (s > j.divergeS - 2f && s < j.mergeS + 10f) return; // would stand in the exit lane
            var size = PlateSize(lines, minWidth);
            var rot = Plumb(p, s);
            float lat = p.HalfWidth(s) + clear + size.x / 2f;
            var c = p.Point(s, lat); var right = p.Right(s);
            var bottom = c + Vector3.up * elevation;
            Plate(parent, bottom, rot, lines, size, face, border, ink);
            float top = c.y + elevation + size.y;
            if (tab != null)
            {
                var tabLines = new[] { L(tab, TabCap) }; var ts = PlateSize(tabLines);
                Plate(parent, bottom + Vector3.up * (size.y - .03f) + right * (size.x / 2f - ts.x / 2f), rot, tabLines, ts, face, border, ink);
            }
            float thick = size.x > 3f ? .2f : .12f;
            foreach (float d in size.x > 1.6f ? new[] { -size.x * .32f, size.x * .32f } : new[] { 0f })
            {
                if (solid) Solid(s, lat + d, .12f, .12f, size.x > 10f ? "pole" : "post");
                var foot = c + right * d + p.Tangent(s) * (.06f + thick / 2f); foot.y = top / 2f;
                Box(parent, foot, rot, new Vector3(thick, top, thick), m.post, "Post");
            }
        }

        static void Diamond(Transform parent, RoutePath p, float s, float lateralFromEdge, string text, Mats m, bool solid)
        {
            const float size = 1.7f, elevation = 2.3f;
            var rot = Plumb(p, s);
            var c = p.Point(s, p.HalfWidth(s) + lateralFromEdge);
            var mid = c + Vector3.up * (elevation + size * .71f);
            var turned = rot * Quaternion.Euler(0f, 0f, 45f);
            Box(parent, mid + p.Tangent(s) * .01f, turned, new Vector3(size, size, .06f), signBlack, "Border").GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.On;
            Box(parent, mid, turned, new Vector3(size - .09f, size - .09f, .08f), m.yellow, "Panel");
            var foot = c + p.Tangent(s) * .14f; float top = mid.y; foot.y = top / 2f;
            Box(parent, foot, rot, new Vector3(.12f, top, .12f), m.post, "Post");
            if (solid) Solid(s, p.HalfWidth(s) + lateralFromEdge, .12f, .12f, "post");
            // A diamond is widest across the middle, so the words are sized to fit there.
            var words = text.Split('\n');
            float cap = Mathf.Min(.22f, .22f * size * .95f / words.Max(w => Width(w, .22f)));
            float y = (words.Length - 1) * cap * .8f;
            foreach (var w in words) { Ink(parent, mid + Vector3.up * y - p.Tangent(s) * .05f, rot, w, cap, SignInk); y -= cap * 1.6f; }
        }

        // Signs stand plumb even where the road climbs. Tilted with the road, a tall sign's lower lines sank behind its own panel.
        static Quaternion Plumb(RoutePath p, float s) { var along = p.Tangent(s); along.y = 0f; return Quaternion.LookRotation(along.normalized, Vector3.up); }

        /// <summary>Counts the lines of sign text that stick out past their own panel. Must be 0.</summary>
        public static string CheckSigns()
        {
            var signs = GameObject.Find("Signs");
            if (signs == null) return "ERROR no signs in the scene";
            var borders = signs.GetComponentsInChildren<MeshRenderer>().Where(r => r.name == "Border").Select(r => r.bounds).ToArray();
            int texts = 0, outside = 0; float worst = 0f; string worstText = "";
            foreach (var tm in signs.GetComponentsInChildren<TextMesh>())
            {
                var tb = tm.GetComponent<MeshRenderer>().bounds; texts++;
                Bounds? home = null; float best = float.MaxValue;
                foreach (var b in borders) { float d = (b.center - tb.center).sqrMagnitude; if (d < best) { best = d; home = b; } }
                if (home == null) continue;
                var h = home.Value;
                // How far the words reach past the panel's edge, sideways or up and down.
                float over = Mathf.Max(Mathf.Max(h.min.x - tb.min.x, tb.max.x - h.max.x), Mathf.Max(h.min.z - tb.min.z, tb.max.z - h.max.z), Mathf.Max(h.min.y - tb.min.y, tb.max.y - h.max.y));
                if (over > .03f) { outside++; if (over > worst) { worst = over; worstText = tm.text; } }
            }
            return System.FormattableString.Invariant($"signs={borders.Length} textLines={texts} outsidePanel={outside} worst={worst:F2}m {worstText}");
        }

        static string CleanRef(string r) => r == null ? "" : System.Text.RegularExpressions.Regex.Replace(r, "[^0-9A-Z]", "");

        // Signs abbreviate the way Florida's do: "NW 62nd St", not "Northwest 62nd Street".
        static string Short(string name) => name
            .Replace("Northwest", "NW").Replace("Northeast", "NE").Replace("Southwest", "SW").Replace("Southeast", "SE")
            .Replace("Boulevard", "Blvd").Replace("Street", "St").Replace("Avenue", "Ave").Replace("Drive", "Dr").Replace("Road", "Rd")
            .Replace("International", "Int'l").Replace("Medical Center", "Med Ctr");

        /// <summary>The places an exit leads to, one or more per line, wrapped to a sign's width. Whole places are dropped rather than cut in half.</summary>
        static List<Line> Destinations(string name, float cap, int maxLines = 3, int maxChars = 22)
        {
            var lines = new List<Line>();
            foreach (var part in (name ?? "").Split(';'))
            {
                var mine = new List<string>(); string line = "";
                foreach (var w in Short(part.Trim()).Split(' '))
                {
                    if (line.Length > 0 && line.Length + 1 + w.Length > maxChars) { mine.Add(line); line = w; }
                    else line = line.Length > 0 ? line + " " + w : w;
                }
                if (line.Length > 0) mine.Add(line);
                if (lines.Count > 0 && lines.Count + mine.Count > maxLines) break;
                lines.AddRange(mine.Take(maxLines).Select(t => L(t, cap)));
            }
            return lines;
        }

        static void BuildSigns(RoutePath p, Mats m)
        {
            PrepareSigns();
            var g = new GameObject("Signs");
            var t = g.transform;
            foreach (var j in p.Junctions)
            {
                var real = p.File.exits.Where(e => !string.IsNullOrEmpty(e.@ref)).OrderBy(e => Mathf.Abs(e.s - j.divergeS)).FirstOrDefault();
                bool known = real != null && Mathf.Abs(real.s - j.divergeS) < 300f;
                string number = known ? CleanRef(real.@ref) : "";
                string tab = known ? "EXIT " + number : null;
                // Half a mile out: where it goes and how far.
                var ahead = known ? Destinations(real.name, PlaceCap, 2) : new List<Line>();
                if (ahead.Count == 0) ahead.Add(L("EXIT", PlaceCap));
                ahead.Add(L("1/2 MILE", ActionCap));
                RoadSign(t, p, Mathf.Max(5f, j.divergeS - 805f), 4.2f, 2.5f, tab, ahead.ToArray(), m.signGreen, m.white, Color.white, m);
                // Yellow signs mark the exits you can actually drive.
                foreach (float back in new[] { 420f, 130f })
                    RoadSign(t, p, Mathf.Max(5f, j.divergeS - back), 3.8f, 2.5f, null, new[] { L(known ? "EXIT " + number : "EXIT", PlaceCap), L("KEEP RIGHT", ActionCap) }, m.yellow, signBlack, SignInk, m);
                RoadSign(t, p, j.divergeS + 175f, 2.4f, 1.7f, null, known ? new[] { L("EXIT", .34f), L(number, PlaceCap) } : new[] { L("EXIT", ActionCap) }, m.signGreen, m.white, Color.white, m, placedForExit: true);
                Diamond(t, p, j.mergeS - 310f, 7f, "MERGING\nTRAFFIC", m, true);
                RoadSign(t, j.road, 235f, 2.1f, 1.5f, null, new[] { L("RAMP", .2f), L("45", .42f), L("MPH", .18f) }, m.yellow, signBlack, SignInk, m, solid: false);
                Diamond(t, j.road, j.road.Length - 250f, 3.4f, "MERGE", m, false);
            }
            float? baseMile = null;
            foreach (var e in p.File.exits)
            {
                if (string.IsNullOrEmpty(e.@ref)) continue;
                string number = CleanRef(e.@ref);
                var digits = new string(e.@ref.TakeWhile(char.IsDigit).ToArray());
                if (baseMile == null && digits.Length > 0) baseMile = float.Parse(digits) - e.s / 1609.34f;
                var places = Destinations(e.name, PlaceCap);
                if (places.Count > 0) RoadSign(t, p, e.s - 340f, 4.2f, 2.4f, "EXIT " + number, places.ToArray(), m.signGreen, m.white, Color.white, m);
                else RoadSign(t, p, e.s - 340f, 4.2f, 2.4f, null, new[] { L("EXIT " + number, PlaceCap) }, m.signGreen, m.white, Color.white, m);
                RoadSign(t, p, Mathf.Max(5, e.s - 15f), 7.5f, 2.4f, null, new[] { L("EXIT", .34f), L(number, PlaceCap) }, m.signGreen, m.white, Color.white, m);
            }
            if (baseMile != null)
                for (int mile = Mathf.CeilToInt(baseMile.Value); (mile - baseMile.Value) * 1609.34f < p.Length; mile++)
                {
                    // Mile markers are tall and narrow, with the digits stacked.
                    var stack = new List<Line> { L("MILE", .09f) };
                    foreach (char d in mile.ToString()) stack.Add(L(d.ToString(), .2f));
                    RoadSign(t, p, Mathf.Max(5, (mile - baseMile.Value) * 1609.34f), 4.9f, 1.3f, null, stack.ToArray(), m.signGreen, m.white, Color.white, m);
                }
            // Real posted limits: a sign just past every change, then repeats along the way.
            float lastSign = -9999f, lastMph = -1f;
            for (float s = 120f; s < p.Length - 300f; s += 50f)
            {
                float mph = p.LimitMph(s);
                bool changed = mph != lastMph && lastMph > 0f;
                if (!changed && s - lastSign < 2400f && lastMph > 0f) continue;
                if (p.Junctions.Any(j => s > j.divergeS - 60f && s < j.divergeS + 160f)) continue; // keep the exit gore clear
                RoadSign(t, p, s + (changed ? 60f : 0f), 5.6f, 1.8f, null, new[] { L("SPEED", .24f), L("LIMIT", .24f), L(mph.ToString("0"), .58f) }, m.white, signBlack, SignInk, m);
                lastSign = s; lastMph = mph;
            }
            string[] ads = { "HURT ON I-95?\nCALL BIG RICK", "GATOR WORLD\nNEXT EXIT", "FIREWORKS\nFIREWORKS\nFIREWORKS", "EARLY BIRD\nBUFFET 3:45PM" };
            for (int i = 0; i < 9; i++)
            {
                // Billboards keep clear of exit signs, so nothing competes with them.
                float at = 1400f + i * 2500f;
                while (p.Junctions.Any(j => at > j.divergeS - 900f && at < j.mergeS + 40f)) at += 100f;
                var words = ads[i % ads.Length].Split('\n');
                float cap = Mathf.Min(1.1f, 1.1f * 12.5f / words.Max(w => Width(w, 1.1f)));
                Debug.Log("[FloridaSlice] billboard at " + at.ToString("F0") + ": " + string.Join(" / ", words));
                RoadSign(t, p, at, 14.5f, 6f, null, words.Select(w => L(w, cap)).ToArray(), m.billboard, signBlack, new Color(.08f, .08f, .08f), m, minWidth: 15f);
            }
            Debug.Log("[FloridaSlice] sign panel shapes " + plates.Count);
        }
    }
}
