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
            var path = RoutePath.Load("florida_jupiter");
            var m = MakeMaterials();

            BuildEnvironment();
            var road = new GameObject("Road");
            Func<float, float> half = path.HalfWidth;
            Func<float, float> south = s => -(2 * path.HalfWidth(s) + 26f);

            Add(road, "Ground", Ribbon(path, s => -650, s => 650, -0.25f, 12, 12), m.grass);
            BuildCarriageway(road, "Northbound", path, s => 0, m);
            BuildCarriageway(road, "Southbound", path, south, m);
            BuildBarriers(road, path, m);
            BuildOverpasses(road, path, south, m);
            BuildSigns(path, m);
            BuildTrees(path, south);

            var cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            cam.tag = "MainCamera"; cam.farClipPlane = 3000f; cam.fieldOfView = 62f;
            var car = BuildCar(cam);
            car.Place(3000f);

            EditorSceneManager.SaveScene(scene, Gen + "/FloridaSlice.unity");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Gen + "/FloridaSlice.unity", true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[FloridaSlice] built " + (path.Length / 1000f).ToString("F1") + " km");

            var shots = ArgValue("-shotDir");
            if (shots != null) Screenshots(car, cam, path, shots);
        }

        static string ArgValue(string name)
        {
            var a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }

        static void Screenshots(PlayerCar car, Camera cam, RoutePath path, string dir)
        {
            var points = new Dictionary<string, float> { { "warmup", 500f }, { "overpass", 7890f }, { "exit", 10438f - 345f }, { "road", 3000f } }; // first capture in batch mode is unreliable
            ShaderUtil.allowAsyncCompilation = false;
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            foreach (var p in points)
            {
                car.Place(p.Value);
                var req = new RenderPipeline.StandardRequest { destination = rt };
                RenderPipeline.SubmitRenderRequest(cam, req);
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                File.WriteAllBytes(Path.Combine(dir, "slice_" + p.Key + ".png"), tex.EncodeToPNG());
            }
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

        static Mats MakeMaterials()
        {
            var m = new Mats
            {
                asphalt = Lit("Asphalt", new Color(.62f, .62f, .62f), .18f, "asphalt_02"),
                shoulder = Lit("Shoulder", new Color(.9f, .9f, .88f), .12f, "asphalt_02"),
                grass = Lit("Grass", new Color(.62f, .9f, .48f), .08f, "grass_ground"),
                concrete = Lit("Concrete", new Color(.86f, .85f, .81f), .2f, "brushed_concrete"),
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
        static Mesh Ribbon(RoutePath p, Func<float, float> left, Func<float, float> right, float y, float uTile, float vTile, float from = 0, float to = -1, float step = 10f)
        {
            if (to < 0) to = p.Length;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tris = new List<int>();
            int rows = Mathf.Max(1, Mathf.CeilToInt((to - from) / step));
            for (int r = 0; r <= rows; r++)
            {
                float s = from + (to - from) * r / rows, l = left(s), rr = right(s);
                v.Add(p.Point(s, l) + Vector3.up * y); v.Add(p.Point(s, rr) + Vector3.up * y);
                uv.Add(new Vector2(l / uTile, s / vTile)); uv.Add(new Vector2(rr / uTile, s / vTile));
                if (r > 0) { int a = (r - 1) * 2; tris.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 }); }
            }
            return Finish(v, uv, tris);
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

        static void BuildCarriageway(GameObject root, string name, RoutePath p, Func<float, float> c, Mats m)
        {
            var g = new GameObject(name); g.transform.SetParent(root.transform);
            Func<float, float> h = p.HalfWidth;
            const float rightShoulder = 3.05f, leftShoulder = 1.3f;
            Add(g, "Shoulder", Ribbon(p, s => c(s) - h(s) - leftShoulder, s => c(s) + h(s) + rightShoulder, 0.0f, 5, 5), m.shoulder);
            Add(g, "Lanes", Ribbon(p, s => c(s) - h(s), s => c(s) + h(s), 0.012f, 7, 7), m.asphalt);
            Add(g, "RumbleLeft", Ribbon(p, s => c(s) - h(s) - .95f, s => c(s) - h(s) - .4f, 0.02f, 1, .6f, step: 10f), m.rumble);
            Add(g, "RumbleRight", Ribbon(p, s => c(s) + h(s) + .4f, s => c(s) + h(s) + .95f, 0.02f, 1, .6f, step: 10f), m.rumble);
            Add(g, "EdgeLeft", Ribbon(p, s => c(s) - h(s) - .08f, s => c(s) - h(s) + .08f, 0.03f, 1, 1), m.yellow);
            Add(g, "EdgeRight", Ribbon(p, s => c(s) + h(s) - .08f, s => c(s) + h(s) + .08f, 0.03f, 1, 1), m.white);

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
            Add(g, "Dashes", Finish(v, uv, t), m.white);
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
                bool on = p.IsBridge(s) || p.File.overpasses.Any(o => Mathf.Abs(o.s - s) < 90);
                if (on && start < 0) start = s;
                if (!on && start >= 0) { spans.Add((start, s)); start = -1; }
            }
            foreach (var (a, b) in spans)
            {
                Add(g, "Left", Extrude(p, -2.2f, a, b), m.concrete, true);
                Add(g, "Right", Extrude(p, 4.0f, a, b), m.concrete, true);
            }
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
                foreach (var lat in new[] { p.HalfWidth(o.s) + 7f, -p.HalfWidth(o.s) - 7f, south(o.s) - p.HalfWidth(o.s) - 7f })
                    Box(g.transform, p.Point(o.s, lat) + Vector3.up * 2.9f, rot, new Vector3(1.2f, 5.8f, 10f), m.concrete, "Pier");
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

        static void Sign(Transform parent, RoutePath p, float s, float lateralFromEdge, float width, float height, float elevation, string text, float textH, Material panel, Color tc, Mats m)
        {
            var rot = Quaternion.LookRotation(p.Tangent(s), Vector3.up);
            var c = p.Point(s, p.HalfWidth(s) + lateralFromEdge);
            var mid = c + Vector3.up * (elevation + height / 2);
            Box(parent, mid, rot, new Vector3(width, height, .08f), panel, "Panel").GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.On;
            var right = p.Right(s);
            foreach (var d in new[] { -width * .32f, width * .32f })
                Box(parent, c + right * d + p.Tangent(s) * .14f + Vector3.up * (elevation + height) / 2, rot, new Vector3(.14f, elevation + height, .14f), m.post, "Post");
            Text(parent, mid - p.Tangent(s) * .06f, rot, text, textH, tc, m.text);
        }

        static void BuildSigns(RoutePath p, Mats m)
        {
            var g = new GameObject("Signs");
            float? baseMile = null;
            foreach (var e in p.File.exits)
            {
                if (string.IsNullOrEmpty(e.@ref)) continue;
                var digits = new string(e.@ref.TakeWhile(char.IsDigit).ToArray());
                if (baseMile == null && digits.Length > 0) baseMile = float.Parse(digits) - e.s / 1609.34f;
                var label = "EXIT " + e.@ref + "\n" + Wrap(e.name);
                Sign(g.transform, p, e.s - 340f, 8f, 7.5f, 3.8f, 2.4f, label, .62f, m.signGreen, Color.white, m);
                Sign(g.transform, p, Mathf.Max(5, e.s - 15f), 10f, 4.5f, 1.7f, 2.4f, "EXIT\n" + e.@ref, .55f, m.signGreen, Color.white, m);
            }
            if (baseMile != null)
                for (int mile = Mathf.CeilToInt(baseMile.Value); (mile - baseMile.Value) * 1609.34f < p.Length; mile++)
                    Sign(g.transform, p, Mathf.Max(5, (mile - baseMile.Value) * 1609.34f), 5.2f, .55f, 1.0f, 1.3f, "MILE\n" + mile, .17f, m.signGreen, Color.white, m);
            string[] ads = { "HURT ON I-95?\nCALL BIG RICK", "GATOR WORLD\nNEXT EXIT", "FIREWORKS\nFIREWORKS\nFIREWORKS", "EARLY BIRD\nBUFFET 3:45PM" };
            for (int i = 0; i < 9; i++)
                Sign(g.transform, p, 1400f + i * 2500f, 22f, 15f, 6f, 6f, ads[i % ads.Length], 1.15f, m.billboard, new Color(.08f, .08f, .08f), m);
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

        static void Frond(List<Vector3> v, List<int> t, Vector3 top, float angle)
        {
            var dir = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
            var side = new Vector3(-dir.z, 0, dir.x);
            int start = v.Count;
            for (int i = 0; i <= 6; i++)
            {
                float k = i / 6f, w = .55f * Mathf.Sin(Mathf.PI * Mathf.Min(.96f, k + .08f));
                var p = top + dir * (4.6f * k) + Vector3.up * (1.1f * k - 3.4f * k * k);
                v.Add(p - side * w); v.Add(p + side * w);
                if (i > 0) { int q = start + (i - 1) * 2; t.AddRange(new[] { q, q + 1, q + 2, q + 2, q + 1, q + 3, q, q + 2, q + 1, q + 2, q + 3, q + 1 }); }
            }
        }

        static Mesh TreeMesh(bool palm)
        {
            var v = new List<Vector3>(); var bark = new List<int>(); var leaf = new List<int>();
            if (palm)
            {
                Cone(v, bark, Vector3.zero, .24f, .15f, 9.5f, 6);
                for (int i = 0; i < 11; i++) Frond(v, leaf, Vector3.up * 9.4f, i * 2.4f);
            }
            else
            {
                Cone(v, bark, Vector3.zero, .28f, .08f, 18f, 6);
                for (int i = 0; i < 4; i++) Cone(v, leaf, Vector3.up * (9f + i * 2.4f), 3.2f - i * .62f, .25f, 4.6f, 8);
            }
            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(v); mesh.subMeshCount = 2; mesh.SetTriangles(bark, 0); mesh.SetTriangles(leaf, 1);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var file = Gen + "/Meshes/Tree_" + (palm ? "Palm" : "Pine") + ".asset";
            AssetDatabase.DeleteAsset(file); AssetDatabase.CreateAsset(mesh, file);
            return mesh;
        }

        static void BuildTrees(RoutePath p, Func<float, float> south)
        {
            var bark = Lit("Bark", new Color(.33f, .26f, .19f), .1f); bark.enableInstancing = true;
            var pineLeaf = Lit("PineLeaf", new Color(.09f, .23f, .1f), .08f); pineLeaf.enableInstancing = true;
            var palmLeaf = Lit("PalmLeaf", new Color(.2f, .42f, .13f), .12f); palmLeaf.enableInstancing = true;
            var palmMesh = TreeMesh(true); var pineMesh = TreeMesh(false);
            var g = new GameObject("Trees");
            var rnd = new System.Random(95);
            const int count = 1400;
            float gap = p.Length / count;
            for (int i = 0; i < count; i++)
            {
                float s = (i + (float)rnd.NextDouble()) * gap, off = 22f + (float)rnd.NextDouble() * 170f;
                bool right = rnd.NextDouble() < .55, palm = rnd.NextDouble() < .42;
                float lat = right ? p.HalfWidth(s) + off : south(s) - p.HalfWidth(s) - off;
                var t = new GameObject(palm ? "Palm" : "Pine", typeof(MeshFilter), typeof(MeshRenderer));
                t.transform.SetParent(g.transform);
                t.transform.position = p.Point(s, lat) - Vector3.up * .25f;
                t.transform.rotation = Quaternion.Euler(0, (float)rnd.NextDouble() * 360f, 0);
                t.transform.localScale = Vector3.one * (.8f + (float)rnd.NextDouble() * .5f);
                t.GetComponent<MeshFilter>().sharedMesh = palm ? palmMesh : pineMesh;
                t.GetComponent<MeshRenderer>().sharedMaterials = new[] { bark, palm ? palmLeaf : pineLeaf };
            }
        }

        static Bounds Bounds(GameObject g)
        {
            var rs = g.GetComponentsInChildren<Renderer>();
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        static PlayerCar BuildCar(Camera cam)
        {
            var root = new GameObject("PlayerCar");
            var player = root.AddComponent<PlayerCar>();
            player.chaseCamera = cam;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(CarPath);
            if (model == null) { Debug.LogError("[FloridaSlice] car model missing"); return player; }
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.transform.position = Vector3.zero;
            var holder = new GameObject("Model");
            holder.transform.SetParent(root.transform, false);
            var b = Bounds(inst);
            bool xLong = b.size.x > b.size.z;
            float length = xLong ? b.size.x : b.size.z;
            float scale = 4.63f / length;
            inst.transform.localScale *= scale;
            b = Bounds(inst);
            inst.transform.position -= new Vector3(b.center.x, b.min.y, b.center.z);
            inst.transform.SetParent(holder.transform, true);
            holder.transform.localRotation = Quaternion.Euler(0, (xLong ? 90f : 0f) + CarYawFix, 0);
            Debug.Log($"[FloridaSlice] car xLong={xLong} size={b.size}");
            return player;
        }

        static void BuildEnvironment()
        {
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
