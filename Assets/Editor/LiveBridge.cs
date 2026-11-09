using System;
using System.Globalization;
using System.IO;
using SurviveI95.Game;
using SurviveI95.Sim;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SurviveI95.EditorTools
{
    /// <summary>
    /// Local-only remote control for the open editor. Tools/live.ps1 drops a command file in .livebridge/,
    /// and this answers it, so play mode can be entered, driven, inspected and photographed while running.
    /// </summary>
    [InitializeOnLoad]
    public static class LiveBridge
    {
        static readonly string Dir = Path.GetFullPath(".livebridge");
        static double nextPoll, nextBeat;
        static float slowest;
        static int fpsFrames; static float fpsTime;
        static LaneBot bot;

        static LiveBridge()
        {
            if (Application.isBatchMode) return;
            Directory.CreateDirectory(Path.Combine(Dir, "shots"));
            PlayerSettings.runInBackground = true;
            Application.logMessageReceivedThreaded += OnLog;
            EditorApplication.update += Tick;
        }

        static void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Log && !message.StartsWith("[")) return;
            try { File.AppendAllText(Path.Combine(Dir, "console.log"), $"{DateTime.Now:HH:mm:ss} {type}: {message}\n"); } catch (IOException) { }
        }

        static void Tick()
        {
            if (EditorApplication.isPlaying) slowest = Mathf.Max(slowest, Time.unscaledDeltaTime);
            double now = EditorApplication.timeSinceStartup;
            if (now > nextBeat)
            {
                nextBeat = now + 0.5;
                try { File.WriteAllText(Path.Combine(Dir, "heartbeat.txt"), $"{DateTime.UtcNow:o} playing={EditorApplication.isPlaying} compiling={EditorApplication.isCompiling}"); } catch (IOException) { }
            }
            if (now < nextPoll) return;
            nextPoll = now + 0.05;
            string file = Path.Combine(Dir, "cmd.txt");
            if (!File.Exists(file)) return;
            string text;
            try { text = File.ReadAllText(file).Trim(); File.Delete(file); } catch (IOException) { return; }
            var parts = text.Split(' ');
            string reply;
            try { reply = Run(parts); } catch (Exception e) { reply = "ERROR " + e.Message; }
            File.WriteAllText(Path.Combine(Dir, "res_" + parts[0] + ".txt"), reply);
        }

        static float F(string[] a, int i) => float.Parse(a[i], CultureInfo.InvariantCulture);

        static string Run(string[] a)
        {
            var car = EditorApplication.isPlaying ? UnityEngine.Object.FindAnyObjectByType<PlayerCar>() : null;
            switch (a[1])
            {
                case "ping": return "ok";
                case "laneaudit": return LaneAudit.Run(Path.Combine(Dir, "audit.txt"));
                case "bench": DriveSmokeTest.Run(); return "bench done";
                case "islands": return Islands();
                case "refresh": AssetDatabase.Refresh(); return "refreshing";
                case "play": EditorApplication.isPlaying = true; return "entering play";
                case "stop": PlayerCar.AutoDriver = null; EditorApplication.isPlaying = false; return "stopping";
                case "rebuild": if (EditorApplication.isPlaying) return "ERROR stop first"; FloridaSliceBuilder.Build(); return "rebuilt";
            }
            if (car == null) return "ERROR not in play mode";
            switch (a[1])
            {
                case "drive":
                    bot = new LaneBot { SpeedMph = F(a, 2), TargetLaneFromRight = a.Length > 3 ? (int)F(a, 3) : 0 };
                    PlayerCar.AutoDriver = (p, c) => bot.Think(p, c);
                    return "driving";
                case "hold":
                    float hs = F(a, 2), hm = F(a, 3);
                    PlayerCar.AutoDriver = (p, c) => new DriveInput { Steer = hs, Throttle = c.Speed * DriveSim.MetersPerSecondToMph < hm ? 1f : 0f };
                    return "holding steering";
                case "reverse": PlayerCar.AutoDriver = (p, c) => new DriveInput { Brake = 1f }; return "reversing";
                case "park": PlayerCar.AutoDriver = (p, c) => new DriveInput(); return "no input";
                case "lever": car.LeverOverride = a[2][0]; return "lever to " + a[2];
                case "teleport": car.Place(F(a, 2)); car.Sim.Lateral = car.Path.HalfWidth(F(a, 2)) + F(a, 3); return "teleported";
                case "aim":
                    foreach (var o in car.Path.Obstacles)
                        if (o.kind == a[2] && o.s > 3000f) { car.Place(o.s - 130f); car.Sim.Lateral = o.lat + (a.Length > 3 ? F(a, 3) : 0f); return $"aimed at {o.kind} s={o.s:F0}"; }
                    return "ERROR none found";
                case "launch": car.Place(F(a, 2)); car.Sim.Lateral = car.Path.HalfWidth(F(a, 2)) + F(a, 3); car.Sim.Speed = F(a, 4) / DriveSim.MetersPerSecondToMph; car.Sim.Theta = F(a, 5) * Mathf.Deg2Rad; return "launched";
                case "launchat":
                    foreach (var o in car.Path.Obstacles)
                        if (o.kind == a[2] && o.s > 3000f && o.halfS > -1f) { car.Place(o.s - o.halfS - 25f); car.Sim.Lateral = o.lat + F(a, 3); car.Sim.Speed = F(a, 4) / DriveSim.MetersPerSecondToMph; return "launched at " + o.kind; }
                    return "ERROR none found";
                case "repair": car.Sim.Reset(car.Path, car.Sim.S); return "repaired";
                case "setdamage": car.Sim.Damage = F(a, 2); return "damage set";
                case "frontview": car.FrontView = a[2] == "on"; return "front view " + a[2];
                case "topview": car.TopView = F(a, 2); return "top view";
                case "findwall": for (float q = F(a, 2); q < car.Path.Length; q += 50f) if (car.Path.HasSoundWall(q) && car.Path.HasSoundWall(q + 300f)) { car.Place(q); car.Sim.Speed = 25f; return "wall at " + q; } return "ERROR no wall";
                case "takeexit":
                    var j = car.Path.Junctions[(int)F(a, 2)];
                    car.Place(j.divergeS - 260f);
                    car.Sim.Speed = 27f;
                    bot = new LaneBot { SpeedMph = F(a, 3), TargetLaneFromRight = 0, ExitAtS = j.divergeS - 120f };
                    PlayerCar.AutoDriver = (p, c) => bot.Think(p, c);
                    return $"exit {a[2]} at s={j.divergeS:F0} merge {j.mergeS:F0} length {j.road.Length:F0} of {car.Path.Junctions.Count}";
                case "release": PlayerCar.AutoDriver = null; return "released";
                case "cockpit": car.SetCockpit(a[2] == "on"); return "cockpit " + a[2];
                case "look": car.ScriptedLook = new Vector2(F(a, 2), F(a, 3)); return "looking";
                case "place": car.Place(F(a, 2)); return "placed";
                case "status":
                    var s = car.Sim;
                    string r = FormattableString.Invariant($"mph={s.Speed * DriveSim.MetersPerSecondToMph:F1} km={s.S / 1000f:F2} lateral={s.Lateral:F2} heading={s.Theta * Mathf.Rad2Deg:F1} steer={s.Steer:F2} offroad={s.OffRoad} wall={s.HitWall} slowestFrameMs={slowest * 1000f:F0} cockpit={car.Cockpit} gear={s.Gear} onRamp={s.Current != null} rampS={(s.Current != null ? s.S : 0f):F0} roadY={(s.Current ?? car.Path).Center(s.S).y:F1} damage={s.Damage * 100f:F0} crush={s.FrontCrush:F2} disabled={s.Disabled} impacts={s.ImpactCount} lastHit={s.LastHitKind}@{s.LastImpact * DriveSim.MetersPerSecondToMph:F0}mph");
                    slowest = 0;
                    return r;
                case "fps":
                    float fpsDt = Time.realtimeSinceStartup - fpsTime; int fpsN = Time.frameCount - fpsFrames;
                    fpsTime = Time.realtimeSinceStartup; fpsFrames = Time.frameCount;
                    return $"fps={fpsN / Mathf.Max(.001f, fpsDt):F1} frames={fpsN} seconds={fpsDt:F1}";
                case "shot": return Shot(a[2]);
            }
            return "ERROR unknown command " + a[1];
        }

        // Lists separate mesh pieces around the center console, used to locate parts fused into larger meshes.
        static string Islands()
        {
            var car = UnityEngine.Object.FindAnyObjectByType<PlayerCar>();
            var sb = new System.Text.StringBuilder();
            foreach (var r in car.GetComponentsInChildren<MeshRenderer>(true))
            {
                var f = r.GetComponent<MeshFilter>(); if (f == null) continue;
                var m = f.sharedMesh; var v = m.vertices; var tr = m.triangles;
                var parent = new int[v.Length]; for (int i = 0; i < v.Length; i++) parent[i] = i;
                System.Func<int, int> find = null; find = x => { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; };
                for (int i = 0; i < tr.Length; i += 3) { parent[find(tr[i])] = find(tr[i + 1]); parent[find(tr[i])] = find(tr[i + 2]); }
                var boxes = new System.Collections.Generic.Dictionary<int, Bounds>(); var counts = new System.Collections.Generic.Dictionary<int, int>();
                for (int i = 0; i < v.Length; i++)
                {
                    int g = find(i); var p = car.transform.InverseTransformPoint(r.transform.TransformPoint(v[i]));
                    if (boxes.TryGetValue(g, out var b)) { b.Encapsulate(p); boxes[g] = b; counts[g]++; } else { boxes[g] = new Bounds(p, Vector3.zero); counts[g] = 1; }
                }
                foreach (var kv in boxes)
                {
                    var c = kv.Value.center; var s = kv.Value.size;
                    if (Mathf.Abs(c.x) < 0.22f && c.z > -0.2f && c.z < 0.75f && c.y > 0.3f && c.y < 1.1f && s.y < 0.45f && s.magnitude > 0.03f)
                        sb.AppendLine(FormattableString.Invariant($"{r.name} v{counts[kv.Key]} c({c.x:F2},{c.y:F2},{c.z:F2}) s({s.x:F2},{s.y:F2},{s.z:F2})"));
                }
            }
            File.WriteAllText(Path.Combine(Dir, "islands.txt"), sb.ToString());
            return "written";
        }

        static string Shot(string name)
        {
            var cam = Camera.main;
            var rt = RenderTexture.GetTemporary(1600, 900, 24, RenderTextureFormat.ARGB32);
            RenderPipeline.SubmitRenderRequest(cam, new RenderPipeline.StandardRequest { destination = rt });
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            string path = Path.Combine(Dir, "shots", name + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            return path;
        }
    }
}
