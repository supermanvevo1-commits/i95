using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using SurviveI95.World;

namespace SurviveI95.Game
{
    /// <summary>
    /// Real grass on the highway's own land: the mowed strip of bahiagrass between the shoulder and the right-of-way
    /// fence (or the sound wall), as clumps of blades that sway in the wind. Drawn only within about 80 m of the player,
    /// in 20 m tiles worked out once and reused; past about 60 m it thins into the lawn texture underneath.
    /// </summary>
    public class RoadsideGrass : MonoBehaviour
    {
        const float Tile = 20f, Range = 70f, Near = 30f, Spacing = .46f, Shoulder = 3.55f, ToFence = 25.2f, WallGap = 7f, Ground = -.25f;
        // Grass that leans over with the wind, shared with the shader.
        public static float WindStrength = 1f;

        PlayerCar car; RoutePath path; Mesh clump; Material mat;
        // Each tile keeps half its clumps for the near view only: past about 30 m the extra half can't be seen.
        readonly Dictionary<long, (List<Matrix4x4[]> core, List<Matrix4x4[]> extra)> tiles = new Dictionary<long, (List<Matrix4x4[]>, List<Matrix4x4[]>)>();
        readonly Dictionary<(int, int), List<Vector2>> exitPts = new Dictionary<(int, int), List<Vector2>>();
        RenderParams rp;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindAnyObjectByType<PlayerCar>() == null || FindAnyObjectByType<RoadsideGrass>() != null) return;
            new GameObject("RoadsideGrass").AddComponent<RoadsideGrass>();
        }

        void Start()
        {
            car = FindAnyObjectByType<PlayerCar>();
            var sh = Resources.Load<Shader>("Shaders/Grass");
            if (car == null || sh == null) { enabled = false; return; }
            mat = new Material(sh) { enableInstancing = true, hideFlags = HideFlags.DontSave };
            mat.SetTexture("_BaseMap", BladeTexture());
            clump = ClumpMesh();
            rp = new RenderParams(mat) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true, layer = 0, worldBounds = new Bounds(Vector3.zero, Vector3.one * 1e6f) };
        }

        void Update()
        {
            if (path == null)
            {
                path = car.Path; if (path == null) return;
                foreach (var j in path.Junctions) for (float s = 0f; s < j.road.Length; s += 6f) { var c = j.road.Center(s); var k = ((int)Mathf.Floor(c.x / 20f), (int)Mathf.Floor(c.z / 20f)); if (!exitPts.TryGetValue(k, out var l)) exitPts[k] = l = new List<Vector2>(); l.Add(new Vector2(c.x, c.z)); }
            }
            mat.SetFloat("_Wind", WindStrength);
            // The main view only: the mirrors and the map would each draw it all again.
            var cam = Camera.main; if (cam == null) return; rp.camera = cam; float s0 = car.Sim.S;
            int t0 = Mathf.FloorToInt((s0 - Range) / Tile), t1 = Mathf.FloorToInt((s0 + Range) / Tile);
            for (int t = t0; t <= t1; t++)
                foreach (int side in new[] { 1, -1 })
                {
                    if (t < 0 || (t + 1) * Tile > path.Length) continue;
                    long key = (long)t * 2 + (side > 0 ? 1 : 0);
                    if (!tiles.TryGetValue(key, out var tile)) tiles[key] = tile = MakeTile(t, side);
                    foreach (var b in tile.core) Graphics.RenderMeshInstanced(rp, clump, 0, b);
                    if (Mathf.Abs((t + .5f) * Tile - s0) < Near + Tile * .5f) foreach (var b in tile.extra) Graphics.RenderMeshInstanced(rp, clump, 0, b);
                }
            // Forget tiles far behind so memory stays flat on a long drive.
            if (tiles.Count > 80)
            {
                var drop = new List<long>();
                foreach (var k in tiles.Keys) { long t = k / 2; if (t < t0 - 4 || t > t1 + 4) drop.Add(k); }
                foreach (var k in drop) tiles.Remove(k);
            }
        }

        bool NearExit(Vector2 q, float d)
        {
            float d2 = d * d;
            int cx = (int)Mathf.Floor(q.x / 20f), cz = (int)Mathf.Floor(q.y / 20f);
            for (int a = -1; a <= 1; a++) for (int b = -1; b <= 1; b++)
                    if (exitPts.TryGetValue((cx + a, cz + b), out var l)) foreach (var e in l) if ((e - q).sqrMagnitude < d2) return true;
            return false;
        }

        bool NearOverpass(float s) { foreach (var o in path.File.overpasses) if (Mathf.Abs(o.s - s) < 22f) return true; return false; }

        (List<Matrix4x4[]>, List<Matrix4x4[]>) MakeTile(int t, int side)
        {
            var list = new List<Matrix4x4>(); var more = new List<Matrix4x4>();
            var rnd = new System.Random(t * 7919 + side * 31);
            for (float s = t * Tile; s < (t + 1) * Tile; s += Spacing)
            {
                if (path.IsBridge(s) || path.Center(s).y > 1.5f) continue;
                bool overpass = false; foreach (var o in path.File.overpasses) if (Mathf.Abs(o.s - s) < 22f) overpass = true;
                if (overpass) continue;
                float hw = path.HalfWidth(s), south = -(2f * hw + RoutePath.MedianGap);
                float inner = side > 0 ? hw + Shoulder : south - hw - Shoulder;
                // Where the road climbs on earth fill, the grass starts past the foot of the bank (1 in 2.5, as the
                // builder makes it).
                float y = path.Center(s).y, bank = y > .2f ? 2.5f * (y + .24f) : 0f;
                float reach = path.HasSoundWall(s) ? WallGap - .6f : ToFence;
                var right = path.Right(s); var c = path.Center(s); float swale = path.SwaleAmount(s, side), vergeReach = path.VergeReach(s, side);
                for (float d = bank; d < reach - Shoulder; d += Spacing)
                {
                    float lat = inner + side * (d + (float)rnd.NextDouble() * Spacing);
                    float along = (float)(rnd.NextDouble() - .5) * Spacing;
                    var p = c + right * lat + path.Tangent(s) * along;
                    var q = new Vector2(p.x, p.z);
                    if (exitPts.Count > 0 && NearExit(q, 9f)) continue;
                    // Fewer, taller clumps out by the fence where the mower misses; a short even lawn near the road.
                    float edge = Mathf.InverseLerp(reach - Shoulder - 3f, reach - Shoulder, d);
                    float h = Mathf.Lerp(.16f, .3f, (float)rnd.NextDouble()) * (1f + edge * 1.6f);
                    float w = .5f + (float)rnd.NextDouble() * .35f;
                    // Standing on the verge: down in the swale or up on its back slope where there is one.
                    float out_ = side * (lat - path.PavedEdge(s, side));
                    if (out_ > vergeReach) continue; // a street, water or a ramp starts here
                    float gy = out_ > 0f && out_ < RoutePath.SwaleReach ? Mathf.Max(Ground, RoutePath.VergeY(out_, swale, vergeReach) - .01f) : Ground;
                    (rnd.NextDouble() < .5 ? list : more).Add(Matrix4x4.TRS(new Vector3(p.x, gy, p.z), Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f), new Vector3(w, h, w)));
                }
            }
            return (Batches(list), Batches(more));
        }

        static List<Matrix4x4[]> Batches(List<Matrix4x4> list)
        {
            var batches = new List<Matrix4x4[]>();
            for (int i = 0; i < list.Count; i += 1023) batches.Add(list.GetRange(i, Mathf.Min(1023, list.Count - i)).ToArray());
            return batches;
        }

        /// <summary>Three blade cards crossed like a star, 1 m tall before scaling, lit from straight up so no face goes dark.</summary>
        static Mesh ClumpMesh()
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var n = new List<Vector3>(); var tri = new List<int>();
            for (int k = 0; k < 3; k++)
            {
                float a = k * Mathf.PI / 3f; var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * .5f; int b = v.Count;
                v.Add(-dir); v.Add(dir); v.Add(dir + Vector3.up); v.Add(-dir + Vector3.up);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
                for (int i = 0; i < 4; i++) n.Add(Vector3.up);
                tri.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
            }
            var m = new Mesh { name = "GrassClump" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetNormals(n); m.SetTriangles(tri, 0); m.RecalculateBounds();
            return m;
        }

        /// <summary>Thin tapering blades, some bent, gray-white so the shader colors them; alpha cuts out the gaps.</summary>
        static Texture2D BladeTexture()
        {
            const int W = 128, H = 128; var px = new Color32[W * H];
            var r = new System.Random(4242);
            for (int k = 0; k < 46; k++)
            {
                float x0 = (float)r.NextDouble() * W, top = .45f + (float)r.NextDouble() * .55f, lean = (float)(r.NextDouble() - .5) * 26f, wid = 1.2f + (float)r.NextDouble() * 1.6f;
                byte tone = (byte)(170 + r.Next(85));
                for (int y = 0; y < H * top; y++)
                {
                    float f = y / (H * top), cx = x0 + lean * f * f, half = wid * (1f - f * .85f);
                    for (int x = Mathf.FloorToInt(cx - half - 1); x <= Mathf.CeilToInt(cx + half + 1); x++)
                    {
                        float cov = Mathf.Clamp01(half + .5f - Mathf.Abs(x + .5f - cx)); if (cov <= 0f) continue;
                        int xi = ((x % W) + W) % W, i = y * W + xi;
                        byte a = (byte)Mathf.Max(px[i].a, cov * 255f);
                        px[i] = new Color32(tone, tone, tone, a);
                    }
                }
            }
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, true) { name = "GrassBlades", wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px); tex.Apply(true, true);
            return tex;
        }
    }
}
