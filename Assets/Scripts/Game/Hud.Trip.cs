using UnityEngine;

namespace SurviveI95.Game
{
    /// <summary>The trip on screen: a strip along the top with the clock, when you're expected, your money and the gas
    /// gauge; the partner's lines as subtitles; and the arrival card.</summary>
    public partial class Hud
    {
        static readonly Color SubtitleName = new Color(1f, .78f, .45f), FuelLow = new Color(1f, .3f, .2f);

        void DrawTrip()
        {
            var trip = car.Trip;
            if (trip == null || trip.Plan == null) return;
            float s = scale;

            // The strip: CLOCK · DUE · MONEY · GAS, centered at the top like a phone's status bar.
            if (!trip.Arrived)
            {
                string clock = trip.Now, due = "DUE " + trip.Due, money = "$" + trip.Money.ToString("0");
                float cw = small.CalcSize(new GUIContent(clock)).x, dw = small.CalcSize(new GUIContent(due)).x, mw = small.CalcSize(new GUIContent(money)).x;
                float gauge = 110f * s, gap = 22f * s, pad = 16f * s;
                float w = pad * 2f + cw + dw + mw + gauge + 38f * s + gap * 3f, h = 36f * s;
                var r = new Rect((Screen.width - w) / 2f, 14f * s, w, h);
                Panel(r, ChipColor, 9f * s);
                float x = r.x + pad;
                Text(new Rect(x, r.y, cw, h), clock, small, Ink); x += cw + gap;
                bool late = trip.MinutesLate > 0f;
                Text(new Rect(x, r.y, dw, h), due, small, late ? Accent : Dim); x += dw + gap;
                Text(new Rect(x, r.y, mw, h), money, small, CruiseGreen); x += mw + gap;
                Text(new Rect(x, r.y, 34f * s, h), "GAS", tiny, Dim, false); x += 38f * s;
                float f = trip.FuelFraction;
                var keep = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, .15f); GUI.DrawTexture(new Rect(x, r.y + 14f * s, gauge, 8f * s), white);
                GUI.color = f < .125f ? FuelLow : Ink; GUI.DrawTexture(new Rect(x, r.y + 14f * s, gauge * f, 8f * s), white);
                GUI.color = keep;
            }

            // The partner's line, as a subtitle above the bottom messages.
            if (trip.Line != null && Time.time < trip.LineUntil)
            {
                string who = trip.Plan.partner.ToUpper() + ":  ";
                var st = new GUIStyle(label) { wordWrap = false };
                float ww = st.CalcSize(new GUIContent(who)).x, lw = st.CalcSize(new GUIContent(trip.Line)).x;
                float total = ww + lw, y = Screen.height - (trip.Arrived ? 70f : 118f) * s, x0 = (Screen.width - total) / 2f;
                var shade = GUI.color; GUI.color = new Color(0f, 0f, 0f, .45f);
                GUI.DrawTexture(new Rect(x0 - 60f * s, y - 10f * s, total + 120f * s, 54f * s), soft); GUI.color = shade;
                Text(new Rect(x0, y, ww, 34f * s), who, st, SubtitleName);
                Text(new Rect(x0 + ww, y, lw, 34f * s), trip.Line, st, Ink);
            }

            if (trip.Arrived) DrawArrival(trip);
        }

        void DrawArrival(Trip trip)
        {
            float s = scale, w = 620f * s, h = 470f * s;
            var r = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f - 30f * s, w, h);
            Panel(r, new Color(.05f, .06f, .07f, .88f), 14f * s);
            Text(new Rect(r.x, r.y + 22f * s, w, 22f * s), Spaced("ARRIVED"), small, Accent, false);
            Text(new Rect(r.x, r.y + 46f * s, w, 56f * s), trip.Plan.to, new GUIStyle(big) { fontSize = Mathf.RoundToInt(44 * s) }, Ink, false);
            Text(new Rect(r.x, r.y + 100f * s, w, 24f * s), trip.Plan.place + (string.IsNullOrEmpty(trip.Plan.where) ? "" : "  ·  " + trip.Plan.where), tiny, Dim, false);

            float late = trip.MinutesLate;
            string verdict = late < -2f ? $"{-late:0} MIN EARLY" : late <= 2f ? "ON TIME" : $"{late:0} MIN LATE";
            Color vc = late <= 2f ? CruiseGreen : Accent;
            Text(new Rect(r.x, r.y + 132f * s, w, 40f * s), verdict, label, vc, false);

            float gasCost = trip.GallonsUsed * trip.Plan.gasPrice;
            trip.Ticket(out float fine);
            string avg = trip.ArriveMinutes > 0f ? (trip.Miles / (trip.ArriveMinutes / 60f)).ToString("0") : "0";
            var rows = new[]
            {
                ("Arrived", $"{trip.Clock(trip.ArriveMinutes)}  (expected {trip.Due})"),
                ("Drive", $"{Mathf.FloorToInt(trip.ArriveMinutes)} min {Mathf.FloorToInt(trip.ArriveMinutes * 60f % 60f):00} s  ·  {trip.Miles:0.0} mi  ·  avg {avg} mph  ·  top {trip.TopMph:0}"),
                ("Crashes", trip.Crashes == 0 ? "None" : trip.Crashes.ToString() + (trip.Missed ? "  ·  missed the exit" : "")),
                ("Gas", $"{trip.GallonsUsed:0.00} gal  ·  ${gasCost:0.00} at ${trip.Plan.gasPrice:0.00}"),
                ("If a trooper had clocked you", trip.Ticket(out _)),
                ("Money left", $"${trip.Money:0.00}" + (fine <= 0f ? "" : trip.Money - fine >= 0f ? $"   (${trip.Money - fine:0.00} after that ticket)" : $"   (that ticket: ${fine - trip.Money:0.00} short)")),
            };
            if (trip.Crashes == 0 && trip.Missed) rows[2] = ("Crashes", "None  ·  missed the exit");
            var key = new GUIStyle(tiny) { alignment = TextAnchor.MiddleLeft };
            var val = new GUIStyle(tiny) { alignment = TextAnchor.MiddleRight, font = car.hudFontBold };
            float y = r.y + 186f * s;
            foreach (var (k, v) in rows)
            {
                var keep = GUI.color; GUI.color = new Color(1f, 1f, 1f, .07f);
                GUI.DrawTexture(new Rect(r.x + 30f * s, y + 33f * s, w - 60f * s, 1f * s), white); GUI.color = keep;
                Text(new Rect(r.x + 30f * s, y, w - 60f * s, 32f * s), k, key, Dim, false);
                Text(new Rect(r.x + 30f * s, y, w - 60f * s, 32f * s), v, val, Ink, false);
                y += 38f * s;
            }
            Text(new Rect(r.x, r.yMax - 46f * s, w, 30f * s), "ENTER  ·  DRIVE IT AGAIN", tiny, Dim, false);
        }
    }
}
