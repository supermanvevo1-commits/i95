using System.Collections.Generic;
using SurviveI95.Sim;
using UnityEngine;

namespace SurviveI95.Game
{
    /// <summary>
    /// The game's sound, from real recordings (Sonniss GDC bundles; Tools/cut_sounds.js makes the loops).
    /// The aim is a road that sounds like a road, never a pile of effects:
    /// - Your engine: steady-RPM recordings crossfaded by the engine's RPM. The sedan has six of a 1.6 L four-cylinder
    ///   on a six-speed automatic (about 2,000 RPM at 70 mph); the muscle car and the pickup have three of a V8 (a loping
    ///   idle, 1,140 and 2,385 RPM, the top one pitched up past that), each on its own gearbox and redline.
    /// - Tire roar and wind from a recording made on a busy freeway, rising with speed; tire squeal when you slide.
    /// - Every nearby traffic car has its own positioned sound (tire hiss and engine by vehicle type) with Doppler,
    ///   duller the farther away it is, as air does to sound; a close fast pass gets a real pass-by recording.
    /// - Horns and crashes come from where they happen, quiet and muffled when far.
    /// - A far highway rumble underneath, so there's never dead silence; planes and trains carry their own roar.
    /// </summary>
    public class GameAudio : MonoBehaviour
    {
        PlayerCar car;
        static readonly int[] FourLoops = { 890, 2050, 3100, 4000, 5100, 6000 }, V8Loops = { 745, 1135, 2385 };
        int[] loopRpm = FourLoops;
        AudioSource[] engine = new AudioSource[0];
        GameObject engineObj; string engineFor;
        bool v8; float idleRpm = 890f, redline = 6300f, engineVolume = 1f;
        AudioSource road, skid, bed, rumble, offBumps, offGravel;
        RumbleStripSound rumbleFx;
        AudioLowPassFilter engineFilter;
        AudioClip passCar, passTruck, crashHit, crashSmash, crashGlass, tireHiss, planeLoop, trainLoop, truckEngine, carEngine, bikeEngine;
        AudioClip[] horns;
        int gear = 1, lastImpacts;
        float rpm = 890f, shiftHold, demand;
        AudioSource title; bool titleFading;
        public float Rpm => rpm;
        public int Gear => gear;

        // mph per 1,000 RPM in each gear of a six-speed automatic, spaced like a compact sedan's (cars without their own).
        static readonly float[] SedanGears = { 6.5f, 11f, 16f, 21.5f, 27.5f, 34f };
        float[] mphPerK = SedanGears;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var car = FindAnyObjectByType<PlayerCar>();
            if (car == null || car.GetComponent<GameAudio>() != null) return;
            if (Resources.Load<AudioClip>("Sound/engine_0890") == null) { Debug.LogWarning("[Audio] no recordings (run node Tools/fetch_assets.js sounds, then node Tools/cut_sounds.js)"); return; }
            car.gameObject.AddComponent<GameAudio>();
        }

        static AudioClip Load(string n) => Resources.Load<AudioClip>("Sound/" + n);

        AudioSource Loop(GameObject on, AudioClip clip, float spatial)
        {
            var s = on.AddComponent<AudioSource>();
            s.clip = clip; s.loop = true; s.playOnAwake = false; s.spatialBlend = spatial; s.volume = 0f; s.dopplerLevel = 0f;
            s.time = Random.value * clip.length * .9f;
            s.Play();
            return s;
        }

