using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SurviveI95.Game
{
    /// <summary>
    /// A commuter train on the railway beside I-95 (from the map, Data/Routes/&lt;route&gt;_rail.json): a diesel
    /// locomotive and four double-deck coaches, as South Florida's commuter rail runs, up to 79 mph (35 m/s).
    /// A train appears on the line nearest the player every few minutes, one way or the other, and runs past.
    /// Plain white and blue, no railroad's marks.
    /// </summary>
    public class Trains : MonoBehaviour
    {
        [Serializable] class RailLine { public float[] p; }
        [Serializable] class RailFile { public RailLine[] lines; }

        class Line { public List<Vector2> pts = new List<Vector2>(); public List<float> at = new List<float>(); public float length; }
        class Train { public Line line; public float s, speed, dir; public List<Transform> cars = new List<Transform>(); public float life; }

        const float CarLength = 26f, Gap = 1.2f, Cruise = 33f;
        readonly List<Line> lines = new List<Line>();
        readonly List<Train> trains = new List<Train>();
        PlayerCar car; Mesh loco, coach; Material[] mats;
        float next;
        readonly System.Random rnd = new System.Random(79);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var car = FindAnyObjectByType<PlayerCar>();
            if (car == null || FindAnyObjectByType<Trains>() != null) return;
            var asset = Resources.Load<TextAsset>("Data/Routes/" + car.routeId + "_rail");
            if (asset == null) return;
            var t = new GameObject("Trains").AddComponent<Trains>(); t.car = car;
            foreach (var l in JsonUtility.FromJson<RailFile>(asset.text).lines)
            {
                var line = new Line();
                for (int i = 0; i + 1 < l.p.Length; i += 2)
                {
                    var q = new Vector2(l.p[i], l.p[i + 1]);
                    if (line.pts.Count > 0) line.length += (q - line.pts[line.pts.Count - 1]).magnitude;
                    line.pts.Add(q); line.at.Add(line.length);
                }
                if (line.length > 1500f) t.lines.Add(line);
            }
        }

        public int Count => trains.Count;
        public string Describe() { var s = new List<string>(); foreach (var t in trains) { var p = t.cars[0].position; s.Add($"{(t.dir > 0 ? "+" : "-")} {p.x:F0},{p.z:F0} at {t.speed * 2.237f:F0} mph"); } return string.Join(" | ", s); }
        public Transform First => trains.Count > 0 ? trains[0].cars[0] : null;

        void Start() { Build(); next = Time.time + 8f; }

        static Vector3 Point(Line l, float s, out Vector3 dir)
        {
            s = Mathf.Clamp(s, 0f, l.length);
            int i = l.at.BinarySearch(s); if (i < 0) i = ~i; i = Mathf.Clamp(i, 1, l.pts.Count - 1);
            var a = l.pts[i - 1]; var b = l.pts[i]; float k = Mathf.InverseLerp(l.at[i - 1], l.at[i], s);
            var d = (b - a).normalized; dir = new Vector3(d.x, 0, d.y);
            var q = Vector2.Lerp(a, b, k); return new Vector3(q.x, -.0f, q.y);
        }

        float Nearest(Line l, Vector3 to, out float dist)
        {
            float best = float.MaxValue, bs = 0;
            for (int i = 0; i < l.pts.Count; i++) { float d = (new Vector2(to.x, to.z) - l.pts[i]).sqrMagnitude; if (d < best) { best = d; bs = l.at[i]; } }
            dist = Mathf.Sqrt(best); return bs;
        }

        void Update()
        {
            if (car == null || lines.Count == 0) return;
            if (Time.time > next && trains.Count < 2)
            {
                next = Time.time + 150f + (float)rnd.NextDouble() * 150f;
                Line best = null; float bs = 0, bd = float.MaxValue;
                foreach (var l in lines) { float s = Nearest(l, car.transform.position, out var d); if (d < bd) { bd = d; bs = s; best = l; } }
                if (best != null && bd < 1500f)
                {
                    // Starts out of sight ahead or behind, and runs toward and past the player.
                    float dir = rnd.NextDouble() < .5 ? 1f : -1f, start = bs - dir * (1600f + (float)rnd.NextDouble() * 600f);
                    if (start > 50f && start < best.length - 50f) Spawn(best, start, dir);
                }
            }
            for (int i = trains.Count - 1; i >= 0; i--)
            {
                var t = trains[i];
                t.s += t.dir * t.speed * Time.deltaTime; t.life += Time.deltaTime;
                bool gone = t.s < 0f || t.s > t.line.length || t.life > 240f;
                if (gone) { foreach (var c in t.cars) Destroy(c.gameObject); trains.RemoveAt(i); continue; }
                for (int k = 0; k < t.cars.Count; k++)
                {
                    float s = t.s - t.dir * k * (CarLength + Gap);
                    var front = Point(t.line, s, out _); var back = Point(t.line, s - t.dir * CarLength, out _);
                    var mid = (front + back) / 2f; var fwd = (front - back); if (fwd.sqrMagnitude < .01f) continue;
                    t.cars[k].SetPositionAndRotation(mid, Quaternion.LookRotation(fwd.normalized));
                }
            }
        }

        void Spawn(Line l, float s, float dir)
        {
            var t = new Train { line = l, s = s, dir = dir, speed = Cruise * (.85f + (float)rnd.NextDouble() * .2f) };
            for (int k = 0; k < 5; k++)
            {
                var go = new GameObject(k == 0 ? "Locomotive" : "Coach", typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(transform);
                go.GetComponent<MeshFilter>().sharedMesh = k == 0 ? loco : coach;
                var r = go.GetComponent<MeshRenderer>(); r.sharedMaterials = mats; r.shadowCastingMode = ShadowCastingMode.On;
                t.cars.Add(go.transform);
            }
            trains.Add(t);
        }

        // ---------- the cars, built in code: a 26 m body, 3 m wide, 4.8 m tall for the double-deckers ----------
        void Build()
        {
            Material M(Color c, float sm) { var m = new Material(Shader.Find("Universal Render Pipeline/Lit")); m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", sm); return m; }
            mats = new[] { M(new Color(.92f, .93f, .93f), .5f), M(new Color(.1f, .28f, .6f), .5f), M(new Color(.06f, .07f, .09f), .85f), M(new Color(.18f, .18f, .18f), .3f) };
            loco = Body(true); coach = Body(false);
        }

        Mesh Body(bool isLoco)
        {
            var v = new List<Vector3>(); var parts = new List<int>[4]; for (int i = 0; i < 4; i++) parts[i] = new List<int>();
            void Box(int part, Vector3 c, Vector3 size)
            {
                var h = size / 2f; int n = v.Count;
                for (int i = 0; i < 8; i++) v.Add(c + new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z));
                int[] q = { 0, 2, 1, 1, 2, 3, 4, 5, 6, 5, 7, 6, 0, 1, 4, 1, 5, 4, 2, 6, 3, 3, 6, 7, 0, 4, 2, 2, 4, 6, 1, 3, 5, 3, 7, 5 };
                foreach (var i in q) parts[part].Add(n + i);
            }
            float h = isLoco ? 4.3f : 4.8f;
            Box(0, new Vector3(0, 1.2f + h / 2f, 0), new Vector3(3.05f, h, CarLength - .4f));          // body
            Box(1, new Vector3(0, 1.45f, 0), new Vector3(3.08f, .5f, CarLength - .3f));                   // blue band low
            Box(3, new Vector3(0, .6f, 0), new Vector3(2.6f, 1f, CarLength - 3f));                        // underframe and trucks
            if (isLoco)
            {
                Box(2, new Vector3(0, 4.3f, CarLength / 2f - .6f), new Vector3(2.4f, .9f, .12f));         // cab windows
                Box(1, new Vector3(0, 2.6f, CarLength / 2f - .25f), new Vector3(3.08f, 1.2f, .3f));       // blue nose
                Box(3, new Vector3(0, 5.6f, -2f), new Vector3(1.6f, .3f, 8f));                            // roof grilles
            }
            else
            {
                foreach (float y in new[] { 2.6f, 4.6f }) Box(2, new Vector3(0, y, 0), new Vector3(3.1f, .85f, CarLength - 4f)); // two decks of windows
                Box(1, new Vector3(0, 5.8f, 0), new Vector3(3.06f, .25f, CarLength - .5f));               // blue roof edge
            }
            var m = new Mesh { name = isLoco ? "Locomotive" : "Coach" }; m.SetVertices(v); m.subMeshCount = 4;
            for (int i = 0; i < 4; i++) m.SetTriangles(parts[i], i);
            m.RecalculateNormals(); m.RecalculateBounds(); return m;
        }
    }
}
