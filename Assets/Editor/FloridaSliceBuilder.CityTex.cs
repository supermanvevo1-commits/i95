using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SurviveI95.EditorTools
{
    /// <summary>
    /// The city's surfaces, painted in code so nothing is copied: South Florida stucco houses with white-framed windows,
    /// garage doors and the occasional security bars; garden apartments with wall AC units; strip-mall storefronts with
    /// their signs; tilt-up concrete warehouses with roll-up doors; and the roofs: clay barrel tile, flat concrete tile,
    /// shingle and white flat roofs, all streaked with the black mildew every Florida roof gets.
    /// Each wall texture is an atlas of rows (one color per row), so one material covers every color.
    /// </summary>
    public static partial class FloridaSliceBuilder
    {
        // ---------- a small painting canvas (origin bottom left; x wraps, so atlases tile across) ----------
        class Canvas
        {
            public readonly int W, H; public readonly Color[] px;
            public Canvas(int w, int h, Color fill) { W = w; H = h; px = new Color[w * h]; for (int i = 0; i < px.Length; i++) px[i] = fill; }
            public void Rect(float x0, float y0, float x1, float y1, Color c, float a = 1f)
            {
                int ya = Mathf.Max(0, Mathf.RoundToInt(y0)), yb = Mathf.Min(H, Mathf.RoundToInt(y1));
                int xa = Mathf.RoundToInt(x0), xb = Mathf.RoundToInt(x1);
                for (int y = ya; y < yb; y++) for (int x = xa; x < xb; x++)
                {
                    int i = y * W + ((x % W) + W) % W;
                    px[i] = a >= 1f ? c : Color.Lerp(px[i], c, a);
                }
            }
            public void Mul(float x0, float y0, float x1, float y1, float k)
            {
                int ya = Mathf.Max(0, Mathf.RoundToInt(y0)), yb = Mathf.Min(H, Mathf.RoundToInt(y1));
                for (int y = ya; y < yb; y++) for (int x = Mathf.RoundToInt(x0); x < Mathf.RoundToInt(x1); x++)
                {
                    int i = y * W + ((x % W) + W) % W; var c = px[i] * k; c.a = 1f; px[i] = c;
                }
            }
            public void Frame(float x0, float y0, float x1, float y1, float t, Color c)
            {
                Rect(x0, y0, x1, y0 + t, c); Rect(x0, y1 - t, x1, y1, c); Rect(x0, y0, x0 + t, y1, c); Rect(x1 - t, y0, x1, y1, c);
            }
            public Color Get(int x, int y) => px[Mathf.Clamp(y, 0, H - 1) * W + ((x % W) + W) % W];
            public Texture2D Save(string name)
            {
                var tex = new Texture2D(W, H, TextureFormat.RGBA32, true);
                for (int i = 0; i < px.Length; i++) px[i].a = 1f;
                tex.SetPixels(px); tex.Apply();
                return SaveTexture(tex, name);
            }
        }

        // ---------- lettering copied from the font's own glyphs, for shop signs ----------
        class GlyphSheet { public Font font; public float[] a; public int w, h, size; }

        static GlyphSheet Glyphs(Font f, string chars, int size)
        {
            f.RequestCharactersInTexture(chars, size, FontStyle.Normal);
            var src = f.material.mainTexture;
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var t = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false, true);
            t.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0); t.Apply();
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
            var c = t.GetPixels32(); Object.DestroyImmediate(t);
            // A one-channel font texture comes back in alpha or in red depending on the format; use whichever varies.
            long sa = 0, sr = 0; foreach (var p in c) { sa += p.a; sr += p.r; }
            bool useAlpha = sa > 0 && sa < (long)c.Length * 255;
            var g = new GlyphSheet { font = f, w = src.width, h = src.height, size = size, a = new float[c.Length] };
            for (int i = 0; i < c.Length; i++) g.a[i] = (useAlpha ? c[i].a : c[i].r) / 255f;
            return g;
        }

        static float SampleGlyph(GlyphSheet g, Vector2 uv)
        {
            float x = uv.x * g.w - .5f, y = uv.y * g.h - .5f; int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y); float fx = x - x0, fy = y - y0;
            float A(int i, int j) => g.a[Mathf.Clamp(j, 0, g.h - 1) * g.w + Mathf.Clamp(i, 0, g.w - 1)];
            return Mathf.Lerp(Mathf.Lerp(A(x0, y0), A(x0 + 1, y0), fx), Mathf.Lerp(A(x0, y0 + 1), A(x0 + 1, y0 + 1), fx), fy);
        }

        /// <summary>Writes a line of text centered on (cx, cy) with capitals capPx tall, squeezed sideways to fit maxW.</summary>
        static void Letter(Canvas cv, GlyphSheet g, string text, float cx, float cy, float capPx, float maxW, Color col)
        {
            float scale = capPx / (g.size * .7f), adv = 0;
            foreach (var ch in text) if (g.font.GetCharacterInfo(ch, out var ci, g.size)) adv += ci.advance;
            float sx = Mathf.Min(scale, maxW / Mathf.Max(1f, adv)), sy = scale;
            float pen = cx - adv * sx / 2f, baseY = cy - capPx / 2f;
            foreach (var ch in text)
            {
                if (!g.font.GetCharacterInfo(ch, out var ci, g.size)) continue;
                float x0 = pen + ci.minX * sx, x1 = pen + ci.maxX * sx, y0 = baseY + ci.minY * sy, y1 = baseY + ci.maxY * sy;
                for (int y = Mathf.FloorToInt(y0); y < Mathf.CeilToInt(y1); y++) for (int x = Mathf.FloorToInt(x0); x < Mathf.CeilToInt(x1); x++)
                {
                    if (y < 0 || y >= cv.H) continue;
                    float fx = (x + .5f - x0) / (x1 - x0), fy = (y + .5f - y0) / (y1 - y0);
                    if (fx < 0 || fx > 1 || fy < 0 || fy > 1) continue;
                    var uv = Vector2.Lerp(Vector2.Lerp(ci.uvBottomLeft, ci.uvBottomRight, fx), Vector2.Lerp(ci.uvTopLeft, ci.uvTopRight, fx), fy);
                    float a = SampleGlyph(g, uv);
                    if (a > .01f) { int i = y * cv.W + ((x % cv.W) + cv.W) % cv.W; cv.px[i] = Color.Lerp(cv.px[i], col, a); }
                }
                pen += ci.advance * sx;
            }
        }

        // ---------- shared details ----------
        static Color Grain(Color c, int x, int y, int seed, float amount)
        {
            float n = Noise(x / 37f, y / 37f, 4096, 4096, seed) - .5f, f = Hash(x, y, seed + 7) - .5f;
            var o = c * (1f + n * amount + f * amount * .35f); o.a = 1f; return o;
        }

        static void Stucco(Canvas cv, int y0, int y1, Color wall, int seed)
        {
            for (int y = y0; y < y1; y++) for (int x = 0; x < cv.W; x++)
            {
                var c = Grain(wall, x, y, seed, .07f);
                float mil = Noise(x / 90f, y / 18f, 4096, 4096, seed + 3);
                if (mil > .7f) c *= 1f - (mil - .7f) * .25f;
                c.a = 1f; cv.px[y * cv.W + x] = c;
            }
        }

        // A white-framed single-hung window with a reflection of the sky, as on most South Florida block houses.
        static void Window(Canvas cv, float x, float y, float w, float h, float m, System.Random r, bool bars, bool shutter, Color frame)
        {
            cv.Mul(x - m * .06f, y - m * .9f, x + w + m * .06f, y, .93f); // stain under the sill
            cv.Rect(x - m * .07f, y - m * .07f, x + w + m * .07f, y + h + m * .07f, frame);
            if (shutter)
            {
                // Accordion hurricane shutters, closed.
                var sc = new Color(.82f, .8f, .74f);
                for (float sx = x; sx < x + w; sx += m * .1f) { cv.Rect(sx, y, sx + m * .05f, y + h, sc); cv.Rect(sx + m * .05f, y, sx + m * .1f, y + h, sc * .86f); }
                return;
            }
            for (int yy = Mathf.RoundToInt(y); yy < Mathf.RoundToInt(y + h); yy++)
            {
                float k = (yy - y) / h;
                var g = Color.Lerp(new Color(.13f, .17f, .21f), new Color(.42f, .5f, .58f), k * k * .8f + (float)r.NextDouble() * .02f);
                cv.Rect(x, yy, x + w, yy + 1, g);
            }
            cv.Rect(x, y + h * .48f, x + w, y + h * .48f + m * .05f, frame); // the meeting rail
            if (bars) for (float bx = x + m * .08f; bx < x + w; bx += m * .13f) cv.Rect(bx, y, bx + m * .025f, y + h, new Color(.08f, .08f, .08f));
            if (bars) { cv.Rect(x, y + m * .1f, x + w, y + m * .13f, new Color(.08f, .08f, .08f)); cv.Rect(x, y + h - m * .13f, x + w, y + h - m * .1f, new Color(.08f, .08f, .08f)); }
        }

        static readonly Color[] StuccoColors =
        {
            new Color(.93f, .88f, .74f), new Color(.93f, .92f, .88f), new Color(.95f, .78f, .63f), new Color(.91f, .7f, .66f),
            new Color(.72f, .86f, .77f), new Color(.71f, .82f, .88f), new Color(.96f, .9f, .62f), new Color(.84f, .76f, .62f),
        };

        public const int AtlasRows = 8;
        public const float HouseStory = 3.3f, HouseTexW = 26.4f, AptStory = 3.1f, AptTexW = 24.8f, ShopH = 5.6f, ShopTexW = 44.8f, WareH = 9f, WareTexW = 72f;

        /// <summary>Single-family houses: 8 stucco colors, one story per row, 26.4 m of facade across.</summary>
        static Texture2D MakeHouseAtlas()
        {
            const int W = 2048, RH = 256; float m = RH / HouseStory;
            var cv = new Canvas(W, RH * AtlasRows, Color.white);
            for (int row = 0; row < AtlasRows; row++)
            {
                var r = new System.Random(500 + row); int y0 = row * RH;
                var wall = StuccoColors[row];
                Stucco(cv, y0, y0 + RH, wall, row + 40);
                cv.Mul(0, y0, W, y0 + m * .2f, .8f); // rain splash at the foot
                cv.Mul(0, y0 + RH - m * .14f, W, y0 + RH, .82f); // under the eave
                bool bars = row % 3 == 1;
                var frame = row == 1 ? new Color(.97f, .97f, .95f) : new Color(.95f, .95f, .93f);
                float x = m * .6f;
                while (x < W - m * 3.2f)
                {
                    double pick = r.NextDouble(); float w;
                    if (pick < .5) { w = m * (1f + (float)r.NextDouble() * .4f); Window(cv, x, y0 + m * .95f, w, m * 1.35f, m, r, bars, r.NextDouble() < .12, frame); }
                    else if (pick < .62) { w = m * 2.3f; Window(cv, x, y0 + m * .95f, w * .48f, m * 1.35f, m, r, bars, false, frame); Window(cv, x + w * .52f, y0 + m * .95f, w * .48f, m * 1.35f, m, r, bars, false, frame); }
                    else if (pick < .74)
                    {
                        w = m * 1f; var door = new[] { new Color(.45f, .3f, .2f), new Color(.15f, .4f, .42f), new Color(.6f, .15f, .12f), new Color(.92f, .92f, .9f) }[r.Next(4)];
                        cv.Rect(x - m * .08f, y0, x + w + m * .08f, y0 + m * 2.2f, frame); cv.Rect(x, y0, x + w, y0 + m * 2.12f, door);
                        cv.Rect(x + w * .15f, y0 + m * 1.6f, x + w * .85f, y0 + m * 1.95f, new Color(.2f, .25f, .3f));
                        cv.Rect(x + w + m * .25f, y0 + m * 1.7f, x + w + m * .4f, y0 + m * 1.95f, new Color(.2f, .2f, .18f));
                    }
                    else if (pick < .86)
                    {
                        w = m * 2.75f; var gd = new Color(.93f, .92f, .88f);
                        cv.Rect(x, y0, x + w, y0 + m * 2.25f, gd);
                        for (float gy = y0 + m * .53f; gy < y0 + m * 2.2f; gy += m * .54f) cv.Rect(x, gy, x + w, gy + m * .05f, gd * .78f);
                        cv.Mul(x, y0 + m * 2.05f, x + w, y0 + m * 2.25f, .8f);
                    }
                    else
                    {
                        w = m * 2.2f; // sliding glass door
                        cv.Rect(x - m * .06f, y0, x + w + m * .06f, y0 + m * 2.25f, new Color(.78f, .78f, .76f));
                        Window(cv, x, y0 + m * .05f, w * .49f, m * 2.12f, m, r, false, false, new Color(.78f, .78f, .76f));
                        Window(cv, x + w * .51f, y0 + m * .05f, w * .49f, m * 2.12f, m, r, false, false, new Color(.78f, .78f, .76f));
                    }
                    x += w + m * (1.1f + (float)r.NextDouble() * 2.2f);
                }
            }
            return cv.Save("HouseAtlas");
        }

        /// <summary>Garden apartments and condos: one floor per row, window pairs, wall AC units, some with balconies.</summary>
        static Texture2D MakeAptAtlas()
        {
            const int W = 2048, RH = 256; float m = RH / AptStory;
            var cv = new Canvas(W, RH * AtlasRows, Color.white);
            for (int row = 0; row < AtlasRows; row++)
            {
                var r = new System.Random(700 + row); int y0 = row * RH;
                var wall = StuccoColors[(row + 3) % StuccoColors.Length];
                Stucco(cv, y0, y0 + RH, wall, row + 60);
                var trim = row % 2 == 0 ? new Color(.95f, .95f, .93f) : wall * .82f;
                cv.Rect(0, y0, W, y0 + m * .18f, trim);
                bool balconies = row % 4 >= 2;
                float bay = m * 3.2f;
                for (float x = m * .5f; x + bay <= W; x += bay)
                {
                    if (balconies && r.NextDouble() < .6)
                    {
                        float bx = x + m * .2f, bw = bay - m * .4f;
                        cv.Rect(bx, y0 + m * .18f, bx + bw, y0 + m * 2.4f, new Color(.2f, .23f, .26f));
                        Window(cv, bx + m * .15f, y0 + m * .2f, bw - m * .3f, m * 2.1f, m, r, false, false, new Color(.8f, .8f, .78f));
                        cv.Mul(bx, y0 + m * .18f, bx + bw, y0 + m * 1.2f, .75f);
                        for (float rx = bx; rx < bx + bw; rx += m * .12f) cv.Rect(rx, y0 + m * .2f, rx + m * .03f, y0 + m * 1.15f, new Color(.93f, .93f, .9f));
                        cv.Rect(bx, y0 + m * 1.1f, bx + bw, y0 + m * 1.18f, new Color(.95f, .95f, .93f));
                        continue;
                    }
                    bool shut = r.NextDouble() < .08;
                    Window(cv, x + m * .45f, y0 + m * .95f, m * 1.0f, m * 1.4f, m, r, false, shut, new Color(.95f, .95f, .93f));
                    Window(cv, x + m * 1.75f, y0 + m * .95f, m * 1.0f, m * 1.4f, m, r, false, r.NextDouble() < .08, new Color(.95f, .95f, .93f));
                    if (r.NextDouble() < .45)
                    {
                        float ax = x + (r.NextDouble() < .5 ? m * .55f : m * 1.85f);
                        cv.Rect(ax, y0 + m * .45f, ax + m * .7f, y0 + m * .85f, new Color(.78f, .78f, .74f));
                        for (float gx = ax + m * .05f; gx < ax + m * .65f; gx += m * .07f) cv.Rect(gx, y0 + m * .5f, gx + m * .03f, y0 + m * .8f, new Color(.5f, .5f, .48f));
                        cv.Mul(ax + m * .3f, y0 + m * .1f, ax + m * .38f, y0 + m * .45f, .82f); // drip stain
                    }
                }
            }
            return cv.Save("AptAtlas");
        }

        /// <summary>Strip-mall storefronts: 4 front rows of 7 shops each with their signs, and 4 matching plain back rows.</summary>
        public static readonly string[][] ShopWords =
        {
            new[] { "CUBAN CAFE", "BARBER SHOP", "NAILS", "FARMACIA", "COIN LAUNDRY", "PIZZA", "TAX SERVICE" },
            new[] { "LIQUORS", "CHECK CASHING", "DENTAL", "CELL PHONES", "SEGUROS", "BAKERY", "BEAUTY SALON" },
            new[] { "AUTO INSURANCE", "CHINESE FOOD", "SMOKE SHOP", "MEDICAL CENTER", "PAWN", "DISCOUNT", "BOTANICA" },
            new[] { "MERCADO", "WINGS", "FURNITURE", "TATTOO", "TRAVEL & ENVIOS", "MATTRESS", "FOR LEASE" },
        };
        public const float ShopUnit = 6.4f;

        static Texture2D MakeShopAtlas()
        {
            const int W = 2048, RH = 256; float m = RH / ShopH;
            var cv = new Canvas(W, RH * AtlasRows, Color.white);
            var font = AssetDatabase.LoadAssetAtPath<Font>(SignFontFile) ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var g = Glyphs(font, "ABCDEFGHIJKLMNOPQRSTUVWXYZ&0123456789 ", 64);
            var walls = new[] { new Color(.93f, .89f, .78f), new Color(.92f, .92f, .89f), new Color(.86f, .78f, .66f), new Color(.9f, .8f, .72f) };
            var signBacks = new[] { new Color(.75f, .1f, .1f), new Color(.1f, .25f, .6f), new Color(.98f, .85f, .2f), new Color(.95f, .95f, .93f), new Color(.08f, .45f, .3f), new Color(.1f, .1f, .12f) };
            for (int set = 0; set < 4; set++)
            {
                var wall = walls[set];
                var r = new System.Random(900 + set);
                // Front.
                int y0 = set * RH;
                Stucco(cv, y0, y0 + RH, wall, set + 80);
                var trim = new[] { new Color(.32f, .45f, .5f), new Color(.55f, .25f, .2f), new Color(.25f, .3f, .4f), new Color(.85f, .85f, .82f) }[set];
                cv.Rect(0, y0 + m * 5.25f, W, y0 + m * 5.6f, trim); // the cornice
                cv.Mul(0, y0 + m * 3.05f, W, y0 + m * 3.4f, .7f); // the canopy's shadow line
                var mull = set % 2 == 0 ? new Color(.72f, .73f, .72f) : new Color(.3f, .24f, .18f);
                for (int u = 0; u < 7; u++)
                {
                    float x0 = u * m * ShopUnit, x1 = x0 + m * ShopUnit;
                    cv.Rect(x0, y0, x0 + m * .35f, y0 + m * 5.25f, wall * .93f); // pilaster between shops
                    float gx0 = x0 + m * .5f, gx1 = x1 - m * .15f;
                    // Storefront glass with posters and a door.
                    for (int yy = Mathf.RoundToInt(y0 + m * .45f); yy < y0 + m * 3f; yy++)
                    {
                        float k = (yy - y0 - m * .45f) / (m * 2.55f);
                        cv.Rect(gx0, yy, gx1, yy + 1, Color.Lerp(new Color(.1f, .12f, .14f), new Color(.38f, .44f, .5f), k * k));
                    }
                    cv.Rect(gx0, y0, gx1, y0 + m * .45f, mull * .9f); // kick plate
                    for (float mx = gx0; mx <= gx1; mx += (gx1 - gx0) / 4f) cv.Rect(mx - m * .04f, y0, mx + m * .04f, y0 + m * 3f, mull);
                    cv.Rect(gx0, y0 + m * 2.95f, gx1, y0 + m * 3.05f, mull);
                    float door = gx0 + (gx1 - gx0) * (r.NextDouble() < .5 ? .25f : .5f);
                    cv.Rect(door + m * .05f, y0 + m * .05f, door + (gx1 - gx0) / 4f - m * .05f, y0 + m * 2.2f, new Color(.08f, .09f, .1f));
                    cv.Rect(door + m * .05f, y0 + m * 2.2f, door + (gx1 - gx0) / 4f - m * .05f, y0 + m * 2.28f, mull);
                    for (int pz = 0; pz < 2; pz++)
                    {
                        float px = gx0 + (gx1 - gx0) * (.62f + pz * .17f), pw = m * (.4f + (float)r.NextDouble() * .3f);
                        var pc = Color.HSVToRGB((float)r.NextDouble(), .6f, .8f);
                        cv.Rect(px, y0 + m * 1.3f, px + pw, y0 + m * 1.3f + pw * 1.3f, pc);
                        cv.Rect(px + pw * .1f, y0 + m * 1.3f + pw * .9f, px + pw * .9f, y0 + m * 1.3f + pw * 1.15f, Color.white * .95f);
                    }
                    // The sign.
                    string word = ShopWords[set][u];
                    float sx0 = x0 + m * .9f, sx1 = x1 - m * .55f, sy0 = y0 + m * 3.6f, sy1 = y0 + m * 4.85f;
                    int style = (set * 3 + u) % 4;
                    if (word == "FOR LEASE")
                    {
                        cv.Rect(sx0 + m, sy0 + m * .1f, sx1 - m, sy1 - m * .1f, Color.white);
                        cv.Frame(sx0 + m, sy0 + m * .1f, sx1 - m, sy1 - m * .1f, m * .06f, new Color(.7f, .1f, .1f));
                        Letter(cv, g, word, (sx0 + sx1) / 2f, (sy0 + sy1) / 2f, m * .45f, sx1 - sx0 - m * 2.4f, new Color(.7f, .1f, .1f));
                        // Blank papered-over glass: the unit is empty.
                        cv.Rect(gx0 + m * .1f, y0 + m * .5f, gx1 - m * .1f, y0 + m * 2.9f, new Color(.78f, .74f, .64f), .85f);
                        continue;
                    }
                    if (style == 0 || style == 1)
                    {
                        var back = signBacks[(set * 2 + u) % signBacks.Length];
                        var ink = back.grayscale > .6f ? new Color(.7f, .08f, .08f) : Color.white;
                        if (back.r > .9f && back.g > .8f && back.b < .4f) ink = new Color(.1f, .1f, .12f);
                        cv.Rect(sx0, sy0, sx1, sy1, back);
                        cv.Frame(sx0, sy0, sx1, sy1, m * .05f, back * .7f);
                        Letter(cv, g, word, (sx0 + sx1) / 2f, (sy0 + sy1) / 2f, m * .6f, sx1 - sx0 - m * .5f, ink);
                    }
                    else
                    {
                        // Channel letters straight on the stucco, with a little shadow.
                        var ink = new[] { new Color(.75f, .08f, .08f), new Color(.08f, .2f, .55f), new Color(.1f, .1f, .1f), new Color(.05f, .45f, .35f) }[(set + u) % 4];
                        Letter(cv, g, word, (sx0 + sx1) / 2f + m * .05f, (sy0 + sy1) / 2f - m * .06f, m * .7f, sx1 - sx0, wall * .65f);
                        Letter(cv, g, word, (sx0 + sx1) / 2f, (sy0 + sy1) / 2f, m * .7f, sx1 - sx0, ink);
                    }
                }
                // Back: same stucco, steel service doors, meters and a few stains.
                int b0 = (set + 4) * RH;
                Stucco(cv, b0, b0 + RH, wall, set + 90);
                cv.Rect(0, b0 + m * 5.25f, W, b0 + m * 5.6f, trim);
                cv.Mul(0, b0, W, b0 + m * .25f, .8f);
                for (float x = m * 3f; x < W - m * 3f; x += m * (6f + (float)r.NextDouble() * 6f))
                {
                    cv.Rect(x, b0, x + m * 1f, b0 + m * 2.15f, new Color(.6f, .6f, .58f));
                    cv.Frame(x, b0, x + m * 1f, b0 + m * 2.15f, m * .05f, new Color(.4f, .4f, .38f));
                    cv.Rect(x + m * 1.4f, b0 + m * 1.2f, x + m * 1.7f, b0 + m * 1.6f, new Color(.55f, .56f, .55f));
                    cv.Mul(x - m * .2f, b0 + m * 2.2f, x + m * 1.2f, b0 + m * 5.2f, .95f);
                }
            }
            return cv.Save("ShopAtlas");
        }

        /// <summary>Tilt-up concrete warehouses: panel joints every 7.6 m; rows 0-3 have the office end, 4-7 roll-up doors.</summary>
        static Texture2D MakeWareAtlas()
        {
            const int W = 2048, RH = 256; float m = RH / WareH;
            var cv = new Canvas(W, RH * AtlasRows, Color.white);
            var paints = new[] { new Color(.88f, .87f, .83f), new Color(.8f, .8f, .78f), new Color(.85f, .79f, .68f), new Color(.78f, .82f, .84f) };
            var stripes = new[] { new Color(.15f, .35f, .6f), new Color(.1f, .45f, .45f), new Color(.55f, .2f, .15f), new Color(.3f, .3f, .32f) };
            for (int row = 0; row < AtlasRows; row++)
            {
                var r = new System.Random(1100 + row); int y0 = row * RH; int c = row % 4; bool docks = row >= 4;
                Stucco(cv, y0, y0 + RH, paints[c], row + 120);
                if (row % 2 == 0) cv.Rect(0, y0 + m * 6.9f, W, y0 + m * 7.3f, stripes[c]);
                for (float x = 0; x < W; x += m * 7.6f) { cv.Rect(x, y0, x + m * .05f, y0 + RH, paints[c] * .7f); cv.Mul(x + m * .05f, y0, x + m * .12f, y0 + RH, 1.06f); }
                cv.Mul(0, y0, W, y0 + m * .3f, .82f);
                for (float x = 0; x < W; x += m * 7.6f)
                {
                    if (docks)
                    {
                        float dx = x + m * 1.9f;
                        cv.Rect(dx, y0 + m * 1.2f, dx + m * 3.7f, y0 + m * 5.5f, new Color(.72f, .73f, .72f));
                        for (float gy = y0 + m * 1.2f; gy < y0 + m * 5.5f; gy += m * .12f) cv.Rect(dx, gy, dx + m * 3.7f, gy + m * .03f, new Color(.6f, .61f, .6f));
                        cv.Rect(dx - m * .3f, y0 + m * .9f, dx, y0 + m * 1.6f, new Color(.08f, .08f, .08f));
                        cv.Rect(dx + m * 3.7f, y0 + m * .9f, dx + m * 4f, y0 + m * 1.6f, new Color(.08f, .08f, .08f));
                        cv.Mul(dx - m * .5f, y0, dx + m * 4.2f, y0 + m * 1.2f, .78f);
                        cv.Mul(dx + m * 1.5f, y0 + m * 5.5f, dx + m * 1.7f, y0 + m * 8.5f, .93f);
                    }
                    else if (r.NextDouble() < .35)
                    {
                        Window(cv, x + m * 1.2f, y0 + m * 3.6f, m * 5f, m * 1.2f, m, r, false, false, new Color(.4f, .42f, .44f));
                        Window(cv, x + m * 2.8f, y0 + m * .05f, m * 1.8f, m * 2.4f, m, r, false, false, new Color(.4f, .42f, .44f));
                    }
                }
            }
            return cv.Save("WareAtlas");
        }

        // ---------- roofs: 512 px for 2 m by 2 m ----------
        static void Mildew(Canvas cv, int seed, float amount)
        {
            for (int y = 0; y < cv.H; y++) for (int x = 0; x < cv.W; x++)
            {
                float n = Noise(x / 64f, y / 26f, 8, 20, seed) * .7f + Noise(x / 16f, y / 9f, 32, 57, seed + 1) * .3f;
                if (n > .5f) { var c = cv.px[y * cv.W + x] * (1f - (n - .5f) * amount); c.a = 1f; cv.px[y * cv.W + x] = c; }
            }
        }

        static Texture2D MakeBarrelTile()
        {
            const int S = 512, cols = 8, courses = 5; float cw = S / (float)cols, ch = S / (float)courses;
            var cv = new Canvas(S, S, Color.white);
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
            {
                int col = (int)(x / cw), course = (int)(y / ch); float fx = x / cw - col, fy = y / ch - course;
                bool cap = col % 2 == 0;
                float tint = Hash(col, course, 31);
                var baseC = Color.Lerp(new Color(.72f, .36f, .22f), new Color(.82f, .5f, .3f), tint);
                if (Hash(col, course, 77) < .12f) baseC = new Color(.58f, .3f, .2f);
                float shade = cap ? .72f + .42f * Mathf.Sin(fx * Mathf.PI) : .55f + .2f * Mathf.Sin(fx * Mathf.PI);
                if (fy > .9f) shade *= Mathf.Lerp(1f, .55f, (fy - .9f) / .1f); // the lip of the tile above
                var c = Grain(baseC * shade, x, y, 5, .08f);
                cv.px[y * S + x] = c;
            }
            Mildew(cv, 11, .9f);
            return cv.Save("RoofBarrelTile");
        }

        static Texture2D MakeFlatTile()
        {
            const int S = 512, cols = 6, courses = 6; float cw = S / (float)cols, ch = S / (float)courses;
            var cv = new Canvas(S, S, Color.white);
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
            {
                int course = (int)(y / ch); float off = course % 2 == 0 ? 0f : cw / 2f;
                int col = (int)((x + off) / cw); float fx = (x + off) / cw - col, fy = y / ch - course;
                var baseC = new Color(.66f, .66f, .64f) * (.93f + Hash(col, course, 41) * .12f);
                float shade = fy > .88f ? .6f : fx < .02f ? .8f : 1f;
                cv.px[y * S + x] = Grain(baseC * shade, x, y, 9, .07f);
            }
            Mildew(cv, 13, 1.1f);
            return cv.Save("RoofFlatTile");
        }

        static Texture2D MakeShingle()
        {
            const int S = 512, courses = 14; float ch = S / (float)courses;
            var cv = new Canvas(S, S, Color.white);
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
            {
                int course = (int)(y / ch); float fy = y / ch - course;
                float tab = S / 6f, off = Hash(course, 0, 3) * tab; int t = (int)((x + off) / tab);
                var baseC = new Color(.36f, .33f, .3f) * (.85f + Hash(t, course, 17) * .3f);
                float shade = fy < .12f ? .55f : 1f;
                if (((x + off) % tab) < 2f) shade *= .7f;
                cv.px[y * S + x] = Grain(baseC * shade, x, y, 15, .2f);
            }
            Mildew(cv, 19, .5f);
            return cv.Save("RoofShingle");
        }

        static Texture2D MakeMembrane()
        {
            const int S = 512;
            var cv = new Canvas(S, S, Color.white);
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
            {
                var c = Grain(new Color(.8f, .8f, .78f), x, y, 23, .1f);
                float stain = Noise(x / 128f, y / 128f, 4, 4, 29);
                if (stain > .55f) c *= 1f - (stain - .55f) * .5f;
                if (y % 256 < 3) c *= .85f; // seam
                c.a = 1f; cv.px[y * S + x] = c;
            }
            return cv.Save("RoofMembrane");
        }
    }
}