        void Start()
        {
            car = GetComponent<PlayerCar>();
            // Each engine loop on its own child so a low-pass can sit on the engine only (in the cockpit it's muffled).
            BuildEngine();
            // The title music (the creator's pick), with the title card; it fades once you're properly on your way.
            var song = Resources.Load<AudioClip>("Music/title");
            if (song != null) { title = gameObject.AddComponent<AudioSource>(); title.clip = song; title.spatialBlend = 0f; title.volume = .55f; title.playOnAwake = false; title.Play(); }
            road = Loop(gameObject, Load("road_roar"), 0f);
            skid = Loop(gameObject, Load("skid_loop"), 0f);
            // Rumble strips: the same tire roar, struck groove by groove (RumbleStripSound), on its own child.
            var strip = new GameObject("RumbleStrip"); strip.transform.SetParent(transform, false);
            rumble = Loop(strip, Load("road_roar"), 0f); rumble.volume = 1f; rumbleFx = strip.AddComponent<RumbleStripSound>(); rumbleFx.Init(rumble.clip);
            offBumps = Loop(gameObject, Load("offroad_bumps"), 0f); offGravel = Loop(gameObject, Load("offroad_gravel"), 0f);
            var amb = new GameObject("HighwayBed"); amb.transform.SetParent(transform, false);
            bed = Loop(amb, Load("highway_bed"), 0f); bed.volume = .14f;
            passCar = Load("passby_car"); passTruck = Load("passby_truck");
            crashHit = Load("crash_hit"); crashSmash = Load("crash_smash"); crashGlass = Load("crash_glass");
            horns = new[] { Load("horn_short_1"), Load("horn_short_2"), Load("horn_short_3") };
            planeLoop = Load("plane_loop"); trainLoop = Load("train_loop");
            carEngine = Load("engine_2050"); truckEngine = Load("engine_0890"); bikeEngine = Load("engine_4000");
            tireHiss = MakeTireHiss();
            for (int i = 0; i < Voices; i++) voices.Add(NewVoice());
            lastImpacts = car.Sim.ImpactCount;
        }

        /// <summary>The engine loops for the car being driven; rebuilt when F5 swaps cars.</summary>
        void BuildEngine()
        {
            var c = car.Sim.Car; engineFor = c.id;
            v8 = c.engine == "v8" && Load("v8_0745") != null;
            loopRpm = v8 ? V8Loops : FourLoops;
            idleRpm = v8 ? 745f : 890f;
            redline = c.redline > 0f ? c.redline : 6300f;
            mphPerK = c.gears != null && c.gears.Length > 0 ? c.gears : SedanGears;
            // The muscle car's exhaust is meant to be heard; the pickup's V8 is tuned quiet and sits behind more insulation.
            engineVolume = c.id == "muscle" ? 1.7f : c.id == "pickup" ? .85f : 1f;
            if (engineObj != null) Destroy(engineObj);
            engineObj = new GameObject("EngineSound"); engineObj.transform.SetParent(transform, false);
            engine = new AudioSource[loopRpm.Length];
            for (int i = 0; i < engine.Length; i++) engine[i] = Loop(engineObj, Load((v8 ? "v8_" : "engine_") + loopRpm[i].ToString("0000")), 0f);
            engineFilter = engineObj.AddComponent<AudioLowPassFilter>(); engineFilter.cutoffFrequency = 9000f;
            if (gear > mphPerK.Length) gear = mphPerK.Length;
        }

