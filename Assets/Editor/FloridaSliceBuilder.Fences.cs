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
    /// chain-link (most common in older Miami blocks) or a 6 ft wood privacy fence, behind about 4 houses in 10.
    /// </summary>
    public static partial class FloridaSliceBuilder
    {
        const int FFabric = 0, FMetal = 1, FWood = 2, FenceSlots = 3;

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

        static void BuildFences(RoutePath p, Func<float, float> south)
        {
            var old = GameObject.Find("Fences"); if (old != null) UnityEngine.Object.DestroyImmediate(old);
            var fabric = Lit("ChainLinkFabric", Color.white, .4f);
            fabric.SetTexture("_BaseMap", MakeChainLink()); fabric.SetFloat("_Metallic", .4f);
            fabric.SetFloat("_AlphaClip", 1f); fabric.SetFloat("_Cutoff", .4f); fabric.EnableKeyword("_ALPHATEST_ON"); fabric.SetFloat("_Cull", 0f); fabric.renderQueue = 2450;
            var metal = Lit("FencePost", new Color(.6f, .62f, .62f), .4f); metal.SetFloat("_Metallic", .5f);
            var wood = Lit("WoodFence", Color.white, .1f); wood.SetTexture("_BaseMap", MakeWoodFence()); wood.SetFloat("_Cull", 0f);
            var map = new Dictionary<(int, int), Bucket>();
            int highway = 0, yardsFenced = 0;
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
                if (r.NextDouble() > .4) continue;
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
            Flush(map, "Fences", new[] { fabric, metal, wood }, FenceSlots, true);
            Debug.Log($"[FloridaSlice] fences: highway runs {highway}, fenced yards {yardsFenced}");
        }
    }
}
