using System.Collections.Generic;
using System.IO;
using SurviveI95.World;
using UnityEditor;
using UnityEngine;

namespace SurviveI95.EditorTools
{
    /// <summary>
    /// The road's wear, made here so it never repeats the way one photo of asphalt does. A wear layer covers four lanes
    /// and 150 m of road: darker tire tracks in each lane, an oil streak down the middle, the seams between paving
    /// passes, patched repairs and tar-sealed cracks. Paint is worn too, with asphalt showing through in spots.
    /// </summary>
    public static partial class FloridaSliceBuilder
    {
        const float WearLanes = 4f, WearLength = 150f, LaneW = 3.66f;

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xffffff) / 16777215f;
            }
        }

        // Smooth value noise that repeats every px cells across and py cells along, so the texture tiles without a seam.
        static float Noise(float x, float y, int px, int py, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0; fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            int Wx(int i) => ((i % px) + px) % px; int Wy(int i) => ((i % py) + py) % py;
            float a = Hash(Wx(x0), Wy(y0), seed), b = Hash(Wx(x0 + 1), Wy(y0), seed), c = Hash(Wx(x0), Wy(y0 + 1), seed), d = Hash(Wx(x0 + 1), Wy(y0 + 1), seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static Texture2D SaveTexture(Texture2D tex, string name, bool srgb = true)
        {
            string file = $"{Gen}/Materials/{name}.png";
            Directory.CreateDirectory(Gen + "/Materials");
            File.WriteAllBytes(file, tex.EncodeToPNG());
            AssetDatabase.ImportAsset(file);
            if (AssetImporter.GetAtPath(file) is TextureImporter ti)
            {
                ti.anisoLevel = 8; ti.sRGBTexture = srgb; ti.wrapMode = TextureWrapMode.Repeat; ti.mipmapEnabled = true;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(file);
        }

        /// <summary>The wear layer. Mid grey changes nothing; darker and lighter tint the asphalt photo underneath.</summary>
        static Texture2D MakeLaneWear()
        {
            const int W = 512, H = 2048;
            float pxU = W / (WearLanes * LaneW), pxV = H / WearLength; // pixels per meter across and along
            var val = new float[W * H];
            var rnd = new System.Random(95);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);

            // Repairs: rectangles of newer (darker) or older (paler) asphalt, inside one lane or across its tire tracks.
            var patches = new List<(float u0, float u1, float v0, float v1, float tone)>();
            for (int i = 0; i < 9; i++)
            {
                int lane = rnd.Next(4); float w = R(1.4f, 3.3f), len = R(2.5f, 11f);
                float u0 = lane * LaneW + R(.1f, LaneW - w - .1f), v0 = R(0f, WearLength);
                patches.Add((u0, u0 + w, v0, v0 + len, rnd.NextDouble() < .7 ? R(.8f, .88f) : R(1.04f, 1.09f)));
            }

            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = (x + .5f) / pxU, v = (y + .5f) / pxV;
                    int lane = Mathf.Min(3, (int)(u / LaneW)); float lx = u - lane * LaneW - LaneW / 2f;
                    float k = 1f;
                    // Broad blotches, so the 7 m asphalt photo doesn't read as a pattern.
                    k *= 1f + (Noise(x / (float)W * 4f, y / (float)H * 25f, 4, 25, 1) - .5f) * .16f + (Noise(x / (float)W * 12f, y / (float)H * 75f, 12, 75, 2) - .5f) * .07f;
                    // Tire tracks: two darker bands per lane, about 0.9 m either side of its middle, fading in and out along the road.
                    float along = .65f + .7f * Noise(lane * 3.1f, y / (float)H * 30f, 64, 30, 3);
                    float tracks = Mathf.Exp(-Mathf.Pow((lx - .9f) / .36f, 2f)) + Mathf.Exp(-Mathf.Pow((lx + .9f) / .36f, 2f));
                    k *= 1f - .075f * tracks * along;
                    // Oil and drips down the middle of the lane, patchy.
                    float drip = Mathf.Exp(-Mathf.Pow(lx / .28f, 2f)) * Mathf.Clamp01((Noise(x / (float)W * 40f, y / (float)H * 260f, 40, 260, 4) - .35f) * 2.2f);
                    k *= 1f - .07f * drip;
                    // The seam where two paving passes meet, at each lane line, with gaps.
                    float toSeam = LaneW / 2f - Mathf.Abs(lx);
                    if (toSeam < .035f && Noise(lane, y / (float)H * 120f, 8, 120, 5) > .3f) k *= .86f;
                    // Repairs, with a dark sealed edge.
                    foreach (var p in patches)
                    {
                        float vv = v; if (vv < p.v0) vv += WearLength; // wraps around the end of the texture
                        if (u < p.u0 || u > p.u1 || vv < p.v0 || vv > p.v1) continue;
                        float edge = Mathf.Min(Mathf.Min(u - p.u0, p.u1 - u), Mathf.Min(vv - p.v0, p.v1 - vv));
                        k = k / Mathf.Max(.9f, 1f - .075f * tracks) * p.tone; // a fresh patch hasn't worn tracks in yet
                        if (edge < .05f) k *= .72f;
                    }
                    // Fine grain.
                    k *= 1f + (Hash(x, y, 6) - .5f) * .03f;
                    val[y * W + x] = k;
                }

            // Tar-sealed cracks: thin glossy black lines, across a lane every so often and along the seams.
            void Dab(float u, float v)
            {
                int cx = Mathf.RoundToInt(u * pxU), cy = Mathf.RoundToInt(v * pxV);
                for (int dx = -1; dx <= 1; dx++)
                {
                    int xx = ((cx + dx) % W + W) % W, yy = ((cy % H) + H) % H;
                    val[yy * W + xx] *= dx == 0 ? .5f : .74f;
                }
            }
            for (int i = 0; i < 26; i++)
            {
                int lane = rnd.Next(4); float v = R(0f, WearLength), u = lane * LaneW + R(0f, .4f), end = lane * LaneW + LaneW - R(0f, .4f);
                if (rnd.NextDouble() < .35) end = u + R(.8f, 2.2f); // part way across
                for (; u < end; u += 1f / pxU) { v += R(-.02f, .02f) + .004f; Dab(u, v); }
            }
            for (int i = 0; i < 7; i++)
            {
                float u = Mathf.Floor(R(1f, 4f)) * LaneW + R(-.15f, .15f), v = R(0f, WearLength), len = R(6f, 30f);
                for (float d = 0f; d < len; d += 1f / pxV) { u += R(-.012f, .012f); Dab(u, v + d); }
            }

            var tex = new Texture2D(W, H, TextureFormat.RGBA32, true);
            var px = new Color32[W * H];
            for (int i = 0; i < px.Length; i++) { byte g = (byte)Mathf.Clamp(Mathf.RoundToInt(137f * val[i]), 0, 255); /* above mid grey, so on average the wear doesn't darken the road */ px[i] = new Color32(g, g, g, 255); }
            tex.SetPixels32(px); tex.Apply();
            return SaveTexture(tex, "LaneWear");
        }

        /// <summary>Worn road paint, one meter square: white with spots where the asphalt shows through, and a little grime.</summary>
        static Texture2D MakePaintWear()
        {
            const int N = 256;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true);
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float big = Noise(x / 16f, y / 16f, N / 16, N / 16, 11), small = Noise(x / 4f, y / 4f, N / 4, N / 4, 12);
                    float worn = big * .6f + small * .4f;
                    float k = worn < .24f ? .38f : Mathf.Lerp(.38f, 1f, Mathf.Clamp01((worn - .24f) / .05f)); // asphalt showing through
                    k *= .9f + .1f * Noise(x / 64f, y / 64f, 4, 4, 13);
                    byte g = (byte)Mathf.RoundToInt(255f * k);
                    px[y * N + x] = new Color32(g, g, g, 255);
                }
            tex.SetPixels32(px); tex.Apply();
            return SaveTexture(tex, "PaintWear");
        }

        // ---------------------------------------------------------------- guardrail

        // A W-beam rail in cross-section, x outward from traffic and y up: 0.31 m tall with two ridges facing the road,
        // its top at 0.79 m (the US crash-tested height).
        static readonly Vector2[] RailProfile = { new Vector2(.05f, .48f), new Vector2(0f, .53f), new Vector2(.08f, .63f), new Vector2(0f, .73f), new Vector2(.05f, .79f) };
        const float PostSpacing = 1.905f; // 6 ft 3 in

        static void Cuboid(List<Vector3> v, List<Vector2> uv, List<int> t, Vector3 c, Quaternion r, Vector3 size)
        {
            var h = size / 2f;
            Vector3[] n = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var f in n)
            {
                var a = f.x != 0 ? Vector3.up : Vector3.right; var b = Vector3.Cross(f, a);
                int k = v.Count;
                foreach (var (i, j) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
                {
                    var local = Vector3.Scale(f + a * i + b * j, h);
                    v.Add(c + r * local); uv.Add(new Vector2((i + 1) / 2, (j + 1) / 2));
                }
                t.AddRange(new[] { k, k + 1, k + 2, k, k + 2, k + 3 }); // clockwise seen from outside, Unity's front
            }
        }

        static Texture2D MakeHazardMarker()
        {
            const int N = 64; var tex = new Texture2D(N, N, TextureFormat.RGBA32, true);
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
                tex.SetPixel(x, y, ((x + y) / 11) % 2 == 0 ? new Color(1f, .78f, 0f) : new Color(.03f, .03f, .03f));
            tex.Apply();
            return SaveTexture(tex, "HazardMarker");
        }

        const float RailPanel = 3.81f; // a standard 12 ft 6 in W-beam panel

        /// <summary>
        /// Galvanized W-beam over two panels (7.62 m by the rail's 0.31 m height): zinc spangle, the lap splice with
        /// its bolts at each panel end, a bolt at each post, road grime along the bottom, rain streaks, white oxide
        /// bloom, a few rust spots and black tire scuffs on the ridges that face traffic.
        /// </summary>
        static Texture2D MakeGuardrailTex()
        {
            const int W = 2048, H = 160; float pxm = W / (RailPanel * 2f);
            var px = new Color[W * H];
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
            {
                // Spangle: irregular crystals of slightly different brightness.
                int cx = x / 14, cy = y / 14; float best = 9e9f, id = 0;
                for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++)
                {
                    int gx = cx + i, gy = cy + j; float ox = (gx + Hash(gx, gy, 501)) * 14f, oy = (gy + Hash(gx, gy, 502)) * 14f;
                    float d = (ox - x) * (ox - x) * .6f + (oy - y) * (oy - y);
                    if (d < best) { best = d; id = Hash(((gx % (W / 14)) + W / 14) % (W / 14), gy, 503); }
                }
                float v = .66f + (id - .5f) * .08f + (Noise(x / 60f, y / 30f, W / 60 + 1, 6, 504) - .5f) * .08f;
                var c = new Color(v, v * 1.01f, v * 1.03f);
                float k = y / (float)H;
                c *= Mathf.Lerp(.72f, 1f, Mathf.SmoothStep(0f, .35f, k));                                  // road grime at the foot
                float streak = Noise(x / 7f, y / 90f, W / 7 + 1, 3, 505); if (streak > .62f) c *= 1f - (streak - .62f) * .5f;  // rain streaks
                float bloom = Noise(x / 40f, y / 20f, W / 40 + 1, 9, 506); if (bloom > .72f) c = Color.Lerp(c, new Color(.82f, .83f, .82f), (bloom - .72f) * 1.6f);
                float rust = Noise(x / 9f, y / 9f, W / 9 + 1, 19, 507); if (rust > .83f) c = Color.Lerp(c, new Color(.42f, .27f, .17f), (rust - .83f) * 4f);
                c.a = 1f; px[y * W + x] = c;
            }
            void Bolt(float bx, float by)
            {
                for (int y = -6; y <= 6; y++) for (int x = -6; x <= 6; x++)
                {
                    int X = Mathf.RoundToInt(bx) + x, Y = Mathf.RoundToInt(by) + y; if (Y < 0 || Y >= H) continue;
                    float r = Mathf.Sqrt(x * x + y * y); int i = Y * W + ((X % W) + W) % W;
                    if (r < 4.5f) px[i] = new Color(.5f, .5f, .5f) * (1f - y * .04f);
                    else if (r < 6.5f) px[i] *= .75f;
                }
            }
            for (int panel = 0; panel < 2; panel++)
            {
                float x0 = panel * RailPanel * pxm;
                // Lap splice: the overlap edge and eight bolts in two columns.
                for (int y = 0; y < H; y++) for (int x = 0; x < 4; x++) px[y * W + ((Mathf.RoundToInt(x0) + x) % W)] *= .55f;
                foreach (float bx in new[] { x0 + 28f, x0 + 60f }) foreach (float by in new[] { H * .14f, H * .36f, H * .64f, H * .86f }) Bolt(bx, by);
                Bolt(x0 + RailPanel * pxm / 2f, H * .5f); Bolt(x0 + 44f, H * .5f);
            }
            // Tire scuffs where cars have brushed the ridges.
            var rnd = new System.Random(77);
            for (int n = 0; n < 5; n++)
            {
                float sx = (float)rnd.NextDouble() * W, len = 80f + (float)rnd.NextDouble() * 260f, sy = rnd.NextDouble() < .5 ? H * .58f : H * .9f;
                for (int x = 0; x < len; x++) for (int y = -4; y <= 4; y++)
                {
                    int X = (int)(sx + x) % W, Y = (int)(sy + y + Mathf.Sin(x * .05f) * 2f); if (Y < 0 || Y >= H) continue;
                    float a = (1f - Mathf.Abs(y) / 5f) * Mathf.Min(1f, Mathf.Min(x, len - x) / 30f) * .7f;
                    px[Y * W + X] = Color.Lerp(px[Y * W + X], new Color(.08f, .08f, .08f), a);
                }
            }
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, true); tex.SetPixels(px); tex.Apply();
            return SaveTexture(tex, "GuardrailSteel");
        }

        static void BuildGuardrails(GameObject parent, RoutePath p)
        {
            var spans = new List<(float a, float b)>(); float start = -1f;
            for (float s = 0f; s < p.Length; s += RoutePath.RailStep)
            {
                bool on = p.HasGuardrail(s);
                if (on && start < 0f) start = s;
                if (!on && start >= 0f) { spans.Add((start, s)); start = -1f; }
            }
            var steel = Lit("Guardrail", Color.white, .38f); steel.SetFloat("_Metallic", .5f); steel.SetTexture("_BaseMap", MakeGuardrailTex());
            var postSteel = Lit("GuardrailPost", new Color(.36f, .37f, .37f), .22f); postSteel.SetFloat("_Metallic", .4f);
            var marker = Lit("HazardMarker", Color.white, .3f); marker.SetTexture("_BaseMap", MakeHazardMarker());

            var rv = new List<Vector3>(); var ruv = new List<Vector2>(); var rt = new List<int>();
            var pv = new List<Vector3>(); var puv = new List<Vector2>(); var pt = new List<int>();
            var mv = new List<Vector3>(); var muv = new List<Vector2>(); var mt = new List<int>();
            float Lat(float s) => p.HalfWidth(s) + RoutePath.BarrierRight - .15f;
            foreach (var (a, b) in spans)
            {
                // Traffic meets the upstream end first: open roadside there gets the terminal head. A downstream end
                // either joins concrete or stops with a small cap.
                bool head = !p.HasConcrete(a - RoutePath.RailStep), cap = !p.HasConcrete(b);
                // The rail: both faces of the W, so it reads from either side.
                int rows = Mathf.Max(1, Mathf.CeilToInt((b - a) / 2.5f));
                for (int side = 0; side < 2; side++)
                {
                    int first = rv.Count;
                    for (int r = 0; r <= rows; r++)
                    {
                        float s = a + (b - a) * r / rows;
                        foreach (var q in RailProfile) { rv.Add(p.Point(s, Lat(s) + q.x + side * .004f) + Vector3.up * q.y); ruv.Add(new Vector2(s / (RailPanel * 2f), (q.y - .48f) / .31f)); }
                        if (r == 0) continue;
                        int prev = first + (r - 1) * RailProfile.Length, cur = first + r * RailProfile.Length;
                        for (int k = 0; k + 1 < RailProfile.Length; k++)
                        {
                            if (side == 0) rt.AddRange(new[] { prev + k, cur + k, prev + k + 1, prev + k + 1, cur + k, cur + k + 1 });
                            else rt.AddRange(new[] { prev + k, prev + k + 1, cur + k, prev + k + 1, cur + k + 1, cur + k });
                        }
                    }
                }
                // Posts with a spacer block between post and rail.
                for (float s = a + .4f; s < b; s += PostSpacing)
                {
                    var rot = Quaternion.LookRotation(p.Tangent(s), Vector3.up);
                    var at = p.Point(s, Lat(s));
                    var right = rot * Vector3.right;
                    Cuboid(pv, puv, pt, at + right * .37f + Vector3.up * .3f, rot, new Vector3(.15f, 1.2f, .15f));
                    Cuboid(pv, puv, pt, at + right * .19f + Vector3.up * .6f, rot, new Vector3(.2f, .36f, .14f));
                }
                // The open end: an energy-absorbing terminal head facing traffic, with a yellow and black marker.
                if (head)
                {
                    var er = Quaternion.LookRotation(p.Tangent(a), Vector3.up); var ep = p.Point(a, Lat(a) + .02f);
                    Cuboid(pv, puv, pt, ep + Vector3.up * .63f - er * Vector3.forward * .3f, er, new Vector3(.28f, .5f, .6f));
                    Cuboid(mv, muv, mt, ep + Vector3.up * .63f - er * Vector3.forward * .61f, er, new Vector3(.3f, .48f, .01f));
                }
                if (cap)
                {
                    var er = Quaternion.LookRotation(p.Tangent(b), Vector3.up); var ep = p.Point(b, Lat(b) + .02f);
                    Cuboid(pv, puv, pt, ep + Vector3.up * .63f + er * Vector3.forward * .1f, er, new Vector3(.12f, .32f, .2f)); // end cap
                }
            }
            Add(parent, "Guardrail", Finish(rv, ruv, rt), steel, true);
            Add(parent, "GuardrailPosts", Finish(pv, puv, pt), postSteel, true);
            Add(parent, "GuardrailMarkers", Finish(mv, muv, mt), marker, false);
            Debug.Log("[FloridaSlice] guardrail spans " + spans.Count + ": " + string.Join(" ", spans.ConvertAll(x => $"{x.a:F0}-{x.b:F0}")));
        }

        /// <summary>Puts the wear on the road materials in place, so the scene's references to them stay good.</summary>
        static void DressRoad(Mats m)
        {
            var wear = MakeLaneWear();
            m.asphalt.SetTexture("_DetailAlbedoMap", wear);
            // Lane UVs run 1 per 7 m from the left edge of the lanes; the wear layer covers 4 lanes by 150 m.
            m.asphalt.SetTextureScale("_DetailAlbedoMap", new Vector2(7f / (WearLanes * LaneW), 7f / WearLength));
            m.asphalt.SetFloat("_DetailAlbedoMapScale", 1f);
            m.asphalt.EnableKeyword("_DETAIL_MULX2");
            // Road paint has its own materials: sign faces share the plain white and yellow and must stay clean.
            var paint = MakePaintWear();
            if (m.roadWhite == null) m.roadWhite = Lit("RoadPaintWhite", new Color(.95f, .95f, .92f), .3f);
            if (m.roadYellow == null) m.roadYellow = Lit("RoadPaintYellow", new Color(.95f, .72f, .1f), .3f);
            foreach (var mat in new[] { m.roadWhite, m.roadYellow }) { mat.SetTexture("_BaseMap", paint); EditorUtility.SetDirty(mat); }
            foreach (var mat in new[] { m.white, m.yellow }) { mat.SetTexture("_BaseMap", null); EditorUtility.SetDirty(mat); } // plain, as they were made
            EditorUtility.SetDirty(m.asphalt);
            DressLawn(m.grass);
        }

        /// <summary>
        /// South Florida grass in the dry season (the game starts in March): St. Augustine and Bahia, greener where it's
        /// watered and shaded, straw-colored patches where it isn't, and mottled at every scale. A wear layer over the grass
        /// photo covering 192 m, so the ground stops looking like one carpet.
        /// </summary>
        static void DressLawn(Material grass)
        {
            if (grass == null) return;
            const int S = 1024;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, true);
            var px = new Color[S * S];
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
            {
                float big = Noise(x / 128f, y / 128f, 8, 8, 301), mid = Noise(x / 40f, y / 40f, 26, 26, 302) - .5f, fine = Noise(x / 9f, y / 9f, 114, 114, 303) - .5f;
                float dry = Mathf.Clamp01((big - .52f) * 3.2f + mid * .7f);
                float lush = Mathf.Clamp01((.4f - big) * 3f - mid * .5f);
                var c = new Color(.5f, .5f, .5f);
                c = Color.Lerp(c, new Color(.66f, .6f, .4f), dry * .85f);           // straw
                c = Color.Lerp(c, new Color(.4f, .47f, .38f), lush * .7f);          // dark, watered
                float k = 1f + fine * .16f + mid * .1f; c *= k;
                if (Hash(x / 6, y / 6, 304) > .985f) c = Color.Lerp(c, new Color(.6f, .55f, .45f), .6f); // bare spots
                c.a = 1f; px[y * S + x] = c;
            }
            tex.SetPixels(px); tex.Apply();
            var t = SaveTexture(tex, "LawnWear");
            grass.SetTexture("_DetailAlbedoMap", t);
            // The grass photo repeats every 12 m; the wear layer covers 16 of those.
            grass.SetTextureScale("_DetailAlbedoMap", new Vector2(1f / 16f, 1f / 16f));
            grass.SetFloat("_DetailAlbedoMapScale", 1f);
            grass.EnableKeyword("_DETAIL_MULX2");
            grass.SetColor("_BaseColor", new Color(.74f, .86f, .55f));
            EditorUtility.SetDirty(grass);
        }
    }
}
