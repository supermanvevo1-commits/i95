using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SurviveI95.Game
{
    /// <summary>
    /// Lights the player car's own lamps on the imported model: brake lights, turn signals, hazards and high beams.
    /// Each lens in the model is one mesh covering both sides, so at startup it is split into a left and a right half
    /// that can light separately. The halves keep the lens's own material when off and glow when on, so a lit lamp is
    /// exactly the shape of the lens. Rear signals flash the red tail lens, as on most US cars including this one.
    /// </summary>
    public class CarLamps
    {
        // Lamp colors in HDR: above 1 they catch the bloom, so a lit lamp reads as a light in daylight, not paint.
        public static readonly Color Red = new Color(1f, .01f, .015f) * 1.1f, DimRed = new Color(1f, .05f, .04f) * .7f;
        public static readonly Color Amber = new Color(1f, .3f, 0f) * 1.8f, White = new Color(.92f, .96f, 1f) * 2.4f, Headlight = new Color(.92f, .96f, 1f) * 8f; // LED white; a headlight is far brighter than a marker

        class Half { public MeshRenderer r; public Material off, glow; public bool lit; public Color shown; }
        readonly List<Half> tailL = new List<Half>(), tailR = new List<Half>(), headL = new List<Half>(), headR = new List<Half>(), cornerL = new List<Half>(), cornerR = new List<Half>(), signalL = new List<Half>(), signalR = new List<Half>();
        public int LampCount => tailL.Count + tailR.Count + headL.Count + headR.Count + cornerL.Count + cornerR.Count + signalL.Count + signalR.Count;

        /// <summary>
        /// Finds the lenses by what they look like: a red part at the back is the tail lens, an amber part at the front
        /// is the front signal, and the front light housing's glass is the headlight cover.
        /// </summary>
        public CarLamps(Transform car, Transform model)
        {
            if (model == null) return;
            foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                var f = r.GetComponent<MeshFilter>();
                if (f == null || f.sharedMesh == null || !f.sharedMesh.isReadable || r.sharedMaterial == null) continue;
                var c = BaseColor(r.sharedMaterial);
                float z = car.InverseTransformPoint(r.bounds.center).z;
                bool red = c.r > .4f && c.g < .25f && c.b < .25f, amber = c.r > .4f && c.g > .1f && c.g < .4f && c.b < .1f;
                if (red && z < -1.2f) Split(car, r, tailL, tailR, lampsOnTop: true);
                else if (amber && z > 1.2f) Split(car, r, signalL, signalR);
                // The headlight cover hides the bulbs behind it, so the cover itself lights: its outer corner is the front signal.
                else if (r.name.Contains("frontlights") && r.sharedMaterial.name.StartsWith("klosz")) Split(car, r, headL, headR, outer: (cornerL, cornerR));
            }
        }

        static Color BaseColor(Material m)
        {
            foreach (var p in new[] { "baseColorFactor", "_BaseColor", "_Color" }) if (m.HasProperty(p)) return m.GetColor(p);
            return Color.gray;
        }

        static Material glowShader;
        static Material Glow(Color c)
        {
            if (glowShader == null) glowShader = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            var m = new Material(glowShader); m.SetColor("_BaseColor", c);
            return m;
        }

        // Splits a lens into the triangles left and right of the car's center line, as two new parts beside the original.
        static void Split(Transform car, MeshRenderer src, List<Half> left, List<Half> right, bool lampsOnTop = false, (List<Half> l, List<Half> r)? outer = null)
        {
            var mesh = src.GetComponent<MeshFilter>().sharedMesh;
            var verts = mesh.vertices; var normals = mesh.normals; var uv = mesh.uv; var tris = mesh.triangles;
            var toCar = car.worldToLocalMatrix * src.transform.localToWorldMatrix;
            // The tail lens mesh also holds the red reflectors low on the bumper. Those never light, so they stay a part of their own.
            float top = float.MinValue;
            float wide = 0f;
            foreach (var v in verts) { var p = toCar.MultiplyPoint3x4(v); top = Mathf.Max(top, p.y); wide = Mathf.Max(wide, Mathf.Abs(p.x)); }
            var sides = new[] { new List<int>(), new List<int>(), new List<int>(), new List<int>(), new List<int>() };
            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 a = toCar.MultiplyPoint3x4(verts[tris[i]]), b = toCar.MultiplyPoint3x4(verts[tris[i + 1]]), c = toCar.MultiplyPoint3x4(verts[tris[i + 2]]);
                float x = (a.x + b.x + c.x) / 3f, y = (a.y + b.y + c.y) / 3f;
                int side = lampsOnTop && y < top - .3f ? 2 : outer != null && Mathf.Abs(x) > wide - .14f ? (x < 0f ? 3 : 4) : x < 0f ? 0 : 1;
                sides[side].AddRange(new[] { tris[i], tris[i + 1], tris[i + 2] });
            }
            for (int s = 0; s < 5; s++)
            {
                if (sides[s].Count == 0) continue;
                var half = new Mesh { indexFormat = IndexFormat.UInt32, name = src.name + new[] { "_L", "_R", "_unlit", "_cornerL", "_cornerR" }[s] };
                half.SetVertices(verts); if (normals.Length == verts.Length) half.SetNormals(normals); if (uv.Length == verts.Length) half.SetUVs(0, uv);
                half.SetTriangles(sides[s], 0); half.RecalculateBounds();
                // The name keeps the source's, so crash crumpling still finds it.
                var go = new GameObject(half.name, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(src.transform.parent, false);
                go.transform.localPosition = src.transform.localPosition; go.transform.localRotation = src.transform.localRotation; go.transform.localScale = src.transform.localScale;
                go.GetComponent<MeshFilter>().sharedMesh = half;
                var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = src.sharedMaterial; r.shadowCastingMode = src.shadowCastingMode;
                var h = new Half { r = r, off = src.sharedMaterial };
                if (s == 0) left.Add(h); else if (s == 1) right.Add(h); else if (s == 3) outer?.l.Add(h); else if (s == 4) outer?.r.Add(h);
            }
            src.enabled = false;
        }

        static void Set(List<Half> lamps, bool on, Color color)
        {
            foreach (var h in lamps)
            {
                if (h.lit == on && (!on || h.shown == color)) continue;
                if (on)
                {
                    if (h.glow == null) h.glow = Glow(color); else h.glow.SetColor("_BaseColor", color);
                    h.r.sharedMaterial = h.glow; h.shown = color;
                }
                else h.r.sharedMaterial = h.off;
                h.lit = on;
            }
        }

        /// <summary>Flashes about 90 times a minute; US law allows 60 to 120.</summary>
        public static bool Blink => BlinkTest ?? Mathf.Repeat(Time.time * 1.5f, 1f) < .5f;
        /// <summary>Holds the flasher on or off, for screenshots.</summary>
        public static bool? BlinkTest;

        /// <summary>signal: -1 left, +1 right, 0 off.</summary>
        public void Update(int signal, bool hazards, bool braking, bool highBeams)
        {
            bool blink = Blink;
            bool left = blink && (hazards || signal < 0), right = blink && (hazards || signal > 0);
            bool leftFlashing = hazards || signal < 0, rightFlashing = hazards || signal > 0;
            // A US combined lamp: the flashing side blinks between bright and off even while braking; the other side shows the brake.
            Set(tailL, leftFlashing ? left : braking, Red);
            Set(tailR, rightFlashing ? right : braking, Red);
            Set(signalL, left, Amber);
            Set(signalR, right, Amber);
            Set(headL, highBeams, Headlight);
            Set(headR, highBeams, Headlight);
            // The corner shows the signal when flashing, and is part of the headlight otherwise.
            Set(cornerL, left || (!leftFlashing && highBeams), left ? Amber : Headlight);
            Set(cornerR, right || (!rightFlashing && highBeams), right ? Amber : Headlight);
        }

        /// <summary>For tests: what each lamp shows right now.</summary>
        public string State()
        {
            string S(List<Half> l) => l.Count == 0 ? "none" : l[0].lit ? "on" : "off";
            return $"tailL={S(tailL)} tailR={S(tailR)} signalL={S(signalL)} signalR={S(signalR)} cornerL={S(cornerL)} cornerR={S(cornerR)} headL={S(headL)} headR={S(headR)} parts={LampCount}";
        }
    }
}
