using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using SurviveI95.World;

namespace SurviveI95.EditorTools
{
    /// <summary>
    /// The roadside swale: the verge shaped like Florida's drainage swales (profile in RoutePath.VergeY), from the edge
    /// of the paved shoulder out about 22 m, wherever the verge is open (not at walls, ramps, bridges or under
    /// overpasses), easing in and out over 40 m. A ditch-bottom inlet every 120 m: a concrete box standing a hand's
    /// width proud of the swale's floor with a dark steel grate on top, which the rain drains into.
    /// </summary>
    public static partial class FloridaSliceBuilder
    {
        static readonly float[] SwaleCols = { 0f, .6f, 1.2f, 2.2f, 3.2f, 4.2f, 4.9f, 5.6f, 6.8f, 8f, 9f, 10.5f, 12f, 14.5f, 17f, 19.5f, 22f };

        static void BuildSwales(GameObject root, RoutePath p, Mats m)
        {
            var old = root.transform.Find("Swales"); if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var g = new GameObject("Swales"); g.transform.SetParent(root.transform);
            var concrete = Lit("InletConcrete", new Color(.62f, .6f, .56f), .1f); concrete.SetFloat("_Cull", 0f);
            var grate = Lit("InletGrate", new Color(.07f, .07f, .065f), .35f); grate.SetFloat("_Cull", 0f);
            MeasureVerge(p);
            var inlets = new Dictionary<(int, int), Bucket>();
            var wet = Lit("SwaleWet", new Color(.13f, .2f, .03f, .42f), 0f);
            wet.SetFloat("_SpecularHighlights", 0f); wet.EnableKeyword("_SPECULARHIGHLIGHTS_OFF"); wet.SetFloat("_EnvironmentReflections", 0f); wet.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            wet.SetTexture("_BaseMap", SaveTexture(WetTexture(), "SwaleWetTex"));
            wet.SetFloat("_Surface", 1f); wet.SetFloat("_Blend", 0f); wet.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            wet.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); wet.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            wet.SetInt("_ZWrite", 0); wet.renderQueue = 2990; wet.SetOverrideTag("RenderType", "Transparent"); wet.SetShaderPassEnabled("ShadowCaster", false);
            EditorUtility.SetDirty(wet); EditorUtility.SetDirty(concrete); EditorUtility.SetDirty(grate);
            const float step = 5f; float km = 0f; int count = 0;
            foreach (int side in new[] { 1, -1 })
            {
                float s0 = -1f;
                for (float s = 0f; s <= p.Length; s += step)
                {
                    bool on = s < p.Length - step && p.SwaleAmount(s, side) > 0f;
                    if (on && s0 < 0f) s0 = s;
                    if (on || s0 < 0f) continue;
                    float a = Mathf.Max(0f, s0 - step), b = s; s0 = -1f;
                    var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>(); int cols = SwaleCols.Length;
                    var wv = new List<Vector3>(); var wuv = new List<Vector2>(); var wt = new List<int>();
                    for (float q = a; q <= b + .01f; q += step)
                    {
                        float k = p.SwaleAmount(q, side), edge = p.PavedEdge(q, side), reach = p.VergeReach(q, side);
                        foreach (float d in SwaleCols)
                        {
                            var at = p.Point(q, edge + side * d); at.y = RoutePath.VergeY(d, k, reach);
                            v.Add(at); uv.Add(new Vector2(at.x / 12f, at.z / 12f));
                        }
                        // The damp bottom, greener than the rest in the dry season: a strip across it laid just above the grass.
                        float sq = Mathf.Min(1f, reach / RoutePath.SwaleReach);
                        for (int w = 0; w <= 4; w++)
                        {
                            float d = (2.4f + 5f * w / 4f) * sq; var at = p.Point(q, edge + side * d); at.y = RoutePath.VergeY(d, k, reach) + .025f;
                            wv.Add(at); wuv.Add(new Vector2(w / 4f, q / 9f));
                        }
                        int wr = wv.Count / 5 - 1;
                        if (wr > 0) for (int c = 0; c < 4; c++) { int i0 = (wr - 1) * 5 + c, j0 = wr * 5 + c; wt.AddRange(side > 0 ? new[] { i0, j0, i0 + 1, i0 + 1, j0, j0 + 1 } : new[] { i0, i0 + 1, j0, i0 + 1, j0 + 1, j0 }); }
                        int r = v.Count / cols - 1;
                        if (r > 0)
                            for (int c = 0; c + 1 < cols; c++)
                            {
                                int i0 = (r - 1) * cols + c, i1 = i0 + 1, j0 = r * cols + c, j1 = j0 + 1;
                                if (side > 0) t.AddRange(new[] { i0, j0, i1, i1, j0, j1 }); else t.AddRange(new[] { i0, i1, j0, i1, j1, j0 });
                            }
                    }
                    km += (b - a) / 1000f;
                    Add(g, "Verge", Finish(v, uv, t), m.grass, true);
                    Add(g, "Damp", Finish(wv, wuv, wt), wet, false);
                }
                // Inlets, staggered so the two sides don't line up.
                for (float s = side > 0 ? 60f : 120f; s < p.Length; s += 120f)
                {
                    if (p.SwaleAmount(s, side) < .99f) continue;
                    float squeeze = Mathf.Min(1f, p.VergeReach(s, side) / RoutePath.SwaleReach);
                    var at = p.Point(s, p.PavedEdge(s, side) + side * RoutePath.SwaleBottom * squeeze);
                    Inlet(Get(inlets, new Vector2(at.x, at.z), 900f, 2), at, p.Tangent(s), p.Right(s));
                    count++;
                }
            }
            if (inlets.Count > 0)
            {
                var holder = Flush(inlets, "Inlets", new[] { concrete, grate }, 2, true);
                var made = GameObject.Find("Inlets"); if (made != null) made.transform.SetParent(g.transform);
            }
            Debug.Log($"[FloridaSlice] swales: {km:F1} km of shaped verge, {count} ditch-bottom inlets");
        }

