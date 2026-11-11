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
    /// <summary>A posted speed limit that holds from s until the next entry.</summary>
    [Serializable] public class RouteLimit { public float s, mph; }
    [Serializable] public class RouteLimitFile { public RouteLimit[] limits; }

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
        public const float GateLength = 150f;
        /// <summary>Shape shared by the exit builder and the driving sim, so what you see is what you hit.</summary>
        public const float ExitSpan = 720f, ExitSwing = 260f, ExitReach = 36f, ExitEdge = 3f, ExitHalf = 2.745f, ExitWallGap = 1.5f, ExitOpen = 135f, ExitHold = 90f, ExitTaper = 80f;

        /// <summary>How far the exit lane's center sits outside the highway's lane edge, a given distance past the split.</summary>
        public static float ExitOffset(float d) => ExitEdge + (ExitReach - ExitEdge) * ExitBlend(d);

        /// <summary>0 while running beside the highway as a speed-change lane, 1 once fully away from it.</summary>
        public static float ExitBlend(float d) => Mathf.Min(Mathf.SmoothStep(0f, 1f, (d - ExitHold) / ExitSwing), Mathf.SmoothStep(0f, 1f, (ExitSpan - d - ExitHold) / ExitSwing));

        public bool InGate(float s)
        {
            foreach (var j in Junctions) if ((s > j.divergeS - 5f && s < j.divergeS + GateLength) || (s > j.mergeS - GateLength && s < j.mergeS + 40f)) return true;
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
            return new RoutePath(file, 1, 1, true); // already a smooth curve; more averaging would pull it off the pavement it joins
        }
        public float Length => (pos.Length - 1) * Step;

        /// <summary>Posted limits along the road, from real map data. Empty means the default applies.</summary>
        public RouteLimit[] Limits { get; private set; } = new RouteLimit[0];
        public const float DefaultLimitMph = 65f, RampLimitMph = 45f;

        /// <summary>The posted speed limit at this point, in mph.</summary>
        public float LimitMph(float s)
        {
            if (IsRamp) return RampLimitMph;
            float mph = Limits.Length > 0 ? Limits[0].mph : DefaultLimitMph;
            foreach (var l in Limits) { if (l.s > s) break; mph = l.mph; }
            return mph;
        }

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
            foreach (var j in Junctions) if (s > j.divergeS - 30f && s < j.mergeS + 60f) return false;
            foreach (var e in File.exits) if (Mathf.Abs(e.s - s) < 430f) return false;
            return true;
        }

        /// <summary>Something solid along the right edge at BarrierRight: a concrete barrier or a steel guardrail.</summary>
        public bool HasBarrier(float s) => HasConcrete(s) || HasGuardrail(s);

        /// <summary>Concrete barrier: on bridges, on raised road, and past overpass piers.</summary>
        public bool HasConcrete(float s)
        {
            if (IsBridge(s) || Center(s).y > 1.5f) return true;
            foreach (var o in File.overpasses) if (Mathf.Abs(o.s - s) < 90f) return true;
            return false;
        }

        /// <summary>
        /// Steel guardrail where open roadside meets a concrete barrier, as on Florida interstates: about 75 m leading
        /// up to it and 30 m trailing after it, never across an exit or entrance.
        /// </summary>
        public bool HasGuardrail(float s)
        {
            if (guardrail == null) MapGuardrail();
            int i = Mathf.FloorToInt(s / RailStep);
            return i >= 0 && i < guardrail.Length && guardrail[i];
        }

        public const float RailLead = 75f, RailTrail = 30f, RailStep = 2.5f;
        /// <summary>Overhead signs stand half a mile before each exit, as on Florida's urban interstates.</summary>
        public const float SignBridgeAhead = 805f, SignBridgeShield = 45f;

        /// <summary>
        /// Where overhead sign bridges span the northbound lanes: half a mile before each exit, unless that spot falls
        /// inside another interchange (its column would stand in an exit lane), in which case the sign goes on posts.
        /// </summary>
        public System.Collections.Generic.List<float> SignBridges()
        {
            var list = new System.Collections.Generic.List<float>();
            foreach (var j in Junctions)
            {
                float s = j.divergeS - SignBridgeAhead;
                if (s < 60f) continue;
                bool clear = true;
                foreach (var k in Junctions) if (s > k.divergeS - SignBridgeShield - 40f && s < k.mergeS + 60f) { clear = false; break; }
                if (clear) list.Add(s);
            }
            return list;
        }
        bool[] guardrail;

        void MapGuardrail()
        {
            int n = Mathf.CeilToInt(Length / RailStep) + 1;
            var concrete = new bool[n];
            for (int i = 0; i < n; i++) concrete[i] = HasConcrete(i * RailStep);
            guardrail = new bool[n];
            int lead = Mathf.RoundToInt(RailLead / RailStep), trail = Mathf.RoundToInt(RailTrail / RailStep);
            for (int i = 1; i < n; i++)
            {
                if (concrete[i] && !concrete[i - 1]) for (int k = 1; k <= lead && i - k >= 0 && !concrete[i - k]; k++) guardrail[i - k] = true; // leading up to it
                if (!concrete[i] && concrete[i - 1]) for (int k = 0; k < trail && i + k < n && !concrete[i + k]; k++) guardrail[i + k] = true; // trailing after
            }
            // A guardrail shields the roadside column of each overhead sign bridge.
            foreach (float b in SignBridges())
                for (float s = b - SignBridgeShield; s <= b + 10f; s += RailStep) { int i = Mathf.FloorToInt(s / RailStep); if (i >= 0 && i < n && !concrete[i]) guardrail[i] = true; }
            for (int i = 0; i < n; i++)
            {
                float s = i * RailStep;
                if (!guardrail[i]) continue;
                foreach (var j in Junctions) if (s > j.divergeS - 30f && s < j.mergeS + 60f) { guardrail[i] = false; break; }
            }
        }

        /// <summary>Signed curvature in 1/m; positive means the road bends right.</summary>
        public float Curvature(float s)
        {
            var a = Tangent(s - 20); var b = Tangent(s + 20);
            return Vector3.SignedAngle(a, b, Vector3.up) * Mathf.Deg2Rad / 40f;
        }

        public void LoadJunctions(RouteExitFile data)
        {
            Junctions.Clear(); guardrail = null;
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
            var limits = Resources.Load<TextAsset>("Data/Routes/" + id + "_limits");
            if (limits != null) path.Limits = JsonUtility.FromJson<RouteLimitFile>(limits.text).limits;
            foreach (var o in path.Obstacles) path.MaxObstacleHalfS = Mathf.Max(path.MaxObstacleHalfS, o.halfS);
            return path;
        }
    }
}