        // ---------- the player's car ----------
        void Update()
        {
            if (car == null) return;
            if (car.Sim.Car.id != engineFor) BuildEngine();
            if (title != null)
            {
                if (!titleFading && (Mathf.Abs(car.Sim.Speed) > 13f || title.time > 60f)) titleFading = true; // about 30 mph, or a minute in
                if (titleFading) { title.volume = Mathf.MoveTowards(title.volume, 0f, Time.deltaTime * .55f / 6f); if (title.volume <= 0f) { Destroy(title); title = null; } }
            }
            var s = car.Sim; float dt = Time.deltaTime;
            float v = Mathf.Abs(s.Speed), mph = v * DriveSim.MetersPerSecondToMph;
            float throttle = Mathf.Clamp01(car.LastInput.Throttle), brake = Mathf.Clamp01(car.LastInput.Brake);
            UpdateRpm(mph, throttle, dt, s);
            // Crossfade the two loops either side of the RPM (equal power, on a log scale), each pitched to match.
            float lr = Mathf.Log(rpm);
            int last = loopRpm.Length - 1;
            int lo = 0; while (lo < last - 1 && rpm > loopRpm[lo + 1]) lo++;
            float t = Mathf.Clamp01((lr - Mathf.Log(loopRpm[lo])) / (Mathf.Log(loopRpm[lo + 1]) - Mathf.Log(loopRpm[lo])));
            for (int i = 0; i < engine.Length; i++)
            {
                float w = 0f;
                if (i == lo) w = Mathf.Cos(t * Mathf.PI / 2f); else if (i == lo + 1) w = Mathf.Sin(t * Mathf.PI / 2f);
                // The top loop carries the engine on up to its redline (the V8's only goes to 2,385 RPM, so it's pitched further).
                engine[i].pitch = Mathf.Clamp(rpm / loopRpm[i], .6f, i == last ? Mathf.Max(1.6f, redline / loopRpm[i]) : 1.6f);
                // Louder with RPM and with load: a car cruising is quiet, one floored is not.
                float loud = Mathf.Lerp(.16f, .5f, Mathf.InverseLerp(idleRpm, redline - 100f, rpm)) * Mathf.Lerp(.55f, 1f, throttle) * engineVolume;
                if (s.Disabled) loud = 0f;
                engine[i].volume = Mathf.MoveTowards(engine[i].volume, w * loud * (car.Cockpit ? .8f : 1f), dt * 3f);
            }
            // Inside the car the engine is behind the firewall: duller. Outside, the exhaust is open.
            float shut = car.Cockpit ? (engineFor == "pickup" ? 2000f : 2600f) : 9000f;
            engineFilter.cutoffFrequency = Mathf.Lerp(engineFilter.cutoffFrequency, shut, dt * 4f);
            // Tire roar and wind grow faster than speed; at 120+ the wind takes over.
            float roar = Mathf.Pow(Mathf.Clamp01(mph / 75f), 1.4f) * .42f + Mathf.Pow(Mathf.Clamp01((mph - 75f) / 70f), 1.5f) * .3f;
            bool off = s.OffRoad && v > 1f;
            road.volume = Mathf.MoveTowards(road.volume, roar * (car.Cockpit ? .85f : 1f) * (off ? .45f : 1f), dt * (off ? 3f : 1f));
            // Rumble strips: a hard low buzz the moment the tires touch them, at the rate they hit the grooves.
            rumbleFx.Rate = v / RumbleStripSound.GrooveSpacing;
            float strip = s.OnRumble && v > 2f ? Mathf.Pow(Mathf.Clamp01(mph / 45f), .7f) * .6f : 0f;
            rumbleFx.Level = strip * (car.Cockpit ? 1.1f : 1f);
            // Off the pavement: the car thumps over rough ground and the tires crunch dirt, louder and quicker with speed.
            offBumps.volume = Mathf.MoveTowards(offBumps.volume, off ? Mathf.Clamp01(mph / 35f) * .55f : 0f, dt * 5f);
            offGravel.volume = Mathf.MoveTowards(offGravel.volume, off ? Mathf.Clamp01(mph / 30f) * .3f : 0f, dt * 5f);
            offBumps.pitch = offGravel.pitch = .85f + Mathf.Clamp01(mph / 60f) * .3f;
            road.pitch = .82f + Mathf.Clamp01(mph / 140f) * .3f;
            // Squeal when the tires slide or a hard stop locks them up.
            float slip = Mathf.Abs(s.Beta);
            float squeal = v > 4f ? Mathf.Clamp01((slip - .09f) / .2f) + (brake > .85f && mph > 20f && s.LongAccel < -8f ? .5f : 0f) + (car.LastInput.Handbrake && v > 5f ? .6f : 0f) : 0f;
            skid.volume = Mathf.MoveTowards(skid.volume, Mathf.Clamp01(squeal) * .45f, dt * 4f);
            skid.pitch = .9f + Mathf.Clamp01(v / 40f) * .2f;
            // A crash: the sound is picked by how hard, glass from about 25 mph.
            if (s.ImpactCount != lastImpacts)
            {
                lastImpacts = s.ImpactCount;
                float hitMph = s.LastImpact * DriveSim.MetersPerSecondToMph;
                if (hitMph > 2f) Crash(transform.position, hitMph, true);
            }
            UpdateTraffic(dt);
            UpdateWorld();
        }

