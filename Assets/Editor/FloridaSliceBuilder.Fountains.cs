using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SurviveI95.EditorTools
{
    /// <summary>
    /// Aerating fountains: about a third of South Florida's retention ponds have one floating in the middle, a pump that
    /// throws a white plume 3 to 5 m up and out in arcs, with a ring of foam where it lands. Each is one shared mesh,
    /// scaled to its own height and spread; `Fountains` (runtime) makes the plume pulse and turn.
    /// </summary>
    public static partial class FloridaSliceBuilder
    {
        static void BuildFountains(List<(Vector2 c, float room)> spots)
        {
            var old = GameObject.Find("Fountains"); if (old != null) Object.DestroyImmediate(old);
            if (spots.Count == 0) return;
            var spray = SprayMaterial("FountainSpray", SaveTexture(SprayTexture(), "FountainSprayTex"), new Color(.93f, .96f, .97f, .8f));
            var foam = SprayMaterial("FountainFoam", SaveTexture(FoamTexture(), "FountainFoamTex"), new Color(.9f, .93f, .92f, .75f));
            var mesh = FountainMesh();
            var meshFile = $"{Gen}/Meshes/FountainPlume.asset"; AssetDatabase.DeleteAsset(meshFile); AssetDatabase.CreateAsset(mesh, meshFile);
            var parent = new GameObject("Fountains");
            var r = new System.Random(905);
            var near = new List<string>();
            foreach (var (c, room) in spots)
            {
                if (Locate(c, out var fs, out var fl) && Mathf.Abs(fl) < 400f) near.Add($"{fs:F0}/{fl:F0}");
                float h = 3f + (float)r.NextDouble() * 1.8f, spread = Mathf.Min(room - 2f, 3.2f + (float)r.NextDouble() * 1.6f);
                var go = new GameObject("Fountain", typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(parent.transform);
                go.transform.SetPositionAndRotation(new Vector3(c.x, -.19f, c.y), Quaternion.Euler(0f, (float)r.NextDouble() * 360f, 0f));
                go.transform.localScale = new Vector3(spread, h, spread);
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.GetComponent<MeshRenderer>(); mr.sharedMaterials = new[] { spray, foam }; mr.shadowCastingMode = ShadowCastingMode.Off;
            }
            Debug.Log("[FloridaSlice] fountains near the road (s/lat): " + string.Join(" ", near));
        }

        static Material SprayMaterial(string name, Texture2D tex, Color tint)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mat.SetTexture("_BaseMap", tex); mat.SetColor("_BaseColor", tint);
            mat.SetFloat("_Surface", 1f); mat.SetFloat("_Blend", 0f); mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0); mat.SetFloat("_Cull", 0f); mat.renderQueue = 3000; mat.SetOverrideTag("RenderType", "Transparent");
            return Save(mat, name);
        }

        /// <summary>
        /// A unit plume (1 m out, 1 m up): eight crossed upright sheets, each the parabola the jets trace, so from any side
        /// some face the viewer; then a flat disk of foam on the water. Submesh 0 is spray, 1 is foam.
        /// </summary>
        static Mesh FountainMesh()
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var spray = new List<int>(); var foam = new List<int>();
            const int sheets = 8, cols = 20;
            for (int k = 0; k < sheets; k++)
            {
                float a = k * Mathf.PI / sheets; var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)); int b = v.Count;
                for (int i = 0; i <= cols; i++)
                {
                    float x = -1f + 2f * i / cols, top = 1f - x * x;
                    // The falling curtain reaches the water; the arc's foot is a little past the outer edge.
                    v.Add(dir * x * 1.05f); v.Add(dir * x + Vector3.up * Mathf.Max(top, .02f));
                    uv.Add(new Vector2(i / (float)cols, 0f)); uv.Add(new Vector2(i / (float)cols, 1f));
                    if (i > 0) { int q = b + (i - 1) * 2; spray.AddRange(new[] { q, q + 1, q + 2, q + 2, q + 1, q + 3 }); }
                }
            }
            int c = v.Count; v.Add(Vector3.up * .01f); uv.Add(new Vector2(.5f, .5f));
            const int seg = 28;
            for (int i = 0; i <= seg; i++)
            {
                float a = i * 2f * Mathf.PI / seg; var p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 1.3f;
                v.Add(p + Vector3.up * .01f); uv.Add(new Vector2(.5f + p.x / 2.6f, .5f + p.z / 2.6f));
                if (i > 0) foam.AddRange(new[] { c, c + i + 1, c + i });
            }
            var m = new Mesh { name = "FountainPlume" };
            m.SetVertices(v); m.SetUVs(0, uv); m.subMeshCount = 2; m.SetTriangles(spray, 0); m.SetTriangles(foam, 1);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        /// <summary>
        /// Spray seen side on (u across, v from the water up to the arc): a bright column in the middle, the arcs along
        /// the top edge breaking into droplets, and thin curtains falling at the ends.
        /// </summary>
        static Texture2D SprayTexture()
        {
            const int W = 128, H = 128; var px = new Color32[W * H]; var r = new System.Random(77);
            var streak = new float[W]; for (int i = 0; i < W; i++) streak[i] = .35f + (float)r.NextDouble() * .65f;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = (x + .5f) / W, vv = (y + .5f) / H, xs = Mathf.Abs(u * 2f - 1f);
                    float column = Mathf.Exp(-Mathf.Pow((u - .5f) / .045f, 2f)) * (.55f + .45f * vv);
                    float arc = Mathf.Exp(-Mathf.Pow((1f - vv) / .16f, 2f)) * (1f - .3f * xs);
                    float curtain = Mathf.SmoothStep(0f, 1f, (xs - .55f) / .4f) * .55f * (1f - vv * .5f);
                    float drops = (float)r.NextDouble();
                    float a = Mathf.Clamp01(column + arc * (.5f + drops * .7f) + curtain * streak[x] * (.6f + drops * .5f));
                    a *= Mathf.SmoothStep(0f, 1f, (1f - xs) * 6f + .25f); // fade the very ends into the water
                    px[y * W + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, true); tex.SetPixels32(px); tex.Apply();
            return tex;
        }

        /// <summary>Foam on the water: a churned ring where the jets land, broken up, fading out and thin in the middle.</summary>
        static Texture2D FoamTexture()
        {
            const int N = 128; var px = new Color32[N * N]; var r = new System.Random(78);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + .5f) / N * 2f - 1f, dy = (y + .5f) / N * 2f - 1f, d = Mathf.Sqrt(dx * dx + dy * dy) * 1.3f;
                    float ring = Mathf.Exp(-Mathf.Pow((d - .95f) / .2f, 2f)) + Mathf.Exp(-Mathf.Pow(d / .18f, 2f)) * .8f;
                    float a = Mathf.Clamp01(ring * (.45f + (float)r.NextDouble() * .7f)) * (d < 1.3f ? 1f : 0f);
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true); tex.SetPixels32(px); tex.Apply();
            return tex;
        }
    }
}
