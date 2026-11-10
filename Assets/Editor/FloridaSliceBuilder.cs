using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SurviveI95.Game;
using SurviveI95.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace SurviveI95.EditorTools
{
    /// <summary>Generates the Florida test stretch from route data. Safe to re-run at any time.</summary>
    public static class FloridaSliceBuilder
    {
        public const string RouteId = "miami_ftl";
        const string Gen = "Assets/Generated", PH = "Assets/ThirdParty/PolyHaven";
        const string CarPath = "Assets/ThirdParty/Sketchfab/corolla/scene.gltf";
        const float CarYawFix = 0f; // degrees, corrects the imported model's facing

        class Mats { public Material asphalt, shoulder, grass, concrete, white, yellow, rumble, signGreen, post, billboard, text; }

        [MenuItem("Survive I-95/Build Florida Slice")]
        public static void Build()
        {
            Directory.CreateDirectory(Gen + "/Meshes");
            AssetDatabase.Refresh();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var path = RoutePath.Load(RouteId);
            var m = MakeMaterials();

            placed.Clear();
            BuildEnvironment();
            var road = new GameObject("Road");
            Func<float, float> half = path.HalfWidth;
            Func<float, float> south = s => -(2 * path.HalfWidth(s) + RoutePath.MedianGap);

            Add(road, "Ground", GroundPlane(path, 3200f, -0.25f, 12f), m.grass);
            BuildCarriageway(road, "Northbound", path, s => 0, m);
            BuildCarriageway(road, "Southbound", path, south, m);
            PrepareLookup(path, south);
            ComputeExits(path);
            BuildBarriers(road, path, m);
            BuildOverpasses(road, path, south, m);
            BuildSigns(path, m);
            PrepareLookup(path, south);
            BuildGroundDetail(path, m);
            BuildBuildings(path, south, m);
            BuildLamps(path);
            BuildPillars(road, path, south, m);
            BuildExitRoads(road, path, m);
            BuildSoundWalls(road, path, south, m);
            BuildTrees(path, south);

            var cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            cam.tag = "MainCamera"; cam.farClipPlane = 3000f; cam.fieldOfView = 62f;
            var car = BuildCar(cam, m);
            car.Place(3000f);

            WriteObstacles();
            EditorSceneManager.SaveScene(scene, Gen + "/FloridaSlice.unity");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Gen + "/FloridaSlice.unity", true) };
            AssetDatabase.SaveAssets();
            SliceAutoRebuild.WriteStamp();
            Debug.Log("[FloridaSlice] built " + (path.Length / 1000f).ToString("F1") + " km");

            var shots = ArgValue("-shotDir");
            if (shots != null) Screenshots(car, cam, path, shots);
        }

        // Everything solid is recorded as it is placed, so what you see and what you hit can never disagree.
        static readonly List<RouteObstacle> placed = new List<RouteObstacle>();
        static void Solid(float s, float lat, float halfS, float halfLat, string kind) =>
            placed.Add(new RouteObstacle { s = s, lat = lat, halfS = halfS, halfLat = halfLat, kind = kind });

        static void WriteObstacles()
        {
            string file = $"Assets/Resources/Data/Routes/{RouteId}_obstacles.json";
            File.WriteAllText(file, JsonUtility.ToJson(new RouteObstacleFile { items = placed.OrderBy(o => o.s).ToArray() }));
            AssetDatabase.ImportAsset(file);
            Debug.Log("[FloridaSlice] solid objects: " + placed.Count);
        }

        static string ArgValue(string name)
        {
            var a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }

        static void Screenshots(PlayerCar car, Camera cam, RoutePath path, string dir)
        {
            ShaderUtil.allowAsyncCompilation = false;
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            Action<string> shot = name =>
            {
                RenderPipeline.SubmitRenderRequest(cam, new RenderPipeline.StandardRequest { destination = rt });
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                File.WriteAllBytes(Path.Combine(dir, "view_" + name + ".png"), tex.EncodeToPNG());
            };
            car.Place(500f); car.SetCockpit(false); shot("warmup");
            car.Place(3000f); car.SetTestView(0.8f, 0, 0); shot("chase_steer");
            var c = car.transform;
            cam.transform.position = c.position + c.right * 3.4f + c.forward * 1.2f + Vector3.up * 0.55f;
            cam.transform.rotation = Quaternion.LookRotation(c.position + Vector3.up * 0.45f + c.forward * 0.6f - cam.transform.position);
            shot("side_ground");
            car.SetCockpit(true);
            foreach (var v in new[] { ("ahead", 0f, 0f, 0f), ("left", -80f, -8f, 0f), ("right", 75f, -10f, 0f), ("rear", 130f, 0f, 0f), ("down", 0f, -34f, 1f) })
            { car.SetTestView(v.Item4, v.Item2, v.Item3); shot("cockpit_" + v.Item1); }
            RenderTexture.active = null;
        }

        // ---------- materials ----------
        static Texture2D Tex(string file, bool normal)
        {
            if (normal)
            {
                var imp = AssetImporter.GetAtPath(file) as TextureImporter;
                if (imp != null && imp.textureType != TextureImporterType.NormalMap) { imp.textureType = TextureImporterType.NormalMap; imp.SaveAndReimport(); }
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(file);
        }

        static Material Lit(string name, Color color, float smooth, string texSet = null)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smooth);
            if (texSet != null)
            {
                var b = $"{PH}/Textures/{texSet}/{texSet}_";
                mat.SetTexture("_BaseMap", Tex(b + "Diffuse.jpg", false));
                var n = Tex(b + "nor_gl.jpg", true);
                if (n != null) { mat.SetTexture("_BumpMap", n); mat.EnableKeyword("_NORMALMAP"); }
            }
            return Save(mat, name);
        }

        static Material Save(Material mat, string name)
        {
            Directory.CreateDirectory(Gen + "/Materials");
            var file = $"{Gen}/Materials/{name}.mat";
            AssetDatabase.DeleteAsset(file);
            AssetDatabase.CreateAsset(mat, file);
            return mat;
        }

        static Material Textured(string name, Texture2D tex, float smooth)
        {
            var mat = Lit(name, Color.white, smooth);
            mat.SetTexture("_BaseMap", tex);
            return mat;
        }

        // Light concrete with soft blotches; panels > 0 adds the vertical joints of a sound wall.
        static Texture2D MakeConcrete(string name, Color baseColor, int panels)
        {
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            var rnd = new System.Random(name.Length * 97);
            var blotch = new float[17, 17];
            for (int a = 0; a < 17; a++) for (int b = 0; b < 17; b++) blotch[a, b] = (float)rnd.NextDouble();
            for (int a = 0; a < 17; a++) { blotch[a, 16] = blotch[a, 0]; blotch[16, a] = blotch[0, a]; }
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float gx = x / 16f, gy = y / 16f; int ix = (int)gx, iy = (int)gy; float fx = gx - ix, fy = gy - iy;
                float soft = Mathf.Lerp(Mathf.Lerp(blotch[ix, iy], blotch[ix + 1, iy], fx), Mathf.Lerp(blotch[ix, iy + 1], blotch[ix + 1, iy + 1], fx), fy);
                float shade = .93f + soft * .12f + (float)(rnd.NextDouble() - .5) * .04f;
                if (panels > 0)
                {
                    int cell = size / panels, px = x % cell, py = y % (size / 3);
                    if (px < 4) shade *= .62f; else if (px < 7) shade *= 1.08f;
                    if (py < 2) shade *= .82f;
                }
                var c = baseColor * shade; c.a = 1f;
                tex.SetPixel(x, y, c);
            }
            tex.Apply();
            string file = $"{Gen}/Materials/{name}.png";
            Directory.CreateDirectory(Gen + "/Materials");
            File.WriteAllBytes(file, tex.EncodeToPNG());
            AssetDatabase.ImportAsset(file);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(file);
        }

        static Mats MakeMaterials()
        {
            var m = new Mats
            {
                asphalt = Lit("Asphalt", new Color(.62f, .62f, .62f), .18f, "asphalt_02"),
                shoulder = Lit("Shoulder", new Color(.9f, .9f, .88f), .12f, "asphalt_02"),
                grass = Lit("Grass", new Color(.66f, .94f, .5f), .08f, "grass_ground"),
                concrete = Textured("Concrete", MakeConcrete("ConcreteTex", new Color(.74f, .73f, .7f), 0), .18f),
                white = Lit("PaintWhite", new Color(.95f, .95f, .92f), .3f),
                yellow = Lit("PaintYellow", new Color(.95f, .72f, .1f), .3f),
                signGreen = Lit("SignGreen", new Color(.02f, .3f, .16f), .45f),
                post = Lit("Post", new Color(.45f, .46f, .47f), .4f),
                billboard = Lit("Billboard", new Color(.95f, .9f, .75f), .2f),
            };
            var grooves = new Texture2D(4, 16) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat };
            for (int y = 0; y < 16; y++) for (int x = 0; x < 4; x++) grooves.SetPixel(x, y, y < 7 ? new Color(.1f, .1f, .1f) : new Color(.42f, .42f, .4f));
            grooves.Apply();
            File.WriteAllBytes(Gen + "/Materials/Rumble.png", grooves.EncodeToPNG());
            AssetDatabase.ImportAsset(Gen + "/Materials/Rumble.png");
            m.rumble = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.rumble.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Gen + "/Materials/Rumble.png"));
            m.rumble.SetFloat("_Smoothness", .1f);
            Save(m.rumble, "Rumble");
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            m.text = new Material(Shader.Find("SurviveI95/SignText")) { mainTexture = font.material.mainTexture };
            Save(m.text, "SignText");
            return m;
        }

        // ---------- geometry ----------
        static Mesh Ribbon(RoutePath p, Func<float, float> left, Func<float, float> right, float y, float uTile, float vTile, float from = 0, float to = -1, float step = 10f, bool flat = false)
        {
            if (to < 0) to = p.Length;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tris = new List<int>();
            int rows = Mathf.Max(1, Mathf.CeilToInt((to - from) / step));
            for (int r = 0; r <= rows; r++)
            {
                float s = from + (to - from) * r / rows, l = left(s), rr = right(s);
                var pl = p.Point(s, l) + Vector3.up * y; var pr = p.Point(s, rr) + Vector3.up * y;
                if (flat) { pl.y = y; pr.y = y; }
                v.Add(pl); v.Add(pr);
                uv.Add(new Vector2(l / uTile, s / vTile)); uv.Add(new Vector2(rr / uTile, s / vTile));
                if (r > 0) { int a = (r - 1) * 2; tris.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 }); }
            }
            return Finish(v, uv, tris);
        }

        // A single flat sheet: a ribbon this wide would fold over itself on the inside of curves.
        static Mesh GroundPlane(RoutePath p, float margin, float y, float tile)
        {
            float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
            for (float s = 0; s < p.Length; s += 50f) { var c = p.Center(s); x0 = Mathf.Min(x0, c.x); x1 = Mathf.Max(x1, c.x); z0 = Mathf.Min(z0, c.z); z1 = Mathf.Max(z1, c.z); }
            x0 -= margin; x1 += margin; z0 -= margin; z1 += margin;
            var v = new List<Vector3> { new Vector3(x0, y, z0), new Vector3(x0, y, z1), new Vector3(x1, y, z0), new Vector3(x1, y, z1) };
            var uv = v.Select(q => new Vector2(q.x / tile, q.z / tile)).ToList();
            return Finish(v, uv, new List<int> { 0, 1, 2, 2, 1, 3 });
        }

        static Mesh Finish(List<Vector3> v, List<Vector2> uv, List<int> tris)
        {
            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            return mesh;
        }

        static int meshCount;
        static void Add(GameObject parent, string name, Mesh mesh, Material mat, bool shadows = false)
        {
            var file = $"{Gen}/Meshes/{parent.name}_{name}_{meshCount++}.asset";
            AssetDatabase.DeleteAsset(file);
            AssetDatabase.CreateAsset(mesh, file);
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent.transform);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        }

        // Distance from each end of an exit, or -1 outside one.
        static float ExitEnd(RoutePath p, float s, out float d)
        {
            d = 0f;
            foreach (var j in p.Junctions) if (s >= j.divergeS && s <= j.mergeS) { d = s - j.divergeS; return Mathf.Min(d, j.mergeS - s); }
            return -1f;
        }

        // Beside the highway the exit lane IS the highway's own pavement widening, so there is only ever one surface.
        static float AuxLane(RoutePath p, float s)
        {
            float e = ExitEnd(p, s, out var d);
            if (e < 0f || e > RoutePath.ExitHold) return 0f;
            return (RoutePath.ExitOffset(d) + RoutePath.ExitHalf + RoutePath.ExitWallGap) * Mathf.SmoothStep(0f, 1f, e / RoutePath.ExitTaper);
        }

        // Past the split the shoulder stops exactly where the exit road starts, then widens back out as the road leaves.
        static float ShoulderWidth(RoutePath p, float s, float normal)
        {
            float e = ExitEnd(p, s, out var d);
            if (e < RoutePath.ExitHold - 15f) return normal; // the widened pavement hides the shoulder until then
            float left = RoutePath.ExitOffset(d) - RoutePath.ExitHalf - Mathf.Lerp(.2f, RoutePath.ExitWallGap, Mathf.InverseLerp(RoutePath.ExitOpen, RoutePath.ExitOpen + 20f, e));
            return Mathf.Clamp(left - .05f, .05f, normal);
        }

        // A white arrow painted on the road; a positive angle leans it to the right.
        static void RoadArrow(List<Vector3> v, List<Vector2> uv, List<int> t, RoutePath p, float s, float lat, float angle)
        {
            float r = angle * Mathf.Deg2Rad, c = Mathf.Cos(r), n = Mathf.Sin(r);
            Func<float, float, Vector3> at = (u, w) => p.Point(s + u * c - w * n, lat + u * n + w * c) + Vector3.up * .03f;
            const float shaft = .18f, body = 4.2f, head = .62f, tip = 6.2f;
            int a = v.Count;
            v.Add(at(0f, -shaft)); v.Add(at(0f, shaft)); v.Add(at(body, -shaft)); v.Add(at(body, shaft));
            t.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
            v.Add(at(body, -head)); v.Add(at(body, head)); v.Add(at(tip, 0f));
            t.AddRange(new[] { a + 4, a + 6, a + 5 });
            for (int i = 0; i < 7; i++) uv.Add(Vector2.zero);
        }

        static void BuildCarriageway(GameObject root, string name, RoutePath p, Func<float, float> c, Mats m)
        {
            bool main = Mathf.Abs(c(0f)) < .01f;
            Func<float, float> aux = s => main ? AuxLane(p, s) : 0f;
            Func<float, float> shoulder = s => main ? ShoulderWidth(p, s, 3.05f) : 3.05f;
            Func<float, bool> plainEdge = s => aux(s) <= 0f && shoulder(s) >= 1.3f;
            var g = new GameObject(name); g.transform.SetParent(root.transform);
            Func<float, float> h = p.HalfWidth;
            const float rightShoulder = 3.05f, leftShoulder = 1.3f;
            Add(g, "Shoulder", Ribbon(p, s => c(s) - h(s) - leftShoulder, s => c(s) + h(s) + shoulder(s), 0.0f, 5, 5, step: 5f), m.shoulder);
            Add(g, "Lanes", Ribbon(p, s => c(s) - h(s), s => c(s) + h(s) + aux(s), 0.012f, 7, 7, step: 5f), m.asphalt);
            Add(g, "RumbleLeft", Ribbon(p, s => c(s) - h(s) - .95f, s => c(s) - h(s) - .4f, 0.02f, 1, .6f, step: 10f), m.rumble);
            Add(g, "RumbleRight", Ribbon(p, s => c(s) + h(s) + .4f, s => c(s) + h(s) + (plainEdge(s) ? .95f : .4f), 0.02f, 1, .6f, step: 5f), m.rumble);
            Add(g, "EdgeLeft", Ribbon(p, s => c(s) - h(s) - .08f, s => c(s) - h(s) + .08f, 0.03f, 1, 1), m.yellow);
            Add(g, "EdgeRight", Ribbon(p, s => c(s) + h(s) - .08f, s => c(s) + h(s) + (aux(s) > 0f ? -.08f : .08f), 0.03f, 1, 1, step: 5f), m.white);

            // US standard broken lane line: 10 ft painted, 30 ft gap.
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (float s = 0; s < p.Length - 4; s += 12.19f)
            {
                int lanes = Mathf.RoundToInt(p.Lanes(s));
                for (int i = 1; i < lanes; i++)
                {
                    float x = c(s) - h(s) + i * (2 * h(s)) / lanes;
                    int a = v.Count;
                    v.Add(p.Point(s, x - .06f) + Vector3.up * .03f); v.Add(p.Point(s, x + .06f) + Vector3.up * .03f);
                    v.Add(p.Point(s + 3.05f, x - .06f) + Vector3.up * .03f); v.Add(p.Point(s + 3.05f, x + .06f) + Vector3.up * .03f);
                    uv.Add(Vector2.zero); uv.Add(Vector2.right); uv.Add(Vector2.up); uv.Add(Vector2.one);
                    t.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
                }
            }
            // The speed-change lane is separated from the through lanes by a broken line.
            if (main)
                foreach (var j in p.Junctions)
                    foreach (var from in new[] { j.divergeS, j.mergeS - RoutePath.ExitHold })
                        for (float s = from; s < from + RoutePath.ExitHold - 3f; s += 9f)
                        {
                            float x = h(s); int a = v.Count;
                            v.Add(p.Point(s, x - .1f) + Vector3.up * .03f); v.Add(p.Point(s, x + .1f) + Vector3.up * .03f);
                            v.Add(p.Point(s + 3f, x - .1f) + Vector3.up * .03f); v.Add(p.Point(s + 3f, x + .1f) + Vector3.up * .03f);
                            uv.Add(Vector2.zero); uv.Add(Vector2.right); uv.Add(Vector2.up); uv.Add(Vector2.one);
                            t.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
                        }
            if (main)
                foreach (var j in p.Junctions)
                {
                    float gap = RoutePath.ExitWallGap, hold = RoutePath.ExitHold;
                    Func<float, float> edge = s => h(s) + Mathf.Max(0f, aux(s) - gap);
                    Add(g, "AuxEdgeOut", Ribbon(p, s => edge(s) - .08f, s => edge(s) + .08f, 0.035f, 1, 1, j.divergeS, j.divergeS + hold, 3f), m.white);
                    Add(g, "AuxEdgeIn", Ribbon(p, s => edge(s) - .08f, s => edge(s) + .08f, 0.035f, 1, 1, j.mergeS - hold, j.mergeS, 3f), m.white);
                    RoadArrow(v, uv, t, p, j.divergeS + 62f, h(j.divergeS + 62f) + RoutePath.ExitEdge, 16f);
                    RoadArrow(v, uv, t, p, j.mergeS - 66f, h(j.mergeS - 66f) + RoutePath.ExitEdge, -16f);
                    // Painted stripes fill the wedge where the two roads part.
                    foreach (var dir in new[] { 1f, -1f })
                        for (float e = hold + 8f; e < hold + 118f; e += 5f)
                        {
                            float s = dir > 0f ? j.divergeS + e : j.mergeS - e, d = s - j.divergeS;
                            float w = Mathf.Min(RoutePath.ExitOffset(d) - RoutePath.ExitHalf - .4f, 3f);
                            if (w < .7f) continue;
                            float x0 = h(s) + .2f, x1 = h(s) + w, run = dir * Mathf.Min(3f, w * 1.2f); int a = v.Count;
                            v.Add(p.Point(s, x0) + Vector3.up * .03f); v.Add(p.Point(s + run, x1) + Vector3.up * .03f);
                            v.Add(p.Point(s + .4f, x0) + Vector3.up * .03f); v.Add(p.Point(s + run + .4f, x1) + Vector3.up * .03f);
                            uv.Add(Vector2.zero); uv.Add(Vector2.right); uv.Add(Vector2.up); uv.Add(Vector2.one);
                            t.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
                        }
                }
            Add(g, "Dashes", Finish(v, uv, t), m.white);
            Add(g, "SkirtR", WallStrip(p, s => c(s) + h(s) + Mathf.Max(shoulder(s), aux(s)), 0f, p.Length, -1.6f, 0f), m.concrete);
            Add(g, "SkirtL", WallStrip(p, s => c(s) - h(s) - leftShoulder, 0f, p.Length, -1.6f, 0f), m.concrete);
        }

        static Mesh Extrude(RoutePath p, float lateral, float from, float to)
        {
            Vector2[] profile = { new Vector2(-.3f, 0), new Vector2(-.1f, .3f), new Vector2(-.08f, .81f), new Vector2(.08f, .81f), new Vector2(.1f, .3f), new Vector2(.3f, 0) };
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            int rows = Mathf.Max(1, Mathf.CeilToInt((to - from) / 5f)), w = profile.Length;
            for (int r = 0; r <= rows; r++)
            {
                float s = from + (to - from) * r / rows;
                for (int j = 0; j < w; j++)
                {
                    v.Add(p.Point(s, (lateral < 0 ? lateral - p.HalfWidth(s) : lateral + p.HalfWidth(s)) + profile[j].x) + Vector3.up * profile[j].y);
                    uv.Add(new Vector2(j * .35f, s / 2f));
                    if (r > 0 && j > 0)
                    {
                        int d = r * w + j, c = r * w + j - 1, b = (r - 1) * w + j, a = (r - 1) * w + j - 1;
                        t.AddRange(new[] { a, c, b, b, c, d });
                    }
                }
            }
            return Finish(v, uv, t);
        }

        static void BuildBarriers(GameObject root, RoutePath p, Mats m)
        {
            var g = new GameObject("Barriers"); g.transform.SetParent(root.transform);
            var spans = new List<(float a, float b)>();
            float start = -1;
            for (float s = 0; s < p.Length; s += 5)
            {
                bool on = p.HasBarrier(s) && !p.InGate(s); // must match the collision rule exactly, or walls become invisible
                if (on && start < 0) start = s;
                if (!on && start >= 0) { spans.Add((start, s)); start = -1; }
            }
            foreach (var (a, b) in spans)
            {
                Add(g, "Right", Extrude(p, 4.0f, a, b), m.concrete, true);
            }
            Add(g, "Median", Extrude(p, -RoutePath.BarrierLeft, 0, p.Length), m.concrete, true);
            Debug.Log("[FloridaSlice] barrier spans " + spans.Count);
        }

        static GameObject Box(Transform parent, Vector3 pos, Quaternion rot, Vector3 size, Material mat, string name = "Box")
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = name; go.transform.SetParent(parent); go.transform.SetPositionAndRotation(pos, rot); go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        static void BuildOverpasses(GameObject root, RoutePath p, Func<float, float> south, Mats m)
        {
            var g = new GameObject("Overpasses"); g.transform.SetParent(root.transform);
            float last = -999; int count = 0;
            foreach (var o in p.File.overpasses.OrderBy(x => x.s))
            {
                if (o.s - last < 60) continue;
                last = o.s; count++;
                var rot = Quaternion.LookRotation(p.Tangent(o.s), Vector3.up);
                float mid = south(o.s) / 2f, span = -south(o.s) + 2 * p.HalfWidth(o.s) + 30f;
                var c = p.Point(o.s, mid);
                Box(g.transform, c + Vector3.up * 6.3f, rot, new Vector3(span, 1.2f, 14f), m.concrete, o.name).GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.On;
                foreach (var lat in new[] { p.HalfWidth(o.s) + 7f, -p.HalfWidth(o.s) - RoutePath.BarrierLeft, south(o.s) - p.HalfWidth(o.s) - 7f })
                    Box(g.transform, p.Point(o.s, lat) + Vector3.up * 2.9f, rot, new Vector3(1.2f, 5.8f, 10f), m.concrete, "Pier");
                foreach (var lat in new[] { p.HalfWidth(o.s) + 7f, -p.HalfWidth(o.s) - RoutePath.BarrierLeft, south(o.s) - p.HalfWidth(o.s) - 7f }) Solid(o.s, lat, 5f, 0.6f, "pier");
            }
            Debug.Log("[FloridaSlice] overpasses " + count);
        }

        static void Text(Transform parent, Vector3 pos, Quaternion rot, string text, float height, Color color, Material mat)
        {
            var go = new GameObject("Text", typeof(TextMesh));
            go.transform.SetParent(parent); go.transform.SetPositionAndRotation(pos, rot);
            var tm = go.GetComponent<TextMesh>();
            tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.text = text; tm.fontSize = 96; tm.characterSize = height / 9.6f / 1.0f * 1f;
            tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center; tm.color = color;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        static void Sign(Transform parent, RoutePath p, float s, float lateralFromEdge, float width, float height, float elevation, string text, float textH, Material panel, Color tc, Mats m, bool solid = true, bool placedForExit = false)
        {
            if (!placedForExit) foreach (var j in p.Junctions) if (s > j.divergeS - 2f && s < j.mergeS + 10f) return; // would stand in the exit lane
            var rot = Quaternion.LookRotation(p.Tangent(s), Vector3.up);
            var c = p.Point(s, p.HalfWidth(s) + lateralFromEdge);
            var mid = c + Vector3.up * (elevation + height / 2);
            Box(parent, mid, rot, new Vector3(width, height, .08f), panel, "Panel").GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.On;
            var right = p.Right(s);
            if (solid) foreach (var d in new[] { -width * .32f, width * .32f })
                Solid(s, p.HalfWidth(s) + lateralFromEdge + d, 0.12f, 0.12f, width > 10f ? "pole" : "post");
            foreach (var d in new[] { -width * .32f, width * .32f })
                { var foot = c + right * d + p.Tangent(s) * .14f; float top = c.y + elevation + height; foot.y = top / 2f; Box(parent, foot, rot, new Vector3(.14f, top, .14f), m.post, "Post"); }
            Text(parent, mid - p.Tangent(s) * .06f, rot, text, textH, tc, m.text);
        }

        static readonly Color SignInk = new Color(.06f, .06f, .05f);

        static void Diamond(Transform parent, RoutePath p, float s, float lateralFromEdge, string text, Mats m, bool solid)
        {
            const float size = 1.7f, elevation = 2.3f;
            var rot = Quaternion.LookRotation(p.Tangent(s), Vector3.up);
            var c = p.Point(s, p.HalfWidth(s) + lateralFromEdge);
            var mid = c + Vector3.up * (elevation + size * .71f);
            Box(parent, mid, rot * Quaternion.Euler(0f, 0f, 45f), new Vector3(size, size, .08f), m.yellow, "Panel").GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.On;
            var foot = c + p.Tangent(s) * .14f; float top = mid.y; foot.y = top / 2f;
            Box(parent, foot, rot, new Vector3(.14f, top, .14f), m.post, "Post");
            if (solid) Solid(s, p.HalfWidth(s) + lateralFromEdge, .12f, .12f, "post");
            Text(parent, mid - p.Tangent(s) * .06f, rot, text, .32f, SignInk, m.text);
        }

        static string CleanRef(string r) => r == null ? "" : System.Text.RegularExpressions.Regex.Replace(r, "[^0-9A-Z]", "");

        static void BuildSigns(RoutePath p, Mats m)
        {
            var g = new GameObject("Signs");
            // Yellow signs mark the exits you can actually drive.
            foreach (var j in p.Junctions)
            {
                var real = p.File.exits.Where(e => !string.IsNullOrEmpty(e.@ref)).OrderBy(e => Mathf.Abs(e.s - j.divergeS)).FirstOrDefault();
                string name = real != null && Mathf.Abs(real.s - j.divergeS) < 300f ? "EXIT " + CleanRef(real.@ref) : "EXIT";
                string label = name + "\nKEEP RIGHT";
                Sign(g.transform, p, Mathf.Max(5f, j.divergeS - 805f), 7.5f, 6.5f, 2.6f, 2.6f, name + "\n1/2 MILE", .55f, m.signGreen, Color.white, m);
                foreach (float back in new[] { 420f, 130f })
                    Sign(g.transform, p, Mathf.Max(5f, j.divergeS - back), 7f, 6.5f, 2.6f, 2.6f, label, .5f, m.yellow, SignInk, m);
                Sign(g.transform, p, j.divergeS + 175f, 3.8f, 2.8f, 1.5f, 1.7f, name + "\n>>>", .42f, m.signGreen, Color.white, m, placedForExit: true);
                Diamond(g.transform, p, j.mergeS - 310f, 7f, "MERGING\nTRAFFIC", m, true);
                Sign(g.transform, j.road, 235f, 3.2f, 2.2f, 1.5f, 1.5f, "RAMP\n45 MPH", .38f, m.yellow, SignInk, m, solid: false);
                Diamond(g.transform, j.road, j.road.Length - 250f, 3.4f, "MERGE", m, false);
            }
            float? baseMile = null;
            foreach (var e in p.File.exits)
            {
                if (string.IsNullOrEmpty(e.@ref)) continue;
                var digits = new string(e.@ref.TakeWhile(char.IsDigit).ToArray());
                if (baseMile == null && digits.Length > 0) baseMile = float.Parse(digits) - e.s / 1609.34f;
                var label = "EXIT " + CleanRef(e.@ref) + "\n" + Wrap(e.name);
                Sign(g.transform, p, e.s - 340f, 8f, 7.5f, 3.8f, 2.4f, label, .62f, m.signGreen, Color.white, m);
                Sign(g.transform, p, Mathf.Max(5, e.s - 15f), 10f, 4.5f, 1.7f, 2.4f, "EXIT\n" + CleanRef(e.@ref), .55f, m.signGreen, Color.white, m);
            }
            if (baseMile != null)
                for (int mile = Mathf.CeilToInt(baseMile.Value); (mile - baseMile.Value) * 1609.34f < p.Length; mile++)
                    Sign(g.transform, p, Mathf.Max(5, (mile - baseMile.Value) * 1609.34f), 5.2f, .55f, 1.0f, 1.3f, "MILE\n" + mile, .17f, m.signGreen, Color.white, m);
            for (float s = 620f; s < p.Length - 300f; s += 2400f)
                Sign(g.transform, p, s, 6.5f, 1.7f, 2.2f, 1.6f, "SPEED\nLIMIT\n65", .42f, m.white, SignInk, m);
            string[] ads = { "HURT ON I-95?\nCALL BIG RICK", "GATOR WORLD\nNEXT EXIT", "FIREWORKS\nFIREWORKS\nFIREWORKS", "EARLY BIRD\nBUFFET 3:45PM" };
            for (int i = 0; i < 9; i++)
            {
                // Billboards keep clear of exit signs, so nothing competes with them.
                float at = 1400f + i * 2500f;
                while (p.Junctions.Any(j => at > j.divergeS - 900f && at < j.mergeS + 40f)) at += 100f;
                Sign(g.transform, p, at, 22f, 15f, 6f, 6f, ads[i % ads.Length], 1.15f, m.billboard, new Color(.08f, .08f, .08f), m);
            }
        }

        static string Wrap(string name) => string.IsNullOrEmpty(name) ? "" : name.Replace(";", "\n").ToUpperInvariant();

        // Lightweight stand-ins until proper game-ready Florida vegetation is sourced.
        static void Cone(List<Vector3> v, List<int> t, Vector3 c, float r0, float r1, float h, int sides = 7)
        {
            int start = v.Count;
            for (int i = 0; i <= sides; i++)
            {
                float a = i * Mathf.PI * 2 / sides;
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                v.Add(c + d * r0); v.Add(c + d * r1 + Vector3.up * h);
                if (i > 0) { int q = start + (i - 1) * 2; t.AddRange(new[] { q, q + 1, q + 2, q + 2, q + 1, q + 3 }); }
            }
        }

        static Mesh PalmMesh(int variant)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var bark = new List<int>(); var leaf = new List<int>();
            var rnd = new System.Random(40 + variant);
            float bend = 0.35f + variant * 0.28f;
            Func<float, Vector3> spine = k => new Vector3(bend * k * k, 10f * k, 0);
            const int rings = 9, sides = 7;
            for (int r = 0; r <= rings; r++)
            {
                float k = r / (float)rings, rad = Mathf.Lerp(.27f, .15f, k) * (1f + 0.06f * Mathf.Sin(r * 2.7f));
                for (int s = 0; s <= sides; s++) { float a = s * Mathf.PI * 2 / sides; v.Add(spine(k) + new Vector3(Mathf.Cos(a) * rad, 0, Mathf.Sin(a) * rad)); uv.Add(new Vector2(s / (float)sides, k * 6f)); }
                if (r > 0) for (int s = 0; s < sides; s++) { int q = (r - 1) * (sides + 1) + s, w = sides + 1; bark.AddRange(new[] { q, q + w, q + 1, q + 1, q + w, q + w + 1 }); }
            }
            var top = spine(1f);
            int fronds = 15 + variant * 2;
            for (int f = 0; f < fronds; f++)
            {
                float az = f * 2.4f + (float)rnd.NextDouble() * .4f;
                float lift = (f % 3 == 0 ? 1.7f : f % 3 == 1 ? .9f : .25f) + (float)rnd.NextDouble() * .3f;
                float reach = 4.4f + (float)rnd.NextDouble() * 1.5f;
                var dir = new Vector3(Mathf.Cos(az), 0, Mathf.Sin(az)); var side = new Vector3(-dir.z, 0, dir.x);
                int start = v.Count; const int seg = 8;
                for (int i = 0; i <= seg; i++)
                {
                    float k = i / (float)seg, w = 0.68f * Mathf.Sin(Mathf.PI * Mathf.Min(.97f, k * .9f + .08f));
                    var c = top + dir * (reach * k) + Vector3.up * (lift * k - (lift + 2.7f) * k * k);
                    var droop = Vector3.down * w * .28f;
                    v.Add(c - side * w + droop); uv.Add(new Vector2(0, k)); v.Add(c + side * w + droop); uv.Add(new Vector2(1, k));
                    if (i > 0) { int q = start + (i - 1) * 2; leaf.AddRange(new[] { q, q + 2, q + 1, q + 1, q + 2, q + 3 }); }
                }
            }
            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.subMeshCount = 2; mesh.SetTriangles(bark, 0); mesh.SetTriangles(leaf, 1);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var file = Gen + "/Meshes/Palm_" + variant + ".asset";
            AssetDatabase.DeleteAsset(file); AssetDatabase.CreateAsset(mesh, file);
            return mesh;
        }

        static Material FrondMaterial()
        {
            var tex = new Texture2D(128, 256, TextureFormat.RGBA32, true);
            var rnd = new System.Random(7);
            var tones = new Color[32];
            for (int i = 0; i < 32; i++) tones[i] = Color.Lerp(new Color(.27f, .5f, .13f), new Color(.56f, .72f, .22f), (float)rnd.NextDouble());
            for (int y = 0; y < 256; y++) for (int x = 0; x < 128; x++)
            {
                float d = Mathf.Abs(x - 63.5f) / 63.5f, k = y / 255f;
                float env = Mathf.Lerp(1f, .25f, k * k);
                float slant = y + d * 46f;
                bool blade = d < env && Mathf.Repeat(slant, 9f) < 5.2f;
                bool rib = d < .05f;
                var c = rib ? new Color(.42f, .45f, .18f) : tones[(int)(slant / 9f) & 31];
                c.a = blade || rib ? 1f : 0f;
                tex.SetPixel(x, y, c);
            }
            tex.Apply();
            const string file = Gen + "/Materials/Frond.png";
            File.WriteAllBytes(file, tex.EncodeToPNG());
            AssetDatabase.ImportAsset(file);
            var imp = (TextureImporter)AssetImporter.GetAtPath(file);
            imp.alphaIsTransparency = true; imp.mipMapsPreserveCoverage = true; imp.alphaTestReferenceValue = 0.4f; imp.SaveAndReimport();
            var mat = Lit("PalmFrond", Color.white, .25f);
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(file));
            mat.SetFloat("_AlphaClip", 1f); mat.SetFloat("_Cutoff", .4f); mat.EnableKeyword("_ALPHATEST_ON");
            mat.SetFloat("_Cull", 0f); mat.renderQueue = 2450; mat.enableInstancing = true;
            return mat;
        }

        [Serializable] class BuildingRow { public float h; public string k; public float[] p; }
        [Serializable] class BuildingFile { public BuildingRow[] items; }
        static readonly HashSet<(int, int)> occupied = new HashSet<(int, int)>();

        static Material Facade(string name, Color wall, Color glass, int kind, int seed, float smooth)
        {
            const int size = 256, cells = 8, cw = size / cells;
            var rnd = new System.Random(seed);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            var pane = new Color[cells * cells];
            for (int i = 0; i < pane.Length; i++)
            {
                double roll = rnd.NextDouble();
                float spread = kind == 0 ? .1f : .32f, blindChance = kind == 0 ? .03f : .16f;
                pane[i] = roll < blindChance ? Color.Lerp(glass, new Color(.72f, .7f, .62f), kind == 0 ? .25f : .55f) : glass * (float)(1f - spread / 2f + rnd.NextDouble() * spread);
                pane[i].a = 1f;
            }
            var missing = new bool[cells * cells];
            for (int i = 0; i < missing.Length; i++) missing[i] = kind == 3 && rnd.NextDouble() < .22;
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                int ix = x / cw, iy = y / cw, px = x % cw, py = y % cw;
                bool win;
                switch (kind)
                {
                    case 0: win = px > 0 && py > 1; break;
                    case 1: win = px > 8 && px < 24 && py > 9 && py < 26; break;
                    case 2: win = py > 12 && py < 26 && px % 11 != 0; break;
                    default: win = !missing[ix + iy * cells] && px > 10 && px < 22 && py > 10 && py < 25; break;
                }
                float grain = 1f + (float)(rnd.NextDouble() - .5) * .05f;
                var c = win ? pane[ix + iy * cells] : wall * grain;
                c.a = 1f;
                tex.SetPixel(x, y, c);
            }
            tex.Apply();
            string file = $"{Gen}/Materials/{name}.png";
            File.WriteAllBytes(file, tex.EncodeToPNG());
            AssetDatabase.ImportAsset(file);
            var mat = Lit(name, Color.white, smooth);
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(file));
            return mat;
        }

        const int FacadeCount = 10, RoofSlot = 10, ApronSlot = 11, SlotCount = 12;
        class Bucket { public List<Vector3>[] v = new List<Vector3>[SlotCount]; public List<Vector2>[] uv = new List<Vector2>[SlotCount]; public List<int>[] t = new List<int>[SlotCount]; }

        static Dictionary<(int, int), List<float>> cells;
        static RoutePath lookupPath;
        static Func<float, float> lookupSouth;

        static void PrepareLookup(RoutePath p, Func<float, float> south)
        {
            lookupPath = p; lookupSouth = south;
            cells = new Dictionary<(int, int), List<float>>();
            for (float s = 0; s < p.Length; s += 20f)
            {
                var c = p.Center(s); var key = ((int)Mathf.Floor(c.x / 150f), (int)Mathf.Floor(c.z / 150f));
                if (!cells.TryGetValue(key, out var list)) cells[key] = list = new List<float>();
                list.Add(s);
            }
        }

        static bool Locate(Vector2 q, out float s, out float lat)
        {
            int cx = (int)Mathf.Floor(q.x / 150f), cz = (int)Mathf.Floor(q.y / 150f);
            float best = float.MaxValue; s = -1; lat = 0;
            for (int ring = 1; ring <= 6 && s < 0; ring += 5)
                for (int a = -ring; a <= ring; a++) for (int b = -ring; b <= ring; b++)
                    if (cells.TryGetValue((cx + a, cz + b), out var list))
                        foreach (var ss in list) { var c = lookupPath.Center(ss); float d = (c.x - q.x) * (c.x - q.x) + (c.z - q.y) * (c.z - q.y); if (d < best) { best = d; s = ss; } }
            if (s < 0) return false;
            var at = lookupPath.Center(s);
            lat = Vector3.Dot(new Vector3(q.x, at.y, q.y) - at, lookupPath.Right(s));
            return true;
        }

        static bool OnRoad(Vector2 q, float margin = 14f)
        {
            if (!Locate(q, out var s, out var lat)) return false;
            return lat > lookupSouth(s) - lookupPath.HalfWidth(s) - margin && lat < lookupPath.HalfWidth(s) + margin + 2f;
        }

        static void Fan(Bucket bk, int slot, List<Vector2> pts, float y, float uvScale)
        {
            var c = new Vector2(pts.Average(q => q.x), pts.Average(q => q.y));
            var v = bk.v[slot]; var uv = bk.uv[slot]; int n = v.Count;
            v.Add(new Vector3(c.x, y, c.y)); uv.Add(c / uvScale);
            for (int i = 0; i < pts.Count; i++)
            {
                v.Add(new Vector3(pts[i].x, y, pts[i].y)); uv.Add(pts[i] / uvScale);
                bk.t[slot].AddRange(new[] { n, n + 1 + (i + 1) % pts.Count, n + 1 + i });
            }
        }

        static void Walls(Bucket bk, int style, List<Vector2> pts, float y0, float y1, float shift)
        {
            const float TileU = 28.8f, TileV = 27.2f;
            float run = shift;
            for (int i = 0; i < pts.Count; i++)
            {
                var a = pts[i]; var c = pts[(i + 1) % pts.Count]; float len = (c - a).magnitude;
                var v = bk.v[style]; var uv = bk.uv[style]; int n = v.Count;
                v.Add(new Vector3(a.x, y0, a.y)); v.Add(new Vector3(a.x, y1, a.y)); v.Add(new Vector3(c.x, y0, c.y)); v.Add(new Vector3(c.x, y1, c.y));
                float u0 = Mathf.Round(run / 3.6f) * 3.6f / TileU, u1 = u0 + Mathf.Max(1f, Mathf.Round(len / 3.6f)) * 3.6f / TileU, top = Mathf.Max(1f, Mathf.Round((y1 - y0) / 3.4f)) * 3.4f / TileV;
                uv.Add(new Vector2(u0, shift)); uv.Add(new Vector2(u0, shift + top)); uv.Add(new Vector2(u1, shift)); uv.Add(new Vector2(u1, shift + top));
                bk.t[style].AddRange(new[] { n, n + 1, n + 2, n + 2, n + 1, n + 3 });
                run += len;
            }
        }

        static Bucket Get(Dictionary<(int, int), Bucket> map, Vector2 at, float size)
        {
            var key = ((int)Mathf.Floor(at.x / size), (int)Mathf.Floor(at.y / size));
            if (!map.TryGetValue(key, out var bk))
            {
                map[key] = bk = new Bucket();
                for (int i = 0; i < SlotCount; i++) { bk.v[i] = new List<Vector3>(); bk.uv[i] = new List<Vector2>(); bk.t[i] = new List<int>(); }
            }
            return bk;
        }

        static int Flush(Dictionary<(int, int), Bucket> map, string name, Material[] mats, int slots, bool shadows)
        {
            var parent = new GameObject(name); int chunk = 0;
            foreach (var bk in map.Values)
            {
                var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
                var all = new List<Vector3>(); var allUv = new List<Vector2>(); var subs = new List<int[]>();
                for (int i = 0; i < slots; i++) { int off = all.Count; all.AddRange(bk.v[i]); allUv.AddRange(bk.uv[i]); subs.Add(bk.t[i].Select(x => x + off).ToArray()); }
                mesh.SetVertices(all); mesh.SetUVs(0, allUv); mesh.subMeshCount = slots;
                for (int i = 0; i < slots; i++) mesh.SetTriangles(subs[i], i);
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                var meshFile = $"{Gen}/Meshes/{name}_{chunk++}.asset";
                AssetDatabase.DeleteAsset(meshFile); AssetDatabase.CreateAsset(mesh, meshFile);
                var go = new GameObject(name + "Block", typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(parent.transform);
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.GetComponent<MeshRenderer>(); r.sharedMaterials = mats;
                r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            }
            return chunk;
        }

        static bool Inside(List<Vector2> poly, Vector2 q)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                if ((poly[i].y > q.y) != (poly[j].y > q.y) && q.x < (poly[j].x - poly[i].x) * (q.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x) inside = !inside;
            return inside;
        }

        static readonly List<Vector2> exitCorridor = new List<Vector2>();

        static bool NearExit(Vector2 q, float margin)
        {
            float m2 = margin * margin;
            foreach (var c in exitCorridor) if ((c - q).sqrMagnitude < m2) return true;
            return false;
        }

        // Every exit has the same engineered shape, placed at a real exit: a lane peeling off the shoulder, a wide sweep down to street level, and a merge lane back on.
        static void ComputeExits(RoutePath p)
        {
            const float minGap = 1900f, first = 900f, clearOverpass = 60f, streetY = .35f;
            var rows = new List<RouteExitRow>();
            float last = -9999f;
            foreach (var e in p.File.exits.Where(x => System.Text.RegularExpressions.Regex.IsMatch(x.@ref ?? "", "^[0-9]+[A-Z]?$")).OrderBy(x => x.s))
            {
                if (rows.Count >= 12) break;
                float at = -1f;
                for (int n = 0; n <= 12 && at < 0f; n++)
                {
                    float c = e.s + (n % 2 == 0 ? n : -n - 1) * 20f;
                    if (c < first || c - last < minGap || c + RoutePath.ExitSpan > p.Length - 200f) continue;
                    if (!p.File.overpasses.Any(o => o.s > c - clearOverpass && o.s < c + RoutePath.ExitSpan + clearOverpass)) at = c;
                }
                if (at < 0f) continue;
                var xyz = new List<float>();
                int steps = Mathf.RoundToInt(RoutePath.ExitSpan / 5f);
                for (int i = 0; i <= steps; i++)
                {
                    float d = RoutePath.ExitSpan * i / steps, s = at + d;
                    var q = p.Point(s, p.HalfWidth(s) + RoutePath.ExitOffset(d));
                    float k = RoutePath.ExitBlend(d);
                    xyz.Add(q.x); xyz.Add(Mathf.Lerp(q.y, streetY, k)); xyz.Add(q.z);
                }
                rows.Add(new RouteExitRow { divergeS = at, mergeS = at + RoutePath.ExitSpan, xyz = xyz.ToArray() });
                last = at;
            }
            var data = new RouteExitFile { items = rows.ToArray() };
            string outFile = $"Assets/Resources/Data/Routes/{RouteId}_exits.json";
            File.WriteAllText(outFile, JsonUtility.ToJson(data));
            AssetDatabase.ImportAsset(outFile);
            p.LoadJunctions(data);
            exitCorridor.Clear();
            foreach (var j in p.Junctions)
                for (float s = 0f; s < j.road.Length; s += 8f) { var c = j.road.Center(s); exitCorridor.Add(new Vector2(c.x, c.z)); }
            Debug.Log("[FloridaSlice] drivable exits " + rows.Count + ": " + string.Join(", ", rows.Select(r => Mathf.RoundToInt(r.divergeS))));
        }

        static void BuildExitRoads(GameObject root, RoutePath p, Mats m)
        {
            var g = new GameObject("DrivableExits"); g.transform.SetParent(root.transform);
            foreach (var j in p.Junctions)
            {
                var r = j.road; Func<float, float> h = r.HalfWidth; float open = RoutePath.ExitOpen, gap = RoutePath.ExitWallGap;
                // Stays on the shoulder at both ends, so it never paints over the highway's lanes.
                Func<float, float> leftEdge = s => -h(s) - Mathf.Lerp(.2f, gap, Mathf.Min(Mathf.InverseLerp(open, open + 20f, s), Mathf.InverseLerp(open, open + 20f, r.Length - s)));
                float pave = RoutePath.ExitHold; // beside the highway the pavement belongs to the highway
                Add(g, "Road", Ribbon(r, leftEdge, s => h(s) + gap, 0.05f, 7, 7, pave - 12f, r.Length - pave + 12f, 3f), m.asphalt);
                Add(g, "EdgeL", Ribbon(r, s => -h(s) - .08f, s => -h(s) + .08f, 0.07f, 1, 1, pave, r.Length - pave, 3f), m.yellow);
                Add(g, "EdgeR", Ribbon(r, s => h(s) - .08f, s => h(s) + .08f, 0.07f, 1, 1, pave, r.Length - pave, 3f), m.white);
                Add(g, "WallL", WallStrip(r, s => -h(s) - gap, open, r.Length - open, -0.1f, 0.9f), m.concrete, true);
                Add(g, "WallR", WallStrip(r, s => h(s) + gap, RoutePath.ExitTaper, r.Length - RoutePath.ExitTaper, -0.1f, 0.9f), m.concrete, true);
                for (float s = 15f; s < r.Length; s += 24f)
                {
                    float y = r.Center(s).y; if (y < 2.4f) continue;
                    var at = r.Center(s);
                    Box(g.transform, new Vector3(at.x, y / 2f - .4f, at.z), Quaternion.identity, new Vector3(1.3f, y - .8f, 1.3f), m.concrete, "Pillar");
                }
            }
        }

        static void BuildBuildings(RoutePath p, Func<float, float> south, Mats m)
        {
            occupied.Clear();
            var file = $"Tools/Data/{RouteId}_buildings.json";
            if (!File.Exists(file)) { Debug.LogError("[FloridaSlice] no building data"); return; }
            var data = JsonUtility.FromJson<BuildingFile>(File.ReadAllText(file));
            var mats = new[]
            {
                Facade("FacadeGlassBlue", new Color(.3f, .37f, .44f), new Color(.16f, .28f, .4f), 0, 1, .8f),
                Facade("FacadeGlassGreen", new Color(.32f, .4f, .4f), new Color(.17f, .33f, .35f), 0, 2, .8f),
                Facade("FacadeGlassDark", new Color(.2f, .22f, .26f), new Color(.1f, .13f, .18f), 0, 3, .85f),
                Facade("FacadeBandWhite", new Color(.84f, .84f, .8f), new Color(.2f, .3f, .4f), 2, 4, .45f),
                Facade("FacadeBandTan", new Color(.72f, .66f, .56f), new Color(.17f, .23f, .3f), 2, 5, .35f),
                Facade("FacadeCondo", new Color(.88f, .86f, .79f), new Color(.2f, .27f, .35f), 1, 6, .3f),
                Facade("FacadeStuccoCream", new Color(.9f, .84f, .68f), new Color(.2f, .23f, .28f), 3, 7, .15f),
                Facade("FacadeStuccoWhite", new Color(.9f, .9f, .86f), new Color(.2f, .24f, .3f), 3, 8, .15f),
                Facade("FacadeStuccoCoral", new Color(.88f, .67f, .57f), new Color(.2f, .23f, .28f), 3, 9, .15f),
                Facade("FacadeStuccoTeal", new Color(.58f, .78f, .74f), new Color(.2f, .25f, .3f), 3, 10, .15f),
                Lit("Roof", new Color(.5f, .5f, .48f), .1f),
                Lit("Apron", new Color(.8f, .79f, .74f), .1f),
            };
            var rnd = new System.Random(305);
            var map = new Dictionary<(int, int), Bucket>();
            int built = 0, dropped = 0, solid = 0;
            foreach (var b in data.items)
            {
                var pts = new List<Vector2>();
                for (int i = 0; i + 1 < b.p.Length; i += 2) pts.Add(new Vector2(b.p[i], b.p[i + 1]));
                if (pts.Count < 3 || pts.Any(q => OnRoad(q) || NearExit(q, 12f)) || exitCorridor.Any(c => Inside(pts, c))) { dropped++; continue; }
                float area = 0;
                for (int i = 0; i < pts.Count; i++) { var a = pts[i]; var c = pts[(i + 1) % pts.Count]; area += a.x * c.y - c.x * a.y; }
                if (area < 0) pts.Reverse();
                float h = b.h > 2f ? b.h : 3.5f + (float)rnd.NextDouble() * 6f;
                int style = h > 45f ? rnd.Next(3) + (rnd.NextDouble() < .2 ? 3 : 0) : h > 14f ? 3 + rnd.Next(3) : 6 + rnd.Next(4);
                var mid = new Vector2(pts.Average(q => q.x), pts.Average(q => q.y));
                var bk = Get(map, mid, 900f);
                Walls(bk, style, pts, -0.3f, h, (float)rnd.NextDouble() * 3f);
                Fan(bk, RoofSlot, pts, h, 8f);
                float rad = pts.Average(q => (q - mid).magnitude);
                Fan(bk, ApronSlot, pts.Select(q => mid + (q - mid) * (1f + 2.4f / Mathf.Max(3f, rad))).ToList(), -0.14f, 4f);
                if (h > 30f)
                {
                    var core = pts.Select(q => mid + (q - mid) * .48f).ToList();
                    float h2 = h + 3f + h * .05f;
                    Walls(bk, style, core, h, h2, 1f);
                    Fan(bk, RoofSlot, core, h2, 8f);
                }
                float minX = pts.Min(q => q.x), maxX = pts.Max(q => q.x), minZ = pts.Min(q => q.y), maxZ = pts.Max(q => q.y);
                for (int ix = (int)(minX / 30f); ix <= (int)(maxX / 30f); ix++) for (int iz = (int)(minZ / 30f); iz <= (int)(maxZ / 30f); iz++) occupied.Add((ix, iz));
                if (Locate(mid, out var cs, out var clat) && Mathf.Abs(clat) < 500f)
                {
                    // Small blocks that follow the real footprint; one box around an angled building can span the highway.
                    var tan = p.Tangent(cs); var right = p.Right(cs);
                    for (float gx = minX + 4f; gx < maxX; gx += 9f) for (float gz = minZ + 4f; gz < maxZ; gz += 9f)
                    {
                        if (!Inside(pts, new Vector2(gx, gz))) continue;
                        var d = new Vector3(gx - mid.x, 0, gz - mid.y);
                        float bs = cs + Vector3.Dot(d, tan), bl = clat + Vector3.Dot(d, right);
                        if (bl - 5f < p.HalfWidth(bs) + 5f && bl + 5f > south(bs) - p.HalfWidth(bs) - 5f) continue;
                        Solid(bs, bl, 5f, 5f, "building"); solid++;
                    }
                }
                built++;
            }
            int chunks = Flush(map, "City", mats, SlotCount, true);
            Debug.Log($"[FloridaSlice] buildings placed {built}, removed from roadway {dropped}, solid {solid}, chunks {chunks}");
        }

        [Serializable] class GroundRow { public float w; public float[] p; }
        [Serializable] class GroundFile { public GroundRow[] roads; public GroundRow[] lots; }

        static void BuildGroundDetail(RoutePath p, Mats m)
        {
            var file = $"Tools/Data/{RouteId}_ground.json";
            if (!File.Exists(file)) { Debug.LogError("[FloridaSlice] no ground data"); return; }
            var data = JsonUtility.FromJson<GroundFile>(File.ReadAllText(file));
            var street = Lit("LocalStreet", new Color(.58f, .58f, .57f), .15f, "asphalt_02");
            var line = Lit("StreetLine", new Color(.86f, .68f, .15f), .2f);
            var stripes = new Texture2D(64, 64, TextureFormat.RGBA32, true);
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
                stripes.SetPixel(x, y, (x < 3 && y > 6) ? new Color(.86f, .86f, .82f) : new Color(.3f, .3f, .3f) * (1f + (x * 7 + y * 13) % 5 * .012f));
            stripes.Apply();
            const string sf = Gen + "/Materials/ParkingStripes.png";
            File.WriteAllBytes(sf, stripes.EncodeToPNG()); AssetDatabase.ImportAsset(sf);
            var lot = Lit("ParkingLot", Color.white, .15f);
            lot.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(sf));
            var mats = new[] { street, line, lot };
            var map = new Dictionary<(int, int), Bucket>();
            int seg = 0;
            foreach (var r in data.roads)
                for (int i = 0; i + 3 < r.p.Length; i += 2)
                {
                    var a = new Vector2(r.p[i], r.p[i + 1]); var b = new Vector2(r.p[i + 2], r.p[i + 3]);
                    if (OnRoad((a + b) / 2f, 3f)) continue;
                    var bk = Get(map, (a + b) / 2f, 1200f);
                    var dir = (b - a).normalized; var n = new Vector2(-dir.y, dir.x);
                    Strip(bk, 0, a, b, n * r.w / 2f, -0.17f, r.w);
                    if (r.w >= 10f) Strip(bk, 1, a, b, n * .09f, -0.155f, 1f);
                    seg++;
                }
            foreach (var l in data.lots)
            {
                var pts = new List<Vector2>();
                for (int i = 0; i + 1 < l.p.Length; i += 2) pts.Add(new Vector2(l.p[i], l.p[i + 1]));
                if (pts.Any(q => OnRoad(q, 3f) || NearExit(q, 8f))) continue;
                float area = 0;
                for (int i = 0; i < pts.Count; i++) { var a = pts[i]; var c = pts[(i + 1) % pts.Count]; area += a.x * c.y - c.x * a.y; }
                if (area < 0) pts.Reverse();
                Fan(Get(map, pts[0], 1200f), 2, pts, -0.16f, 5.4f);
            }
            Flush(map, "Streets", mats, 3, false);
            Debug.Log($"[FloridaSlice] street segments {seg}");
        }

        static void Strip(Bucket bk, int slot, Vector2 a, Vector2 b, Vector2 half, float y, float uvW)
        {
            var v = bk.v[slot]; var uv = bk.uv[slot]; int n = v.Count; float len = (b - a).magnitude;
            v.Add(new Vector3(a.x - half.x, y, a.y - half.y)); v.Add(new Vector3(a.x + half.x, y, a.y + half.y));
            v.Add(new Vector3(b.x - half.x, y, b.y - half.y)); v.Add(new Vector3(b.x + half.x, y, b.y + half.y));
            uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(uvW / 7f, 0)); uv.Add(new Vector2(0, len / 7f)); uv.Add(new Vector2(uvW / 7f, len / 7f));
            bk.t[slot].AddRange(new[] { n, n + 1, n + 2, n + 2, n + 1, n + 3 });
        }

        static Mesh WallStrip(RoutePath p, Func<float, float> lat, float from, float to, float y0, float y1)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            int rows = Mathf.Max(1, Mathf.CeilToInt((to - from) / 8f));
            for (int r = 0; r <= rows; r++)
            {
                float s = from + (to - from) * r / rows; var c = p.Point(s, lat(s));
                v.Add(c + Vector3.up * y0); v.Add(c + Vector3.up * y1);
                uv.Add(new Vector2(s / 4f, 0)); uv.Add(new Vector2(s / 4f, (y1 - y0) / 4f));
                if (r > 0) { int a = (r - 1) * 2; t.AddRange(new[] { a, a + 1, a + 2, a + 2, a + 1, a + 3 }); }
            }
            // The back face needs its own vertices: shared ones average opposite normals to zero and render black.
            int front = v.Count, frontTris = t.Count;
            v.AddRange(v.ToArray()); uv.AddRange(uv.ToArray());
            for (int i = 0; i < frontTris; i += 3) t.AddRange(new[] { t[i] + front, t[i + 2] + front, t[i + 1] + front });
            return Finish(v, uv, t);
        }

        static void BuildPillars(GameObject root, RoutePath p, Func<float, float> south, Mats m)
        {
            var g = new GameObject("Pillars"); g.transform.SetParent(root.transform);
            int count = 0;
            for (float s = 16f; s < p.Length; s += 32f)
            {
                float y = p.Center(s).y;
                if (y < 2.4f) continue;
                var rot = Quaternion.LookRotation(p.Tangent(s), Vector3.up);
                foreach (var c in new[] { 0f, south(s) })
                {
                    var at = p.Point(s, c); float top = y - 1.5f;
                    Box(g.transform, new Vector3(at.x, top / 2f, at.z), rot, new Vector3(1.7f, top, 1.5f), m.concrete, "Pillar");
                    Box(g.transform, new Vector3(at.x, y - 1.15f, at.z), rot, new Vector3(p.HalfWidth(s) * 2f + 2f, 1.1f, 1.8f), m.concrete, "Cap");
                    count++;
                }
            }
            Debug.Log("[FloridaSlice] pillars " + count);
        }

        static void BuildSoundWalls(GameObject root, RoutePath p, Func<float, float> south, Mats m)
        {
            var g = new GameObject("SoundWalls"); g.transform.SetParent(root.transform);
            var panels = Textured("SoundWall", MakeConcrete("SoundWallTex", new Color(.78f, .75f, .68f), 2), .15f);
            float start = -1; int count = 0;
            for (float s = 0; s < p.Length; s += 10f)
            {
                bool on = p.HasSoundWall(s);
                if (on && start < 0) start = s;
                if ((!on || s + 10f >= p.Length) && start >= 0)
                {
                    float a = start; start = -1;
                    Add(g, "North", WallStrip(p, q => p.HalfWidth(q) + RoutePath.SoundWallOffset, a, s, -0.3f, 5.2f), panels, true);
                    Add(g, "South", WallStrip(p, q => south(q) - p.HalfWidth(q) - RoutePath.SoundWallOffset, a, s, -0.3f, 5.2f), panels, true);
                    count++;
                }
            }
            Debug.Log("[FloridaSlice] sound wall sections " + count);
        }

        static void LampBox(List<Vector3> v, List<int> t, Vector3 c, Vector3 size)
        {
            int n = v.Count;
            for (int i = 0; i < 8; i++) v.Add(c + new Vector3((i & 1) == 0 ? -size.x : size.x, (i & 2) == 0 ? -size.y : size.y, (i & 4) == 0 ? -size.z : size.z) * .5f);
            int[] q = { 0, 2, 1, 1, 2, 3, 4, 5, 6, 5, 7, 6, 0, 1, 4, 1, 5, 4, 2, 6, 3, 3, 6, 7, 0, 4, 2, 2, 4, 6, 1, 3, 5, 3, 7, 5 };
            foreach (var i in q) t.Add(n + i);
        }

        // Twin-arm lights standing on the concrete median, as on urban stretches of I-95.
        static void BuildLamps(RoutePath p)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            Cone(v, t, Vector3.zero, .13f, .08f, 12.5f, 8);
            LampBox(v, t, new Vector3(1.7f, 12.5f, 0), new Vector3(3.4f, .09f, .09f));
            LampBox(v, t, new Vector3(-1.7f, 12.5f, 0), new Vector3(3.4f, .09f, .09f));
            LampBox(v, t, new Vector3(3.5f, 12.42f, 0), new Vector3(.75f, .13f, .32f));
            LampBox(v, t, new Vector3(-3.5f, 12.42f, 0), new Vector3(.75f, .13f, .32f));
            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(v); mesh.SetTriangles(t, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var file = Gen + "/Meshes/StreetLamp.asset";
            AssetDatabase.DeleteAsset(file); AssetDatabase.CreateAsset(mesh, file);
            var mat = Lit("LampSteel", new Color(.47f, .49f, .5f), .45f); mat.enableInstancing = true;
            var g = new GameObject("StreetLamps");
            int count = 0;
            for (float s = 40f; s < p.Length - 30f; s += 75f)
            {
                var go = new GameObject("Lamp", typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(g.transform);
                go.transform.position = p.Point(s, -p.HalfWidth(s) - RoutePath.BarrierLeft) + Vector3.up * .7f;
                go.transform.rotation = Quaternion.LookRotation(p.Tangent(s), Vector3.up);
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                go.GetComponent<MeshRenderer>().sharedMaterial = mat;
                count++;
            }
            Debug.Log("[FloridaSlice] street lamps " + count);
        }

        static void BuildTrees(RoutePath p, Func<float, float> south)
        {
            var bark = Lit("PalmBark", new Color(.56f, .49f, .39f), .1f); bark.enableInstancing = true;
            var frond = FrondMaterial();
            var palms = new[] { PalmMesh(0), PalmMesh(1), PalmMesh(2) };
            var dome = new List<Vector3>(); var domeT = new List<int>();
            Cone(dome, domeT, Vector3.zero, .95f, .55f, .55f, 7); Cone(dome, domeT, Vector3.up * .55f, .55f, .04f, .4f, 7);
            var shrubMesh = new Mesh(); shrubMesh.SetVertices(dome); shrubMesh.SetTriangles(domeT, 0); shrubMesh.RecalculateNormals(); shrubMesh.RecalculateBounds();
            AssetDatabase.DeleteAsset(Gen + "/Meshes/Shrub.asset"); AssetDatabase.CreateAsset(shrubMesh, Gen + "/Meshes/Shrub.asset");
            var shrubMat = Lit("Shrub", new Color(.13f, .36f, .13f), .12f); shrubMat.enableInstancing = true;
            var g = new GameObject("Trees");
            var rnd = new System.Random(95);
            const int count = 2200;
            float gap = p.Length / count;
            for (int i = 0; i < count; i++)
            {
                float s = (i + (float)rnd.NextDouble()) * gap, off = 12f + (float)(rnd.NextDouble() * rnd.NextDouble()) * 190f;
                bool right = rnd.NextDouble() < .55;
                float lat = right ? p.HalfWidth(s) + off : south(s) - p.HalfWidth(s) - off;
                var spot = p.Point(s, lat);
                if (occupied.Contains(((int)(spot.x / 30f), (int)(spot.z / 30f))) || OnRoad(new Vector2(spot.x, spot.z), 11f) || NearExit(new Vector2(spot.x, spot.z), 8f)) continue;
                var t = new GameObject("Palm", typeof(MeshFilter), typeof(MeshRenderer));
                t.transform.SetParent(g.transform);
                t.transform.position = new Vector3(spot.x, -.25f, spot.z);
                t.transform.rotation = Quaternion.Euler(0, (float)rnd.NextDouble() * 360f, 0);
                t.transform.localScale = Vector3.one * (.85f + (float)rnd.NextDouble() * .55f);
                t.GetComponent<MeshFilter>().sharedMesh = palms[rnd.Next(3)];
                t.GetComponent<MeshRenderer>().sharedMaterials = new[] { bark, frond };
                Solid(s, lat, 0.3f, 0.3f, "tree");
            }
            var sg = new GameObject("Shrubs");
            for (int i = 0; i < 2600; i++)
            {
                float s = (float)rnd.NextDouble() * p.Length, off = 10f + (float)rnd.NextDouble() * 45f;
                bool right = rnd.NextDouble() < .5;
                float lat = right ? p.HalfWidth(s) + off : south(s) - p.HalfWidth(s) - off;
                var spot = p.Point(s, lat);
                if (occupied.Contains(((int)(spot.x / 30f), (int)(spot.z / 30f))) || OnRoad(new Vector2(spot.x, spot.z), 9f) || NearExit(new Vector2(spot.x, spot.z), 8f)) continue;
                var t = new GameObject("Shrub", typeof(MeshFilter), typeof(MeshRenderer));
                t.transform.SetParent(sg.transform);
                t.transform.position = new Vector3(spot.x, -.25f, spot.z);
                t.transform.localScale = new Vector3(1f + (float)rnd.NextDouble() * 1.8f, .8f + (float)rnd.NextDouble() * .9f, 1f + (float)rnd.NextDouble() * 1.8f);
                t.GetComponent<MeshFilter>().sharedMesh = shrubMesh;
                var r = t.GetComponent<MeshRenderer>(); r.sharedMaterial = shrubMat; r.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        static Bounds Bounds(GameObject g)
        {
            var rs = g.GetComponentsInChildren<Renderer>();
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        static PlayerCar BuildCar(Camera cam, Mats m)
        {
            var root = new GameObject("PlayerCar");
            var player = root.AddComponent<PlayerCar>();
            root.AddComponent<TrafficManager>();
            player.chaseCamera = cam;
            player.routeId = RouteId;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(CarPath);
            if (model == null) { Debug.LogError("[FloridaSlice] car model missing"); return player; }
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            var holder = new GameObject("Model");
            holder.transform.SetParent(root.transform, false);
            inst.transform.position = Vector3.zero;
            var b = Bounds(inst);
            inst.transform.localScale *= 4.63f / Mathf.Max(b.size.x, b.size.z);
            b = Bounds(inst);

            // Rest the car on its tires. Overall bounds are unreliable because some interior meshes overshoot.
            var wheels = inst.GetComponentsInChildren<MeshRenderer>().Where(r => r.name.Contains("wheels")).ToList();
            float ground = wheels.Min(r => r.bounds.min.y);
            inst.transform.position -= new Vector3(b.center.x, ground, b.center.z);
            inst.transform.SetParent(holder.transform, true);
            Debug.Log($"[FloridaSlice] car lowered by {ground:F2} m onto its tires");

            FixCarMaterials(inst);
            SplitWheels(root.transform, wheels);
            BuildSteeringPivot(holder.transform, inst);
            MakeBodyDeformable(inst);
            BuildMirrors(holder.transform, inst);
            BuildShifter(holder.transform, inst);
            BuildGauges(holder.transform, m);
            var smokeShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var smokeMat = new Material(smokeShader); smokeMat.SetFloat("_Surface", 1f); smokeMat.SetFloat("_Blend", 0f); smokeMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); smokeMat.renderQueue = 3000;
            smokeMat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); smokeMat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha); smokeMat.SetInt("_ZWrite", 0);
            smokeMat.SetTexture("_BaseMap", SoftDot());
            player.smokeMaterial = Save(smokeMat, "Smoke");
            return player;
        }

        // Corrects problems in the downloaded model: leaky roof shadows, heavy glass tint and broken paint normals.
        static void FixCarMaterials(GameObject inst)
        {
            var copies = new Dictionary<Material, Material>();
            foreach (var r in inst.GetComponentsInChildren<MeshRenderer>())
            {
                bool glass = r.name.Contains("windows");
                r.shadowCastingMode = glass ? ShadowCastingMode.Off : ShadowCastingMode.TwoSided;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var src = mats[i];
                    bool paint = src.name.StartsWith("main_paint") || src.name.StartsWith("tyre");
                    if (!glass && !paint) continue;
                    if (!copies.TryGetValue(src, out var copy))
                    {
                        copy = new Material(src);
                        if (paint && copy.HasProperty("normalTexture")) { copy.SetTexture("normalTexture", null); copy.DisableKeyword("_NORMALMAP"); }
                        if (glass && copy.HasProperty("baseColorFactor")) { var c = copy.GetColor("baseColorFactor"); c.a *= 0.3f; copy.SetColor("baseColorFactor", c); }
                        Debug.Log($"[Check] {src.name} shader {src.shader.name} hasNormal {src.HasProperty("normalTexture")} hasColor {src.HasProperty("baseColorFactor")}");
                        Save(copy, "Car_" + src.name.Replace(".", "_"));
                        copies[src] = copy;
                    }
                    mats[i] = copy;
                }
                r.sharedMaterials = mats;
            }
        }

        static void SplitWheels(Transform root, List<MeshRenderer> wheels)
        {
            string[] names = { "FL", "FR", "RL", "RR" };
            Func<Vector3, int> quadrant = p => (p.z > 0 ? 0 : 2) + (p.x < 0 ? 0 : 1);
            var pivotMin = new Vector3[4]; var pivotMax = new Vector3[4]; var seen = new bool[4];
            foreach (var r in wheels)
            {
                var mesh = r.GetComponent<MeshFilter>().sharedMesh; var m = r.transform.localToWorldMatrix;
                foreach (var v in mesh.vertices)
                {
                    var w = m.MultiplyPoint3x4(v); int q = quadrant(w);
                    if (!seen[q]) { pivotMin[q] = pivotMax[q] = w; seen[q] = true; }
                    pivotMin[q] = Vector3.Min(pivotMin[q], w); pivotMax[q] = Vector3.Max(pivotMax[q], w);
                }
            }
            for (int q = 0; q < 4; q++)
            {
                var pivot = (pivotMin[q] + pivotMax[q]) / 2f;
                var node = new GameObject("Wheel_" + names[q]).transform;
                node.SetParent(root, false); node.position = pivot;
                var spin = new GameObject("Spin").transform;
                spin.SetParent(node, false);
                foreach (var r in wheels)
                {
                    var src = r.GetComponent<MeshFilter>().sharedMesh; var m = r.transform.localToWorldMatrix;
                    var verts = src.vertices; var norms = src.normals; var uvs = src.uv; var tris = src.triangles;
                    var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
                    var map = new Dictionary<int, int>();
                    for (int i = 0; i < tris.Length; i += 3)
                    {
                        var c = (m.MultiplyPoint3x4(verts[tris[i]]) + m.MultiplyPoint3x4(verts[tris[i + 1]]) + m.MultiplyPoint3x4(verts[tris[i + 2]])) / 3f;
                        if (quadrant(c) != q) continue;
                        for (int j = 0; j < 3; j++)
                        {
                            int id = tris[i + j];
                            if (!map.TryGetValue(id, out int ni))
                            {
                                ni = v.Count; map[id] = ni;
                                v.Add(m.MultiplyPoint3x4(verts[id]) - pivot);
                                n.Add(m.MultiplyVector(norms[id]).normalized);
                                uv.Add(uvs[id]);
                            }
                            t.Add(ni);
                        }
                    }
                    if (t.Count == 0) continue;
                    var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
                    mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
                    mesh.RecalculateTangents(); mesh.RecalculateBounds();
                    var file = $"{Gen}/Meshes/Wheel_{names[q]}_{meshCount++}.asset";
                    AssetDatabase.DeleteAsset(file); AssetDatabase.CreateAsset(mesh, file);
                    var go = new GameObject(r.sharedMaterial.name, typeof(MeshFilter), typeof(MeshRenderer));
                    go.transform.SetParent(spin, false);
                    go.GetComponent<MeshFilter>().sharedMesh = mesh;
                    go.GetComponent<MeshRenderer>().sharedMaterials = r.sharedMaterials;
                }
            }
            foreach (var r in wheels) UnityEngine.Object.DestroyImmediate(r.gameObject);
        }

        // Live instruments laid over the model's painted-on dials. Positions measured from the instrument panel.
        static void BuildGauges(Transform holder, Mats m)
        {
            var root = new GameObject("Gauges").transform;
            root.SetParent(holder, false); root.localPosition = new Vector3(0, 0, 0.872f);
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            Func<string, Color, Material> mat = (n, c) => { var x = new Material(unlit); x.SetColor("_BaseColor", c); return Save(x, n); };
            var face = mat("GaugeFace", new Color(.035f, .04f, .05f));
            var tick = mat("GaugeTick", new Color(.92f, .93f, .95f));
            var needle = mat("GaugeNeedle", new Color(1f, .16f, .08f));
            const float y = 0.95f, r = 0.058f;
            string[] speed = { "0", "20", "40", "60", "80", "100", "120", "140" };
            string[] tach = { "0", "1", "2", "3", "4", "5", "6", "7", "8" };
            Dial(root, "Tach", -0.48f, y, r, tach, face, tick, needle, m);
            Dial(root, "Speed", -0.28f, y, r, speed, face, tick, needle, m);

            Part(root, "Screen", new Vector3(-0.38f, y, 0), Quaternion.identity, Quad(0.062f, 0.098f, false), face);
            Label(root, "SpeedText", new Vector3(-0.38f, y + 0.012f, -0.001f), "0", 0.022f, m);
            Label(root, "MphText", new Vector3(-0.38f, y - 0.008f, -0.001f), "MPH", 0.007f, m);
            Label(root, "GearText", new Vector3(-0.38f, y - 0.03f, -0.001f), "P", 0.016f, m);
        }

        static void Dial(Transform root, string name, float x, float y, float r, string[] labels, Material face, Material tick, Material needle, Mats m)
        {
            var c = new Vector3(x, y, 0);
            Part(root, name + "Face", c, Quaternion.identity, Disc(r), face);
            for (int i = 0; i < labels.Length; i++)
            {
                float a = 135f - 270f * i / (labels.Length - 1);
                var rot = Quaternion.Euler(0, 0, a);
                Part(root, name + "Tick", c + rot * Vector3.up * (r * .88f) + Vector3.back * .0006f, rot, Quad(.0022f, r * .17f, false), tick);
                Label(root, name + "Label", c + rot * Vector3.up * (r * .62f) + Vector3.back * .0008f, labels[i], 0.0095f, m);
            }
            var pivot = new GameObject(name + "Needle").transform;
            pivot.SetParent(root, false); pivot.localPosition = c + Vector3.back * .0014f;
            Part(pivot, "Blade", Vector3.zero, Quaternion.identity, Quad(.003f, r * .84f, true), needle);
            Part(root, name + "Hub", c + Vector3.back * .002f, Quaternion.identity, Disc(r * .1f), tick);
        }

        static void Part(Transform parent, string name, Vector3 pos, Quaternion rot, Mesh mesh, Material mat)
        {
            var file = $"{Gen}/Meshes/Gauge_{meshCount++}.asset";
            AssetDatabase.DeleteAsset(file); AssetDatabase.CreateAsset(mesh, file);
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false); go.transform.localPosition = pos; go.transform.localRotation = rot;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off;
        }

        static void Label(Transform parent, string name, Vector3 pos, string text, float height, Mats m)
        {
            Text(parent, Vector3.zero, Quaternion.identity, text, height, Color.white, m.text);
            var go = parent.GetChild(parent.childCount - 1);
            go.name = name; go.localPosition = pos; go.localRotation = Quaternion.identity;
        }

        static Mesh Disc(float r)
        {
            var v = new List<Vector3> { Vector3.zero }; var t = new List<int>();
            const int n = 40;
            for (int i = 0; i <= n; i++) { float a = i * Mathf.PI * 2 / n; v.Add(new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0)); if (i > 0) t.AddRange(new[] { 0, i + 1, i }); }
            return Plain(v, t);
        }

        static Mesh Quad(float w, float h, bool fromBase)
        {
            float lo = fromBase ? 0 : -h / 2, hi = fromBase ? h : h / 2;
            return Plain(new List<Vector3> { new Vector3(-w / 2, lo), new Vector3(w / 2, lo), new Vector3(-w / 2, hi), new Vector3(w / 2, hi) }, new List<int> { 0, 2, 1, 1, 2, 3 });
        }

        static Mesh Plain(List<Vector3> v, List<int> t)
        {
            var mesh = new Mesh(); mesh.SetVertices(v); mesh.SetTriangles(t, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        // Rebuilds the mirror glass with UVs that map a whole camera image across each mirror.
        static void BuildMirrors(Transform holder, GameObject inst)
        {
            var sources = inst.GetComponentsInChildren<MeshRenderer>().Where(r => r.name.Contains("_miror")).ToList();
            foreach (var r in sources.Where(x => x.name.Contains("body_miror")))
            {
                MirrorPart(holder, "Mirror_L", r, c => c.x < 0, 0);
                MirrorPart(holder, "Mirror_R", r, c => c.x >= 0, 0);
            }
            foreach (var r in sources.Where(x => x.name.Contains("interior1_miror"))) MirrorPart(holder, "Mirror_Rear", r, c => true, 0);
            foreach (var r in sources) UnityEngine.Object.DestroyImmediate(r.gameObject);
        }

        static void MirrorPart(Transform holder, string name, MeshRenderer r, Func<Vector3, bool> keep, int unused)
        {
            var src = r.GetComponent<MeshFilter>().sharedMesh; var m = r.transform.localToWorldMatrix;
            var verts = src.vertices; var tris = src.triangles;
            var v = new List<Vector3>(); var t = new List<int>(); var map = new Dictionary<int, int>();
            for (int i = 0; i < tris.Length; i += 3)
            {
                var c = (m.MultiplyPoint3x4(verts[tris[i]]) + m.MultiplyPoint3x4(verts[tris[i + 1]]) + m.MultiplyPoint3x4(verts[tris[i + 2]])) / 3f;
                if (!keep(c)) continue;
                for (int j = 0; j < 3; j++)
                {
                    int id = tris[i + j];
                    if (!map.TryGetValue(id, out int ni)) { ni = v.Count; map[id] = ni; v.Add(m.MultiplyPoint3x4(verts[id])); }
                    t.Add(ni);
                }
            }
            if (v.Count == 0) { Debug.LogError("[FloridaSlice] empty mirror " + name); return; }
            float minX = v.Min(p => p.x), maxX = v.Max(p => p.x), minY = v.Min(p => p.y), maxY = v.Max(p => p.y);
            // A mirror flips left and right compared with the backward-looking camera.
            var uv = v.Select(p => new Vector2(1f - (p.x - minX) / (maxX - minX), (p.y - minY) / (maxY - minY))).ToList();
            var mesh = new Mesh(); mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var file = $"{Gen}/Meshes/{name}.asset";
            AssetDatabase.DeleteAsset(file); AssetDatabase.CreateAsset(mesh, file);
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(holder, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = r.sharedMaterial;
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        // The lever is fused into the dash meshes, so its pieces are found by position and moved to their own object.
        static void BuildShifter(Transform holder, GameObject inst)
        {
            var pivot = new GameObject("Shifter").transform;
            pivot.SetParent(holder, false); pivot.localPosition = new Vector3(0.01f, 0.55f, 0.62f);
            Vector3 lo = new Vector3(-0.03f, 0.51f, 0.585f), hi = new Vector3(0.055f, 0.73f, 0.685f);
            int moved = 0;
            foreach (var r in inst.GetComponentsInChildren<MeshRenderer>().Where(x => x.name.Contains("_dashboard_")).ToList())
            {
                var filter = r.GetComponent<MeshFilter>(); var src = filter.sharedMesh; var m = r.transform.localToWorldMatrix;
                var verts = src.vertices; var norms = src.normals; var uvs = src.uv; var tris = src.triangles;
                var parent = Enumerable.Range(0, verts.Length).ToArray();
                Func<int, int> find = x => { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; };
                for (int i = 0; i < tris.Length; i += 3) { parent[find(tris[i])] = find(tris[i + 1]); parent[find(tris[i])] = find(tris[i + 2]); }
                var inside = new Dictionary<int, bool>();
                for (int i = 0; i < verts.Length; i++)
                {
                    var w = m.MultiplyPoint3x4(verts[i]); int g = find(i);
                    bool ok = w.x > lo.x && w.x < hi.x && w.y > lo.y && w.y < hi.y && w.z > lo.z && w.z < hi.z;
                    inside[g] = (!inside.ContainsKey(g) || inside[g]) && ok;
                }
                var keep = new List<int>(); var take = new List<int>();
                for (int i = 0; i < tris.Length; i += 3) (inside[find(tris[i])] ? take : keep).AddRange(new[] { tris[i], tris[i + 1], tris[i + 2] });
                if (take.Count == 0) continue;

                var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t2 = new List<int>(); var map = new Dictionary<int, int>();
                foreach (int id in take)
                {
                    if (!map.TryGetValue(id, out int ni)) { ni = v.Count; map[id] = ni; v.Add(m.MultiplyPoint3x4(verts[id]) - pivot.position); n.Add(m.MultiplyVector(norms[id]).normalized); uv.Add(uvs[id]); }
                    t2.Add(ni);
                }
                var part = new Mesh { indexFormat = IndexFormat.UInt32 };
                part.SetVertices(v); part.SetNormals(n); part.SetUVs(0, uv); part.SetTriangles(t2, 0); part.RecalculateTangents(); part.RecalculateBounds();
                var file = $"{Gen}/Meshes/Shifter_{meshCount++}.asset";
                AssetDatabase.DeleteAsset(file); AssetDatabase.CreateAsset(part, file);
                var go = new GameObject(r.sharedMaterial.name, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(pivot, false);
                go.GetComponent<MeshFilter>().sharedMesh = part;
                go.GetComponent<MeshRenderer>().sharedMaterials = r.sharedMaterials;

                var rest = UnityEngine.Object.Instantiate(src);
                rest.SetTriangles(keep, 0);
                var restFile = $"{Gen}/Meshes/DashRest_{meshCount++}.asset";
                AssetDatabase.DeleteAsset(restFile); AssetDatabase.CreateAsset(rest, restFile);
                filter.sharedMesh = rest;
                moved += take.Count / 3;
            }
            Debug.Log("[FloridaSlice] shifter triangles moved: " + moved);
        }

        static Texture2D SoftDot()
        {
            const string file = Gen + "/Materials/SoftDot.png";
            var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
            {
                float d = Mathf.Clamp01(Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f)) / 31.5f);
                tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Pow(1f - d, 1.6f)));
            }
            File.WriteAllBytes(file, tex.EncodeToPNG());
            AssetDatabase.ImportAsset(file);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(file);
        }

        // Crumple needs vertex access at runtime, which imported meshes do not allow in a built game.
        static void MakeBodyDeformable(GameObject inst)
        {
            foreach (var r in inst.GetComponentsInChildren<MeshRenderer>().Where(x => x.name.Contains("_body_") || x.name.Contains("frontlights")))
            {
                var filter = r.GetComponent<MeshFilter>(); var src = filter.sharedMesh;
                var copy = new Mesh { indexFormat = IndexFormat.UInt32 };
                copy.SetVertices(src.vertices); copy.SetNormals(src.normals); copy.SetUVs(0, src.uv); copy.SetTriangles(src.triangles, 0);
                copy.RecalculateTangents(); copy.RecalculateBounds();
                var file = $"{Gen}/Meshes/Body_{meshCount++}.asset";
                AssetDatabase.DeleteAsset(file); AssetDatabase.CreateAsset(copy, file);
                filter.sharedMesh = copy;
            }
        }

        static void BuildSteeringPivot(Transform holder, GameObject inst)
        {
            var parts = inst.GetComponentsInChildren<MeshRenderer>().Where(r => r.name.Contains("steering_wheel")).ToList();
            if (parts.Count == 0) { Debug.LogError("[FloridaSlice] no steering wheel found"); return; }
            var rim = parts.OrderByDescending(r => r.bounds.size.sqrMagnitude).First();
            var pivot = new GameObject("SteeringPivot").transform;
            pivot.SetParent(holder, false); pivot.position = rim.bounds.center;
            foreach (var r in parts) r.transform.SetParent(pivot, true);
        }

        static void BuildEnvironment()
        {
            // Mipmaps make a visible vertical seam where the panorama's edges meet.
            const string skyFile = PH + "/Skies/citrus_orchard_road_puresky.hdr";
            if (AssetImporter.GetAtPath(skyFile) is TextureImporter skyImp && (skyImp.mipmapEnabled || skyImp.wrapMode != TextureWrapMode.Repeat))
            { skyImp.mipmapEnabled = false; skyImp.wrapMode = TextureWrapMode.Repeat; skyImp.SaveAndReimport(); }
            var sky = new Material(Shader.Find("Skybox/Panoramic"));
            sky.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture>(PH + "/Skies/citrus_orchard_road_puresky.hdr"));
            sky.SetFloat("_Exposure", 1f);
            Save(sky, "Sky");
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = .00045f;
            RenderSettings.fogColor = new Color(.78f, .85f, .92f);
            var sun = new GameObject("Sun", typeof(Light)).GetComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 2.2f; sun.color = new Color(1f, .95f, .86f); sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(52, -35, 0);
            RenderSettings.sun = sun;
            DynamicGI.UpdateEnvironment();
        }
    }
}
