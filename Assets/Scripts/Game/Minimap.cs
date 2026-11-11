using System.Collections.Generic;
using SurviveI95.World;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SurviveI95.Game
{
    /// <summary>
    /// The minimap's picture: real streets from OpenStreetMap, the exit ramps and I-95 itself, built once as one flat mesh
    /// far below the world on its own layer, and filmed from above by a small camera that turns with the car (heading up,
    /// like a phone's navigation app) and zooms out as you speed up. The HUD draws the picture and the markers on top.
    /// </summary>
    public class Minimap
    {
        public const int Layer = 31;
        const float Floor = -4000f, Size = 512;
        // Map widths are wider than the real roads so a side street still reads at highway zoom.
        static readonly Color Ground = new Color(.115f, .13f, .145f), Street = new Color(.25f, .275f, .3f), Avenue = new Color(.34f, .365f, .39f);
        static readonly Color Ramp = new Color(.62f, .65f, .68f), Casing = new Color(.03f, .1f, .2f), Route = new Color(.24f, .56f, 1f);

        public RenderTexture Texture { get; }
        /// <summary>Half the map's height in meters, and which way is up on it (degrees from north).</summary>
        public float Radius { get; private set; } = 260f;
        public float Yaw { get; private set; }
        /// <summary>The world point at the middle of the map. The car sits below it, so more road ahead shows.</summary>
        public Vector3 Center { get; private set; }
        public const float Ahead = .35f;

        readonly Camera cam;
        bool placed;

        public Minimap(PlayerCar car)
        {
            Texture = new RenderTexture((int)Size, (int)Size, 16, RenderTextureFormat.ARGB32) { name = "Minimap", antiAliasing = 4 };
            var go = new GameObject("MinimapCamera", typeof(Camera));
            cam = go.GetComponent<Camera>();
            cam.orthographic = true; cam.targetTexture = Texture; cam.cullingMask = 1 << Layer; cam.depth = -20;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Ground;
            cam.nearClipPlane = 1f; cam.farClipPlane = 60f; cam.allowHDR = false; cam.allowMSAA = true;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderShadows = false; data.renderPostProcessing = false; data.requiresDepthTexture = false; data.requiresColorTexture = false;
            foreach (var other in Camera.allCameras) if (other != cam) other.cullingMask &= ~(1 << Layer);
            if (car.chaseCamera != null) car.chaseCamera.cullingMask &= ~(1 << Layer);
            Build(car);
        }

        [System.Serializable] class Row { public float w; public float[] p; }
        [System.Serializable] class MapFile { public Row[] roads; }

        class Lines
        {
            public readonly List<Vector3> v = new List<Vector3>(); public readonly List<Color> c = new List<Color>(); public readonly List<int> t = new List<int>();
            // A thick line with round joins: a quad per segment and a disc at every point.
            public void Add(IList<Vector2> pts, float width, float y, Color col)
            {
                float h = width / 2f;
                for (int i = 0; i + 1 < pts.Count; i++)
                {
                    var a = pts[i]; var b = pts[i + 1]; var d = b - a; if (d.sqrMagnitude < .01f) continue;
                    var n = new Vector2(-d.y, d.x).normalized * h; int k = v.Count;
                    v.Add(V(a - n, y)); v.Add(V(a + n, y)); v.Add(V(b - n, y)); v.Add(V(b + n, y));
                    for (int j = 0; j < 4; j++) c.Add(col);
                    t.AddRange(new[] { k, k + 1, k + 2, k + 2, k + 1, k + 3 });
                }
                foreach (var p in pts) Disc(p, h, y, col);
            }
            void Disc(Vector2 p, float r, float y, Color col)
            {
                const int Sides = 8; int k = v.Count;
                v.Add(V(p, y)); c.Add(col);
                for (int i = 0; i < Sides; i++) { float a = i * Mathf.PI * 2f / Sides; v.Add(V(p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r, y)); c.Add(col); }
                for (int i = 0; i < Sides; i++) t.AddRange(new[] { k, k + 1 + i, k + 1 + (i + 1) % Sides });
            }
            static Vector3 V(Vector2 p, float y) => new Vector3(p.x, Floor + y, p.y);
        }

        static List<Vector2> Sample(RoutePath road, float step)
        {
            var pts = new List<Vector2>();
            for (float s = 0f; ; s += step)
            {
                var p = road.Center(Mathf.Min(s, road.Length)); pts.Add(new Vector2(p.x, p.z));
                if (s >= road.Length) break;
            }
            return pts;
        }

        void Build(PlayerCar car)
        {
            var lines = new Lines();
            var asset = Resources.Load<TextAsset>("Data/Routes/" + car.routeId + "_map");
            if (asset != null)
                foreach (var r in JsonUtility.FromJson<MapFile>(asset.text).roads)
                {
                    var pts = new List<Vector2>();
                    for (int i = 0; i + 1 < r.p.Length; i += 2) pts.Add(new Vector2(r.p[i], r.p[i + 1]));
                    bool big = r.w >= 12f; // primary and secondary roads
                    lines.Add(pts, big ? 17f : r.w >= 10f ? 12f : 9f, big ? .2f : 0f, big ? Avenue : Street);
                }
            var road = car.Path;
            foreach (var j in road.Junctions) lines.Add(Sample(j.road, 8f), 13f, .4f, Ramp);
            // I-95 is the route: blue with a dark edge, as a navigation app draws the way you're going.
            var main = Sample(road, 12f);
            lines.Add(main, 36f, .6f, Casing);
            lines.Add(main, 24f, .8f, Route);

            var mesh = new Mesh { name = "Minimap", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(lines.v);
            var linear = new List<Color>(lines.c.Count); foreach (var col in lines.c) linear.Add(col.linear);
            mesh.SetColors(linear); mesh.SetTriangles(lines.t, 0); mesh.RecalculateBounds();
            var go = new GameObject("MinimapMesh") { layer = Layer };
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = new Material(Shader.Find("Hidden/SurviveI95/MinimapFlat"));
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
        }

        /// <summary>Follows the car: heading up, smoothed so a bump doesn't shake the map, zooming out with speed.</summary>
        public void Follow(Transform car, float speed, float dt)
        {
            var f = car.forward; f.y = 0f;
            if (f.sqrMagnitude < 1e-4f) return;
            float heading = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            float zoom = Mathf.Lerp(230f, 470f, Mathf.InverseLerp(12f, 42f, Mathf.Abs(speed)));
            if (!placed) { Yaw = heading; Radius = zoom; placed = true; }
            Yaw = Mathf.LerpAngle(Yaw, heading, 1f - Mathf.Exp(-dt * 5f));
            Radius = Mathf.Lerp(Radius, zoom, 1f - Mathf.Exp(-dt * 1.2f));
            var rot = Quaternion.Euler(0f, Yaw, 0f);
            Center = new Vector3(car.position.x, 0f, car.position.z) + rot * Vector3.forward * (Radius * Ahead);
            cam.orthographicSize = Radius;
            cam.transform.SetPositionAndRotation(new Vector3(Center.x, Floor + 30f, Center.z), Quaternion.Euler(90f, Yaw, 0f));
        }

        /// <summary>Where a world point falls on the map, from -1 to 1 across and down (y grows downward, as on screen).</summary>
        public Vector2 ToMap(Vector3 world)
        {
            var local = Quaternion.Euler(0f, -Yaw, 0f) * new Vector3(world.x - Center.x, 0f, world.z - Center.z);
            return new Vector2(local.x, -local.z) / Radius;
        }

        public void SetActive(bool on) { if (cam.enabled != on) cam.enabled = on; }
    }
}
