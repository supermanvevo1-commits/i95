using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace SurviveI95.Game
{
    /// <summary>
    /// Shadows of fair-weather cumulus sliding over the land and the road, as on a South Florida day: blobs a few hundred
    /// meters to a kilometer and a half across, about a third of the sky, drifting west with the trade wind at about
    /// 8 m/s. Done with a pattern projected through the sunlight (a light cookie), so everything the sun lights, cars
    /// included, darkens as a shadow passes and nothing else changes. Shade under a cloud keeps the sky's light.
    /// </summary>
    public class CloudShadows : MonoBehaviour
    {
        // How far the pattern repeats, and how the clouds move (world meters, +X east, +Z north).
        const float Span = 6000f;
        public static Vector2 Wind = new Vector2(-8f, 1.2f);
        // How much direct sun gets through a cumulus.
        const float Through = .28f;
        // Share of the ground in cloud shadow.
        const float Cover = .32f;

        Light sun; UniversalAdditionalLightData data; Vector2 drift;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindAnyObjectByType<PlayerCar>() == null || FindAnyObjectByType<CloudShadows>() != null) return;
            new GameObject("CloudShadows").AddComponent<CloudShadows>();
        }

        void Start()
        {
            sun = RenderSettings.sun;
            if (sun == null) { var go = GameObject.Find("Sun"); if (go != null) sun = go.GetComponent<Light>(); }
            if (sun == null) { enabled = false; return; }
            data = sun.GetComponent<UniversalAdditionalLightData>() ?? sun.gameObject.AddComponent<UniversalAdditionalLightData>();
            sun.cookie = Make(512, 1234);
            data.lightCookieSize = new Vector2(Span, Span);
            // Start somewhere different each drive.
            drift = new Vector2(Random.value, Random.value) * Span;
            data.lightCookieOffset = drift;
        }

        void Update()
        {
            if (data == null) return;
            // The pattern lives in the sun's own frame; move it by the wind as seen from the sun.
            var w = new Vector3(Wind.x, 0f, Wind.y);
            drift += new Vector2(Vector3.Dot(w, sun.transform.right), Vector3.Dot(w, sun.transform.up)) * Time.deltaTime;
            drift.x = Mathf.Repeat(drift.x, Span); drift.y = Mathf.Repeat(drift.y, Span);
            data.lightCookieOffset = drift;
        }

        void OnDestroy() { if (sun != null) sun.cookie = null; }

        /// <summary>A seamless pattern of soft-edged cumulus: layered noise, cut at the level that leaves Cover in shadow.</summary>
        static Texture2D Make(int n, int seed)
        {
            var rnd = new System.Random(seed);
            int[] periods = { 5, 10, 20, 40 }; float[] weights = { 1f, .5f, .25f, .12f };
            var grids = new float[periods.Length][];
            for (int o = 0; o < periods.Length; o++) { grids[o] = new float[periods[o] * periods[o]]; for (int i = 0; i < grids[o].Length; i++) grids[o][i] = (float)rnd.NextDouble(); }
            var v = new float[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float s = 0f;
                    for (int o = 0; o < periods.Length; o++) s += weights[o] * Sample(grids[o], periods[o], (float)x / n * periods[o], (float)y / n * periods[o]);
                    v[y * n + x] = s;
                }
            // Find the level that puts Cover of the ground under cloud.
            var sorted = (float[])v.Clone(); System.Array.Sort(sorted);
            float level = sorted[(int)((1f - Cover) * (sorted.Length - 1))], soft = (sorted[sorted.Length - 1] - sorted[0]) * .06f;
            var px = new Color32[n * n];
            for (int i = 0; i < v.Length; i++)
            {
                float c = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(level - soft, level + soft, v[i]));
                byte b = (byte)(255f * Mathf.Lerp(1f, Through, c));
                px[i] = new Color32(b, b, b, b);
            }
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "CloudShadows", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            tex.SetPixels32(px); tex.Apply(true, true);
            return tex;
        }

        // Smooth value noise that wraps at its period, so the pattern tiles without a seam.
        static float Sample(float[] g, int p, float x, float y)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0; fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            float At(int a, int b) => g[((b % p + p) % p) * p + (a % p + p) % p];
            return Mathf.Lerp(Mathf.Lerp(At(x0, y0), At(x0 + 1, y0), fx), Mathf.Lerp(At(x0, y0 + 1), At(x0 + 1, y0 + 1), fx), fy);
        }
    }
}
