using System;
using SurviveI95.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SurviveI95.Game
{
    /// <summary>
    /// The clock and the light (5b.3b). The sun follows its real path over Miami in mid-March (sunset about 7:30 PM), and the
    /// sky carries from the day photo through sunset and dusk into night, each photo turned so its sun sits where the real sun
    /// is. Sunlight, the light in shade and the haze come from colors measured in those same photos (LookBuilder), so they
    /// always agree with the sky. At dusk the median lamps come on in a warm white; the nearest ones really light the road.
    /// The clock runs with the trip; F7 jumps the trip's start to late afternoon, sunset, dusk or night to see them.
    /// </summary>
    public class DayNight : MonoBehaviour
    {
        [Serializable]
        public class SkyKey
        {
            public string name; public Texture2D tex;
            /// <summary>Where the sun (or its glow) sits in the photo, as LookBuilder measured it.</summary>
            public float sunLongitude, photoElevation;
            /// <summary>The real sun height this photo stands for.</summary>
            public float atElevation;
            public float exposure = 1f;
            public Color horizon, overhead;
        }

        // Filled by LookBuilder when the look is applied.
        public SkyKey[] keys;
        public float sunIntensity = 2.4f, ambient = 1f, groundBounce = .2f, hazeDensity = .0003f, hazeBrightness = 1f, postExposure = .35f;
        public Color sunColor = Color.white;
        /// <summary>The color temperature of this state's highway lamps (Florida: 3000 K, warm white).</summary>
        public float lampKelvin = 3000f;
        public Shader skyShader;
        /// <summary>The light a big city throws up into its night sky (linear).</summary>
        public Color CityGlow = new Color(.034f, .022f, .016f);

        // Miami, mid-March, Eastern Daylight Time.
        const float Latitude = 25.8f, Longitude = -80.2f, DayOfYear = 74f, UtcOffset = -4f;
        public static float? TestMinutes; // the bridge's "clock" command
        public static float StartShift; // minutes F7 moved the trip's start
        public float Minutes { get; private set; }
        public float SunElevation { get; private set; }
        public float SunAzimuth { get; private set; }
        public float Night { get; private set; } // 0 day, 1 full night

        Light sun; Material sky; ColorAdjustments grade; PlayerCar car;
        static readonly float[] Starts = { 0f, 190f, 225f, 380f }; // F7: 3:40 PM, 6:50 PM, 7:25 PM, 10:00 PM
        int startPick; float refreshedAt = 999f, refreshTime = -99f;

        void Start()
        {
            car = FindAnyObjectByType<PlayerCar>();
            sun = RenderSettings.sun;
            if (keys == null || keys.Length == 0 || skyShader == null) { enabled = false; return; }
            sky = new Material(skyShader);
            RenderSettings.skybox = sky;
            var vol = FindAnyObjectByType<Volume>();
            if (vol != null && vol.profile != null) vol.profile.TryGet(out grade); // the runtime copy, so the saved look isn't touched
            BuildLampGlow();
            BuildLampLights();
        }

        void Update()
        {
            var k = Keyboard.current;
            if (k != null && k.f7Key.wasPressedThisFrame)
            {
                startPick = (startPick + 1) % Starts.Length; StartShift = Starts[startPick];
                if (car != null) car.Flash("Leaving at " + Clock(15f * 60f + 40f + StartShift), 2.5f);
            }
            var trip = car != null ? car.Trip : null;
            Minutes = TestMinutes ?? (trip != null && trip.Plan != null ? trip.LeaveMinutes + StartShift + trip.Elapsed / 60f : 15f * 60f + 40f + StartShift);
            Solar(Minutes, out float el, out float az);
            SunElevation = el; SunAzimuth = az;
            Night = Mathf.InverseLerp(2f, -10f, el);
            ApplySky(el, az);
            ApplyLight(el, az);
            // Reflections and the light in shade come from a copy of the sky Unity keeps; refresh it as the light changes.
            if (Mathf.Abs(el - refreshedAt) > .7f || Time.time - refreshTime > 20f) { DynamicGI.UpdateEnvironment(); refreshedAt = el; refreshTime = Time.time; }
            RenderSettings.reflectionIntensity = Mathf.Lerp(1f, .35f, Night);
            UpdateLamps();
        }

        public static string Clock(float m)
        {
            int t = Mathf.FloorToInt(m) % (24 * 60), h = t / 60, mm = t % 60;
            return $"{(h % 12 == 0 ? 12 : h % 12)}:{mm:00} {(h < 12 ? "AM" : "PM")}";
        }

        /// <summary>The sun's height and compass bearing over Miami at a local clock time (NOAA's simple solar formulas).</summary>
        public static void Solar(float localMinutes, out float elevation, out float azimuth)
        {
            float g = 2f * Mathf.PI / 365f * (DayOfYear - 1f + (localMinutes / 60f - UtcOffset - 12f) / 24f);
            float eqTime = 229.18f * (.000075f + .001868f * Mathf.Cos(g) - .032077f * Mathf.Sin(g) - .014615f * Mathf.Cos(2f * g) - .040849f * Mathf.Sin(2f * g));
            float decl = .006918f - .399912f * Mathf.Cos(g) + .070257f * Mathf.Sin(g) - .006758f * Mathf.Cos(2f * g) + .000907f * Mathf.Sin(2f * g) - .002697f * Mathf.Cos(3f * g) + .00148f * Mathf.Sin(3f * g);
            float solarMinutes = localMinutes + eqTime + 4f * Longitude - 60f * UtcOffset;
            float ha = (solarMinutes / 4f - 180f) * Mathf.Deg2Rad, lat = Latitude * Mathf.Deg2Rad;
            float cosZen = Mathf.Sin(lat) * Mathf.Sin(decl) + Mathf.Cos(lat) * Mathf.Cos(decl) * Mathf.Cos(ha);
            float zen = Mathf.Acos(Mathf.Clamp(cosZen, -1f, 1f));
            elevation = 90f - zen * Mathf.Rad2Deg;
            float az = Mathf.Acos(Mathf.Clamp((Mathf.Sin(lat) * Mathf.Cos(zen) - Mathf.Sin(decl)) / (Mathf.Cos(lat) * Mathf.Sin(zen)), -1f, 1f)) * Mathf.Rad2Deg;
            azimuth = ha > 0f ? Mathf.Repeat(az + 180f, 360f) : Mathf.Repeat(540f - az, 360f);
        }

        // The two photos either side of the sun's height, and how far between them.
        void Pick(float el, out SkyKey a, out SkyKey b, out float t)
        {
            a = b = keys[0]; t = 0f;
            if (el >= keys[0].atElevation) return;
            for (int i = 0; i < keys.Length - 1; i++)
                if (el < keys[i].atElevation && el >= keys[i + 1].atElevation)
                { a = keys[i]; b = keys[i + 1]; t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a.atElevation, b.atElevation, el)); return; }
            a = b = keys[keys.Length - 1];
        }

        float Rotation(SkyKey key, float az) => Mathf.Repeat(90f - az - key.sunLongitude, 360f);

        void ApplySky(float el, float az)
        {
            Pick(el, out var a, out var b, out float t);
            sky.SetTexture("_TexA", a.tex); sky.SetTexture("_TexB", b.tex);
            sky.SetFloat("_RotA", Rotation(a, az)); sky.SetFloat("_RotB", Rotation(b, az));
            sky.SetFloat("_ExpA", a.exposure); sky.SetFloat("_ExpB", b.exposure); sky.SetFloat("_Blend", t);
            var g = CityGlow * 2.2f; g.a = Mathf.InverseLerp(-2f, -12f, el); sky.SetColor("_Glow", g);
            // Light in shade and the haze: the photos' own measured colors, as for the day look.
            var horizon = Color.Lerp(a.horizon * a.exposure, b.horizon * b.exposure, t);
            var overhead = Color.Lerp(a.overhead * a.exposure, b.overhead * b.exposure, t);
            // A city's night sky glows from its own lights, warm near the horizon: Miami's is orange-pink, never green.
            float glowAmount = Mathf.InverseLerp(-2f, -12f, el);
            horizon = Color.Lerp(horizon, CityGlow, glowAmount * .85f);
            overhead = Color.Lerp(overhead, CityGlow * .45f, glowAmount * .5f);
            float sunUp = Mathf.Clamp01(Mathf.Sin(Mathf.Max(0f, el) * Mathf.Deg2Rad));
            float bounce = groundBounce * sunIntensity * sunUp;
            var sl = sunColor.linear;
            RenderSettings.ambientSkyColor = (overhead * ambient).gamma;
            RenderSettings.ambientEquatorColor = (horizon * ambient).gamma;
            RenderSettings.ambientGroundColor = new Color(sl.r * bounce, sl.g * bounce * .97f, sl.b * bounce * .9f).gamma;
            var haze = horizon * hazeBrightness; haze.a = 1f;
            RenderSettings.fogColor = haze.gamma;
            RenderSettings.fogDensity = hazeDensity;
        }

        void ApplyLight(float el, float az)
        {
            if (sun == null) return;
            // Low sun: weaker through more air, and redder. Below the horizon the same light becomes the moon: faint and blue.
            float up = Mathf.InverseLerp(-1f, 10f, el);
            float warm = Mathf.InverseLerp(25f, 1f, el);
            float moon = Mathf.InverseLerp(-3f, -10f, el);
            Vector3 dir;
            if (el > -2f)
            {
                float a = az * Mathf.Deg2Rad, e = Mathf.Max(el, 1.5f) * Mathf.Deg2Rad;
                dir = new Vector3(Mathf.Sin(a) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Cos(a) * Mathf.Cos(e));
                sun.intensity = sunIntensity * up * Mathf.Lerp(1f, .55f, warm);
                sun.color = Color.Lerp(sunColor, new Color(1f, .62f, .38f), warm * .8f);
            }
            else
            {
                // A waxing moon high in the southeast-to-south sky: enough to see the shapes of things, never to drive by.
                dir = Quaternion.Euler(0f, 150f, 0f) * new Vector3(0f, .8f, .6f);
                sun.intensity = .08f * moon;
                sun.color = new Color(.62f, .72f, 1f);
            }
            sun.transform.rotation = Quaternion.LookRotation(-dir.normalized);
            // The eye adapts: night is brighter to the eye than to a camera.
            if (grade != null) grade.postExposure.value = postExposure + Night * 1.1f;
        }

        // ---------------------------------------------------------------- the median lamps

        Material glow; readonly Light[] pool = new Light[24]; Vector3[] heads = new Vector3[0]; float[] scores = new float[0]; float nextPick;

        // A glowing lens under each arm of every lamp, as one mesh, so it costs one draw.
        void BuildLampGlow()
        {
            var lamps = GameObject.Find("StreetLamps");
            if (lamps == null) return;
            var list = new System.Collections.Generic.List<Vector3>();
            var v = new System.Collections.Generic.List<Vector3>(); var tri = new System.Collections.Generic.List<int>();
            foreach (Transform l in lamps.transform)
                foreach (float side in new[] { -3.5f, 3.5f })
                {
                    var c = l.position + l.right * side + Vector3.up * 12.34f;
                    list.Add(c);
                    var r = l.right * .34f; var f = l.forward * .14f; int n = v.Count;
                    v.Add(c - r - f); v.Add(c + r - f); v.Add(c + r + f); v.Add(c - r + f);
                    tri.AddRange(new[] { n, n + 1, n + 2, n, n + 2, n + 3, n, n + 2, n + 1, n, n + 3, n + 2 });
                }
            heads = list.ToArray(); scores = new float[heads.Length];
            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(v); mesh.SetTriangles(tri, 0); mesh.RecalculateBounds();
            var go = new GameObject("LampGlow", typeof(MeshFilter), typeof(MeshRenderer));
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            glow = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            var mr = go.GetComponent<MeshRenderer>(); mr.sharedMaterial = glow; mr.shadowCastingMode = ShadowCastingMode.Off;
        }

        // The nearest lamps (each arm) cast real light on the road; farther ones are only their glowing lenses.
        void BuildLampLights()
        {
            for (int i = 0; i < pool.Length; i++)
            {
                var l = new GameObject("LampLight").AddComponent<Light>();
                l.type = LightType.Spot; l.spotAngle = 140f; l.innerSpotAngle = 70f; l.range = 42f;
                l.useColorTemperature = true; l.colorTemperature = lampKelvin; l.color = Color.white;
                l.shadows = LightShadows.None; l.enabled = false;
                l.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
                pool[i] = l;
            }
        }

        public float LampsOn { get; private set; }

        void UpdateLamps()
        {
            // Photocells switch the lamps on a little before sunset and off after sunrise.
            float on = Mathf.InverseLerp(4f, 0f, SunElevation);
            LampsOn = on;
            if (glow != null)
            {
                var c = Mathf.CorrelatedColorTemperatureToRGB(lampKelvin);
                glow.SetColor("_BaseColor", Color.Lerp(new Color(.32f, .33f, .33f), c * (1f + 5f * on), on));
            }
            if (heads.Length == 0 || car == null) return;
            if (on <= .01f) { if (pool[0].enabled) foreach (var l in pool) l.enabled = false; return; } // daytime: nothing to do
            // The nearest heads, ahead weighted a little more than behind (the driver looks ahead), re-picked a few times a second.
            if (Time.time >= nextPick)
            {
                nextPick = Time.time + .25f;
                var p = car.transform.position; var fwd = car.transform.forward;
                for (int i = 0; i < heads.Length; i++) scores[i] = Score(heads[i], p, fwd);
                Array.Sort(scores, heads);
            }
            for (int i = 0; i < pool.Length; i++)
            {
                bool use = on > .01f && i < heads.Length;
                pool[i].enabled = use;
                if (!use) continue;
                pool[i].transform.position = heads[i];
                pool[i].intensity = 55f * on;
            }
        }

        static float Score(Vector3 h, Vector3 p, Vector3 fwd) { var d = h - p; d.y = 0f; return d.magnitude - Vector3.Dot(d, fwd) * .4f; }

        public string Status() => FormattableString.Invariant($"clock={Clock(Minutes)} sunEl={SunElevation:F1} sunAz={SunAzimuth:F0} night={Night:F2} lamps={LampsOn:F2} sun={(sun != null ? sun.intensity : 0f):F2}");
    }
}