        void UpdateRpm(float mph, float throttle, float dt, DriveSim s)
        {
            if (s.Gear != 'D' && s.Gear != 'R' || mph < 1f && throttle < .05f)
            {
                gear = 1; float idle = idleRpm + (s.Gear == 'N' || s.Gear == 'P' ? throttle * (redline - 1800f) : 0f);
                rpm = Mathf.Lerp(rpm, idle, dt * 4f); return;
            }
            if (s.Gear == 'R') gear = 1;
            // Shift up early under light throttle, late when floored; down when it would lug or for a kickdown.
            // The gearbox goes by how hard the pedal has been held, not by a tap: a key is all or nothing, and an automatic
            // waits for the pedal to stay down (about half a second) before it kicks down. Letting off counts at once.
            demand = Mathf.MoveTowards(demand, throttle, dt / (throttle > demand ? .6f : .2f));
            // A V8 has the pull to shift sooner: big trucks and muscle cars cruise around 1,500-1,800 RPM.
            float up = Mathf.Lerp(v8 ? 1750f : 2100f, redline * .92f, demand * demand);
            float down = Mathf.Lerp(v8 ? 1000f : 1250f, redline * (v8 ? .4f : .48f), demand * demand);
            shiftHold -= dt;
            float At(int g) => mph / mphPerK[g - 1] * 1000f;
            if (shiftHold <= 0f)
            {
                // Never shift up into a gear it would at once shift back down from (a tall overdrive top gear otherwise hunts).
                if (gear < mphPerK.Length && At(gear) > up && At(gear + 1) > down + 250f) { gear++; shiftHold = .6f; }
                else if (gear > 1 && At(gear) < down) { gear--; shiftHold = .6f; }
            }
            // Below about 12 mph the torque converter slips: the engine runs above road speed.
            float target = Mathf.Max(At(gear), idleRpm + throttle * 1700f * Mathf.Clamp01(1f - mph / 14f));
            rpm = Mathf.Lerp(rpm, Mathf.Clamp(target, idleRpm - 90f, redline), dt * (shiftHold > .3f ? 7f : 10f));
        }

        // ---------- crashes, from anywhere ----------
        void Crash(Vector3 at, float mph, bool player)
        {
            var clip = mph < 14f ? crashHit : crashSmash;
            float vol = Mathf.Clamp01(.35f + mph / 50f);
            OneShot(clip, at, vol, Random.Range(.9f, 1.08f), player);
            if (mph > 25f) OneShot(crashGlass, at, vol * .8f, Random.Range(.95f, 1.05f), player);
        }

        void OneShot(AudioClip clip, Vector3 at, float vol, float pitch, bool close = false, float minDist = 6f)
        {
            if (clip == null) return;
            var go = new GameObject("Sound"); go.transform.position = at;
            var s = go.AddComponent<AudioSource>();
            s.clip = clip; s.volume = vol; s.pitch = pitch; s.spatialBlend = close ? .3f : 1f;
            s.minDistance = minDist; s.maxDistance = 600f; s.rolloffMode = AudioRolloffMode.Logarithmic; s.dopplerLevel = .5f;
            // Far sounds lose their top end in the air.
            var lp = go.AddComponent<AudioLowPassFilter>(); lp.cutoffFrequency = Muffle(Vector3.Distance(at, Listener));
            s.Play();
            Destroy(go, clip.length / Mathf.Max(.1f, pitch) + .2f);
        }

        static Vector3 Listener => Camera.main != null ? Camera.main.transform.position : Vector3.zero;
        static float Muffle(float d) => Mathf.Lerp(20000f, 2200f, Mathf.Clamp01(d / 300f));

        /// <summary>A traffic car honked: a recorded horn at the car, deeper for trucks and RVs, muffled with distance.</summary>
        public void Honk(Transform at, bool big, bool angry, int seed)
        {
            if (horns == null || at == null) return;
            var clip = horns[(seed & 0x7fffffff) % horns.Length];
            float pitch = big ? .72f : .92f + ((seed >> 3) & 15) / 15f * .22f;
            var go = new GameObject("Horn"); go.transform.SetParent(at, false);
            var s = go.AddComponent<AudioSource>(); s.clip = clip; s.pitch = pitch; s.volume = angry ? .8f : .6f;
            s.spatialBlend = 1f; s.minDistance = 8f; s.maxDistance = 400f; s.dopplerLevel = .7f;
            go.AddComponent<AudioLowPassFilter>().cutoffFrequency = Muffle(Vector3.Distance(at.position, Listener));
            s.Play();
            if (angry) { var s2 = go.AddComponent<AudioSource>(); s2.clip = clip; s2.pitch = pitch; s2.volume = s.volume; s2.spatialBlend = 1f; s2.minDistance = 8f; s2.maxDistance = 400f; s2.PlayDelayed(clip.length * .85f); }
            Destroy(go, clip.length * 2.2f / pitch + .3f);
        }

