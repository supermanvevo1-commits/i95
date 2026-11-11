using SurviveI95.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SurviveI95.Game
{
    /// <summary>
    /// The driving HUD: an analog speedometer in the spirit of Forza's, with an mph scale outside and km/h inside,
    /// a needle, the digital speed and gear in the middle, and marks on the rim for the posted limit (amber) and the
    /// cruise setting (green). Signal arrows, a high-beam light and a damage bar sit around it. It shows in third person;
    /// in the cockpit the car's own gauges do the job. Everything is drawn from textures made here, so there are no image
    /// files to keep, and sized to the screen height so it looks the same at any resolution.
    /// </summary>
    public class Hud : MonoBehaviour
    {
        const float DialMaxMph = 160f, SweepDeg = 250f, Ref = 1080f;
        // Florida troopers seldom stop anyone under about 10 over; from 20 over it is a near-certain ticket.
        const float TicketOver = 10f, WayOver = 20f;
        static readonly Color Ink = new Color(.94f, .95f, .96f), Dim = new Color(.62f, .66f, .7f), Accent = new Color(1f, .36f, .12f);
        static readonly Color CruiseGreen = new Color(.35f, 1f, .5f), LimitAmber = new Color(1f, .74f, .16f), BeamBlue = new Color(.35f, .62f, 1f);

        PlayerCar car;
        Texture2D dial, needle, hub, mark, arrow, beam, white;
        GUIStyle big, small, tiny, label, notice, limitDigits;
        float needleMph, scale;
        /// <summary>0 bottom right, 1 bottom left, 2 top right, 3 top left. F2 cycles it until the settings screen exists (M12).</summary>
        public int Corner { get; private set; }

        public void Init(PlayerCar owner) { car = owner; }

        void Start()
        {
            Corner = Mathf.Clamp(PlayerPrefs.GetInt("hudCorner", 0), 0, 3);
            dial = MakeDial(512); needle = MakeNeedle(); hub = MakeDisc(64, new Color(.12f, .13f, .14f), new Color(.32f, .34f, .36f));
            mark = MakeMark(); arrow = MakeArrow(); beam = MakeBeam(); white = Solid(Color.white);
        }

        void Update()
        {
            var k = Keyboard.current;
            if (k != null && k.f2Key.wasPressedThisFrame) { Corner = (Corner + 1) % 4; PlayerPrefs.SetInt("hudCorner", Corner); }
            // The needle has a little weight to it, like a real gauge.
            float mph = Mathf.Abs(car.Sim.Speed) * DriveSim.MetersPerSecondToMph;
            needleMph = Mathf.Lerp(needleMph, mph, 1f - Mathf.Exp(-Time.deltaTime * 9f));
        }

        public void SetCorner(int c) { Corner = Mathf.Clamp(c, 0, 3); PlayerPrefs.SetInt("hudCorner", Corner); }

        // ---------------------------------------------------------------- drawing

        void Styles()
        {
            scale = Screen.height / Ref;
            int Px(float v) => Mathf.Max(1, Mathf.RoundToInt(v * scale));
            if (big == null || big.fontSize != Px(58))
            {
                big = new GUIStyle { font = car.hudFontBold, fontSize = Px(58), alignment = TextAnchor.MiddleCenter };
                small = new GUIStyle(big) { fontSize = Px(19) };
                tiny = new GUIStyle(big) { font = car.hudFontMedium ?? car.hudFontBold, fontSize = Px(16) };
                label = new GUIStyle(big) { fontSize = Px(24) };
                notice = new GUIStyle(big) { fontSize = Px(30), wordWrap = true };
                limitDigits = new GUIStyle(big) { fontSize = Px(40) };
            }
        }

        static void Text(Rect r, string s, GUIStyle st, Color c, bool shadow = true)
        {
            if (shadow) { st.normal.textColor = new Color(0f, 0f, 0f, c.a * .6f); GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), s, st); }
            st.normal.textColor = c; GUI.Label(r, s, st);
        }

        static void Rotated(Texture t, Rect r, Vector2 pivot, float deg, Color c)
        {
            var keep = GUI.matrix;
            GUI.matrix = keep * Matrix4x4.TRS(pivot, Quaternion.Euler(0, 0, deg), Vector3.one) * Matrix4x4.TRS(-pivot, Quaternion.identity, Vector3.one);
            var was = GUI.color; GUI.color = c; GUI.DrawTexture(r, t); GUI.color = was;
            GUI.matrix = keep;
        }

        static float AngleFor(float mph) => -SweepDeg / 2f + SweepDeg * Mathf.Clamp(mph, 0f, DialMaxMph) / DialMaxMph;

        void OnGUI()
        {
            if (car == null || car.Sim == null || Event.current.type != EventType.Repaint) return;
            Styles();
            float s = scale;
            float D = 270f * s, margin = 34f * s;
            bool right = Corner == 0 || Corner == 2, bottom = Corner < 2;
            float x = right ? Screen.width - margin - D : margin, y = bottom ? Screen.height - margin - D : margin;
            var dialRect = new Rect(x, y, D, D);
            var c = dialRect.center; float R = D / 2f;

            if (!car.Cockpit)
            {
                GUI.DrawTexture(dialRect, dial);
                // Outer scale in mph every 20, inner in km/h every 40.
                for (int mph = 0; mph <= DialMaxMph; mph += 20)
                {
                    float a = AngleFor(mph) * Mathf.Deg2Rad; var p = c + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * R * .66f;
                    Text(new Rect(p.x - 30 * s, p.y - 14 * s, 60 * s, 28 * s), mph.ToString(), label, Ink, false);
                }
                for (int kmh = 0; kmh <= 240; kmh += 40)
                {
                    float a = AngleFor(kmh / 1.609f) * Mathf.Deg2Rad; var p = c + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * R * .455f;
                    Text(new Rect(p.x - 24 * s, p.y - 10 * s, 48 * s, 20 * s), kmh.ToString(), tiny, Dim, false);
                }
                // Rim marks: the posted limit here, and the cruise setting.
                float limit = car.LimitMph;
                if (limit > 0f) Mark(c, R, limit, LimitAmber);
                if (car.CruiseOn) Mark(c, R, car.CruiseSetMph, CruiseGreen);
                // Needle and hub.
                float nw = 14f * s, nh = R * .9f, tail = nh * Tail / (1f - Tail);
                Rotated(needle, new Rect(c.x - nw / 2f, c.y - nh, nw, nh + tail), c, AngleFor(needleMph), Color.white);
                GUI.DrawTexture(new Rect(c.x - 22 * s, c.y - 22 * s, 44 * s, 44 * s), hub);
                // Digital speed and gear in the lower half, where the needle never goes.
                int shown = Mathf.RoundToInt(Mathf.Abs(car.Sim.Speed) * DriveSim.MetersPerSecondToMph);
                bool over = limit > 0f && shown >= limit + TicketOver;
                Text(new Rect(c.x - 80 * s, c.y + 16 * s, 160 * s, 60 * s), shown.ToString(), big, over ? Accent : Ink);
                Text(new Rect(c.x - 80 * s, c.y + 70 * s, 160 * s, 20 * s), "MPH  ·  " + Mathf.RoundToInt(shown * 1.609f) + " KM/H", tiny, Dim);
                var gear = new Rect(c.x - 16 * s, c.y + 93 * s, 32 * s, 26 * s);
                Panel(gear, new Color(0f, 0f, 0f, .45f), 6f * s);
                Text(gear, car.GearLetter, small, car.GearLetter == "R" ? Accent : Ink, false);
            }

            // The posted limit, as a US sign beside the dial, like a map app. Red when a ticket gets likely, pulsing well past it.
            float lim = car.LimitMph;
            if (lim > 0f)
            {
                float mphNow = Mathf.Abs(car.Sim.Speed) * DriveSim.MetersPerSecondToMph;
                bool warn = mphNow >= lim + TicketOver, alarm = mphNow >= lim + WayOver;
                float pulse = alarm ? 1f + .07f * Mathf.Sin(Time.unscaledTime * 9f) : 1f;
                float w = 66f * s * pulse, h = 84f * s * pulse;
                float sx = car.Cockpit ? (right ? Screen.width - margin - w : margin) : (right ? x - w - 12f * s : x + D + 12f * s);
                float sy = car.Cockpit ? (bottom ? Screen.height - margin - h - 84f * s : margin + 84f * s) : (bottom ? y + D - h : y);
                var sign = new Rect(sx, sy, w, h);
                Panel(new Rect(sign.x + 2f * s, sign.y + 3f * s, w, h), new Color(0f, 0f, 0f, .35f), 7f * s);
                Panel(sign, warn ? new Color(.86f, .1f, .08f) : new Color(.06f, .06f, .06f), 7f * s);
                var face = new Rect(sign.x + 4f * s, sign.y + 4f * s, w - 8f * s, h - 8f * s);
                Panel(face, warn ? new Color(1f, .93f, .92f) : new Color(.97f, .97f, .95f), 4.5f * s);
                var ink = warn ? new Color(.75f, .06f, .05f) : new Color(.05f, .05f, .05f);
                Text(new Rect(face.x, face.y + 3f * s, face.width, 14f * s), "SPEED", tiny, ink, false);
                Text(new Rect(face.x, face.y + 16f * s, face.width, 14f * s), "LIMIT", tiny, ink, false);
                Text(new Rect(face.x, face.y + 28f * s, face.width, face.height - 30f * s), lim.ToString("0"), limitDigits, ink, false);
            }

            // Status row along the dial's inner edge: cruise, high beams, damage. It stays in the cockpit too.
            float rowY = bottom ? y - 40f * s : y + D + 8f * s;
            if (car.Cockpit) { rowY = bottom ? Screen.height - margin - 36f * s : margin; }
            float rowX = right ? x + D : x; float dir = right ? -1f : 1f;
            void Chip(string text, Color tint, float w)
            {
                var r = new Rect(right ? rowX - w : rowX, rowY, w, 32f * s);
                Panel(r, ChipColor, 8f * s);
                Text(r, text, small, tint);
                rowX += dir * (w + 8f * s);
            }
            if (car.CruiseOn) Chip("CRUISE " + Mathf.RoundToInt(car.CruiseSetMph), CruiseGreen, 112f * s);
            if (car.HighBeams)
            {
                var r = new Rect(right ? rowX - 46f * s : rowX, rowY, 46f * s, 32f * s);
                Panel(r, ChipColor, 8f * s);
                var keep = GUI.color; GUI.color = BeamBlue; GUI.DrawTexture(new Rect(r.x + 7f * s, r.y + 4f * s, 32f * s, 24f * s), beam); GUI.color = keep;
                rowX += dir * (54f * s);
            }
            if (car.Sim.Damage > .01f)
            {
                float w = 150f * s; var r = new Rect(right ? rowX - w : rowX, rowY, w, 32f * s);
                Panel(r, ChipColor, 8f * s);
                var keep = GUI.color; float d = car.Sim.Damage; GUI.color = Color.Lerp(new Color(1f, .82f, .2f), new Color(1f, .2f, .12f), d);
                GUI.DrawTexture(new Rect(r.x + 64f * s, r.y + 12f * s, (w - 74f * s) * Mathf.Clamp01(1f - d), 8f * s), white);
                GUI.color = new Color(1f, 1f, 1f, .15f); GUI.DrawTexture(new Rect(r.x + 64f * s, r.y + 12f * s, w - 74f * s, 8f * s), white); GUI.color = keep;
                Text(new Rect(r.x + 6f * s, r.y, 56f * s, r.height), "CAR", small, Ink);
            }

            // Turn signals: green arrows either side of the dial top, blinking with the lamps.
            bool blink = CarLamps.Blink;
            bool l = blink && (car.Hazards || car.Signal < 0), rr = blink && (car.Hazards || car.Signal > 0);
            float ay = car.Cockpit ? rowY - 44f * s : (bottom ? y - 84f * s : y + D + 48f * s);
            float ax = car.Cockpit ? (right ? Screen.width - margin - 140f * s : margin) : x + D / 2f - 70f * s;
            var on = new Color(.3f, 1f, .45f);
            // Like a real dash, an arrow only shows while it's lit.
            if (l) Rotated(arrow, new Rect(ax, ay, 40f * s, 36f * s), new Vector2(ax + 20f * s, ay + 18f * s), 180f, on);
            if (rr) { GUI.color = on; GUI.DrawTexture(new Rect(ax + 100f * s, ay, 40f * s, 36f * s), arrow); GUI.color = Color.white; }

            // Center messages: short confirmations, looking back, and a disabled car.
            if (car.FlashText != null)
                Text(new Rect(0, Screen.height * .74f, Screen.width, 40f * s), car.FlashText, label, Ink);
            if (car.LookingBack) Text(new Rect(0, 18f * s, Screen.width, 40f * s), "LOOKING BACK", label, Ink);
            if (car.Sim.Disabled) Text(new Rect(0, Screen.height * .3f, Screen.width, 60f * s), "CAR DISABLED  ·  PRESS R TO RECOVER", notice, Accent);
        }

        void Mark(Vector2 c, float R, float mph, Color tint)
        {
            float w = 14f * scale, h = 22f * scale;
            Rotated(mark, new Rect(c.x - w / 2f, c.y - R + 2f * scale, w, h), c, AngleFor(mph), tint);
        }

        /// <summary>Draws a notice from the road (a regular is back, someone wants to race) in the HUD's style.</summary>
        public void DrawNotice(string text, float alpha)
        {
            if (car == null || Event.current.type != EventType.Repaint) return;
            Styles();
            float s = scale, w = Mathf.Min(Screen.width * .7f, 900f * s);
            var size = notice.CalcHeight(new GUIContent(text), w - 48f * s);
            var r = new Rect((Screen.width - w) / 2f, Screen.height * .1f, w, size + 24f * s);
            Panel(r, new Color(.04f, .05f, .06f, .66f * alpha), 10f * s);
            var keep = GUI.color; GUI.color = new Color(Accent.r, Accent.g, Accent.b, alpha); GUI.DrawTexture(new Rect(r.x + 10f * s, r.y + 8f * s, 4f * s, r.height - 16f * s), white); GUI.color = keep;
            Text(new Rect(r.x + 24f * s, r.y + 12f * s, w - 48f * s, size), text, notice, new Color(1f, .96f, .88f, alpha));
        }

        static readonly Color ChipColor = new Color(.04f, .05f, .06f, .72f);
        void Panel(Rect r, Color c, float radius) => GUI.DrawTexture(r, white, ScaleMode.StretchToFill, true, 0f, c, 0f, radius);

        // ---------------------------------------------------------------- textures, made once

        static Texture2D New(int w, int h) => new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        static Texture2D Solid(Color c) { var t = New(2, 2); t.SetPixels(new[] { c, c, c, c }); t.Apply(); return t; }
        static float Edge(float d, float aa) => Mathf.Clamp01(.5f - d / aa); // 1 inside, 0 outside, with a soft pixel edge

        // The dial face: dark glass, a bright rim, and ticks every 5 mph (long every 20) plus small km/h ticks inside.
        static Texture2D MakeDial(int n)
        {
            var t = New(n, n); var px = new Color[n * n]; float half = n / 2f, aa = 1.2f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = x + .5f - half, dy = y + .5f - half, r = Mathf.Sqrt(dx * dx + dy * dy) / half;
                    var col = new Color(0, 0, 0, 0);
                    float face = Edge((r - .985f) * half, aa);
                    if (face > 0f)
                    {
                        float g = Mathf.Lerp(.16f, .05f, r);
                        col = new Color(g * .92f, g * .97f, g * 1.05f, .8f * face);
                        // Rim: a thin bright ring and a darker groove inside it.
                        float rim = Edge((Mathf.Abs(r - .962f) - .012f) * half, aa);
                        col = Color.Lerp(col, new Color(.78f, .8f, .83f, .95f * face), rim);
                        // Angle measured from straight up, clockwise, as the needle moves.
                        float ang = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg; // y grows upward in the texture
                        float mph = (ang + SweepDeg / 2f) / SweepDeg * DialMaxMph;
                        if (mph >= -1f && mph <= DialMaxMph + 1f)
                        {
                            float arcPerMph = SweepDeg / DialMaxMph * Mathf.Deg2Rad * r * half; // pixels along the arc per mph
                            Tick(ref col, mph, 20f, arcPerMph, r, .80f, .935f, 2.4f, Ink, half, aa);
                            Tick(ref col, mph, 10f, arcPerMph, r, .86f, .935f, 1.6f, Ink, half, aa);
                            Tick(ref col, mph, 5f, arcPerMph, r, .895f, .935f, 1.1f, Dim, half, aa);
                            // The km/h ring: a tick every 20 km/h.
                            float kmh = mph * 1.609f;
                            if (kmh <= 241f) Tick(ref col, kmh, 20f, arcPerMph / 1.609f, r, .555f, .6f, 1.1f, Dim, half, aa);
                            // A soft red zone past 120 mph, where the car is close to its limit.
                            if (mph > 120f) { float band = Edge((Mathf.Abs(r - .915f) - .02f) * half, aa); col = Color.Lerp(col, new Color(.95f, .2f, .12f, col.a), band * .7f); }
                        }
                    }
                    px[y * n + x] = col;
                }
            t.SetPixels(px); t.Apply(); return t;
        }

        static void Tick(ref Color col, float value, float every, float arcPerUnit, float r, float r0, float r1, float halfWidthPx, Color tint, float half, float aa)
        {
            float off = Mathf.Abs(value - Mathf.Round(value / every) * every) * arcPerUnit;
            float along = Mathf.Max(r0 - r, r - r1) * half;
            float k = Edge(off - halfWidthPx, aa) * Edge(along, aa);
            if (k > 0f) col = Color.Lerp(col, new Color(tint.r, tint.g, tint.b, Mathf.Max(col.a, .95f)), k);
        }

        const float Tail = .1f; // the share of the needle below the pivot
        // A tapered needle with a short tail below the pivot, glowing orange-red like a lit pointer.
        static Texture2D MakeNeedle()
        {
            int w = 32, h = 512; var t = New(w, h); var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                float k = y / (float)h; // 0 bottom (tail), 1 tip
                float halfW = k < Tail ? 7f : Mathf.Lerp(6f, 1.4f, (k - Tail) / (1f - Tail));
                for (int x = 0; x < w; x++)
                {
                    float d = Mathf.Abs(x + .5f - w / 2f) - halfW;
                    float a = Edge(d, 1.4f);
                    var c = Color.Lerp(new Color(.85f, .2f, .08f), Accent, k);
                    if (k < Tail) c = new Color(.2f, .2f, .22f);
                    px[y * w + x] = new Color(c.r, c.g, c.b, a);
                }
            }
            t.SetPixels(px); t.Apply(); return t;
        }

        static Texture2D MakeDisc(int n, Color inner, Color ring)
        {
            var t = New(n, n); var px = new Color[n * n]; float half = n / 2f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float r = Mathf.Sqrt((x + .5f - half) * (x + .5f - half) + (y + .5f - half) * (y + .5f - half)) / half;
                    var c = Color.Lerp(inner, ring, Mathf.Clamp01((r - .7f) / .3f)); c.a = Edge((r - .97f) * half, 1.2f);
                    px[y * n + x] = c;
                }
            t.SetPixels(px); t.Apply(); return t;
        }

        // A small downward-pointing wedge that sits on the rim.
        static Texture2D MakeMark()
        {
            int w = 32, h = 48; var t = New(w, h); var px = new Color[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                float k = y / (float)h, halfW = Mathf.Lerp(2f, 15f, k);
                px[y * w + x] = new Color(1f, 1f, 1f, Edge(Mathf.Abs(x + .5f - w / 2f) - halfW, 1.4f));
            }
            t.SetPixels(px); t.Apply(); return t;
        }

        // A right-pointing arrow: a stem and a head, as on a dashboard.
        static Texture2D MakeArrow()
        {
            int w = 80, h = 72; var t = New(w, h); var px = new Color[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                float fx = x + .5f, fy = y + .5f - h / 2f;
                float stem = Mathf.Max(Mathf.Max(6f - fx, fx - 44f), Mathf.Abs(fy) - 11f);
                float head = Mathf.Max(38f - fx, (Mathf.Abs(fy) - (76f - fx) * .9f) * .74f);
                px[y * w + x] = new Color(1f, 1f, 1f, Edge(Mathf.Min(stem, head), 1.4f));
            }
            t.SetPixels(px); t.Apply(); return t;
        }

        // The high-beam symbol: a lamp shape with straight beams.
        static Texture2D MakeBeam()
        {
            int w = 96, h = 72; var t = New(w, h); var px = new Color[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                float fx = x + .5f, fy = y + .5f - h / 2f;
                // The lamp: a half disc on the right, flat on the left.
                float lamp = Mathf.Max(Mathf.Sqrt((fx - 52f) * (fx - 52f) + fy * fy) - 26f, 52f - fx - 4f);
                float lampRing = Mathf.Abs(lamp) - 4.5f;
                float beams = 99f;
                for (int i = -2; i <= 2; i++) { float d = Mathf.Max(Mathf.Abs(fy - i * 12f) - 2.8f, Mathf.Max(6f - fx, fx - 38f)); beams = Mathf.Min(beams, d); }
                px[y * w + x] = new Color(1f, 1f, 1f, Edge(Mathf.Min(lampRing, beams), 1.4f));
            }
            t.SetPixels(px); t.Apply(); return t;
        }
    }
}
