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
            public GameObject SignalLeft, SignalRight, Brake, High;
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
        Material amber, red, white;
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
            amber = Glow(new Color(1f, .55f, .05f)); red = Glow(new Color(1f, .05f, .04f)); white = Glow(new Color(1f, 1f, .92f));
        }

        void FixedUpdate()
        {
            if (Sim == null) return;
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
        static AudioClip MakeHorn(float seconds, float low, float high)
        {
            const int Rate = 44100;
            int n = Mathf.RoundToInt(Rate * seconds);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate, s = 0f;
                for (int h = 1; h <= 7; h += 2) s += (Mathf.Sin(2f * Mathf.PI * low * h * t) + Mathf.Sin(2f * Mathf.PI * high * h * t)) / h;
                float env = Mathf.Clamp01(t / .02f) * Mathf.Clamp01((seconds - t) / .05f);
                data[i] = (float)System.Math.Tanh(s * .9f) * .7f * env;
            }
            var clip = AudioClip.Create("Horn", n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        void PlayHorn(TrafficCar c, bool angry)
        {
            if (!(c.View is View v)) return;
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
            var style = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            var r = new Rect(Screen.width * .15f, Screen.height * .12f, Screen.width * .7f, 50f);
            style.normal.textColor = new Color(0f, 0f, 0f, .8f * alpha);
            GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), notice, style);
            style.normal.textColor = new Color(1f, .93f, .78f, alpha);
            GUI.Label(r, notice, style);
        }

        void Update()
        {
            if (Sim == null) return;
            RoutePath road = player.Path;
            float t = Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
            float playerS = player.Sim.Current == null ? player.Sim.S : player.Sim.CurrentJunction.divergeS + player.Sim.S;
            bool blink = Mathf.Repeat(Time.time * 1.5f, 1f) < .5f;
            foreach (var c in Sim.Cars)
            {
                var v = c.View as View;
                if (v == null) c.View = v = Acquire(c);
                float s = Mathf.Lerp(c.PrevS, c.S, t), lat = Mathf.Lerp(c.PrevLateral, c.Lateral, t);
                v.Root.transform.SetPositionAndRotation(road.Point(s, lat), Quaternion.LookRotation(road.Tangent(s), Vector3.up) * Quaternion.Euler(0, Mathf.LerpAngle(c.PrevYaw, c.Yaw, t) * Mathf.Rad2Deg, 0));

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
            }
        }
    }
}