        // ---------- traffic: the nearest cars each get a voice ----------
        const int Voices = 12;
        class Voice { public AudioSource tires, motor; public AudioLowPassFilter lp; public TrafficCar car; public Transform t; public bool passed; }
        readonly List<Voice> voices = new List<Voice>();
        readonly HashSet<TrafficCar> wrecked = new HashSet<TrafficCar>();
        TrafficManager traffic;

        Voice NewVoice()
        {
            var go = new GameObject("TrafficVoice");
            var v = new Voice();
            v.tires = go.AddComponent<AudioSource>(); v.motor = go.AddComponent<AudioSource>();
            foreach (var s in new[] { v.tires, v.motor })
            {
                s.loop = true; s.playOnAwake = false; s.spatialBlend = 1f; s.minDistance = 5f; s.maxDistance = 160f;
                s.rolloffMode = AudioRolloffMode.Logarithmic; s.dopplerLevel = .8f; s.volume = 0f;
            }
            v.tires.clip = tireHiss; v.tires.time = Random.value * tireHiss.length; v.tires.Play();
            v.lp = go.AddComponent<AudioLowPassFilter>();
            return v;
        }

        void UpdateTraffic(float dt)
        {
            if (traffic == null) traffic = GetComponent<TrafficManager>();
            if (traffic == null || traffic.Sim == null) return;
            var ear = Listener;
            // Pick the nearest cars this frame.
            var near = new List<(float d, TrafficCar c, Transform t)>();
            foreach (var c in traffic.Sim.Cars)
            {
                var t = traffic.RootOf(c); if (t == null) continue;
                float d = (t.position - ear).sqrMagnitude;
                if (d < 170f * 170f) near.Add((d, c, t));
                if (c.Wrecked && wrecked.Add(c) && d < 600f * 600f) Crash(t.position, 35f, false);
            }
            near.Sort((a, b) => a.d.CompareTo(b.d));
            var keep = new HashSet<TrafficCar>();
            for (int i = 0; i < near.Count && i < Voices; i++) keep.Add(near[i].c);
            foreach (var v in voices) if (v.car != null && !keep.Contains(v.car)) { v.car = null; v.t = null; }
            foreach (var (_, c, t) in near)
            {
                if (!keep.Contains(c) || voices.Exists(x => x.car == c)) continue;
                var free = voices.Find(x => x.car == null); if (free == null) break;
                free.car = c; free.t = t; free.passed = false;
                string cat = c.Def.category;
                free.motor.clip = cat == "freight" || cat == "recreation" ? truckEngine : cat == "motorcycle" ? bikeEngine : carEngine;
                free.motor.time = Random.value * free.motor.clip.length; free.motor.Play();
                free.tires.volume = 0f; free.motor.volume = 0f;
            }
            float playerS = car.Sim.S;
            foreach (var v in voices)
            {
                if (v.car == null) { v.tires.volume = Mathf.MoveTowards(v.tires.volume, 0f, dt * 3f); v.motor.volume = Mathf.MoveTowards(v.motor.volume, 0f, dt * 3f); continue; }
                var c = v.car; v.tires.transform.position = v.t.position;
                float sp = Mathf.Abs(c.Speed), mph = sp * DriveSim.MetersPerSecondToMph;
                bool big = c.Def.category == "freight" || c.Def.category == "recreation", bike = c.Def.category == "motorcycle";
                // Tire hiss carries a car at highway speed; trucks are deeper and louder.
                float tv = Mathf.Pow(Mathf.Clamp01(mph / 70f), 1.3f) * (big ? .5f : .32f) * (c.Wrecked ? 0f : 1f);
                v.tires.volume = Mathf.MoveTowards(v.tires.volume, tv, dt * 2f);
                v.tires.pitch = (big ? .7f : .9f) + Mathf.Clamp01(mph / 80f) * .25f;
                float mv = (big ? .45f : bike ? .4f : .14f) * (c.Wrecked ? 0f : 1f) * Mathf.Lerp(.7f, 1f, Mathf.Clamp01(c.Accel / 2f));
                v.motor.volume = Mathf.MoveTowards(v.motor.volume, mv, dt * 2f);
                // Each kind at its own engine speed for its road speed.
                v.motor.pitch = big ? .75f + Mathf.Clamp01(mph / 70f) * .55f : bike ? .8f + Mathf.Clamp01(mph / 90f) * .5f : .8f + Mathf.Clamp01(mph / 80f) * .3f;
                float dist = Vector3.Distance(v.t.position, Listener);
                v.lp.cutoffFrequency = Muffle(dist);
                // A close, fast pass gets a real pass-by recording: the whoosh and the engine sweeping past.
                float rel = Mathf.Abs(c.Speed - car.Sim.Speed), ds = c.S - playerS;
                if (!v.passed && rel > 8f && Mathf.Abs(ds) < 25f && Mathf.Abs(c.Lateral - car.Sim.Lateral) < 6.5f && Mathf.Sign(ds) != Mathf.Sign(c.Speed - car.Sim.Speed))
                {
                    v.passed = true;
                    // Timed so the loudest moment of the recording lands as the car draws level.
                    OneShot(big ? passTruck : passCar, v.t.position, Mathf.Clamp01(.25f + rel / 40f) * (big ? .9f : .7f), Mathf.Clamp(.85f + rel / 80f, .85f, 1.25f), false, 6f);
                }
                if (Mathf.Abs(ds) > 60f) v.passed = false;
            }
        }

