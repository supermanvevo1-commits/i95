using System;
using System.Collections.Generic;
using System.Linq;
using SurviveI95.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SurviveI95.EditorTools
{
    /// <summary>
    /// Billboards as they stand along I-95: a "bulletin" face 48 by 14 ft (14.6 by 4.3 m) on a single steel pole with
    /// a catwalk, the bottom about 9 m up, angled toward the traffic it faces. The ads are painted here: Florida's
    /// roadside tropes with made-up names (the injury lawyer, the gator attraction, fireworks, the early-bird buffet).
    /// Placed after the city, on open ground, so none stands hidden behind a building.
    /// </summary>
    public static partial class FloridaSliceBuilder
    {
        class Ad { public string head, sub, small; public Color back, ink, accent; public int art; }

        static readonly Ad[] Ads =
        {
            new Ad { head = "HURT ON I-95?", sub = "CALL BIG RICK", small = "1-800-BIG-RICK  ·  WE FIGHT. YOU HEAL.", back = new Color(.05f, .12f, .32f), ink = new Color(1f, .82f, .1f), accent = Color.white, art = 1 },
            new Ad { head = "GATOR WORLD", sub = "LIVE FEEDINGS DAILY", small = "NEXT EXIT  ·  KIDS FREE", back = new Color(.1f, .42f, .2f), ink = Color.white, accent = new Color(1f, .85f, .2f), art = 2 },
            new Ad { head = "FIREWORKS", sub = "BUY 1 GET 6 FREE", small = "OPEN 24 HRS  ·  EXIT 12", back = new Color(.78f, .08f, .08f), ink = new Color(1f, .9f, .2f), accent = Color.white, art = 3 },
            new Ad { head = "EARLY BIRD BUFFET", sub = "3:45 PM  ·  $11.99", small = "SENIOR NIGHT EVERY NIGHT", back = new Color(.98f, .86f, .7f), ink = new Color(.6f, .15f, .1f), accent = new Color(.15f, .45f, .5f), art = 4 },
            new Ad { head = "CAFECITO 99¢", sub = "VENTANITA OPEN 5 AM", small = "PASTELITOS  ·  CROQUETAS  ·  NEXT EXIT", back = new Color(.36f, .2f, .12f), ink = new Color(.98f, .92f, .8f), accent = new Color(.95f, .6f, .2f), art = 5 },
            new Ad { head = "LUXURY CONDOS", sub = "FROM THE LOW $900s", small = "THE PALMS AT BISCAYNE  ·  NOW SELLING", back = new Color(.92f, .93f, .94f), ink = new Color(.1f, .12f, .15f), accent = new Color(.75f, .6f, .3f), art = 6 },
            new Ad { head = "WE BUY HOUSES", sub = "CA$H  ·  ANY CONDITION", small = "305-555-0199", back = new Color(.98f, .9f, .1f), ink = new Color(.08f, .08f, .08f), accent = new Color(.8f, .1f, .1f), art = 0 },
            new Ad { head = "TOLL BY PLATE", sub = "YOU ALREADY OWE US", small = "PAY ONLINE BEFORE THE LATE FEE DOES", back = new Color(.1f, .4f, .62f), ink = Color.white, accent = new Color(1f, .8f, .2f), art = 0 },
            new Ad { head = "SPEED KILLS.", sub = "SO DO THE FINES.", small = "FLORIDA HIGHWAY PATROL  ·  ARRIVE ALIVE", back = new Color(.08f, .08f, .08f), ink = Color.white, accent = new Color(.9f, .1f, .1f), art = 0 },
        };

        static Texture2D PaintAd(Ad ad, int index)
        {
            const int W = 1536, H = 450; // 14.6 m by 4.3 m
            var cv = new Canvas(W, H, ad.back);
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) cv.px[y * W + x] = Grain(ad.back * (1f - y / (float)H * .08f), x, y, 400 + index, .03f);
            var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/ThirdParty/Fonts/BarlowSemiCondensed-Bold.ttf") ?? AssetDatabase.LoadAssetAtPath<Font>(SignFontFile);
            var g = Glyphs(font, ad.head + ad.sub + ad.small + "0123456789$¢·", 96);
            float textX = ad.art > 0 ? W * .6f : W * .5f, textW = ad.art > 0 ? W * .74f : W * .92f;
            // Simple artwork on the left: shapes only, read from 300 m away.
            float ax = W * .14f, ay = H * .5f;
            switch (ad.art)
            {
                case 1: // a man in a suit, pointing
                    cv.Rect(ax - 70, 0, ax + 70, ay + 20, new Color(.15f, .15f, .18f));
                    cv.Rect(ax - 12, ay - 60, ax + 12, ay + 20, Color.white); cv.Rect(ax - 6, ay - 50, ax + 6, ay + 10, ad.ink);
                    Disc(cv, ax, ay + 75, 55, new Color(.85f, .66f, .52f)); cv.Rect(ax - 55, ay + 105, ax + 55, ay + 132, new Color(.2f, .15f, .1f));
                    cv.Rect(ax + 60, ay - 10, ax + 230, ay + 18, new Color(.15f, .15f, .18f)); Disc(cv, ax + 240, ay + 4, 18, new Color(.85f, .66f, .52f));
                    break;
                case 2: // a gator
                    for (int x = -170; x < 190; x++) { float body = 38f * Mathf.Sin(Mathf.PI * (x + 170) / 360f) * (x > 100 ? .6f : 1f); cv.Rect(ax + x, ay - body * .55f, ax + x + 1, ay + body * .45f, new Color(.25f, .35f, .15f)); }
                    for (int k = 0; k < 9; k++) cv.Rect(ax - 120 + k * 30, ay + 14, ax - 108 + k * 30, ay + 24, new Color(.2f, .28f, .12f));
                    cv.Rect(ax + 120, ay - 6, ax + 195, ay + 2, Color.white); Disc(cv, ax + 110, ay + 18, 6, Color.white);
                    foreach (int lx in new[] { -90, -30, 40, 90 }) cv.Rect(ax + lx, ay - 45, ax + lx + 14, ay - 15, new Color(.25f, .35f, .15f));
                    break;
                case 3: // a burst
                    for (int k = 0; k < 16; k++) { float a = k * Mathf.PI / 8f; for (int r = 20; r < 150; r++) Disc(cv, ax + Mathf.Cos(a) * r, ay + Mathf.Sin(a) * r, 4 + r * .03f, k % 2 == 0 ? ad.ink : ad.accent); }
                    Disc(cv, ax, ay, 30, Color.white);
                    break;
                case 4: // a plate with a fork and knife
                    Disc(cv, ax, ay, 120, Color.white); Disc(cv, ax, ay, 90, new Color(.95f, .95f, .93f));
                    cv.Rect(ax - 170, ay - 100, ax - 155, ay + 100, ad.accent); cv.Rect(ax + 155, ay - 100, ax + 170, ay + 100, ad.accent);
                    break;
                case 5: // a coffee cup with steam
                    cv.Rect(ax - 70, ay - 90, ax + 70, ay + 40, Color.white); cv.Rect(ax - 85, ay - 100, ax + 85, ay - 90, Color.white);
                    cv.Rect(ax + 70, ay - 40, ax + 100, ay + 10, Color.white);
                    for (int k = 0; k < 3; k++) for (int y = 0; y < 90; y++) Disc(cv, ax - 40 + k * 40 + Mathf.Sin(y * .1f + k) * 10, ay + 60 + y, 5, ad.accent);
                    break;
                case 6: // a tower at sunset
                    cv.Rect(ax - 200, 0, ax + 200, H, new Color(.98f, .7f, .45f)); Disc(cv, ax + 60, ay + 40, 70, new Color(1f, .9f, .6f));
                    cv.Rect(ax - 60, 0, ax + 20, H * .92f, new Color(.2f, .25f, .32f));
                    for (int y = 20; y < H * .9f; y += 22) cv.Rect(ax - 55, y, ax + 15, y + 10, new Color(.5f, .6f, .7f));
                    cv.Rect(ax - 200, 0, ax + 200, 30, new Color(.1f, .4f, .5f));
                    break;
            }
            cv.Rect(0, 0, W, 10, ad.accent); cv.Rect(0, H - 10, W, H, ad.accent);
            Letter(cv, g, ad.head, textX, H * .7f, H * .26f, textW, ad.ink);
            Letter(cv, g, ad.sub, textX, H * .38f, H * .15f, textW, ad.accent);
            Letter(cv, g, ad.small, textX, H * .13f, H * .075f, textW, ad.ink);
            return cv.Save("Ad_" + index);
        }

        static void Disc(Canvas cv, float cx, float cy, float r, Color c)
        {
            for (int y = Mathf.FloorToInt(cy - r); y <= Mathf.CeilToInt(cy + r); y++) for (int x = Mathf.FloorToInt(cx - r); x <= Mathf.CeilToInt(cx + r); x++)
            {
                if (y < 0 || y >= cv.H || x < 0 || x >= cv.W) continue;
                if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r) cv.px[y * cv.W + x] = c;
            }
        }

        static void BuildBillboards(RoutePath p, Func<float, float> south)
        {
            var old = GameObject.Find("Billboards"); if (old != null) UnityEngine.Object.DestroyImmediate(old);
            var root = new GameObject("Billboards");
            var steel = Lit("BillboardSteel", new Color(.5f, .51f, .52f), .3f); steel.SetFloat("_Metallic", .4f);
            var faces = Ads.Select((ad, i) => { var mt = Lit("AdFace_" + i, Color.white, .25f); mt.SetTexture("_BaseMap", PaintAd(ad, i)); return mt; }).ToArray();
            int placed = 0;
            for (int i = 0; i < 14; i++)
            {
                bool north = i % 3 != 2; // most face northbound traffic, some the southbound
                float at = 900f + i * 3300f * .82f;
                for (int tries = 0; tries < 30; tries++, at += 70f)
                {
                    if (at > p.Length - 300f) break;
                    if (p.Junctions.Any(j => at > j.divergeS - 900f && at < j.mergeS + 60f)) continue; // clear of exit signs
                    if (p.SignBridges().Any(b => Mathf.Abs(b - at) < 250f) || p.HasSoundWall(at)) continue;
                    float lat = north ? p.HalfWidth(at) + 24f : south(at) - p.HalfWidth(at) - 24f;
                    var foot = p.Point(at, lat); var q = new Vector2(foot.x, foot.z);
                    if (InBuilding(q, 9f) || StreetDistance(q, 9f, out _, out var sw) < sw / 2f + 2f || NearExit(q, 15f) || InWater(q)) continue;
                    // Nothing tall in the 150 m in front of it, so drivers can actually see it.
                    var toward = (p.Point(at - (north ? 150f : -150f), lat * .3f) - foot); bool blocked = false;
                    for (float k = .15f; k <= 1f; k += .15f) { var c = foot + toward * k; if (InBuilding(new Vector2(c.x, c.z), 0f)) { blocked = true; break; } }
                    if (blocked) continue;
                    var face = Quaternion.LookRotation(-(p.Tangent(at) * (north ? 1f : -1f)) + p.Right(at) * (north ? -.35f : .35f), Vector3.up);
                    var g = new GameObject("Billboard"); g.transform.SetParent(root.transform);
                    g.transform.SetPositionAndRotation(new Vector3(foot.x, -.25f, foot.z), face);
                    var ad = faces[placed % faces.Length];
                    var v = new List<Vector3>(); var t = new List<int>(); var uv = new List<Vector2>();
                    Cone(v, t, Vector3.zero, .55f, .45f, 9.2f, 10);
                    Cuboid(v, uv, t, new Vector3(0, 9.1f, -.3f), Quaternion.identity, new Vector3(13f, .5f, .5f));              // the cross beam
                    Cuboid(v, uv, t, new Vector3(0, 11.2f, -.45f), Quaternion.identity, new Vector3(14.8f, 4.5f, .25f));          // the back of the face
                    Cuboid(v, uv, t, new Vector3(0, 8.95f, .35f), Quaternion.identity, new Vector3(14.6f, .08f, 1f));             // the catwalk
                    for (float x = -6f; x <= 6f; x += 3f) Cuboid(v, uv, t, new Vector3(x, 12.6f, .5f), Quaternion.Euler(30, 0, 0), new Vector3(.08f, .08f, 1.2f)); // light arms
                    while (uv.Count < v.Count) uv.Add(Vector2.zero);
                    Add(g, "Structure", Finish(v, uv, t), steel, ShadowCastingMode.On);
                    var fv = new List<Vector3> { new Vector3(-7.3f, 9.05f, -.31f), new Vector3(-7.3f, 13.35f, -.31f), new Vector3(7.3f, 9.05f, -.31f), new Vector3(7.3f, 13.35f, -.31f) };
                    // The face looks along +z of the billboard (toward traffic), so the quad's front is +z.
                    for (int k = 0; k < 4; k++) fv[k] = new Vector3(fv[k].x, fv[k].y, .32f - .63f);
                    var fm = new Mesh(); fm.SetVertices(fv); fm.SetUVs(0, new List<Vector2> { new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 0), new Vector2(0, 1) });
                    fm.SetTriangles(new[] { 0, 2, 1, 2, 3, 1 }, 0); fm.RecalculateNormals(); fm.RecalculateBounds();
                    if (Vector3.Dot(fm.normals[0], Vector3.forward) < 0) { fm.SetTriangles(new[] { 0, 1, 2, 2, 1, 3 }, 0); fm.RecalculateNormals(); }
                    Add(g, "Face", fm, ad, ShadowCastingMode.Off);
                    foreach (Transform ch in g.transform) { ch.localPosition = Vector3.zero; ch.localRotation = Quaternion.identity; ch.localScale = Vector3.one; }
                    Solid(at, lat, .6f, .6f, "billboard");
                    placed++;
                    break;
                }
            }
            Debug.Log($"[FloridaSlice] billboards {placed}");
        }
    }
}
