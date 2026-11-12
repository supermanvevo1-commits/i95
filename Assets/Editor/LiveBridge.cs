using System;
using System.Globalization;
using System.IO;
using SurviveI95.Game;
using SurviveI95.Sim;
using SurviveI95.World;
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
        static SurviveI95.World.RoutePath viewRoad;
        static LaneBot bot;
        static string swerve = "";

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
                case "lanesort": TrafficSim.LaneSorting = a[2] == "on"; return "lane sorting " + a[2];
                case "trafficbench": return TrafficBench(F(a, 2), (int)F(a, 3), a.Length > 4 ? F(a, 4) : 31f, a.Length > 5 ? (int)F(a, 5) : 95, a.Length > 6 ? (Season)Enum.Parse(typeof(Season), a[6], true) : Season.Winter);
                case "rebuild": if (EditorApplication.isPlaying) return "ERROR stop first"; FloridaSliceBuilder.Build(); return "rebuilt";
                case "steerbench": // [car] [keyTap keyBuild]: keyboard steering compared across laws, without rendering
                    if (a.Length > 4) { DriveSim.KeyTap = F(a, 3); DriveSim.KeyBuild = F(a, 4); }
                    if (a.Length > 6) { DriveSim.EscSlip = F(a, 5); DriveSim.EscYaw = F(a, 6); }
                    SteerBench.Verbose = a.Length > 7;
                    return SteerBench.Run(a.Length > 2 ? a[2] : "sedan", Path.Combine(Dir, "steerbench.txt"));
                case "rebuildpart": if (EditorApplication.isPlaying) return "ERROR stop first"; return FloridaSliceBuilder.RebuildPart(a[2]); // signs | barriers: seconds, not minutes
                case "signcheck": return FloridaSliceBuilder.CheckSigns(); // sign text that sticks out past its panel; must be 0
                case "relight": return LookBuilder.Relight(a.Length > 2 ? a[2] : FloridaSliceBuilder.Look); // re-applies sky, sun, haze and grade from looks.json
                case "ambient": // the light that reaches shade, as the renderer has it: facing up, sideways and down
                {
                    var dirs = new[] { Vector3.up, Vector3.right, Vector3.forward, Vector3.down }; var got = new Color[4];
                    RenderSettings.ambientProbe.Evaluate(dirs, got);
                    return FormattableString.Invariant($"mode={RenderSettings.ambientMode} up={got[0]} east={got[1]} north={got[2]} down={got[3]} reflections={RenderSettings.defaultReflectionMode} x{RenderSettings.reflectionIntensity:F2} fog={RenderSettings.fogColor} density={RenderSettings.fogDensity}");
                }
                case "viewshot": // name s lat height lookS lookLat lookHeight: a camera anywhere along the road, for before-and-after pairs (edit or Play mode)
                {
                    var vr = car != null ? car.Path : (viewRoad ??= SurviveI95.World.RoutePath.Load("miami_ftl"));
                    var vc = Camera.main; var keepPos = vc.transform.position; var keepRot = vc.transform.rotation;
                    var from = vr.Point(F(a, 3), F(a, 4)) + Vector3.up * F(a, 5); var to = vr.Point(F(a, 6), F(a, 7)) + Vector3.up * F(a, 8);
                    vc.transform.SetPositionAndRotation(from, Quaternion.LookRotation(to - from));
                    var vp = Shot(a[2]);
                    vc.transform.SetPositionAndRotation(keepPos, keepRot);
                    return vp;
                }
                case "where": // name [max]: road position (s, lateral) of scene objects with that name
                {
                    var wr = car != null ? car.Path : (viewRoad ??= SurviveI95.World.RoutePath.Load("miami_ftl"));
                    int wmax = a.Length > 3 ? (int)F(a, 3) : 20; var wl = new System.Collections.Generic.List<string>();
                    foreach (var go in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude))
                    {
                        if (go.name != a[2] || wl.Count >= wmax) continue;
                        var wp = go.position; float ws = 0, wd = float.MaxValue;
                        for (float q = 0; q < wr.Length; q += 10f) { var c = wr.Center(q); float d = (c.x - wp.x) * (c.x - wp.x) + (c.z - wp.z) * (c.z - wp.z); if (d < wd) { wd = d; ws = q; } }
                        float wlat = Vector3.Dot(wp - wr.Center(ws), wr.Right(ws));
                        wl.Add($"s={ws:F0} lat={wlat:F0}");
                    }
                    return string.Join(" | ", wl);
                }
                case "sunshot": return Shot(a[2], true); // a screenshot aimed straight at the sun, to check the sky and the shadows agree
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
                case "orbit": car.TestOrbit(F(a, 2), F(a, 3)); return "orbit set; now " + car.OrbitYaw.ToString("F0"); // yaw pitch: swings the chase camera as the mouse would
                case "planes": { var at = UnityEngine.Object.FindAnyObjectByType<SurviveI95.Game.AirTraffic>(); return at == null ? "no air traffic" : $"{at.Count} planes: {at.Describe()}"; }
                case "planeshot": // name: from the player's car (1.5 m up), aimed at the nearest plane
                {
                    var at = UnityEngine.Object.FindAnyObjectByType<SurviveI95.Game.AirTraffic>(); var pl = at != null ? at.Nearest(car.transform.position) : null;
                    if (pl == null) return "ERROR no plane";
                    var pc = Camera.main; var kp = pc.transform.position; var kr = pc.transform.rotation;
                    var eye = car.transform.position + Vector3.up * 1.5f;
                    pc.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(pl.position - eye));
                    var ps = Shot(a[2]); pc.transform.SetPositionAndRotation(kp, kr);
                    return ps + $" plane at {(pl.position - eye).magnitude:F0} m, {pl.position.y:F0} m up";
                }
                case "trains": { var tr = UnityEngine.Object.FindAnyObjectByType<SurviveI95.Game.Trains>(); return tr == null ? "no trains (no rail data)" : $"{tr.Count} trains: {tr.Describe()}"; }
                case "trainshot": // name: from the player's car, aimed at the first train's locomotive
                {
                    var tr = UnityEngine.Object.FindAnyObjectByType<SurviveI95.Game.Trains>(); var lo = tr != null ? tr.First : null;
                    if (lo == null) return "ERROR no train";
                    var tc = Camera.main; var tk = tc.transform.position; var tkr = tc.transform.rotation;
                    var teye = car.transform.position + Vector3.up * 1.5f;
                    tc.transform.SetPositionAndRotation(teye, Quaternion.LookRotation(lo.position + Vector3.up * 2f - teye));
                    var tsp = Shot(a[2]); tc.transform.SetPositionAndRotation(tk, tkr);
                    return tsp + $" train at {(lo.position - teye).magnitude:F0} m";
                }
                case "orbitnow": return "orbit yaw " + car.OrbitYaw.ToString("F1");
                case "look": car.ScriptedLook = new Vector2(F(a, 2), F(a, 3)); return "looking";
                case "place": car.Place(F(a, 2)); return "placed";
                case "status":
                    var s = car.Sim;
                    string r = FormattableString.Invariant($"mph={s.Speed * DriveSim.MetersPerSecondToMph:F1} km={s.S / 1000f:F2} lateral={s.Lateral:F2} heading={s.Theta * Mathf.Rad2Deg:F1} steer={s.Steer:F2} offroad={s.OffRoad} wall={s.HitWall} slowestFrameMs={slowest * 1000f:F0} cockpit={car.Cockpit} gear={s.Gear} onRamp={s.Current != null} rampS={(s.Current != null ? s.S : 0f):F0} roadY={(s.Current ?? car.Path).Center(s.S).y:F1} damage={s.Damage * 100f:F0} crush={s.FrontCrush:F2} disabled={s.Disabled} impacts={s.ImpactCount} lastHit={s.LastHitKind}@{s.LastImpact * DriveSim.MetersPerSecondToMph:F0}mph slide={s.Sliding} latG={s.LateralAccel / 9.81f:F2} slipDeg={s.Beta * Mathf.Rad2Deg:F1}");
                    slowest = 0;
                    return r;
                case "fps":
                    float fpsDt = Time.realtimeSinceStartup - fpsTime; int fpsN = Time.frameCount - fpsFrames;
                    fpsTime = Time.realtimeSinceStartup; fpsFrames = Time.frameCount;
                    return $"fps={fpsN / Mathf.Max(.001f, fpsDt):F1} frames={fpsN} seconds={fpsDt:F1}";
                case "shot": return Shot(a[2]);
                case "traffic": return TrafficStatus(car);
                case "trafficcount": car.GetComponent<TrafficManager>().Sim.Target = (int)F(a, 2); return "traffic target " + a[2];
                case "cruise": // mph lane: stays in lane and brakes for traffic, like a careful driver
                {
                    var tm = car.GetComponent<TrafficManager>();
                    float want = F(a, 2);
                    bot = new LaneBot { SpeedMph = want, TargetLaneFromRight = (int)F(a, 3) };
                    PlayerCar.AutoDriver = (p, c) =>
                    {
                        var input = bot.Think(p, c);
                        float gap = float.MaxValue, lead = 0f;
                        foreach (var o in tm.Sim.Cars)
                        {
                            float d = o.S - c.S - o.Length / 2f - DriveSim.CarHalfLength;
                            if (d > -3f && d < gap && Mathf.Abs(o.Lateral - c.Lateral) < (o.Width / 2f + 1.3f)) { gap = d; lead = o.Speed; }
                        }
                        float safe = 8f + c.Speed * 1.4f + Mathf.Max(0f, c.Speed - lead) * 2.2f;
                        if (gap < safe) { input.Throttle = 0f; input.Brake = gap < safe * .7f ? 1f : .35f; }
                        return input;
                    };
                    return "cruising";
                }
                case "cars": return TrafficNear(car);
                case "lineup":
                {
                    var ts = car.GetComponent<TrafficManager>().Sim; ts.Target = 0; ts.Clear();
                    PlayerCar.AutoDriver = (p, c) => new DriveInput();
                    var all = SurviveI95.Data.GameData.LoadVehicles().vehicles;
                    int from = (int)F(a, 2), n = 0; float z = 16f;
                    for (int i = from; i < all.Count && n < (int)F(a, 3); i++, n++)
                    {
                        int lane = n % 3; var c = ts.Create(all[i], DriverKind.Normal, car.Sim.S + z + all[i].lengthM / 2f, lane, 0f);
                        c.Desired = 0.01f; c.HoldDesired = true; if (lane == 2) z += 26f;
                    }
                    return "lineup " + n;
                }
                case "trafficclear": { var ts = car.GetComponent<TrafficManager>().Sim; ts.Target = 0; ts.ForgetRegulars(); ts.Clear(); return "traffic cleared"; }
                case "regulars": return Regulars(car);
                case "speeds": // the posted limit here, and how far over it drivers are going
                {
                    var ts = car.GetComponent<TrafficManager>().Sim;
                    float lim = car.Path.LimitMph(car.Sim.S), sum = 0f; int n = 0;
                    foreach (var c in ts.Cars) if (Mathf.Abs(c.S - car.Sim.S) < 400f && c.Kind != DriverKind.Truck) { sum += c.Speed * DriveSim.MetersPerSecondToMph - car.Path.LimitMph(c.S); n++; }
                    // Measured the way road studies do: actual speed over the limit, every car but trucks.
                    var over = new System.Collections.Generic.List<float>();
                    float wantOver = 0f; int held = 0, inWindow = 0;
                    foreach (var c in ts.Cars) if (c.Kind != DriverKind.Truck && c.Speed > 4f)
                    {
                        over.Add(c.Speed * DriveSim.MetersPerSecondToMph - car.Path.LimitMph(c.S));
                        wantOver += c.Desired * DriveSim.MetersPerSecondToMph - car.Path.LimitMph(c.S);
                        if (c.Speed < c.Desired - 1.3f) held++;
                    }
                    wantOver /= Mathf.Max(1, over.Count);
                    string diag = FormattableString.Invariant($" wantOver={wantOver:F1} heldBack={100 * held / Mathf.Max(1, over.Count)}%");
                    over.Sort();
                    float P(float q) => over.Count == 0 ? 0f : over[Mathf.Clamp((int)(q * over.Count), 0, over.Count - 1)];
                    float mean = 0f; foreach (var o in over) mean += o; mean /= Mathf.Max(1, over.Count);
                    return FormattableString.Invariant($"limit={lim:F0} nearCars={n} nearAvgOver={(n > 0 ? sum / n : 0f):F1} | all: n={over.Count} meanOver={mean:F1} p50={P(.5f):F1} p85={P(.85f):F1} p99={P(.99f):F1}{diag} | habits: {ts.SpeedSpread()}");
                }
                case "lanes": { var ts = car.GetComponent<TrafficManager>().Sim; ts.SampleLanes(); return ts.LaneSpeeds(); }
                case "validate": SurviveI95.EditorTools.DataValidator.Validate(); return "validated (see console)";
                case "profiles": { var ts = car.GetComponent<TrafficManager>().Sim; return $"races={ts.Races} " + ts.ProfileSpread(); }
                case "season": { var ts = car.GetComponent<TrafficManager>().Sim; ts.Season = (Season)Enum.Parse(typeof(Season), a[2], true); ts.Clear(); return "season " + ts.Season; }
                case "plates": return car.GetComponent<TrafficManager>().Sim.PlateSpread();
                case "yields": { var ts = car.GetComponent<TrafficManager>().Sim; return $"yields={ts.Yields} refusals={ts.YieldRefusals} grudges={ts.Grudges} shrugs={ts.Shrugs}"; }
                case "signal": car.TestSignal((int)F(a, 2)); return "signal " + a[2];
                case "highbeams": car.TestHighBeams = a[2] == "on"; return "high beams " + a[2];
                case "kbsteer": // dir mph: keyboard steering held in one direction, throttle holding mph
                {
                    int kd = (int)F(a, 2); float km = F(a, 3);
                    PlayerCar.AutoDriver = (p, c) => new DriveInput { Steer = car.KeyboardSteer(kd, false), Throttle = c.Speed * DriveSim.MetersPerSecondToMph < km ? 1f : 0f };
                    return "keyboard steering " + kd;
                }
                case "kbswerve": // mph secondsRight secondsLeft: a timed dodge on the keys, then hands off. Read the result with swerveresult
                {
                    float sm = F(a, 2), tr = F(a, 3), tl = F(a, 4), st = 0f, startLat = car.Sim.Lateral; swerve = "running";
                    float pg = 0f, ps = 0f, pt = 0f;
                    PlayerCar.AutoDriver = (p, c) =>
                    {
                        st += Time.fixedDeltaTime;
                        int kd2 = st < tr ? 1 : st < tr + tl ? -1 : 0;
                        pg = Mathf.Max(pg, Mathf.Abs(c.LateralAccel) / 9.81f); ps = Mathf.Max(ps, Mathf.Abs(c.Beta) * Mathf.Rad2Deg); pt = Mathf.Max(pt, Mathf.Abs(c.Theta) * Mathf.Rad2Deg);
                        if (st > tr + tl + 2f && swerve == "running")
                            swerve = FormattableString.Invariant($"moved={c.Lateral - startLat:F1}m peakG={pg:F2} peakSlip={ps:F0}deg peakHeading={pt:F0}deg endHeading={c.Theta * Mathf.Rad2Deg:F1}deg mph={c.Speed * DriveSim.MetersPerSecondToMph:F0} offroad={c.OffRoad} wall={c.HitWall}");
                        return new DriveInput { Steer = car.KeyboardSteer(kd2, false), Throttle = c.Speed * DriveSim.MetersPerSecondToMph < sm ? 1f : 0f };
                    };
                    return "swerving";
                }
                case "swerveresult": return swerve;
                case "lamps": return car.LampState;
                case "screen": { string sp = Path.Combine(Dir, "shots", a[2] + ".png"); if (File.Exists(sp)) File.Delete(sp); ScreenCapture.CaptureScreenshot(sp, a.Length > 3 ? (int)F(a, 3) : 1); return sp + " (game view, with the HUD; written at the end of the frame)"; }
                case "notice": car.GetComponent<TrafficManager>().Show(string.Join(" ", a, 2, a.Length - 2)); return "notice shown";
                case "card": { var cp = string.Join(" ", a, 2, a.Length - 2).Split('|'); car.Hud.ShowCard(cp[0], cp.Length > 1 ? cp[1] : "", cp.Length > 2 ? cp[2] : "", 30f); return "card"; } // top|BIG|small, held 30 s for screenshots
                case "hudcorner": car.Hud.SetCorner((int)F(a, 2)); return "hud corner " + a[2];
                case "flashtest": car.ScrollCruise(0); return "ok";
                case "blink": CarLamps.BlinkTest = a[2] == "auto" ? (bool?)null : a[2] == "on"; return "blink " + a[2];
                case "brakelights": car.TestBrakeLights = a[2] == "auto" ? (bool?)null : a[2] == "on"; return "brake lights " + a[2];
                case "hazards": car.TestHazards(a[2] == "on"); return "hazards " + a[2];
                case "carparts": // the player model's parts in car space (x right, y up, z forward), with materials, to find lamps and glass
                {
                    var sbp = new System.Text.StringBuilder();
                    foreach (var pr in car.transform.Find("Model").GetComponentsInChildren<MeshRenderer>())
                    {
                        var pb = pr.bounds; var pc = car.transform.InverseTransformPoint(pb.center);
                        var pm = pr.sharedMaterial; string col = "";
                        if (pm != null) foreach (var prop in new[] { "baseColorFactor", "_BaseColor", "_Color" }) if (pm.HasProperty(prop)) { var cc = pm.GetColor(prop); col = FormattableString.Invariant($"col({cc.r:F2},{cc.g:F2},{cc.b:F2},{cc.a:F2}) "); break; }
                        if (pm != null && pm.HasProperty("emissiveFactor")) { var ec = pm.GetColor("emissiveFactor"); col += FormattableString.Invariant($"emit({ec.r:F2},{ec.g:F2},{ec.b:F2}) "); }
                        sbp.AppendLine(FormattableString.Invariant($"{pr.name} | {string.Join(",", Array.ConvertAll(pr.sharedMaterials, x => x ? x.name : "null"))} | {col}{(pm ? pm.shader.name : "")} | c({pc.x:F2},{pc.y:F2},{pc.z:F2}) s({pb.size.x:F2},{pb.size.y:F2},{pb.size.z:F2})"));
                    }
                    File.WriteAllText(Path.Combine(Dir, "carparts.txt"), sbp.ToString());
                    return "written carparts.txt";
                }
                case "person": // carId years skill: who's driving that car, for tests
                {
                    var c = car.GetComponent<TrafficManager>().Sim.Cars.Find(o => o.Id == (int)F(a, 2));
                    if (c == null) return "ERROR no car";
                    c.AgeYears = (int)F(a, 3); c.Skill = F(a, 4);
                    c.Traits = new DriverTraits { Aggression = c.Traits.Aggression, Attention = Mathf.Min(c.Traits.Attention, c.Skill + .1f), LaneDiscipline = c.Traits.LaneDiscipline };
                    return $"#{c.Id} now {c.AgeYears}y skill {c.Skill:F2}";
                }
                case "forcerace": // carId: this driver always takes up a challenge
                {
                    var c = car.GetComponent<TrafficManager>().Sim.Cars.Find(o => o.Id == (int)F(a, 2));
                    if (c == null) return "ERROR no car";
                    var p = c.Profile;
                    c.Profile = new SurviveI95.Data.DriverProfile { id = p.id + "_test", name = p.name, habit = p.habit, aggression = p.aggression, temper = p.temper, discipline = p.discipline, attention = p.attention, rager = p.rager, racer = 1f, brakeTaps = p.brakeTaps };
                    c.HoldDesired = false; // a test-spawned car holds its speed; a racer has to be free to chase
                    return "racer set";
                }
                case "tempers": { var n = car.GetComponent<TrafficManager>().Sim.TemperCounts(); return $"shrug={n[0]} honk={n[1]} follow={n[2]} yell={n[3]} ram={n[4]}"; }
                case "temper": { var c = car.GetComponent<TrafficManager>().Sim.Cars.Find(o => o.Id == (int)F(a, 2)); if (c == null) return "ERROR no car"; c.Temper = (int)F(a, 3); return "temper " + c.Temper; }
                case "wheel": car.ScrollCruise((int)F(a, 2)); return car.CruiseStatus;
                case "cruisebot": // lane: the test bot steers, the player's own cruise control works the pedals
                {
                    bot = new LaneBot { SpeedMph = 0f, TargetLaneFromRight = (int)F(a, 2) };
                    PlayerCar.AutoDriver = (p, c) => { var input = bot.Think(p, c); input.Throttle = 0f; input.Brake = 0f; return car.ApplyCruise(input); };
                    return "cruise bot";
                }
                case "glance": car.TestGlance = a[2] == "off" ? null : a[2]; return "glance " + a[2];
                case "lookback": car.TestLookBack = a[2] == "on"; return "look back " + a[2];
                case "horn": car.TestHorn = a[2] == "on"; return "horn " + a[2];
                case "hornstate": { var src = car.GetComponent<AudioSource>(); return $"honking={car.Honking} playing={(src != null && src.isPlaying)} loop={(src != null && src.loop)}"; }
                case "fovset": car.SetBaseFov(F(a, 2)); return "fov " + car.BaseFov;
                case "camera": return FormattableString.Invariant($"fov={car.chaseCamera.fieldOfView:F1} glance={car.Glance:F2} pos={car.chaseCamera.transform.position} fwd={car.chaseCamera.transform.forward}");
                case "notices": { var log = car.GetComponent<TrafficManager>().NoticeLog; var res = string.Join(" | ", log); log.Clear(); return res; }
                case "anger": // carId anger: sets how much this driver hates the player
                {
                    var ts = car.GetComponent<TrafficManager>().Sim;
                    var c = ts.Cars.Find(o => o.Id == (int)F(a, 2));
                    if (c == null) return "ERROR no car " + a[2];
                    c.Temper = 4;
                    var reg = ts.Grudge(c, Grievance.CutOff, 0f);
                    reg.Anger = F(a, 3);
                    return $"{reg.Name} anger {reg.Anger:F2} level {reg.Level}";
                }
                case "trafficspawn": // vehicleId kind distanceAhead laneFromRight mph
                {
                    var ts = car.GetComponent<TrafficManager>().Sim;
                    var def = SurviveI95.Data.GameData.LoadVehicles().vehicles.Find(v => v.id == a[2]);
                    var kind = (DriverKind)Enum.Parse(typeof(DriverKind), a[3]);
                    int spawnLane = F(a, 5) < 0f ? ts.LaneCount(car.Sim.S) - 1 : (int)F(a, 5);
                    var prof = a.Length > 7 ? TrafficSim.FindProfile(a[7]) : null;
                    var c = ts.Create(def, kind, car.Sim.S + F(a, 4), spawnLane, F(a, 6) / DriveSim.MetersPerSecondToMph, prof);
                    c.Desired = c.Speed; c.HoldDesired = true;
                    return $"spawned {def.id} {kind} id={c.Id}";
                }
            }
            return "ERROR unknown command " + a[1];
        }

        static string TrafficNear(PlayerCar car)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var c in car.GetComponent<TrafficManager>().Sim.Cars)
                if (Mathf.Abs(c.S - car.Sim.S) < 90f) sb.Append(FormattableString.Invariant($"[#{c.Id} {c.Def.id} {c.Kind} {(c.Profile != null ? c.Profile.id : "-")} {c.AgeYears}y skill={c.Skill:F2} {c.License} {c.PlateState}{(c.Racing ? " RACING" : "")} off={(c.Changing ? "chg" : (c.Lateral - car.GetComponent<TrafficManager>().Sim.LaneCenter(c.S, c.Lane)).ToString("F2", CultureInfo.InvariantCulture))}{(c.Regular != null ? " REG" + c.Regular.Level : "")} d={c.S - car.Sim.S:F0} lane={c.Lane} lat={c.Lateral:F1} {c.Speed * 2.237f:F0}mph sig={c.Signal} brk={c.Braking} flash={c.FlashingHighBeams} wreck={c.Wrecked}] "));
            return sb.ToString();
        }

        static string Regulars(PlayerCar car)
        {
            var ts = car.GetComponent<TrafficManager>().Sim;
            var sb = new System.Text.StringBuilder(FormattableString.Invariant($"regulars={ts.Regulars.Count} grudges={ts.Grudges} shrugs={ts.Shrugs} returns={ts.Returns} honks={ts.Honks} brakeChecks={ts.BrakeChecks} rams={ts.Rams} "));
            foreach (var r in ts.Regulars)
            {
                float s = r.Car != null ? r.Car.S : r.S;
                sb.Append(FormattableString.Invariant($"[{r.Name} '{r.Plate}' {r.Kind} temper={r.Temper} anger={r.Anger:F2} lvl={r.Level} last={r.Last} n={r.Grievances} d={s - car.Sim.S:F0} {(r.Car != null ? r.Car.Speed : r.Speed) * 2.237f:F0}mph {(r.Car != null ? "on" : "off")}{(r.Wrecked || (r.Car != null && r.Car.Wrecked) ? " wrecked" : "")}] "));
            }
            return sb.ToString();
        }

        static string TrafficStatus(PlayerCar car)
        {
            var ts = car.GetComponent<TrafficManager>().Sim;
            int[] kinds = new int[5]; int signaling = 0, braking = 0, wrecks = 0, leftCampers = 0; float nearest = float.MaxValue, minSp = 999f, maxSp = 0f;
            foreach (var c in ts.Cars)
            {
                kinds[(int)c.Kind]++;
                if (c.Signal != 0) signaling++; if (c.Braking) braking++; if (c.Wrecked) wrecks++;
                if (c.Kind == DriverKind.Camper && c.Lane >= ts.LaneCount(c.S) - 1) leftCampers++;
                nearest = Mathf.Min(nearest, Mathf.Abs(c.S - car.Sim.S));
                minSp = Mathf.Min(minSp, c.Speed); maxSp = Mathf.Max(maxSp, c.Speed);
            }
            return FormattableString.Invariant($"cars={ts.Cars.Count} normal={kinds[0]} camper={kinds[1]} (left lane {leftCampers}) tailgater={kinds[2]} truck={kinds[3]} weaver={kinds[4]} signaling={signaling} braking={braking} wrecks={wrecks} nearest={nearest:F0}m speedMph={minSp * 2.237f:F0}-{maxSp * 2.237f:F0} laneChanges={ts.LaneChanges} signaled={ts.LaneChangesSignaled} playerCrashes={ts.CrashesWithPlayer} chain={ts.ChainCrashes} unprovoked={ts.UnprovokedCrashes} spawned={ts.Spawned}");
        }

        // Runs traffic with no rendering and no player, following a moving window down the whole route.
        static string TrafficBench(float seconds, int cars, float windowMs, int seed, Season season = Season.Winter)
        {
            var path = RoutePath.Load("miami_ftl");
            var chapter = SurviveI95.Data.GameData.LoadStateTraffic().chapters.Find(c => c.id == "florida");
            var sim = new TrafficSim(path, SurviveI95.Data.GameData.LoadVehicles(), chapter, seed) { Target = cars, Season = season };
            const float dt = 1f / 60f;
            float anchor = 400f, top = path.Length - 1500f, minGapAll = 999f; int steps = Mathf.RoundToInt(seconds / dt), under = 0, totalCars = 0, jams = 0;
            var stops = new System.Text.StringBuilder(); int campers = 0, campersLeft = 0, trucks = 0, trucksLeft = 0;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < steps; i++)
            {
                anchor += windowMs * dt;
                if (anchor > top) { anchor = 400f; }
                sim.Step(dt, null, anchor, windowMs);
                if (i % 60 != 0) continue;
                sim.SampleLanes();
                foreach (var c in sim.Cars)
                {
                    int leftLane = sim.LaneCount(c.S) - 1;
                    if (c.Kind == DriverKind.Camper) { campers++; if (c.Lane >= leftLane) campersLeft++; }
                    else if (c.Kind == DriverKind.Truck && leftLane >= 2) { trucks++; if (c.Lane >= leftLane) trucksLeft++; }
                }
                totalCars += sim.Cars.Count; if (sim.Cars.Count < cars * .9f) under++;
                foreach (var c in sim.Cars) if (c.Speed < 3f) { jams++; if (jams % 25 == 1) stops.AppendLine(FormattableString.Invariant($"t={i / 60} {c.Def.id} {c.Kind} s={c.S:F0} d={c.S - anchor:F0} lane={c.Lane} laneCount={sim.LaneCount(c.S)} ahead400={sim.LaneCount(c.S + 400f)} lanes={path.Lanes(c.S):F2} v={c.Speed:F1} desired={c.Desired:F1}")); }
            }
            watch.Stop();
            string res = FormattableString.Invariant($"seconds={seconds:F0} target={cars} avgCars={totalCars / Mathf.Max(1f, seconds):F1} under90pct={under} unprovoked={sim.UnprovokedCrashes} chain={sim.ChainCrashes} stoppedCarSamples={jams} laneChanges={sim.LaneChanges} signaled={sim.LaneChangesSignaled} spawned={sim.Spawned} msPerStep={watch.Elapsed.TotalMilliseconds / steps:F4} campersInLeftLane={100f * campersLeft / Mathf.Max(1, campers):F0}pct trucksInLeftLane={100f * trucksLeft / Mathf.Max(1, trucks):F1}pct\n  lanes: {sim.LaneSpeeds()}\n  ages: {sim.AgeSpread()}\n  plates: {sim.PlateSpread()}\n  profiles: {sim.ProfileSpread()}");
            File.WriteAllText(Path.Combine(Dir, "trafficbench.txt"), res + "\n" + stops);
            return res;
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

        static string Shot(string name, bool atSun = false)
        {
            var cam = Camera.main;
            if (atSun) cam.transform.rotation = Quaternion.LookRotation(-RenderSettings.sun.transform.forward);
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
