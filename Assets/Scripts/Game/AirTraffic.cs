using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SurviveI95.Game
{
    /// <summary>
    /// Airliners on the real runways near the route (from OpenStreetMap, in Data/Routes/&lt;route&gt;_air.json).
    /// South Florida's wind blows from the east most of the year, so both airports normally run "east flow":
    /// arrivals come in from the west on a 3° glide path and land heading east, and departures roll east and climb
    /// out over the city. At Fort Lauderdale that brings landing jets low over I-95, about 100 m up, a sight every
    /// driver there knows. Miami International runs about 1,100 flights a day, Fort Lauderdale about 800, so at each
    /// airport a plane lands or leaves every minute or two. Planes are built in code, plain white with a colored tail
    /// and no airline marks; landing lights burn on approach, as real ones do in daylight.
    /// </summary>
    public class AirTraffic : MonoBehaviour
    {
        [Serializable] class Runway { public string airport, @ref; public float ax, az, bx, bz; }
        [Serializable] class AirFile { public Runway[] runways; }

        class Plane { public Transform t; public Vector3 a, b; public float time, length, speed; public bool arrival; public Renderer lights; public Transform gear; }

        const float Glide = 3f, ApproachStart = 14000f, ClimbOut = 16000f;
        readonly List<Runway> runways = new List<Runway>();
        readonly List<Plane> planes = new List<Plane>();
        readonly Dictionary<string, float> nextArrival = new Dictionary<string, float>(), nextDeparture = new Dictionary<string, float>();
        Mesh body, gearMesh; Material white, glass, metal, lamp; Material[] tails;
        Transform player;
        System.Random rnd = new System.Random(1958);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var car = FindAnyObjectByType<PlayerCar>();
            if (car == null || FindAnyObjectByType<AirTraffic>() != null) return;
            var asset = Resources.Load<TextAsset>("Data/Routes/" + car.routeId + "_air");
            if (asset == null) return;
            var go = new GameObject("AirTraffic"); var at = go.AddComponent<AirTraffic>();
            at.player = car.transform;
            at.runways.AddRange(JsonUtility.FromJson<AirFile>(asset.text).runways);
        }

        void Start()
        {
            BuildModel();
            foreach (var r in runways)
            {
                // Spread the first ones out, and put one plane already on approach at each airport.
                if (!nextArrival.ContainsKey(r.airport)) { nextArrival[r.airport] = Time.time + (float)rnd.NextDouble() * 20f; nextDeparture[r.airport] = Time.time + 30f + (float)rnd.NextDouble() * 40f; }
            }
            foreach (var airport in new List<string>(nextArrival.Keys)) Spawn(airport, true, .55f);
        }

        Runway Pick(string airport, bool arrival)
        {
            // East flow: the longest runways that run roughly east-west. Arrivals and departures alternate between them.
            Runway best = null; float bestLen = 0; int n = 0;
            foreach (var r in runways)
            {
                if (r.airport != airport) continue;
                var d = new Vector2(r.bx - r.ax, r.bz - r.az); float len = d.magnitude;
                if (Mathf.Abs(d.y) > Mathf.Abs(d.x) * .75f) continue; // a crosswind runway
                n++;
                float score = len * (arrival == (n % 2 == 1) ? 1.05f : 1f);
                if (score > bestLen) { bestLen = score; best = r; }
            }
            return best;
        }

        void Spawn(string airport, bool arrival, float progress = 0f)
        {
            var r = Pick(airport, arrival); if (r == null) return;
            // Make "a" the west end, so the plane always moves east.
            Vector3 w = new Vector3(r.ax, 0, r.az), e = new Vector3(r.bx, 0, r.bz);
            if (w.x > e.x) { var t = w; w = e; e = t; }
            var dir = (e - w).normalized;
            var p = new Plane { arrival = arrival };
            if (arrival)
            {
                // Over the threshold at 15 m, down a 3° slope; touch down 300 m in and roll out.
                var thr = w + dir * 300f;
                p.a = thr - dir * ApproachStart + Vector3.up * (15f + ApproachStart * Mathf.Tan(Glide * Mathf.Deg2Rad));
                p.b = thr + dir * 1400f + Vector3.up * 1.5f;
                p.speed = 72f; // about 140 knots on final
            }
            else
            {
                p.a = w + dir * 150f + Vector3.up * 1.5f;
                p.b = p.a + dir * ClimbOut + Vector3.up * ClimbOut * .12f;
                p.speed = 85f;
            }
            p.length = (p.b - p.a).magnitude;
            p.time = progress * p.length / p.speed;
            var go = new GameObject(arrival ? "Arrival" : "Departure");
            go.transform.SetParent(transform);
            var mr = go.AddComponent<MeshRenderer>(); go.AddComponent<MeshFilter>().sharedMesh = body;
            mr.sharedMaterials = new[] { white, tails[rnd.Next(tails.Length)], glass, metal, lamp };
            mr.shadowCastingMode = ShadowCastingMode.On;
            var gear = new GameObject("Gear", typeof(MeshFilter), typeof(MeshRenderer)); gear.transform.SetParent(go.transform, false);
            gear.GetComponent<MeshFilter>().sharedMesh = gearMesh; gear.GetComponent<MeshRenderer>().sharedMaterial = metal;
            p.t = go.transform; p.gear = gear.transform;
            planes.Add(p);
            Place(p);
        }

        void Place(Plane p)
        {
            float k = Mathf.Clamp01(p.time * p.speed / p.length);
            Vector3 pos, fwd = (p.b - p.a).normalized;
            if (p.arrival)
            {
                // The flare: the last few meters the nose comes up and the descent flattens onto the runway.
                pos = Vector3.Lerp(p.a, p.b, k);
                float flat = new Vector2(p.b.x - p.a.x, p.b.z - p.a.z).magnitude, along = k * flat, toThreshold = flat - 1400f - along;
                float h = Mathf.Max(1.5f, 15f + toThreshold * Mathf.Tan(Glide * Mathf.Deg2Rad));
                if (toThreshold < 0) h = Mathf.Max(1.5f, 15f + toThreshold * .05f);
                pos.y = h;
                fwd = new Vector3(fwd.x, toThreshold > 0 ? -Mathf.Tan(Glide * Mathf.Deg2Rad) : 0f, fwd.z).normalized;
                var pitchUp = Quaternion.AngleAxis(toThreshold > 0 ? -2.5f : -1f, Vector3.Cross(Vector3.up, fwd));
                p.t.SetPositionAndRotation(pos, pitchUp * Quaternion.LookRotation(fwd));
                p.gear.gameObject.SetActive(h < 900f);
            }
            else
            {
                // Rolls along the runway, rotates after about 1.7 km, then climbs.
                float dist = p.time * p.speed, roll = 1700f;
                var flatDir = new Vector3(fwd.x, 0, fwd.z).normalized;
                if (dist < roll) { pos = p.a + flatDir * dist; p.t.SetPositionAndRotation(pos, Quaternion.LookRotation(flatDir)); }
                else
                {
                    float c = dist - roll;
                    pos = p.a + flatDir * dist + Vector3.up * c * .13f;
                    p.t.SetPositionAndRotation(pos, Quaternion.LookRotation((flatDir + Vector3.up * .13f).normalized) * Quaternion.Euler(-4f, 0, 0));
                }
                p.gear.gameObject.SetActive(dist < roll + 600f);
            }
        }

        void Update()
        {
            foreach (var airport in new List<string>(nextArrival.Keys))
            {
                if (Time.time > nextArrival[airport]) { Spawn(airport, true); nextArrival[airport] = Time.time + 70f + (float)rnd.NextDouble() * 60f; }
                if (Time.time > nextDeparture[airport]) { Spawn(airport, false); nextDeparture[airport] = Time.time + 65f + (float)rnd.NextDouble() * 60f; }
            }
            for (int i = planes.Count - 1; i >= 0; i--)
            {
                var p = planes[i];
                p.time += Time.deltaTime;
                if (p.arrival && p.time * p.speed > p.length - 1400f) p.speed = Mathf.Max(12f, p.speed - 2.2f * Time.deltaTime); // braking on the runway
                if (p.time * p.speed >= p.length) { Destroy(p.t.gameObject); planes.RemoveAt(i); continue; }
                Place(p);
            }
        }

        public int Count => planes.Count;
        public Transform Nearest(Vector3 to) { Transform best = null; float d = float.MaxValue; foreach (var p in planes) { float dd = (p.t.position - to).sqrMagnitude; if (dd < d) { d = dd; best = p.t; } } return best; }
        public string Describe()
        {
            var parts = new List<string>();
            foreach (var p in planes) parts.Add($"{(p.arrival ? "arr" : "dep")} {p.t.position.x:F0},{p.t.position.y:F0},{p.t.position.z:F0}");
            return string.Join(" | ", parts);
        }

        // ---------- the airliner: a narrow-body twinjet, about 37 m long with a 35 m span ----------
        void BuildModel()
        {
            Material M(Color c, float smooth, float metallic = 0f)
            {
                var m = new Material(Shader.Find("Universal Render Pipeline/Lit")); m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metallic); m.SetFloat("_Cull", 0f); return m;
            }
            white = M(new Color(.93f, .94f, .95f), .6f);
            glass = M(new Color(.05f, .06f, .08f), .9f);
            metal = M(new Color(.62f, .64f, .66f), .55f, .6f);
            lamp = M(Color.white, .9f); lamp.EnableKeyword("_EMISSION"); lamp.SetColor("_EmissionColor", new Color(6f, 6f, 5.5f));
            tails = new[] { M(new Color(.08f, .18f, .45f), .5f), M(new Color(.7f, .08f, .1f), .5f), M(new Color(.05f, .45f, .5f), .5f), M(new Color(.95f, .55f, .1f), .5f), M(new Color(.3f, .1f, .45f), .5f), M(new Color(.93f, .94f, .95f), .5f) };
            var parts = new List<int>[5]; for (int i = 0; i < 5; i++) parts[i] = new List<int>();
            var v = new List<Vector3>();
            // Fuselage: rings along z, tapered at the nose and the tail cone.
            const int sides = 14; float len = 37.6f;
            float[] zs = { -18.8f, -17.5f, -14f, -10f, 0f, 10f, 14.5f, 17f, 18.4f, 18.8f };
            float Radius(float z) => z > 14f ? Mathf.Lerp(1.98f, .25f, Mathf.Pow((z - 14f) / 4.8f, 1.6f)) : z < -10f ? Mathf.Lerp(1.98f, .35f, Mathf.Pow((-10f - z) / 8.8f, 1.2f)) : 1.98f;
            float Lift(float z) => z < -10f ? (-10f - z) * .14f : 0f; // the tail cone sweeps up
            int ringStart = v.Count;
            foreach (var z in zs)
                for (int s = 0; s <= sides; s++)
                {
                    float a = s * Mathf.PI * 2f / sides; float r = Radius(z);
                    v.Add(new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r * 1.05f + Lift(z), z));
                }
            for (int i = 0; i + 1 < zs.Length; i++)
                for (int s = 0; s < sides; s++)
                {
                    int q = ringStart + i * (sides + 1) + s, w = sides + 1;
                    parts[0].AddRange(new[] { q, q + 1, q + w, q + 1, q + w + 1, q + w });
                }
            void Box(int part, Vector3 c, Vector3 size, float sweep = 0f, float dihedral = 0f)
            {
                var h = size / 2f; int n = v.Count;
                for (int i = 0; i < 8; i++)
                {
                    var p = new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z);
                    p.z -= Mathf.Abs(p.x) * sweep; p.y += Mathf.Abs(p.x) * dihedral;
                    v.Add(c + p);
                }
                int[] q = { 0, 2, 1, 1, 2, 3, 4, 5, 6, 5, 7, 6, 0, 1, 4, 1, 5, 4, 2, 6, 3, 3, 6, 7, 0, 4, 2, 2, 4, 6, 1, 3, 5, 3, 7, 5 };
                foreach (var i in q) parts[part].Add(n + i);
            }
            Box(0, new Vector3(0, -.9f, 1f), new Vector3(34.3f, .35f, 4.2f), .45f, .1f);       // wings, swept and lifted at the tips
            Box(0, new Vector3(0, .5f, -16.4f), new Vector3(13f, .2f, 2.6f), .5f, .05f);       // tailplane
            Box(1, new Vector3(0, 4.3f, -16.2f), new Vector3(.35f, 6.2f, 4.2f));                // fin, in the tail color
            for (int s = -1; s <= 1; s += 2)
            {
                // Engines under the wings, nacelle in the tail color's place on many airlines: kept white here.
                var ec = new Vector3(s * 5.8f, -2.1f, 3.6f);
                int n0 = v.Count;
                for (int zi = 0; zi < 2; zi++) for (int k = 0; k <= 10; k++)
                {
                    float a = k * Mathf.PI * 2f / 10f; v.Add(ec + new Vector3(Mathf.Cos(a) * .95f, Mathf.Sin(a) * .95f, zi == 0 ? -1.8f : 1.8f));
                }
                for (int k = 0; k < 10; k++) parts[3].AddRange(new[] { n0 + k, n0 + 11 + k, n0 + k + 1, n0 + k + 1, n0 + 11 + k, n0 + 12 + k });
                Box(4, new Vector3(s * 3f, -1.05f, 3.9f), new Vector3(.25f, .2f, .1f));           // landing lights at the wing roots
            }
            Box(2, new Vector3(0, .7f, 17.2f), new Vector3(2.2f, .5f, 1f));                         // cockpit windows
            body = new Mesh { name = "Airliner" }; body.SetVertices(v); body.subMeshCount = 5;
            for (int i = 0; i < 5; i++) body.SetTriangles(parts[i], i);
            body.RecalculateNormals(); body.RecalculateBounds();
            // The gear: nose leg and two main legs with their wheels.
            var gv = new List<Vector3>(); var gt = new List<int>();
            void GBox(Vector3 c, Vector3 size) { var h = size / 2f; int n = gv.Count; for (int i = 0; i < 8; i++) gv.Add(c + new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z)); int[] q = { 0, 2, 1, 1, 2, 3, 4, 5, 6, 5, 7, 6, 0, 1, 4, 1, 5, 4, 2, 6, 3, 3, 6, 7, 0, 4, 2, 2, 4, 6, 1, 3, 5, 3, 7, 5 }; foreach (var i in q) gt.Add(n + i); }
            GBox(new Vector3(0, -2.6f, 14f), new Vector3(.25f, 1.6f, .25f)); GBox(new Vector3(0, -3.3f, 14f), new Vector3(.5f, .7f, .7f));
            foreach (int s in new[] { -1, 1 }) { GBox(new Vector3(s * 2.9f, -2.4f, -.5f), new Vector3(.3f, 1.8f, .3f)); GBox(new Vector3(s * 2.9f, -3.2f, -.5f), new Vector3(.9f, 1.1f, 1.6f)); }
            gearMesh = new Mesh { name = "Gear" }; gearMesh.SetVertices(gv); gearMesh.SetTriangles(gt, 0); gearMesh.RecalculateNormals();
        }
    }
}
