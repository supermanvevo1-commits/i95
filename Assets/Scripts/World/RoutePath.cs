using System;
using UnityEngine;

namespace SurviveI95.World
{
    [Serializable] public class RoutePoint { public float x, y, z, s, lanes, bridge; }
    [Serializable] public class RouteExit { public float s; public string @ref; public string name; }
    [Serializable] public class RouteOverpass { public float s; public string name; }
    [Serializable] public class RouteFile
    {
        public string id; public float length;
        public RoutePoint[] points; public RouteExit[] exits; public RouteOverpass[] overpasses;
    }

    /// <summary>Smoothed centerline of one carriageway, built from real map data.</summary>
    public class RoutePath
    {
        public const float LaneWidth = 3.66f, Step = 5f;
        readonly Vector3[] pos;
        readonly float[] lanes, bridge;
        public RouteFile File { get; }
        public float Length => (pos.Length - 1) * Step;

        public RoutePath(RouteFile file)
        {
            File = file;
            int n = Mathf.FloorToInt(file.points[file.points.Length - 1].s / Step) + 1;
            var raw = new Vector3[n];
            var rawLanes = new float[n];
            var rawBridge = new float[n];
            int k = 0;
            for (int i = 0; i < n; i++)
            {
                float s = i * Step;
                while (k < file.points.Length - 2 && file.points[k + 1].s < s) k++;
                var a = file.points[k]; var b = file.points[k + 1];
                float t = b.s > a.s ? Mathf.Clamp01((s - a.s) / (b.s - a.s)) : 0;
                raw[i] = Vector3.Lerp(new Vector3(a.x, a.y, a.z), new Vector3(b.x, b.y, b.z), t);
                rawLanes[i] = t < 0.5f ? a.lanes : b.lanes;
                rawBridge[i] = a.bridge > 0 && b.bridge > 0 ? 1 : 0;
            }
            // Map vertices are coarse; average them so curves flow and terrain noise disappears.
            var xz = Average(Average(raw, 14), 14);
            var y = Average(Average(raw, 50), 50);
            pos = new Vector3[n];
            for (int i = 0; i < n; i++) pos[i] = new Vector3(xz[i].x, y[i].y, xz[i].z);
            lanes = Average(rawLanes, 30);
            bridge = rawBridge;
        }

        static Vector3[] Average(Vector3[] v, int r)
        {
            var o = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                int a = Mathf.Max(0, i - r), b = Mathf.Min(v.Length - 1, i + r), m = Mathf.Min(i - a, b - i);
                Vector3 sum = Vector3.zero;
                for (int j = i - m; j <= i + m; j++) sum += v[j];
                o[i] = sum / (2 * m + 1);
            }
            return o;
        }

        static float[] Average(float[] v, int r)
        {
            var o = new float[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                int a = Mathf.Max(0, i - r), b = Mathf.Min(v.Length - 1, i + r);
                float sum = 0;
                for (int j = a; j <= b; j++) sum += v[j];
                o[i] = sum / (b - a + 1);
            }
            return o;
        }

        int Index(float s, out float t)
        {
            float f = Mathf.Clamp(s / Step, 0, pos.Length - 1.001f);
            int i = (int)f; t = f - i; return i;
        }

        public Vector3 Center(float s) { int i = Index(s, out var t); return Vector3.Lerp(pos[i], pos[i + 1], t); }
        public Vector3 Tangent(float s)
        {
            int i = Index(s, out var t);
            int a = Mathf.Max(0, i - 2), b = Mathf.Min(pos.Length - 1, i + 3);
            return (pos[b] - pos[a]).normalized;
        }
        public Vector3 Right(float s) { var t = Tangent(s); return new Vector3(t.z, 0, -t.x).normalized; }
        public Vector3 Point(float s, float lateral) => Center(s) + Right(s) * lateral;
        public float Lanes(float s) { int i = Index(s, out var t); return Mathf.Lerp(lanes[i], lanes[i + 1], t); }
        public float HalfWidth(float s) => Lanes(s) * LaneWidth * 0.5f;
        public bool IsBridge(float s) { int i = Index(s, out _); return bridge[i] > 0.5f; }

        /// <summary>Signed curvature in 1/m; positive means the road bends right.</summary>
        public float Curvature(float s)
        {
            var a = Tangent(s - 20); var b = Tangent(s + 20);
            return Vector3.SignedAngle(a, b, Vector3.up) * Mathf.Deg2Rad / 40f;
        }

        public static RoutePath Load(string id)
        {
            var asset = Resources.Load<TextAsset>("Data/Routes/" + id);
            if (asset == null) throw new InvalidOperationException("Missing route: " + id);
            return new RoutePath(JsonUtility.FromJson<RouteFile>(asset.text));
        }
    }
}