        // ---------- planes and trains carry their roar ----------
        float nextScan;
        void UpdateWorld()
        {
            if (Time.time < nextScan) return;
            nextScan = Time.time + .5f;
            var air = FindAnyObjectByType<AirTraffic>();
            if (air != null) foreach (Transform p in air.transform) Attach(p, planeLoop, 1.4f, 120f, 5000f, .8f);
            var tr = FindAnyObjectByType<Trains>();
            if (tr != null) foreach (Transform p in tr.transform) if (p.name == "Locomotive" || p.GetSiblingIndex() % 5 == 2) Attach(p, trainLoop, .9f, 25f, 900f, .8f);
        }

        void Attach(Transform t, AudioClip clip, float vol, float min, float max, float doppler)
        {
            if (clip == null || t.GetComponent<AudioSource>() != null) return;
            var s = t.gameObject.AddComponent<AudioSource>();
            s.clip = clip; s.loop = true; s.spatialBlend = 1f; s.minDistance = min; s.maxDistance = max; s.volume = vol;
            s.rolloffMode = AudioRolloffMode.Logarithmic; s.dopplerLevel = doppler; s.time = Random.value * clip.length; s.Play();
            t.gameObject.AddComponent<AudioLowPassFilter>().cutoffFrequency = 6000f;
        }

        /// <summary>
        /// Tire noise on asphalt is broadband rumble with most of its energy below about 1 kHz. Made here as shaped noise,
        /// 4 s long and seamless, because a recording of one car's tires alone isn't in the bundles.
        /// </summary>
        static AudioClip MakeTireHiss()
        {
            const int Rate = 48000, N = Rate * 4, X = Rate / 4;
            var raw = new float[N + X]; var rnd = new System.Random(7); float b0 = 0, b1 = 0, b2 = 0, lp = 0;
            for (int i = 0; i < raw.Length; i++)
            {
                float w = (float)rnd.NextDouble() * 2f - 1f;
                // Pink-ish noise, then a gentle low-pass, so it rumbles rather than hisses.
                b0 = .99765f * b0 + w * .099046f; b1 = .963f * b1 + w * .2965164f; b2 = .57f * b2 + w * 1.0526913f;
                float pink = (b0 + b1 + b2 + w * .1848f) * .2f;
                lp += (pink - lp) * .18f; raw[i] = lp;
            }
            var data = new float[N]; float peak = 0f;
            for (int i = 0; i < N; i++)
            {
                float v = raw[i];
                if (i < X) { float t = i / (float)X; v = raw[i] * Mathf.Sin(t * Mathf.PI / 2f) + raw[N + i] * Mathf.Cos(t * Mathf.PI / 2f); }
                data[i] = v; peak = Mathf.Max(peak, Mathf.Abs(v));
            }
            for (int i = 0; i < N; i++) data[i] *= .8f / peak;
            var clip = AudioClip.Create("TireHiss", N, 1, Rate, false); clip.SetData(data, 0);
            return clip;
        }
    }
}
