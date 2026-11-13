using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SurviveI95.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SurviveI95.EditorTools
{
    /// <summary>
    /// Plants as they really grow beside I-95: shade trees and palms in the yards, foundation shrubs along house fronts,
    /// palms lining the avenues and the strip-mall lots, and clumps of trees on the highway's own land.
    /// Each species is photographed once from three sides and from above, and those pictures become light "cards", so
    /// tens of thousands of plants cost little. Palms close to the highway use the full model and switch to cards farther off.
    /// Models: Poly Haven island trees and shrubs (CC0), Sketchfab palms (CC BY, credited in ASSET_CREDITS.md).
    /// </summary>
    public static partial class FloridaSliceBuilder
    {
        class Species
        {
            public string name, file; public float height; public bool palm, fullModel;
            public Mesh mesh, cards; public Material[] mats; public Material cardMat;
            public float cardHalf, cardMid, topY, topHalf; // card size and placement, in meters at scale 1
        }

        const int CardPx = 512;
        static List<Species> species;

        static readonly (string name, string file, float h, bool palm, bool full)[] SpeciesList =
        {
            ("coconut", "Assets/ThirdParty/Sketchfab/coconut_palm/scene.gltf", 10.5f, true, true),
            ("young_palm", "Assets/ThirdParty/Sketchfab/curly_palm/scene.gltf", 5.5f, true, true),
            ("date_palm", "Assets/ThirdParty/Sketchfab/date_palm/scene.gltf", 8.5f, true, true),
            ("shade_tree", PH + "/Models/island_tree_01/island_tree_01.gltf", 11f, false, false),
            ("round_tree", PH + "/Models/island_tree_02/island_tree_02.gltf", 8.5f, false, false),
            ("small_tree", PH + "/Models/island_tree_03/island_tree_03.gltf", 5.5f, false, false),
            // A dense, leafy bush (Searsia lucida), standing in for the ficus, sea grape and cocoplum hedges in Miami yards.
            // "#name" keeps one bush out of the seven the model file holds side by side.
            ("shrub", PH + "/Models/searsia_lucida/searsia_lucida.gltf#searsia_lucida_b", 1.6f, false, false),
        };
        const int SpCoconut = 0, SpYoung = 1, SpDate = 2, SpShade = 3, SpRound = 4, SpSmall = 5, SpShrub = 6;

        static void SetLayer(GameObject g, int layer) { foreach (var t in g.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer; }

        static Bounds RendererBounds(GameObject g)
        {
            var rs = g.GetComponentsInChildren<Renderer>(); var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        static List<Species> PrepareSpecies()
        {
            Directory.CreateDirectory(Gen + "/Plants");
            var list = new List<Species>();
            foreach (var (name, file, h, palm, full) in SpeciesList)
            {
                string keep = null, path = file; int hash = file.IndexOf('#');
                if (hash >= 0) { keep = file.Substring(hash + 1); path = file.Substring(0, hash); }
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) { Debug.LogWarning("[FloridaSlice] plant model missing: " + path + " (run node Tools/fetch_assets.js plants)"); continue; }
                var inst = UnityEngine.Object.Instantiate(prefab);
                inst.transform.position = new Vector3(0, -6000, 0);
                SetLayer(inst, 30);
                if (keep != null)
                    foreach (var rr in inst.GetComponentsInChildren<Renderer>(true))
                    {
                        bool mine = false; for (var t = rr.transform; t != null && !mine; t = t.parent) mine = t.name.StartsWith(keep);
                        if (!mine) UnityEngine.Object.DestroyImmediate(rr.gameObject);
                    }
                var b = RendererBounds(inst);
                float scale = h / b.size.y;
                var sp = new Species { name = name, file = path, height = h, palm = palm, fullModel = full };
                var pivot = new Vector3(b.center.x, b.min.y, b.center.z);
                if (full) sp.mesh = CombineModel(inst, name, scale, pivot, out sp.mats);
                MakeCards(inst, sp, b, scale);
                UnityEngine.Object.DestroyImmediate(inst);
                list.Add(sp);
            }
            return list;
        }

        static Mesh CombineModel(GameObject inst, string name, float scale, Vector3 pivot, out Material[] mats)
        {
            var toLocal = Matrix4x4.Scale(Vector3.one * scale) * Matrix4x4.Translate(-pivot);
            var groups = new Dictionary<Material, List<CombineInstance>>();
            foreach (var mr in inst.GetComponentsInChildren<MeshRenderer>())
            {
                var mf = mr.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                for (int i = 0; i < mf.sharedMesh.subMeshCount; i++)
                {
                    var mat = mr.sharedMaterials[Mathf.Min(i, mr.sharedMaterials.Length - 1)];
                    if (!groups.TryGetValue(mat, out var l)) groups[mat] = l = new List<CombineInstance>();
                    l.Add(new CombineInstance { mesh = mf.sharedMesh, subMeshIndex = i, transform = toLocal * mr.transform.localToWorldMatrix });
                }
            }
            var parts = new List<CombineInstance>(); var src = groups.Keys.ToArray();
            mats = new Material[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                // Plain URP materials from the model's own textures: leaves cut out by their alpha and seen from both sides.
                var s = src[i];
                Texture Pick(params string[] names) { foreach (var n in names) if (s != null && s.HasProperty(n) && s.GetTexture(n) != null) return s.GetTexture(n); return null; }
                var mt = Lit($"Plant_{name}_{i}", Color.white, .15f);
                var baseTex = Pick("baseColorTexture", "_BaseMap", "_MainTex"); if (baseTex != null) mt.SetTexture("_BaseMap", baseTex);
                if (s != null && s.HasProperty("baseColorFactor")) { var c = s.GetColor("baseColorFactor"); c.a = 1f; mt.SetColor("_BaseColor", c); }
                var nrm = Pick("normalTexture", "_BumpMap"); if (nrm != null) { mt.SetTexture("_BumpMap", nrm); mt.EnableKeyword("_NORMALMAP"); }
                mt.SetFloat("_AlphaClip", 1f); mt.SetFloat("_Cutoff", .5f); mt.EnableKeyword("_ALPHATEST_ON"); mt.renderQueue = 2450;
                mt.SetFloat("_Cull", 0f); mt.enableInstancing = true;
                mats[i] = mt;
            }
            foreach (var m in src)
            {
                var g = new Mesh { indexFormat = IndexFormat.UInt32 }; g.CombineMeshes(groups[m].ToArray(), true, true);
                parts.Add(new CombineInstance { mesh = g, transform = Matrix4x4.identity });
            }
            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 }; mesh.CombineMeshes(parts.ToArray(), false, false);
            mesh.RecalculateBounds();
            var f = $"{Gen}/Plants/{name}.asset"; AssetDatabase.DeleteAsset(f); AssetDatabase.CreateAsset(mesh, f);
            return mesh;
        }

        /// <summary>Photographs the model from three sides (60° apart) and from above into one strip of four pictures.</summary>
        static void MakeCards(GameObject inst, Species sp, Bounds b, float scale)
        {
            const int Hi = CardPx * 2;
            float half = Mathf.Max(b.extents.y, Mathf.Max(b.extents.x, b.extents.z)) * 1.04f;
            float topHalf = Mathf.Max(b.extents.x, b.extents.z) * 1.04f;
            var camGo = new GameObject("CardCam"); var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true; cam.cullingMask = 1 << 30; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.allowHDR = false; cam.allowMSAA = false; cam.nearClipPlane = .1f; cam.farClipPlane = 400f;
            // Two pictures of each view: the plant's own colors under flat, even light (no sun baked in), and which way
            // every leaf and branch faces. The game's real sun then lights the cards through that second picture,
            // bright on the sunny side and shaded on the other, so the plants sit in the same light as everything else.
            var lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude).Where(l => l.enabled).ToList();
            foreach (var l in lights) l.enabled = false;
            bool fog = RenderSettings.fog; var probe = RenderSettings.ambientProbe; float refl = RenderSettings.reflectionIntensity;
            RenderSettings.fog = false; var sh = new SphericalHarmonicsL2(); sh.AddAmbientLight(new Color(1f, 1f, 1f)); RenderSettings.ambientProbe = sh; RenderSettings.reflectionIntensity = 0f;
            var rt = new RenderTexture(Hi, Hi, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var renderers = inst.GetComponentsInChildren<Renderer>();
            var keep = renderers.Select(r => r.sharedMaterials).ToArray();
            var normalShader = Shader.Find("Hidden/SurviveI95/CardNormal");
            Color[] Capture(bool normals)
            {
                if (normals)
                    for (int i = 0; i < renderers.Length; i++)
                        renderers[i].sharedMaterials = keep[i].Select(m =>
                        {
                            var nm = new Material(normalShader);
                            Texture t = null; foreach (var p in new[] { "baseColorTexture", "_BaseMap", "_MainTex" }) if (m != null && m.HasProperty(p) && m.GetTexture(p) != null) { t = m.GetTexture(p); break; }
                            if (t != null) nm.SetTexture("_MainTex", t); nm.SetFloat("_Cutoff", .5f);
                            return nm;
                        }).ToArray();
                var strip = new Color[CardPx * 4 * CardPx];
                for (int k = 0; k < 4; k++)
                {
                    Vector3 dir = k < 3 ? Quaternion.Euler(0, k * 60f, 0) * Vector3.forward : Vector3.down;
                    cam.orthographicSize = k < 3 ? half : topHalf;
                    cam.transform.SetPositionAndRotation(b.center - dir * 200f, Quaternion.LookRotation(dir, k < 3 ? Vector3.up : Vector3.forward));
                    RenderPipeline.SubmitRenderRequest(cam, new RenderPipeline.StandardRequest { destination = rt });
                    var prev = RenderTexture.active; RenderTexture.active = rt;
                    var t = new Texture2D(Hi, Hi, TextureFormat.RGBA32, false, true); t.ReadPixels(new Rect(0, 0, Hi, Hi), 0, 0); t.Apply();
                    RenderTexture.active = prev;
                    var px = t.GetPixels(); UnityEngine.Object.DestroyImmediate(t);
                    // Down to half size with premultiplied alpha, so edges are smooth and never dark.
                    for (int y = 0; y < CardPx; y++) for (int x = 0; x < CardPx; x++)
                    {
                        Color sum = Color.clear; float a = 0;
                        for (int j = 0; j < 2; j++) for (int i = 0; i < 2; i++) { var c = px[(y * 2 + j) * Hi + x * 2 + i]; sum += c * c.a; a += c.a; }
                        var o = a > 0 ? sum / a : (normals ? new Color(.5f, .5f, 1f, 0f) : Color.clear); o.a = a / 4f;
                        if (normals && a > 0) { var n = new Vector3(o.r * 2f - 1f, o.g * 2f - 1f, o.b * 2f - 1f).normalized; o = new Color(n.x * .5f + .5f, n.y * .5f + .5f, n.z * .5f + .5f, a / 4f); }
                        strip[y * CardPx * 4 + k * CardPx + x] = o;
                    }
                }
                if (normals) for (int i = 0; i < renderers.Length; i++) renderers[i].sharedMaterials = keep[i];
                return strip;
            }
            var albedo = Capture(false); var normalStrip = Capture(true);
            foreach (var l in lights) l.enabled = true;
            UnityEngine.Object.DestroyImmediate(camGo); rt.Release();
            RenderSettings.fog = fog; RenderSettings.ambientProbe = probe; RenderSettings.reflectionIntensity = refl;
            Dilate(albedo, CardPx * 4, CardPx);
            // The color picture came out of a linear render; store it as normal sRGB colors.
            for (int i = 0; i < albedo.Length; i++) { var c = albedo[i]; albedo[i] = new Color(Mathf.LinearToGammaSpace(c.r), Mathf.LinearToGammaSpace(c.g), Mathf.LinearToGammaSpace(c.b), c.a); }
            for (int i = 0; i < normalStrip.Length; i++) if (normalStrip[i].a < .02f) normalStrip[i] = new Color(.5f, .5f, 1f, 1f); else normalStrip[i].a = 1f;
            var tex = new Texture2D(CardPx * 4, CardPx, TextureFormat.RGBA32, true); tex.SetPixels(albedo); tex.Apply();
            string file = $"{Gen}/Plants/{sp.name}_cards.png";
            File.WriteAllBytes(file, tex.EncodeToPNG()); AssetDatabase.ImportAsset(file);
            if (AssetImporter.GetAtPath(file) is TextureImporter ti)
            {
                ti.alphaIsTransparency = true; ti.mipMapsPreserveCoverage = true; ti.alphaTestReferenceValue = .45f; ti.wrapMode = TextureWrapMode.Clamp; ti.SaveAndReimport();
            }
            var ntex = new Texture2D(CardPx * 4, CardPx, TextureFormat.RGBA32, true, true); ntex.SetPixels(normalStrip); ntex.Apply();
            string nfile = $"{Gen}/Plants/{sp.name}_cards_n.png";
            File.WriteAllBytes(nfile, ntex.EncodeToPNG()); AssetDatabase.ImportAsset(nfile);
            if (AssetImporter.GetAtPath(nfile) is TextureImporter nti) { nti.textureType = TextureImporterType.NormalMap; nti.wrapMode = TextureWrapMode.Clamp; nti.SaveAndReimport(); }
            var mat = Lit("Card_" + sp.name, Color.white, .05f);
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(file));
            mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(nfile)); mat.EnableKeyword("_NORMALMAP");
            mat.SetFloat("_AlphaClip", 1f); mat.SetFloat("_Cutoff", .45f); mat.EnableKeyword("_ALPHATEST_ON");
            mat.SetFloat("_Cull", 2f); mat.renderQueue = 2450; mat.enableInstancing = true; // each card has its own back face
            mat.SetFloat("_SpecularHighlights", 0f); mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            mat.SetFloat("_EnvironmentReflections", 0f); mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            sp.cardMat = mat;
            sp.cardHalf = half * scale; sp.cardMid = (b.center.y - b.min.y) * scale; // Broadleaf crowns: the flat card from above sits high and small inside the crown, so from the road it never shows as a slab.
            sp.topHalf = topHalf * scale * (sp.palm ? 1f : .62f); sp.topY = sp.palm ? (b.center.y - b.min.y) * scale + b.extents.y * scale * .35f : b.size.y * scale * .86f;
            // A standalone card mesh, for the far step of a full-model palm.
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tr = new List<int>(); var nr = new List<Vector3>();
            CardGeometry(v, uv, nr, tr, sp, Vector3.zero, 0f, 1f);
            var cm = new Mesh(); cm.SetVertices(v); cm.SetUVs(0, uv); cm.SetNormals(nr); cm.SetTriangles(tr, 0); cm.RecalculateTangents(); cm.RecalculateBounds();
            var cf = $"{Gen}/Plants/{sp.name}_cardmesh.asset"; AssetDatabase.DeleteAsset(cf); AssetDatabase.CreateAsset(cm, cf);
            sp.cards = cm;
        }

        // Spreads edge colors into the clear area, so mipmaps never pull in black around leaves.
        static void Dilate(Color[] px, int w, int h)
        {
            for (int pass = 0; pass < 6; pass++)
            {
                var src = (Color[])px.Clone();
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                {
                    int i = y * w + x; if (src[i].a > .02f) continue;
                    Color sum = Color.clear; int n = 0;
                    for (int j = -1; j <= 1; j++) for (int k = -1; k <= 1; k++)
                    {
                        int xx = x + k, yy = y + j; if (xx < 0 || yy < 0 || xx >= w || yy >= h || (xx / CardPx) != (x / CardPx)) continue;
                        var c = src[yy * w + xx]; if (c.a > .02f || (pass > 0 && (c.r + c.g + c.b) > 0)) { sum += c; n++; }
                    }
                    if (n > 0) { var o = sum / n; o.a = 0f; px[i] = o; }
                }
            }
        }

        /// <summary>Three upright cards 60° apart and one flat card at the crown, each facing the way it was photographed.</summary>
        static void CardGeometry(List<Vector3> v, List<Vector2> uv, List<Vector3> nr, List<int> t, Species sp, Vector3 at, float yaw, float scale)
        {
            float half = sp.cardHalf * scale, mid = sp.cardMid * scale;
            for (int k = 0; k < 3; k++)
            {
                var face = Quaternion.Euler(0, yaw + k * 60f, 0);
                var right = face * Vector3.right * half; var fwd = face * Vector3.forward;
                int n = v.Count; float u0 = k / 4f, u1 = (k + 1) / 4f;
                // The camera looked along +forward, so its right is the card's right.
                v.Add(at + new Vector3(0, mid - half, 0) - right); v.Add(at + new Vector3(0, mid + half, 0) - right);
                v.Add(at + new Vector3(0, mid - half, 0) + right); v.Add(at + new Vector3(0, mid + half, 0) + right);
                uv.Add(new Vector2(u0, 0)); uv.Add(new Vector2(u0, 1)); uv.Add(new Vector2(u1, 0)); uv.Add(new Vector2(u1, 1));
                var up = -fwd; // facing the camera that photographed it; the direction picture does the rest
                for (int i = 0; i < 4; i++) nr.Add(up);
                t.AddRange(new[] { n, n + 1, n + 2, n + 2, n + 1, n + 3 });
                // Its own back face, facing the other way, so the far side of a tree is lit as the far side.
                int b = v.Count;
                for (int i = 0; i < 4; i++) { v.Add(v[n + i]); uv.Add(uv[n + i]); nr.Add(fwd); }
                t.AddRange(new[] { b, b + 2, b + 1, b + 2, b + 3, b + 1 });
            }
            {
                var face = Quaternion.Euler(0, yaw, 0); float th = sp.topHalf * scale, y = sp.topY * scale;
                var right = face * Vector3.right * th; var fwd = face * Vector3.forward * th;
                int n = v.Count;
                // Seen from above with the camera's up along +forward.
                v.Add(at + new Vector3(0, y, 0) - right - fwd); v.Add(at + new Vector3(0, y, 0) - right + fwd);
                v.Add(at + new Vector3(0, y, 0) + right - fwd); v.Add(at + new Vector3(0, y, 0) + right + fwd);
                uv.Add(new Vector2(.75f, 0)); uv.Add(new Vector2(.75f, 1)); uv.Add(new Vector2(1f, 0)); uv.Add(new Vector2(1f, 1));
                for (int i = 0; i < 4; i++) nr.Add(Vector3.up);
                t.AddRange(new[] { n, n + 1, n + 2, n + 2, n + 1, n + 3 });
            }
        }

        // ---------- where plants go ----------
        public struct Yard { public Vector2 mid, c, u; public float a, b; public Vector2 toStreet; public bool hasStreet; }
        static readonly List<Yard> yards = new List<Yard>();
        static readonly List<(Vector2 a, Vector2 b)> lotEdges = new List<(Vector2, Vector2)>();
        static readonly Dictionary<(int, int), List<List<Vector2>>> footGrid = new Dictionary<(int, int), List<List<Vector2>>>();
        const float FootCell = 25f;

        static void AddFootprint(List<Vector2> pts)
        {
            var lo = new Vector2(pts.Min(q => q.x), pts.Min(q => q.y)); var hi = new Vector2(pts.Max(q => q.x), pts.Max(q => q.y));
            for (int x = (int)Mathf.Floor(lo.x / FootCell); x <= (int)Mathf.Floor(hi.x / FootCell); x++)
                for (int y = (int)Mathf.Floor(lo.y / FootCell); y <= (int)Mathf.Floor(hi.y / FootCell); y++)
                {
                    if (!footGrid.TryGetValue((x, y), out var l)) footGrid[(x, y)] = l = new List<List<Vector2>>();
                    l.Add(pts);
                }
        }

        static bool InBuilding(Vector2 q, float pad)
        {
            if (!footGrid.TryGetValue(((int)Mathf.Floor(q.x / FootCell), (int)Mathf.Floor(q.y / FootCell)), out var l)) return false;
            foreach (var pts in l)
            {
                if (Inside(pts, q)) return true;
                if (pad > 0 && (Inside(pts, q + new Vector2(pad, 0)) || Inside(pts, q - new Vector2(pad, 0)) || Inside(pts, q + new Vector2(0, pad)) || Inside(pts, q - new Vector2(0, pad)))) return true;
            }
            return false;
        }

        static bool PlantSpotOk(Vector2 q, float pad)
        {
            if (InBuilding(q, pad) || OnRoad(q, 9f) || NearExit(q, 8f) || InWater(q)) return false;
            float d = StreetDistance(q, 12f, out _, out var w);
            return d >= w / 2f + pad + .6f;
        }

        static int PickYardTree(System.Random r)
        {
            double x = r.NextDouble();
            return x < .2 ? SpShade : x < .38 ? SpRound : x < .5 ? SpSmall : x < .68 ? SpCoconut : x < .84 ? SpYoung : SpDate;
        }

        static void BuildPlants(RoutePath p, Func<float, float> south)
        {
            try { PlacePlants(p, south); }
            catch (Exception e) { Debug.LogError("[FloridaSlice] plants failed: " + e); throw; }
        }

        static void PlacePlants(RoutePath p, Func<float, float> south)
        {
            species = PrepareSpecies();
            if (species.Count < SpeciesList.Length) { Debug.LogWarning("[FloridaSlice] plant models missing, using the old palms"); BuildTrees(p, south); return; }
            var cardMats = species.Select(s => s.cardMat).ToArray();
            var chunks = new Dictionary<(int, int), CardBucket>();
            var near = new GameObject("Palms");
            int cards = 0, models = 0, shrubs = 0, poolCount = 0;
            var pools = new Dictionary<(int, int), Bucket>();
            void Card(int sp, Vector2 q, float scale, System.Random r)
            {
                var key = ((int)Mathf.Floor(q.x / 600f), (int)Mathf.Floor(q.y / 600f));
                if (!chunks.TryGetValue(key, out var cb)) chunks[key] = cb = new CardBucket(species.Count);
                CardGeometry(cb.v[sp], cb.uv[sp], cb.n[sp], cb.t[sp], species[sp], new Vector3(q.x, -.25f, q.y), (float)r.NextDouble() * 360f, scale);
                cards++;
            }
            void Model(int sp, Vector2 q, float scale, System.Random r)
            {
                var s = species[sp];
                if (!s.fullModel) { Card(sp, q, scale, r); return; }
                var go = new GameObject(s.name, typeof(LODGroup));
                go.transform.SetParent(near.transform);
                go.transform.SetPositionAndRotation(new Vector3(q.x, -.25f, q.y), Quaternion.Euler(0, (float)r.NextDouble() * 360f, 0));
                go.transform.localScale = Vector3.one * scale;
                var full = new GameObject("Full", typeof(MeshFilter), typeof(MeshRenderer)); full.transform.SetParent(go.transform, false);
                full.GetComponent<MeshFilter>().sharedMesh = s.mesh; full.GetComponent<MeshRenderer>().sharedMaterials = s.mats;
                var far = new GameObject("Card", typeof(MeshFilter), typeof(MeshRenderer)); far.transform.SetParent(go.transform, false);
                far.GetComponent<MeshFilter>().sharedMesh = s.cards; far.GetComponent<MeshRenderer>().sharedMaterial = s.cardMat;
                go.GetComponent<LODGroup>().SetLODs(new[] { new LOD(.09f, new Renderer[] { full.GetComponent<MeshRenderer>() }), new LOD(.004f, new Renderer[] { far.GetComponent<MeshRenderer>() }) });
                models++;
            }

            // Pools first, so no tree lands in one: behind about three houses in ten, with its deck.
            var poolGrid = new Dictionary<(int, int), List<Vector2>>();
            (int, int) PoolKey(Vector2 q) => ((int)Mathf.Floor(q.x / 20f), (int)Mathf.Floor(q.y / 20f));
            bool NearPool(Vector2 q, float clear)
            {
                var k = PoolKey(q);
                for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++)
                        if (poolGrid.TryGetValue((k.Item1 + dx, k.Item2 + dz), out var l)) foreach (var c in l) if ((c - q).sqrMagnitude < clear * clear) return true;
                return false;
            }
            foreach (var y in yards)
            {
                if (!y.hasStreet) continue;
                var r = new System.Random((int)(y.mid.x * 3.3f) ^ (int)(y.mid.y * 5.9f) ^ 0x5151);
                if (r.NextDouble() >= .3) continue;
                float depthP = Mathf.Abs(Vector2.Dot(y.u, y.toStreet)) > .7f ? y.a : y.b;
                var back = -y.toStreet; var pc = y.c + back * (depthP + 5f); var pu = new Vector2(-back.y, back.x);
                if (InBuilding(pc, 4.5f) || !PlantSpotOk(pc, 3f) || NearPool(pc, 10f)) continue;
                var deck = RectPts(pc, pu, 6f, 4.2f); var water = RectPts(pc, pu, 4.2f, 2.4f);
                var bk = Get(pools, pc, 600f, 2);
                Fan(bk, 1, deck, -0.12f, 2f); Fan(bk, 0, water, -0.1f, 2f); poolCount++;
                var pk = PoolKey(pc); if (!poolGrid.TryGetValue(pk, out var pl)) poolGrid[pk] = pl = new List<Vector2>(); pl.Add(pc);
            }
            // Yards: a tree or two beside or behind each house, and shrubs along the front wall.
            foreach (var y in yards)
            {
                var r = new System.Random((int)(y.mid.x * 3.3f) ^ (int)(y.mid.y * 5.9f));
                double roll = r.NextDouble(); int n = roll < .12 ? 0 : roll < .5 ? 1 : roll < .85 ? 2 : 3;
                for (int i = 0; i < n; i++)
                    for (int tries = 0; tries < 6; tries++)
                    {
                        float ang = (float)r.NextDouble() * Mathf.PI * 2f, dist = Mathf.Max(y.a, y.b) + 2.5f + (float)r.NextDouble() * 4f;
                        var q = y.mid + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * dist;
                        // A tree's trunk stays clear of the pool deck (about 7 m across) and its canopy mostly off the water.
                        if (!PlantSpotOk(q, 1.2f) || NearPool(q, 8f)) continue;
                        Card(PickYardTree(r), q, .85f + (float)r.NextDouble() * .45f, r);
                        break;
                    }
                if (!y.hasStreet) continue;
                // Foundation shrubs: the side of the house that faces the street.
                var w = new Vector2(-y.toStreet.y, y.toStreet.x);
                float depth = Mathf.Abs(Vector2.Dot(y.u, y.toStreet)) > .7f ? y.a : y.b, span = Mathf.Abs(Vector2.Dot(y.u, y.toStreet)) > .7f ? y.b : y.a;
                int count = 2 + r.Next(3);
                for (int i = 0; i < count; i++)
                {
                    var q = y.c + y.toStreet * (depth + .9f) + w * span * ((float)r.NextDouble() * 1.6f - .8f);
                    if (InBuilding(q, .3f) || NearPool(q, 5f)) continue;
                    Card(SpShrub, q, 1f + (float)r.NextDouble() * .6f, r); shrubs++;
                }
            }
            // Palms along the strip-mall lots, on the street side.
            foreach (var (a, b) in lotEdges)
            {
                var r = new System.Random((int)(a.x * 2.1f) ^ (int)(b.y * 4.7f));
                float len = (b - a).magnitude; if (len < 8f) continue;
                int sp = r.NextDouble() < .5 ? SpDate : SpYoung;
                for (float d = 4f; d < len - 2f; d += 14f + (float)r.NextDouble() * 4f)
                {
                    var q = Vector2.Lerp(a, b, d / len);
                    if (PlantSpotOk(q, .8f)) Card(sp, q, .85f + (float)r.NextDouble() * .3f, r);
                }
            }
            // Avenues lined with palms; side streets get a tree here and there in the swale.
            if (land != null)
                foreach (var seg in streetGrid.Values.SelectMany(x => x).Distinct())
                {
                    var (a, b, w) = seg; float len = (b - a).magnitude; if (len < 6f) continue;
                    var dir = (b - a) / len; var side = new Vector2(-dir.y, dir.x);
                    var r = new System.Random((int)(a.x * 1.7f) ^ (int)(a.y * 2.9f) ^ (int)(b.x * 3.1f));
                    bool avenue = w >= 12f; float step = avenue ? 18f : 34f;
                    int sp = avenue ? (r.NextDouble() < .6 ? SpCoconut : SpDate) : PickYardTree(r);
                    for (float d = step * (float)r.NextDouble(); d < len; d += step)
                        foreach (int sgn in new[] { 1, -1 })
                        {
                            if (!avenue && r.NextDouble() < .55) continue;
                            var q = a + dir * d + side * sgn * (w / 2f + 2.2f);
                            if (!PlantSpotOk(q, .8f)) continue;
                            if (Locate(q, out var qs, out var ql) && DistanceFromCarriageway(p, south, qs, ql) < 70f) Model(sp, q, .9f + (float)r.NextDouble() * .2f, r);
                            else Card(sp, q, .9f + (float)r.NextDouble() * .2f, r);
                        }
                }
            // The highway's own land: clumps of palms and trees with shrubs at their feet, not an even scatter.
            {
                var r = new System.Random(95);
                for (float s = 60f; s < p.Length - 60f; s += 70f + (float)r.NextDouble() * 120f)
                    foreach (int sgn in new[] { 1, -1 })
                    {
                        if (r.NextDouble() < .35) continue;
                        float off = 14f + (float)(r.NextDouble() * r.NextDouble()) * 50f;
                        float lat = sgn > 0 ? p.HalfWidth(s) + off : south(s) - p.HalfWidth(s) - off;
                        bool palms = r.NextDouble() < .6; int n = 2 + r.Next(5);
                        for (int i = 0; i < n; i++)
                        {
                            float ds = (float)(r.NextDouble() - .5) * 22f, dl = (float)(r.NextDouble() - .5) * 10f * sgn;
                            var spot = p.Point(s + ds, lat + dl); var q = new Vector2(spot.x, spot.z);
                            if (occupied.Contains(((int)(spot.x / 30f), (int)(spot.z / 30f))) && InBuilding(q, 2f)) continue;
                            if (OnRoad(q, 11f) || NearExit(q, 8f)) continue;
                            int sp = palms ? (r.NextDouble() < .45 ? SpCoconut : r.NextDouble() < .5 ? SpDate : SpYoung) : (r.NextDouble() < .5 ? SpShade : SpRound);
                            float sc = .85f + (float)r.NextDouble() * .35f;
                            if (species[sp].fullModel) Model(sp, q, sc, r); else Card(sp, q, sc, r);
                            Solid(s + ds, lat + dl, .4f, .4f, "tree");
                            for (int k = r.Next(3); k > 0; k--) { var sq = q + new Vector2((float)r.NextDouble() * 4f - 2f, (float)r.NextDouble() * 4f - 2f); if (!OnRoad(sq, 10f)) { Card(SpShrub, sq, 1f + (float)r.NextDouble(), r); shrubs++; } }
                        }
                    }
            }
            FlushCards(chunks, "Plants", cardMats);
            var oldPools = GameObject.Find("Pools"); if (oldPools != null) UnityEngine.Object.DestroyImmediate(oldPools);
            var waterMat = Lit("PoolWater", new Color(.18f, .62f, .78f), .92f);
            var deckMat = AssetDatabase.LoadAssetAtPath<Material>(Gen + "/Materials/CityPave.mat") ?? Lit("PoolDeck", new Color(.85f, .83f, .78f), .1f);
            Flush(pools, "Pools", new[] { waterMat, deckMat }, 2, false);
            BuildFences(p, south);
            BuildWater(p); BuildPower(p, south); BuildRail(p); BuildBillboards(p, south);
            Debug.Log($"[FloridaSlice] plants: cards {cards} (shrubs {shrubs}), full palms near the road {models}, pools {poolCount}");
        }

        static float DistanceFromCarriageway(RoutePath p, Func<float, float> south, float s, float lat)
        {
            float h = p.HalfWidth(s), sl = south(s);
            if (lat > h) return lat - h;
            if (lat < sl - h) return sl - h - lat;
            return 0f;
        }

        class CardBucket
        {
            public List<Vector3>[] v, n; public List<Vector2>[] uv; public List<int>[] t;
            public CardBucket(int k)
            {
                v = new List<Vector3>[k]; n = new List<Vector3>[k]; uv = new List<Vector2>[k]; t = new List<int>[k];
                for (int i = 0; i < k; i++) { v[i] = new List<Vector3>(); n[i] = new List<Vector3>(); uv[i] = new List<Vector2>(); t[i] = new List<int>(); }
            }
        }

        static void FlushCards(Dictionary<(int, int), CardBucket> map, string name, Material[] mats)
        {
            var old = GameObject.Find(name); if (old != null) UnityEngine.Object.DestroyImmediate(old);
            var parent = new GameObject(name); int chunk = 0;
            foreach (var cb in map.Values)
            {
                var all = new List<Vector3>(); var allN = new List<Vector3>(); var allUv = new List<Vector2>(); var subs = new List<int[]>();
                for (int i = 0; i < mats.Length; i++) { int off = all.Count; all.AddRange(cb.v[i]); allN.AddRange(cb.n[i]); allUv.AddRange(cb.uv[i]); subs.Add(cb.t[i].Select(x => x + off).ToArray()); }
                var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(all); mesh.SetNormals(allN); mesh.SetUVs(0, allUv); mesh.subMeshCount = mats.Length;
                for (int i = 0; i < mats.Length; i++) mesh.SetTriangles(subs[i], i);
                mesh.RecalculateTangents(); mesh.RecalculateBounds();
                var file = $"{Gen}/Meshes/{name}_{chunk++}.asset"; AssetDatabase.DeleteAsset(file); AssetDatabase.CreateAsset(mesh, file);
                var go = new GameObject(name + "Block", typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(parent.transform);
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.GetComponent<MeshRenderer>(); r.sharedMaterials = mats; r.shadowCastingMode = ShadowCastingMode.On;
            }
        }
    }
}
