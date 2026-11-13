using System;
using System.Collections.Generic;
using SurviveI95.World;
using UnityEditor;
using UnityEngine;

namespace SurviveI95.EditorTools
{
    /// <summary>
    /// Fences. Florida fences its limited-access highways along the property line, so I-95 has a chain-link fence
    /// beyond the clear zone wherever there's no sound wall, broken at exits and streets. Back yards are fenced too:
    /// chain-link (most common in older Miami blocks) or a 6 ft wood privacy fence, behind about 4 houses in 10. Many of
    /// the rest have a tall clipped ficus hedge down the side lot lines and across the front corners instead.
    /// </summary>
    public static partial class FloridaSliceBuilder
    {
        const int FFabric = 0, FMetal = 1, FWood = 2, FHedge = 3, FenceSlots = 4;

        static Texture2D MakeChainLink()
        {
            // 0.5 m of fabric: diamonds about 5 cm across, galvanized wire.
            const int S = 256; var px = new Color[S * S]; const float cell = S / 10f;
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
            {
                float u = (x + y) / cell, v = (x - y + S) / cell;
                float du = Mathf.Abs(u - Mathf.Round(u)), dv = Mathf.Abs(v - Mathf.Round(v));
                bool wire = du < .09f || dv < .09f;
                var c = new Color(.66f, .68f, .68f) * (.9f + Hash(x, y, 71) * .15f); c.a = wire ? 1f : 0f;
                px[y * S + x] = c;
            }
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, true); tex.SetPixels(px); tex.Apply();
            var t = SaveTexture(tex, "ChainLink");
            if (AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t)) is TextureImporter ti) { ti.alphaIsTransparency = true; ti.mipMapsPreserveCoverage = true; ti.alphaTestReferenceValue = .4f; ti.SaveAndReimport(); }
            return t;
        }

        static Texture2D MakeWoodFence()
        {
            // 1.8 m of fence: pressure-treated pine boards gone silver-brown in the sun, with the rails' nail lines.
            const int W = 512, H = 512; var px = new Color[W * H]; float board = W / 13f;
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
            {
                int b = (int)(x / board); float fx = x / board - b;
                var c = Color.Lerp(new Color(.55f, .47f, .38f), new Color(.66f, .62f, .55f), Hash(b, 0, 81));
                c *= .9f + (Noise(x / 3f, y / 60f, W / 3, 9, 82) - .5f) * .25f;
                if (fx < .05f) c *= .45f;
                if (Mathf.Abs(y - H * .15f) < 3 || Mathf.Abs(y - H * .82f) < 3) c *= .7f;
                c.a = 1f; px[y * W + x] = c;
            }
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, true); tex.SetPixels(px); tex.Apply();
            return SaveTexture(tex, "WoodFence");
        }

        /// <summary>A run of fence from a to b on the ground: fabric or boards, with posts every 3 m and a top rail.</summary>
        static void FenceRun(Bucket bk, Vector2 a, Vector2 b, float h, bool wood)
        {
            float len = (b - a).magnitude; if (len < .5f) return;
            Vector3 P(Vector2 q, float y) => new Vector3(q.x, y, q.y);
            float y0 = -.25f;
            int slot = wood ? FWood : FFabric; var v = bk.v[slot]; var uv = bk.uv[slot]; int n = v.Count;
            v.Add(P(a, y0)); v.Add(P(a, y0 + h)); v.Add(P(b, y0)); v.Add(P(b, y0 + h));
            float uw = wood ? len / 1.8f : len / .5f, vh = wood ? 1f : h / .5f;
            uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(0, vh)); uv.Add(new Vector2(uw, 0)); uv.Add(new Vector2(uw, vh));
            bk.t[slot].AddRange(new[] { n, n + 1, n + 2, n + 2, n + 1, n + 3 });
            if (wood) return;
            var dir = (b - a) / len; var rot = Quaternion.LookRotation(new Vector3(dir.x, 0, dir.y));
            for (float d = 0; d <= len + .01f; d += 3f)
            {
                var q = a + dir * Mathf.Min(d, len);
                Cuboid(bk.v[FMetal], bk.uv[FMetal], bk.t[FMetal], P(q, y0 + h / 2f + .05f), rot, new Vector3(.06f, h + .1f, .06f));
            }
            Cuboid(bk.v[FMetal], bk.uv[FMetal], bk.t[FMetal], P((a + b) / 2f, y0 + h), rot, new Vector3(.04f, .04f, len));
        }

        static Texture2D MakeHedge(out Texture2D normal)
        {
            // 1.6 m of clipped ficus: thousands of small glossy leaves, light on their sunny edge, in deep shade between.
            const int S = 512; var px = new Color[S * S];
            for (int i = 0; i < px.Length; i++) { float n = Hash(i % S, i / S, 5); px[i] = new Color(.02f + n * .01f, .05f + n * .02f, .015f, 1f); }
            var r = new System.Random(808);
            for (int k = 0; k < 5200; k++)
            {
                float cx = (float)r.NextDouble() * S, cy = (float)r.NextDouble() * S, ang = (float)r.NextDouble() * Mathf.PI;
                float len = 9f + (float)r.NextDouble() * 6f, wid = len * .45f, tone = (float)r.NextDouble();
                // Clumps: whole patches of the hedge sit in shade between the sunny bulges.
                float clump = .55f + .45f * Mathf.Sin(cx * .031f + Mathf.Sin(cy * .023f) * 2f) * Mathf.Sin(cy * .037f + 1.3f);
                var baseC = Color.Lerp(new Color(.07f, .13f, .03f), new Color(.19f, .31f, .07f), tone) * (.55f + .6f * clump);
                float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
                for (int dy = -15; dy <= 15; dy++) for (int dx = -15; dx <= 15; dx++)
                {
                    float lx = dx * ca + dy * sa, ly = -dx * sa + dy * ca;
                    float e = (lx * lx) / (len * len) + (ly * ly) / (wid * wid); if (e > 1f) continue;
                    // Glossy: brighter toward the upper edge of each leaf, a pale midrib.
                    float lit = .8f + .45f * Mathf.Clamp01(-ly / wid + .3f) + (Mathf.Abs(ly) < .6f ? .15f : 0f);
                    int x = ((int)cx + dx + S) % S, y = ((int)cy + dy + S) % S;
                    var c = baseC * lit; c.a = 1f; px[y * S + x] = c;
                }
            }
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, true); tex.SetPixels(px); tex.Apply();
            // Bumps from the leaves themselves: bright leaves stand out, the dark gaps between sink in.
            var np = new Color[S * S];
            float H(int x, int y) { var c = px[((y + S) % S) * S + (x + S) % S]; return c.r + c.g + c.b; }
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
            {
                float dx = (H(x + 1, y) - H(x - 1, y)) * 4f, dy = (H(x, y + 1) - H(x, y - 1)) * 4f;
                var n = new Vector3(-dx, -dy, 1f).normalized;
                np[y * S + x] = new Color(n.x * .5f + .5f, n.y * .5f + .5f, n.z * .5f + .5f, 1f);
            }
            var nt = new Texture2D(S, S, TextureFormat.RGBA32, true); nt.SetPixels(np); nt.Apply();
            normal = SaveTexture(nt, "HedgeNormal", false);
            if (AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(normal)) is TextureImporter ni) { ni.textureType = TextureImporterType.NormalMap; ni.SaveAndReimport(); }
            normal = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GetAssetPath(normal));
            return SaveTexture(tex, "Hedge");
        }

        /// <summary>A clipped hedge from a to b: a box about 0.9 m deep with a slightly uneven top and sides.</summary>
        static void HedgeRun(Bucket bk, Vector2 a, Vector2 b, float h, System.Random r)
        {
            float len = (b - a).magnitude; if (len < 1f) return;
            var dir = (b - a) / len; var nrm = new Vector2(-dir.y, dir.x);
            var v = bk.v[FHedge]; var uv = bk.uv[FHedge]; var t = bk.t[FHedge];
            int steps = Mathf.Max(1, Mathf.RoundToInt(len)); float y0 = -.25f;
            var top = new float[steps + 1]; var half = new float[steps + 1];
            for (int i = 0; i <= steps; i++) { top[i] = h + (float)(r.NextDouble() - .5) * .25f; half[i] = .45f + (float)(r.NextDouble() - .5) * .14f; }
            Vector3 P(Vector2 q, float y) => new Vector3(q.x, y, q.y);
            for (int i = 0; i < steps; i++)
            {
                float d0 = len * i / steps, d1 = len * (i + 1) / steps;
                var q0 = a + dir * d0; var q1 = a + dir * d1;
                foreach (int sd in new[] { 1, -1 })
                {
                    // Side faces, bellying out a little at mid height.
                    var o0 = nrm * sd * half[i]; var o1 = nrm * sd * half[i + 1];
                    int n = v.Count;
                    v.Add(P(q0 + o0 * .92f, y0)); v.Add(P(q1 + o1 * .92f, y0)); v.Add(P(q0 + o0, top[i] * .55f)); v.Add(P(q1 + o1, top[i + 1] * .55f)); v.Add(P(q0 + o0 * .85f, top[i])); v.Add(P(q1 + o1 * .85f, top[i + 1]));
                    uv.Add(new Vector2(d0, 0)); uv.Add(new Vector2(d1, 0)); uv.Add(new Vector2(d0, top[i] * .55f)); uv.Add(new Vector2(d1, top[i + 1] * .55f)); uv.Add(new Vector2(d0, top[i])); uv.Add(new Vector2(d1, top[i + 1]));
                    if (sd > 0) t.AddRange(new[] { n, n + 2, n + 1, n + 1, n + 2, n + 3, n + 2, n + 4, n + 3, n + 3, n + 4, n + 5 });
                    else t.AddRange(new[] { n, n + 1, n + 2, n + 1, n + 3, n + 2, n + 2, n + 3, n + 4, n + 3, n + 5, n + 4 });
                }
                // Top, rounded over a little.
                int m = v.Count;
                var l0 = nrm * half[i] * .85f; var l1 = nrm * half[i + 1] * .85f;
                v.Add(P(q0 + l0, top[i])); v.Add(P(q1 + l1, top[i + 1])); v.Add(P(q0, top[i] + .08f)); v.Add(P(q1, top[i + 1] + .08f)); v.Add(P(q0 - l0, top[i])); v.Add(P(q1 - l1, top[i + 1]));
                uv.Add(new Vector2(d0, 0)); uv.Add(new Vector2(d1, 0)); uv.Add(new Vector2(d0, .45f)); uv.Add(new Vector2(d1, .45f)); uv.Add(new Vector2(d0, .9f)); uv.Add(new Vector2(d1, .9f));
                t.AddRange(new[] { m, m + 1, m + 2, m + 1, m + 3, m + 2, m + 2, m + 3, m + 4, m + 3, m + 5, m + 4 });
            }
            // Ends.
            foreach (var (q, i, sgn) in new[] { (a, 0, -1), (b, steps, 1) })
            {
                var o = nrm * half[i]; int n = v.Count;
                v.Add(P(q + o * .92f, y0)); v.Add(P(q - o * .92f, y0)); v.Add(P(q + o * .85f, top[i])); v.Add(P(q - o * .85f, top[i]));
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(.9f, 0)); uv.Add(new Vector2(0, top[i])); uv.Add(new Vector2(.9f, top[i]));
                if (sgn > 0) t.AddRange(new[] { n, n + 2, n + 1, n + 1, n + 2, n + 3 }); else t.AddRange(new[] { n, n + 1, n + 2, n + 1, n + 3, n + 2 });
            }
        }

        static void BuildFences(RoutePath p, Func<float, float> south)
        {
            var old = GameObject.Find("Fences"); if (old != null) UnityEngine.Object.DestroyImmediate(old);
            var fabric = Lit("ChainLinkFabric", Color.white, .4f);
            fabric.SetTexture("_BaseMap", MakeChainLink()); fabric.SetFloat("_Metallic", .4f);
            fabric.SetFloat("_AlphaClip", 1f); fabric.SetFloat("_Cutoff", .4f); fabric.EnableKeyword("_ALPHATEST_ON"); fabric.SetFloat("_Cull", 0f); fabric.renderQueue = 2450;
            var metal = Lit("FencePost", new Color(.6f, .62f, .62f), .4f); metal.SetFloat("_Metallic", .5f);
            var wood = Lit("WoodFence", Color.white, .1f); wood.SetTexture("_BaseMap", MakeWoodFence()); wood.SetFloat("_Cull", 0f);
            var hedge = Lit("Hedge", new Color(1.6f, 1.6f, 1.15f), .25f); hedge.SetTexture("_BaseMap", MakeHedge(out var hedgeN)); hedge.SetTexture("_BumpMap", hedgeN); hedge.SetFloat("_BumpScale", 1.4f); hedge.EnableKeyword("_NORMALMAP"); hedge.SetTextureScale("_BaseMap", new Vector2(1f / 1.6f, 1f / 1.6f)); hedge.SetFloat("_Cull", 0f);
            var map = new Dictionary<(int, int), Bucket>();
            int highway = 0, yardsFenced = 0, hedged = 0;
            // The highway fence, on both sides, at the edge of the state's land.
            foreach (int side in new[] { 1, -1 })
            {
                Vector2? prev = null;
                for (float s = 0f; s < p.Length; s += 6f)
                {
                    float lat = side > 0 ? p.HalfWidth(s) + 26f : south(s) - p.HalfWidth(s) - 26f;
                    var c = p.Point(s, lat); var q = new Vector2(c.x, c.z);
                    bool ok = !p.HasSoundWall(s) && !InBuilding(q, 1.5f) && !NearExit(q, 16f) && StreetDistance(q, 10f, out _, out var w) > w / 2f + 1f && !OnRoad(q, 14f);
                    if (ok && prev.HasValue) { FenceRun(Get(map, q, 600f, FenceSlots), prev.Value, q, 1.8f, false); highway++; }
                    prev = ok ? q : (Vector2?)null;
                }
            }
            // Back yards.
            foreach (var y in yards)
            {
                if (!y.hasStreet) continue;
                var r = new System.Random((int)(y.mid.x * 9.1f) ^ (int)(y.mid.y * 3.7f) ^ 0x1f);
                if (r.NextDouble() > .4) { if (Hedge(y, map)) hedged++; continue; }
                bool woodFence = r.NextDouble() < .45;
                float depth = Mathf.Abs(Vector2.Dot(y.u, y.toStreet)) > .7f ? y.a : y.b, span = Mathf.Abs(Vector2.Dot(y.u, y.toStreet)) > .7f ? y.b : y.a;
                var back = -y.toStreet; var side = new Vector2(-back.y, back.x);
                float reach = depth + 11f + (float)r.NextDouble() * 4f, half = span + 2.5f;
                var c0 = y.c + side * half; var c1 = y.c + side * half + back * reach; var c2 = y.c - side * half + back * reach; var c3 = y.c - side * half;
                // From the back corners of the house round the yard; skipped where it would cut a building or a street.
                bool clear = true;
                foreach (var q in new[] { c1, c2, (c1 + c2) / 2f }) if (InBuilding(q, .5f) || StreetDistance(q, 8f, out _, out var sw) < sw / 2f + 1f || OnRoad(q, 10f)) clear = false;
                if (!clear) continue;
                var bk = Get(map, y.mid, 600f, FenceSlots);
                float h = woodFence ? 1.8f : 1.2f;
                FenceRun(bk, c0 + back * depth, c1, h, woodFence); FenceRun(bk, c1, c2, h, woodFence); FenceRun(bk, c2, c3 + back * depth, h, woodFence);
                yardsFenced++;
            }
            Flush(map, "Fences", new[] { fabric, metal, wood, hedge }, FenceSlots, true);
            Debug.Log($"[FloridaSlice] fences: highway runs {highway}, fenced yards {yardsFenced}, hedged yards {hedged}");
        }

        /// <summary>
        /// About 1 in 4 of the unfenced yards: a clipped ficus hedge 2 to 3 m tall down both side lot lines, from the
        /// front line to the back of the lot, and short returns across the front corners, leaving the middle open
        /// for the driveway and the walk. Pieces that would cut a house or a street are left out.
        /// </summary>
        static bool Hedge(Yard y, Dictionary<(int, int), Bucket> map)
        {
            var r = new System.Random((int)(y.mid.x * 5.3f) ^ (int)(y.mid.y * 8.9f) ^ 0x4ed);
            if (r.NextDouble() > .25) return false;
            float d = StreetDistance(y.mid, 40f, out _, out var sw); if (d > 39f) return false;
            float depth = Mathf.Abs(Vector2.Dot(y.u, y.toStreet)) > .7f ? y.a : y.b, span = Mathf.Abs(Vector2.Dot(y.u, y.toStreet)) > .7f ? y.b : y.a;
            var fwd = y.toStreet; var side = new Vector2(-fwd.y, fwd.x);
            // The front line sits just inside the swale; the back of the lot about 12 m behind the house.
            float front = d - sw / 2f - 2.5f, back = -(depth + 10f + (float)r.NextDouble() * 4f), half = span + 2.8f;
            if (front < depth + 1.5f) return false;
            float h = 2f + (float)r.NextDouble();
            var bk = Get(map, y.mid, 600f, FenceSlots);
            bool Clear(Vector2 a, Vector2 b)
            {
                for (float k = 0; k <= 1.001f; k += .25f) { var q = Vector2.Lerp(a, b, k); if (InBuilding(q, 1f) || OnRoad(q, 10f) || NearExit(q, 8f) || StreetDistance(q, 8f, out _, out var w2) < w2 / 2f + 1.5f) return false; }
                return true;
            }
            int runs = 0;
            foreach (int sd in new[] { 1, -1 })
            {
                var f0 = y.c + side * sd * half + fwd * front; var b0 = y.c + side * sd * half + fwd * back;
                if (Clear(f0, b0)) { HedgeRun(bk, f0, b0, h, r); runs++; }
                var f1 = y.c + side * sd * half * .38f + fwd * front;
                if (Clear(f0, f1)) { HedgeRun(bk, f0, f1, h * .8f, r); runs++; }
            }
            return runs > 0;
        }
    }
}