        /// <summary>
        /// How far the open verge reaches from each paved edge every 5 m, out to the right-of-way fence, stopping 2 m short
        /// of a street, 3 m short of water and 6 m short of an exit ramp. Saved beside the route for the game's grass.
        /// </summary>
        static void MeasureVerge(RoutePath p)
        {
            const float step = 5f, fence = 26f;
            int n = Mathf.FloorToInt(p.Length / step) + 1;
            var file = new RouteVergeFile { step = step, north = new float[n], south = new float[n] };
            for (int i = 0; i < n; i++)
                foreach (int side in new[] { 1, -1 })
                {
                    float s = i * step, edge = p.PavedEdge(s, side), limit = fence - (side > 0 ? 3.05f : 1.3f), reach = limit;
                    for (float d = 1f; d < limit; d += 1f)
                    {
                        var c = p.Point(s, edge + side * d); var q = new Vector2(c.x, c.z);
                        float sd = StreetDistance(q, 14f, out _, out var w);
                        if (sd < w / 2f + 2f || InWater(q) || NearExit(q, 6f)) { reach = Mathf.Max(0f, d - 3f); break; }
                    }
                    (side > 0 ? file.north : file.south)[i] = reach;
                }
            p.Verge = file;
            string path = $"Assets/Resources/Data/Routes/{RouteId}_verge.json";
            System.IO.File.WriteAllText(path, JsonUtility.ToJson(file));
            AssetDatabase.ImportAsset(path);
            int narrow = file.north.Concat(file.south).Count(x => x < RoutePath.SwaleMinReach);
            Debug.Log($"[FloridaSlice] verge: {narrow} of {2 * n} spots too narrow for a swale (street, water or ramp close by)");
        }

        /// <summary>Across the strip: strongest along the middle, feathered to nothing at both edges, broken into patches along it.</summary>
        static Texture2D WetTexture()
        {
            const int W = 32, H = 128; var px = new Color32[W * H]; var r = new System.Random(31);
            var along = new float[H + 1]; for (int i = 0; i <= H; i += 8) along[i] = .45f + (float)r.NextDouble() * .55f;
            for (int i = 0; i < H; i++) if (i % 8 != 0) { int a = i / 8 * 8, b = Mathf.Min(H, a + 8); along[i] = Mathf.Lerp(along[a], along[b == H ? 0 : b], (i - a) / 8f); }
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = (x + .5f) / W, across = Mathf.Sin(u * Mathf.PI); across *= across;
                    float a = across * along[y] * (.75f + (float)r.NextDouble() * .25f);
                    px[y * W + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
                }
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, true); tex.SetPixels32(px); tex.Apply();
            return tex;
        }

        /// <summary>A concrete box 1.0 m along the road by 1.3 m across, its top 13 cm above the swale floor, with a grate.</summary>
        static void Inlet(Bucket bk, Vector3 at, Vector3 fwd, Vector3 right)
        {
            float y0 = -.4f, y1 = -.08f, hx = .65f, hz = .5f;
            Vector3 C(float x, float z, float y) { var c = at + right * x + fwd * z; c.y = y; return c; }
            var corners = new[] { (-hx, -hz), (hx, -hz), (hx, hz), (-hx, hz) };
            for (int i = 0; i < 4; i++)
            {
                var (x0, z0) = corners[i]; var (x1, z1) = corners[(i + 1) % 4];
                ScreenQuad(bk, 0, C(x0, z0, y0), C(x1, z1, y0), C(x1, z1, y1), C(x0, z0, y1));
            }
            // The rim of the box, then the grate set into it.
            float rim = .1f;
            ScreenQuad(bk, 0, C(-hx, -hz, y1), C(hx, -hz, y1), C(hx, -hz + rim, y1), C(-hx, -hz + rim, y1));
            ScreenQuad(bk, 0, C(-hx, hz - rim, y1), C(hx, hz - rim, y1), C(hx, hz, y1), C(-hx, hz, y1));
            ScreenQuad(bk, 0, C(-hx, -hz + rim, y1), C(-hx + rim, -hz + rim, y1), C(-hx + rim, hz - rim, y1), C(-hx, hz - rim, y1));
            ScreenQuad(bk, 0, C(hx - rim, -hz + rim, y1), C(hx, -hz + rim, y1), C(hx, hz - rim, y1), C(hx - rim, hz - rim, y1));
            ScreenQuad(bk, 1, C(-hx + rim, -hz + rim, y1 - .02f), C(hx - rim, -hz + rim, y1 - .02f), C(hx - rim, hz - rim, y1 - .02f), C(-hx + rim, hz - rim, y1 - .02f));
        }
    }
}
