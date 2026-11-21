using System.Collections.Generic;
using SurviveI95.Data;
using SurviveI95.Sim;
using SurviveI95.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace SurviveI95.Game
{
    /// <summary>Draws the traffic simulation with pooled objects, so cars appearing and leaving never allocate scene objects.</summary>
    [DefaultExecutionOrder(50)]
    [RequireComponent(typeof(PlayerCar))]
    public class TrafficManager : MonoBehaviour
    {
        public string chapterId = "florida";
        public int carCount = 40;
        public int seed = 95;

        static readonly Color[] Paints =
        {
            new Color(.82f, .83f, .84f), new Color(.82f, .83f, .84f), new Color(.5f, .52f, .54f), new Color(.28f, .29f, .31f), new Color(.035f, .037f, .04f), new Color(.035f, .037f, .04f),
            new Color(.42f, .05f, .05f), new Color(.08f, .15f, .36f), new Color(.05f, .09f, .2f), new Color(.46f, .42f, .34f), new Color(.1f, .22f, .17f), new Color(.68f, .69f, .71f)
        };
        const float ShadowDistance = 160f;

        class View
        {
            public GameObject Root; public MeshFilter Filter; public MeshRenderer Renderer;
            public GameObject SignalLeft, SignalRight, Brake, High, Tail, Low;
            public TrafficModels.Set Set; public bool IsFar, Used;
            public AudioSource Horn;
            public void Lamp(GameObject g, bool on) { if (g.activeSelf != on) g.SetActive(on); }
        }

        public TrafficSim Sim { get; private set; }
        PlayerCar player;
        Transform root;
        readonly Dictionary<string, TrafficModels.Set> sets = new Dictionary<string, TrafficModels.Set>();
        readonly Dictionary<string, Stack<View>> free = new Dictionary<string, Stack<View>>();
        Material[][] paintMaterials;
        Material[] taxi;
        Material amber, red, white, tailGlow, lowGlow;
        DayNight dayNight; readonly Light[] beams = new Light[10];
        AudioClip carHorn, carHornLong, airHorn, airHornLong;
        string notice; float noticeUntil;
        /// <summary>Every line shown this session, for tests.</summary>
        public readonly List<string> NoticeLog = new List<string>();

        void Start()
        {
            player = GetComponent<PlayerCar>();
            var catalog = GameData.LoadVehicles();
            var chapter = GameData.LoadStateTraffic().chapters.Find(c => c.id == chapterId);
            Sim = new TrafficSim(player.Path, catalog, chapter, seed) { Target = carCount, Removed = Release, Notice = Show, Honk = PlayHorn };
            carHorn = MakeHorn(.38f, 415f, 518f); carHornLong = MakeHorn(1.3f, 415f, 518f);
            airHorn = MakeHorn(.6f, 233f, 294f); airHornLong = MakeHorn(1.6f, 233f, 294f);
            root = new GameObject("Traffic").transform;
            MakeMaterials();
            // Build every model up front: making one mid-drive would show up as a hitch.
            foreach (var row in chapter.mix)
            {
                var def = catalog.vehicles.Find(v => v.id == row.id);
                sets[def.id] = TrafficModels.Create(def);
            }
        }

        static Shader Find(string name) => Shader.Find(name) ?? Shader.Find("Universal Render Pipeline/Lit");

        static Material Surface(Shader s, Color c, float smooth, float metal = 0f)
        {
            var m = new Material(s) { enableInstancing = true };
            m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal);
            return m;
        }

        static Material Glow(Color c) { var m = new Material(Find("Universal Render Pipeline/Unlit")); m.SetColor("_BaseColor", c); return m; }

        void MakeMaterials()
        {
            var lit = Find("Universal Render Pipeline/Lit");
            var glass = Surface(lit, new Color(.05f, .07f, .09f), .9f);
            var dark = Surface(lit, new Color(.035f, .035f, .04f), .2f);
            var trim = Surface(lit, new Color(.72f, .73f, .74f), .55f, .2f);
            var head = Surface(lit, new Color(.85f, .86f, .8f), .8f);
            var tail = Surface(lit, new Color(.3f, .015f, .02f), .7f);
            Material[] Set(Material paint) => new[] { paint, glass, dark, trim, head, tail };
            paintMaterials = new Material[Paints.Length][];
            for (int i = 0; i < Paints.Length; i++) paintMaterials[i] = Set(Surface(lit, Paints[i], .62f, .1f));
            taxi = Set(Surface(lit, new Color(.85f, .55f, .03f), .62f, .1f));
            amber = Glow(CarLamps.Amber); red = Glow(CarLamps.Red); white = Glow(CarLamps.White); // the same lit colors as the player car
            tailGlow = Glow(CarLamps.DimRed); lowGlow = Glow(CarLamps.White * .8f); // lights on at night: tail lamps dimmer than the brake
        }

        void FixedUpdate()
        {
            if (Sim == null) return;
            // What other drivers can see and hear from the player.
            Sim.PlayerSignal = player.Signal; Sim.PlayerHorn = player.Honking; Sim.PlayerFlash = player.HighBeams;
            Sim.Step(Time.fixedDeltaTime, player.Sim);
            Sim.HitPlayer(player.Sim);
        }

        View Acquire(TrafficCar c)
        {
            if (!free.TryGetValue(c.Def.id, out var stack)) free[c.Def.id] = stack = new Stack<View>();
            View v = stack.Count > 0 ? stack.Pop() : Make(c);
            v.Root.SetActive(true);
            v.Used = false;
            v.Renderer.sharedMaterials = c.Def.id == "taxi" ? taxi : paintMaterials[c.PaintIndex % paintMaterials.Length];
            return v;
        }

        View Make(TrafficCar c)
        {
            var set = sets[c.Def.id];
            var go = new GameObject(c.Def.id);
            go.transform.SetParent(root, false);
            var v = new View { Root = go, Set = set, Filter = go.AddComponent<MeshFilter>(), Renderer = go.AddComponent<MeshRenderer>() };
            v.Filter.sharedMesh = set.Near;
            v.Renderer.shadowCastingMode = ShadowCastingMode.On;
            v.SignalLeft = Lamp(go.transform, "SignalL", set.SignalLeft, amber);
            v.SignalRight = Lamp(go.transform, "SignalR", set.SignalRight, amber);
            v.Brake = Lamp(go.transform, "Brake", set.Brake, red);
            v.High = Lamp(go.transform, "High", set.HighBeam, white);
            v.Tail = Lamp(go.transform, "Tail", set.Brake, tailGlow);
            v.Low = Lamp(go.transform, "Low", set.HighBeam, lowGlow);
            return v;
        }

        static GameObject Lamp(Transform parent, string name, Mesh mesh, Material mat)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            g.SetActive(false);
            return g;
        }

        void Release(TrafficCar c)
        {
            if (c.View is View v) { v.Root.SetActive(false); free[c.Def.id].Push(v); c.View = null; }
        }

        public void Show(string line)
        {
            notice = line; noticeUntil = Time.time + 4.5f;
            NoticeLog.Add(line);
        }

        // A two-tone horn built from odd harmonics, so the game needs no sound files for it.
        /// <summary>A two-tone horn. With loop on there's no fade, so whole-number frequencies and a whole-second length loop without a click.</summary>
        public static AudioClip MakeHorn(float seconds, float low, float high, bool loop = false)
        {
            const int Rate = 44100;
            int n = Mathf.RoundToInt(Rate * seconds);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate, s = 0f;
                for (int h = 1; h <= 7; h += 2) s += (Mathf.Sin(2f * Mathf.PI * low * h * t) + Mathf.Sin(2f * Mathf.PI * high * h * t)) / h;
                float env = loop ? 1f : Mathf.Clamp01(t / .02f) * Mathf.Clamp01((seconds - t) / .05f);
                data[i] = (float)System.Math.Tanh(s * .9f) * .7f * env;
            }
            var clip = AudioClip.Create("Horn", n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Where a traffic car is drawn, for sounds that come from it.</summary>
        public Transform RootOf(TrafficCar c) => c.View is View v && v.Root != null ? v.Root.transform : null;

        void PlayHorn(TrafficCar c, bool angry)
        {
            if (!(c.View is View v)) return;
            var audio = GetComponent<GameAudio>();
            if (audio != null) { audio.Honk(v.Root.transform, c.Def.category == "freight" || c.Def.category == "recreation", angry, c.GetHashCode()); return; }
            if (v.Horn == null)
            {
                v.Horn = v.Root.AddComponent<AudioSource>();
                v.Horn.spatialBlend = 1f; v.Horn.minDistance = 7f; v.Horn.maxDistance = 320f; v.Horn.dopplerLevel = .6f; v.Horn.playOnAwake = false;
            }
            bool big = c.Def.category == "freight" || c.Def.category == "recreation";
            v.Horn.clip = big ? (angry ? airHornLong : airHorn) : (angry ? carHornLong : carHorn);
            v.Horn.Play();
        }

        void OnGUI()
        {
            if (string.IsNullOrEmpty(notice) || Time.time > noticeUntil) return;
            float alpha = Mathf.Clamp01((noticeUntil - Time.time) / .6f);
            if (player.Hud != null) player.Hud.DrawNotice(notice, alpha);
        }

        void Update()
        {
            if (Sim == null) return;
            RoutePath road = player.Path;
            float t = Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
            float playerS = player.Sim.Current == null ? player.Sim.S : player.Sim.CurrentJunction.divergeS + player.Sim.S;
            bool blink = CarLamps.Blink;
            if (dayNight == null) dayNight = FindAnyObjectByType<DayNight>();
            bool night = dayNight != null && dayNight.LampsOn > .5f; // drivers put their lights on about when the street lamps come on
            foreach (var c in Sim.Cars)
            {
                var v = c.View as View;
                if (v == null) c.View = v = Acquire(c);
                float s = Mathf.Lerp(c.PrevS, c.S, t), lat = Mathf.Lerp(c.PrevLateral, c.Lateral, t);
                var rot = Quaternion.LookRotation(road.Tangent(s), Vector3.up) * Quaternion.Euler(0, Mathf.LerpAngle(c.PrevYaw, c.Yaw, t) * Mathf.Rad2Deg, 0);
                // A bike that goes down topples over in about half a second (falling faster as it goes) toward the side it was
                // pushed, and lies there; one whose rider caught a knock leans with the wobble. A car wreck sits crooked on a
                // broken corner (which corner and how much picked from the car itself).
                bool bike = c.Def.category == "motorcycle";
                if (bike && c.DownAt >= 0f) { float fall = Mathf.Clamp01((Sim.Clock - c.DownAt) / .55f); rot *= Quaternion.Euler(0f, 0f, -c.DownSide * 82f * fall * fall); }
                else if (bike) rot *= Quaternion.Euler(0f, 0f, -c.Wobble * 55f);
                else if (c.Wrecked) rot *= Quaternion.Euler((c.Id % 3 - 1) * 2.5f, 0f, ((c.Id & 1) == 0 ? 1f : -1f) * 3.5f);
                v.Root.transform.SetPositionAndRotation(road.Point(s, lat) + (c.Wrecked && !bike ? Vector3.down * .06f : Vector3.zero), rot);

                float distance = Mathf.Abs(s - playerS);
                bool far = distance > TrafficSim.FarDistance;
                if (far != v.IsFar || !v.Used)
                {
                    v.IsFar = far; v.Used = true;
                    v.Filter.sharedMesh = far ? v.Set.Far : v.Set.Near;
                }
                var shadow = distance > ShadowDistance ? ShadowCastingMode.Off : ShadowCastingMode.On;
                if (v.Renderer.shadowCastingMode != shadow) v.Renderer.shadowCastingMode = shadow;

                bool lamps = !far;
                v.Lamp(v.SignalLeft, lamps && blink && (c.Hazards || c.Signal < 0));
                v.Lamp(v.SignalRight, lamps && blink && (c.Hazards || c.Signal > 0));
                v.Lamp(v.Brake, lamps && c.Braking);
                v.Lamp(v.High, lamps && c.FlashingHighBeams);
                bool lightsOn = night && !c.NoLights && !c.Wrecked;
                v.Lamp(v.Tail, lightsOn && !c.Braking); // the brake lamp is the brighter one over it
                v.Lamp(v.Low, lightsOn && !c.FlashingHighBeams);
            }
            UpdateBeams(playerS, night);
            UpdateWreckSmoke(playerS);
        }
    
        // The nearest cars with their lights on throw real light ahead of them; the rest only show their lamps.
        void UpdateBeams(float playerS, bool night)
        {
            if (beams[0] == null)
                for (int i = 0; i < beams.Length; i++)
                {
                    var l = new GameObject("TrafficBeam").AddComponent<Light>();
                    l.type = LightType.Spot; l.spotAngle = 55f; l.innerSpotAngle = 25f; l.range = 45f; l.intensity = 0f;
                    l.color = new Color(1f, .97f, .92f); l.shadows = LightShadows.None; l.enabled = false;
                    l.transform.SetParent(root, false); beams[i] = l;
                }
            int n = 0;
            if (night)
            {
                var near = new List<TrafficCar>();
                foreach (var c in Sim.Cars) if (!c.NoLights && !c.Wrecked && c.View is View v && v.Root.activeSelf && Mathf.Abs(c.S - playerS) < 160f) near.Add(c);
                near.Sort((a, b) => Mathf.Abs(a.S - playerS).CompareTo(Mathf.Abs(b.S - playerS)));
                foreach (var c in near)
                {
                    if (n >= beams.Length) break;
                    var t = ((View)c.View).Root.transform;
                    var l = beams[n++]; l.enabled = true; l.intensity = 6f;
                    l.transform.position = t.position + t.forward * (c.Length * .5f + .2f) + Vector3.up * .75f;
                    l.transform.rotation = Quaternion.LookRotation(t.forward * 1f - t.up * .09f, Vector3.up); // low beams aim a little down
                }
            }
            for (int i = n; i < beams.Length; i++) if (beams[i] != null && beams[i].enabled) beams[i].enabled = false;
        }
    
        // Smoke rising from the nearest wrecked cars' engines (a few at a time: far wrecks don't need it).
        readonly ParticleSystem[] wreckSmoke = new ParticleSystem[4];
        void UpdateWreckSmoke(float playerS)
        {
            int n = 0;
            foreach (var c in Sim.Cars)
            {
                if (n >= wreckSmoke.Length) break;
                if (!c.Wrecked || c.Def.category == "motorcycle" || Mathf.Abs(c.S - playerS) > 220f || !(c.View is View v)) continue;
                if (wreckSmoke[n] == null) wreckSmoke[n] = player.MakeWreckSmoke(root);
                var ps = wreckSmoke[n++];
                var t = v.Root.transform;
                ps.transform.position = t.position + t.forward * (c.Length * .35f) + Vector3.up * 1.1f;
                if (!ps.isEmitting) ps.Play();
            }
            for (int i = n; i < wreckSmoke.Length; i++) if (wreckSmoke[i] != null && wreckSmoke[i].isEmitting) wreckSmoke[i].Stop();
        }
    }
}
