using System.Collections.Generic;
using SurviveI95.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SurviveI95.Game
{
    /// <summary>
    /// Speed you hear and see (5b.4, asked by the creator on 2026-11-12): every lamp pole, sign-bridge leg and overpass
    /// pier you pass close by gives a quick swish of air from its side, louder, sharper and shorter the faster and closer
    /// you go, so the faster you drive the busier the world sounds. Past about 110 mph a light motion blur creeps in at
    /// the edges of the view. (Cars and trucks passing already whoosh with their own recordings and Doppler.)
    /// </summary>
    public class PassBy : MonoBehaviour
    {
        PlayerCar car;
        readonly List<Vector3> posts = new List<Vector3>();
        readonly List<float> lastZ = new List<float>();
        AudioClip swish; AudioSource[] voices; int next;
        MotionBlur blur;
        public int Swishes { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var c = FindAnyObjectByType<PlayerCar>();
            if (c != null && c.GetComponent<PassBy>() == null) c.gameObject.AddComponent<PassBy>();
        }

        void Start()
        {
            car = GetComponent<PlayerCar>();
            // Things standing beside the road at body height or taller, narrow enough to whip past.
            void From(string root, System.Func<Renderer, bool> keep)
            {
                var g = GameObject.Find(root); if (g == null) return;
                foreach (var r in g.GetComponentsInChildren<Renderer>()) if (keep(r)) posts.Add(r.bounds.center);
            }
            var lamps = GameObject.Find("StreetLamps");
            if (lamps != null) foreach (Transform l in lamps.transform) posts.Add(l.position + Vector3.up);
            From("Overpasses", r => r.name == "Pier");
            foreach (var sb in FindObjectsByType<Transform>(FindObjectsInactive.Exclude))
                if (sb.name == "SignBridge")
                    foreach (var r in sb.GetComponentsInChildren<Renderer>())
                    { var b = r.bounds.size; if (b.y > 4f && b.x < 1.5f && b.z < 1.5f) posts.Add(r.bounds.center); }
            for (int i = 0; i < posts.Count; i++) lastZ.Add(float.MaxValue);
            swish = MakeSwish();
            voices = new AudioSource[6];
            for (int i = 0; i < voices.Length; i++) { var a = gameObject.AddComponent<AudioSource>(); a.playOnAwake = false; a.spatialBlend = 0f; a.clip = swish; voices[i] = a; }
            var vol = FindAnyObjectByType<Volume>();
            if (vol != null && vol.profile != null && !vol.profile.TryGet(out blur)) blur = vol.profile.Add<MotionBlur>(true);
            if (blur != null) { blur.mode.Override(MotionBlurMode.CameraOnly); blur.quality.Override(MotionBlurQuality.Medium); blur.intensity.Override(0f); }
        }

        // A short rush of air: noise through a resonant band that sweeps down as the object goes by, quick in and out.
        static AudioClip MakeSwish()
        {
            const int rate = 48000; int n = (int)(rate * .32f);
            var d = new float[n]; var r = new System.Random(5);
            float lp = 0f, bp = 0f, low = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float f = Mathf.Lerp(2600f, 700f, t); // falling pitch, the sound's Doppler
                float q = .18f, w = 2f * Mathf.Sin(Mathf.PI * f / rate);
                float x = (float)(r.NextDouble() * 2.0 - 1.0);
                // A state-variable filter: low and band outputs.
                low += w * bp; float high = x - low - q * bp; bp += w * high;
                lp += (x - lp) * .08f;
                float env = Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Pow(t, .55f)), 2.2f);
                d[i] = (bp * .55f + lp * .35f) * env;
            }
            float peak = 0f; foreach (var v in d) peak = Mathf.Max(peak, Mathf.Abs(v));
            for (int i = 0; i < n; i++) d[i] *= .9f / Mathf.Max(peak, 1e-4f);
            var clip = AudioClip.Create("swish", n, 1, rate, false); clip.SetData(d, 0);
            return clip;
        }

        void Update()
        {
            if (car == null) return;
            float v = Mathf.Abs(car.Sim.Speed), mph = v * DriveSim.MetersPerSecondToMph;
            var t = car.transform; var p = t.position;
            float reach = 45f * 45f;
            for (int i = 0; i < posts.Count; i++)
            {
                var d = posts[i] - p;
                if (d.sqrMagnitude > reach) { lastZ[i] = float.MaxValue; continue; }
                var local = t.InverseTransformPoint(posts[i]);
                float before = lastZ[i]; lastZ[i] = local.z;
                // It just went by (from ahead to behind the driver), close to one side.
                if (before > .3f && local.z <= .3f && Mathf.Abs(local.x) < 9f && mph > 25f) Swish(local.x, mph);
            }
            // Past about 110 mph the edges start to smear with speed.
            if (blur != null) blur.intensity.value = Mathf.Clamp01((mph - 110f) / 70f) * .35f;
        }

        void Swish(float side, float mph)
        {
            var a = voices[next]; next = (next + 1) % voices.Length;
            float near = Mathf.Clamp01(2.2f / Mathf.Max(1f, Mathf.Abs(side) - 1f));
            float fast = Mathf.Clamp01((mph - 25f) / 100f);
            a.volume = near * fast * .5f * (car.Cockpit ? .45f : 1f);
            a.pitch = .75f + fast * .65f; // faster: a shorter, sharper swish
            a.panStereo = Mathf.Clamp(side / 4f, -.85f, .85f);
            a.Play(); Swishes++;
        }
    }
}
