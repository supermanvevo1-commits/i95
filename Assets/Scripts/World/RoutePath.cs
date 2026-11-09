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

    public class RouteJunction { public float divergeS, mergeS; public RoutePath road; }
    [Serializable] public class RouteExitRow { public float divergeS, mergeS; public float[] xyz; }
    [Serializable] public class RouteExitFile { public RouteExitRow[] items; }
    [Serializable] public class RouteObstacle { public float s, lat, halfS, halfLat; public string kind; }
    [Serializable] public class RouteObstacleFile { public RouteObstacle[] items; }

    /// <summary>Smoothed centerline of one carriageway, built from real map data.</summary>
    public class RoutePath
    {
        public const float LaneWidth = 3.66f, Step = 5f;
        readonly Vector3[] pos;
        readonly float[] lanes, bridge;
        public RouteFile File { get; }
        /// <summary>Solid roadside objects, sorted by distance along the road.</summary>
        public RouteObstacle[] Obstacles { get; private set; } = new RouteObstacle[0];
        public float MaxObstacleHalfS { get; private set; }
        /// <summary>True for exit roads that branch off the highway.</summary>
        public bool IsRamp { get; }
        /// <summary>Drivable exits leaving this road.</summary>
        public System.Collections.Generic.List<RouteJunction> Junctions { get; } = new System.Collections.Generic.List<RouteJunction>();
        public const float GateLength = 110f;

        public bool InGate(float s)
        {
            foreach (var j in Junctions) if ((s > j.divergeS - 5f && s < j.divergeS + GateLength) || (s > j.mergeS - 70f && s < j.mergeS + 5f)) return true;
            return false;
        }

        /// <summary>Builds a one-lane branch road from world points.</summary>
        public static RoutePath Branch(System.Collections.Generic.IList<Vector3> pts)
        {
            var file = new RouteFile { points = new RoutePoint[pts.Count], exits = new RouteExit[0], overpasses = new RouteOverpass[0] };
            float s = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                if (i > 0) s += Vector3.Distance(pts[i], pts[i - 1]);
                file.points[i] = new RoutePoint { x = pts[i].x, y = pts[i].y, z = pts[i].z, s = s, lanes = 1.5f };
            }
            file.length = s;
            return new RoutePath(file, 7, 10, true);
        }
        public float Length => (pos.Length - 1) * Step;

        public RoutePath(RouteFile file, int xzRadius = 14, int yRadius = 50, bool isRamp = false)
        {
            File = file; IsRamp = isRamp;
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
            // Short bridge records in the map belong to one continuous viaduct, so join them before adding height.
            for (int i = 0; i < n; i++)
            {
                bool deck = false;
                for (int j = Mathf.Max(0, i - 45); j <= Mathf.Min(n - 1, i + 45) && !deck; j++) deck = rawBridge[j] > 0.5f;
                if (deck) raw[i].y += DeckRise;
            }
            // Map vertices are coarse; average them so curves flow and terrain noise disappears.
            var xz = Average(Average(raw, xzRadius), xzRadius);
            var y = Average(Average(raw, yRadius), yRadius);
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

        /// <summary>Barrier offsets measured outward from the lane edges.</summary>
        public const float BarrierLeft = 2.2f, BarrierRight = 4.0f, MedianGap = 6f, DeckRise = 9f, SoundWallOffset = 7.5f;
        /// <summary>Sound walls run in long sections, with gaps at structures and interchanges.</summary>
        public bool HasSoundWall(float s)
        {
            if (IsBridge(s) || Center(s).y > 1.5f || (int)(s / 1200f) % 3 == 0) return false;
            foreach (var e in File.exits) if (Mathf.Abs(e.s - s) < 430f) return false;
            return true;
        }

        public bool HasBarrier(float s)
        {
            if (IsBridge(s) || Center(s).y > 1.5f) return true;
            foreach (var o in File.overpasses) if (Mathf.Abs(o.s - s) < 90f) return true;
            return false;
        }

        /// <summary>Signed curvature in 1/m; positive means the road bends right.</summary>
        public float Curvature(float s)
        {
            var a = Tangent(s - 20); var b = Tangent(s + 20);
            return Vector3.SignedAngle(a, b, Vector3.up) * Mathf.Deg2Rad / 40f;
        }

        public void LoadJunctions(RouteExitFile data)
        {
            Junctions.Clear();
            foreach (var row in data.items)
            {
                var pts = new System.Collections.Generic.List<Vector3>();
                for (int i = 0; i + 2 < row.xyz.Length; i += 3) pts.Add(new Vector3(row.xyz[i], row.xyz[i + 1], row.xyz[i + 2]));
                Junctions.Add(new RouteJunction { divergeS = row.divergeS, mergeS = row.mergeS, road = Branch(pts) });
            }
        }

        public static RoutePath Load(string id)
        {
            var asset = Resources.Load<TextAsset>("Data/Routes/" + id);
            if (asset == null) throw new InvalidOperationException("Missing route: " + id);
            var path = new RoutePath(JsonUtility.FromJson<RouteFile>(asset.text));
            var solids = Resources.Load<TextAsset>("Data/Routes/" + id + "_obstacles");
            if (solids != null) path.Obstacles = JsonUtility.FromJson<RouteObstacleFile>(solids.text).items;
            var loops = Resources.Load<TextAsset>("Data/Routes/" + id + "_exits");
            if (loops != null) path.LoadJunctions(JsonUtility.FromJson<RouteExitFile>(loops.text));
            foreach (var o in path.Obstacles) path.MaxObstacleHalfS = Mathf.Max(path.MaxObstacleHalfS, o.halfS);
            return path;
        }
    }
}
