using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SurviveI95.World;
using UnityEditor;
using UnityEngine;

namespace SurviveI95.EditorTools
{
    /// <summary>
    /// The real city beside I-95. Every footprint comes from OpenStreetMap; what it becomes depends on what the map says it
    /// is, the zone it stands in (homes, shops, industry) and its size: a low stucco house with a hip roof of clay barrel
    /// tile and a driveway to its street, a garden apartment block, a strip mall with storefronts and signs facing its
    /// parking lot, a tilt-up warehouse with roll-up doors, an office, a glass tower. Flat roofs carry AC units, as they
    /// do all over Miami. Past the mapped band the city carries on to the horizon as plain low rooftops.
    /// </summary>
    public static partial class FloridaSliceBuilder
    {
        enum BKind { House, Apartment, Shop, Warehouse, Office, Tower, Civic, Shed }

        const int CHouse = 0, CApt = 1, CShop = 2, CWare = 3, CFac = 4, CBarrel = 10, CFlatTile = 11, CShingle = 12, CMembrane = 13,
            CMetal = 14, CPave = 15, CTrim = 16, CLot = 17, CitySlots = 18;

        [Serializable] class ZoneRow { public string k; public float w; public float[] p; }
        [Serializable] class ShopRow { public string k; public float[] p; }
        [Serializable] class LandFile { public ZoneRow[] zones; public ZoneRow[] ways; public ShopRow[] shops; }

        class Zone { public string k; public List<Vector2> pts; public Vector2 min, max; public float area; }
        static List<Zone> zones = new List<Zone>();
        static Dictionary<(int, int), List<Zone>> zoneGrid = new Dictionary<(int, int), List<Zone>>();
        static Dictionary<(int, int), List<ShopRow>> shopGrid = new Dictionary<(int, int), List<ShopRow>>();
        static Dictionary<(int, int), List<(Vector2 a, Vector2 b, float w)>> streetGrid = new Dictionary<(int, int), List<(Vector2, Vector2, float)>>();
        static List<List<Vector2>> osmLots = new List<List<Vector2>>();
        static LandFile land;
        const float ZoneCell = 100f, StreetCell = 50f;

        static float PolyArea(List<Vector2> pts)
        {
            float a = 0; for (int i = 0; i < pts.Count; i++) { var p = pts[i]; var q = pts[(i + 1) % pts.Count]; a += p.x * q.y - q.x * p.y; }
            return a / 2f;
        }

        static List<Vector2> Pts(float[] p) { var l = new List<Vector2>(); for (int i = 0; i + 1 < p.Length; i += 2) l.Add(new Vector2(p[i], p[i + 1])); return l; }

        static void LoadLand()
        {
            zones.Clear(); zoneGrid.Clear(); shopGrid.Clear(); streetGrid.Clear(); osmLots.Clear();
            var file = $"Tools/Data/{RouteId}_landuse.json";
            land = File.Exists(file) ? JsonUtility.FromJson<LandFile>(File.ReadAllText(file)) : new LandFile { zones = new ZoneRow[0], ways = new ZoneRow[0], shops = new ShopRow[0] };
            if (!File.Exists(file)) Debug.LogWarning("[FloridaSlice] no land use data (run node Tools/fetch_landuse.js); every building is classed by size alone");
            foreach (var z in land.zones)
            {
                var pts = Pts(z.p); if (pts.Count < 3) continue;
                var zo = new Zone { k = z.k, pts = pts, area = Mathf.Abs(PolyArea(pts)) };
                zo.min = new Vector2(pts.Min(q => q.x), pts.Min(q => q.y)); zo.max = new Vector2(pts.Max(q => q.x), pts.Max(q => q.y));
                zones.Add(zo);
                for (int x = (int)Mathf.Floor(zo.min.x / ZoneCell); x <= (int)Mathf.Floor(zo.max.x / ZoneCell); x++)
                    for (int y = (int)Mathf.Floor(zo.min.y / ZoneCell); y <= (int)Mathf.Floor(zo.max.y / ZoneCell); y++)
                    {
                        if (!zoneGrid.TryGetValue((x, y), out var l)) zoneGrid[(x, y)] = l = new List<Zone>();
                        l.Add(zo);
                    }
            }
            foreach (var s in land.shops)
            {
                var key = ((int)Mathf.Floor(s.p[0] / ZoneCell), (int)Mathf.Floor(s.p[1] / ZoneCell));
                if (!shopGrid.TryGetValue(key, out var l)) shopGrid[key] = l = new List<ShopRow>();
                l.Add(s);
            }
            var gf = $"Tools/Data/{RouteId}_ground.json";
            if (File.Exists(gf))
            {
                var g = JsonUtility.FromJson<GroundFile>(File.ReadAllText(gf));
                foreach (var r in g.roads)
                    for (int i = 0; i + 3 < r.p.Length; i += 2)
                    {
                        var a = new Vector2(r.p[i], r.p[i + 1]); var b = new Vector2(r.p[i + 2], r.p[i + 3]);
                        var lo = Vector2.Min(a, b) - Vector2.one * r.w; var hi = Vector2.Max(a, b) + Vector2.one * r.w;
                        for (int x = (int)Mathf.Floor(lo.x / StreetCell); x <= (int)Mathf.Floor(hi.x / StreetCell); x++)
                            for (int y = (int)Mathf.Floor(lo.y / StreetCell); y <= (int)Mathf.Floor(hi.y / StreetCell); y++)
                            {
                                if (!streetGrid.TryGetValue((x, y), out var l)) streetGrid[(x, y)] = l = new List<(Vector2, Vector2, float)>();
                                l.Add((a, b, r.w));
                            }
                    }
                foreach (var l in g.lots) osmLots.Add(Pts(l.p));
            }
        }

        /// <summary>The most specific zone (the smallest one) that contains the point.</summary>
        static string ZoneAt(Vector2 q)
        {
            if (!zoneGrid.TryGetValue(((int)Mathf.Floor(q.x / ZoneCell), (int)Mathf.Floor(q.y / ZoneCell)), out var l)) return null;
            Zone best = null;
            foreach (var z in l)
                if (q.x >= z.min.x && q.x <= z.max.x && q.y >= z.min.y && q.y <= z.max.y && (best == null || z.area < best.area) && Inside(z.pts, q)) best = z;
            return best?.k;
        }

        static List<string> ShopsIn(List<Vector2> pts, Vector2 mid, float pad)
        {
            var found = new List<string>();
            int cx = (int)Mathf.Floor(mid.x / ZoneCell), cy = (int)Mathf.Floor(mid.y / ZoneCell);
            for (int a = -1; a <= 1; a++) for (int b = -1; b <= 1; b++)
                if (shopGrid.TryGetValue((cx + a, cy + b), out var l))
                    foreach (var s in l)
                    {
                        var q = new Vector2(s.p[0], s.p[1]);
                        if (Inside(pts, q) || (pad > 0 && pts.Any(v => (v - q).sqrMagnitude < pad * pad))) found.Add(s.k);
                    }
            return found;
        }

        static float StreetDistance(Vector2 q, float max, out Vector2 at, out float width)
        {
            float best = max * max; at = q; width = 0;
            int r = Mathf.CeilToInt(max / StreetCell), cx = (int)Mathf.Floor(q.x / StreetCell), cy = (int)Mathf.Floor(q.y / StreetCell);
            for (int a = -r; a <= r; a++) for (int b = -r; b <= r; b++)
                if (streetGrid.TryGetValue((cx + a, cy + b), out var l))
                    foreach (var (sa, sb, w) in l)
                    {
                        var d = sb - sa; float t = Mathf.Clamp01(Vector2.Dot(q - sa, d) / Mathf.Max(.01f, d.sqrMagnitude));
                        var c = sa + d * t; float dd = (c - q).sqrMagnitude;
                        if (dd < best) { best = dd; at = c; width = w; }
                    }
            return Mathf.Sqrt(best);
        }

        // The smallest rectangle around the footprint: center, long axis, half length and half width.
        static void Obb(List<Vector2> pts, out Vector2 c, out Vector2 u, out float a, out float b)
        {
            float best = float.MaxValue; c = Vector2.zero; u = Vector2.right; a = b = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                var d = pts[(i + 1) % pts.Count] - pts[i]; if (d.sqrMagnitude < .25f) continue; d.Normalize();
                var n = new Vector2(-d.y, d.x);
                float u0 = float.MaxValue, u1 = float.MinValue, v0 = float.MaxValue, v1 = float.MinValue;
                foreach (var q in pts) { float x = Vector2.Dot(q, d), y = Vector2.Dot(q, n); u0 = Mathf.Min(u0, x); u1 = Mathf.Max(u1, x); v0 = Mathf.Min(v0, y); v1 = Mathf.Max(v1, y); }
                float area = (u1 - u0) * (v1 - v0);
                if (area < best)
                {
                    best = area; c = d * (u0 + u1) / 2f + n * (v0 + v1) / 2f;
                    if (u1 - u0 >= v1 - v0) { u = d; a = (u1 - u0) / 2f; b = (v1 - v0) / 2f; }
                    else { u = n; a = (v1 - v0) / 2f; b = (u1 - u0) / 2f; }
                }
            }
        }

        static BKind Classify(string k, float area, float h, string zone, int shops)
        {
            switch (k)
            {
                case "house": case "detached": case "bungalow": case "semidetached_house": case "allotment_house": case "terrace": return BKind.House;
                case "residential": return area < 350f ? BKind.House : h > 30f ? BKind.Tower : BKind.Apartment;
                case "apartments": case "dormitory": case "hotel": return h > 30f ? BKind.Tower : BKind.Apartment;
                case "commercial": case "retail": case "supermarket": case "kiosk": return h > 14f ? (h > 30f ? BKind.Tower : BKind.Office) : BKind.Shop;
                case "warehouse": case "industrial": case "manufacture": case "hangar": return BKind.Warehouse;
                case "office": return h > 30f ? BKind.Tower : BKind.Office;
                case "school": case "university": case "college": case "hospital": case "church": case "public": case "civic": case "library": case "museum": case "train_station": case "government":
                    return h > 20f ? BKind.Office : BKind.Civic;
                case "shed": case "garage": case "garages": case "roof": case "carport": case "construction": return BKind.Shed;
                case "parking": return BKind.Office;
            }
            if (h >= 30f) return BKind.Tower;
            if (area < 30f) return BKind.Shed;
            if (shops > 0) return area > 4000f ? BKind.Warehouse : BKind.Shop;
            if (h >= 12f) return zone == "home" ? BKind.Apartment : BKind.Office;
            switch (zone)
            {
                case "home": return area < 380f ? BKind.House : BKind.Apartment;
                case "shop": return area > 6000f ? BKind.Warehouse : BKind.Shop;
                case "industry": return area < 120f ? BKind.Shed : BKind.Warehouse;
                case "civic": return BKind.Civic;
                case "park": return area < 300f ? BKind.Shed : BKind.Civic;
            }
            return area < 300f ? BKind.House : area < 1800f ? BKind.Shop : BKind.Warehouse;
        }

        // ---------- geometry ----------
        static void Tri(Bucket bk, int slot, Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc, bool up)
        {
            var n = Vector3.Cross(b - a, c - a);
            if ((n.y < 0f) == up) { var t = b; b = c; c = t; var tu = ub; ub = uc; uc = tu; }
            var v = bk.v[slot]; int i = v.Count;
            v.Add(a); v.Add(b); v.Add(c); bk.uv[slot].Add(ua); bk.uv[slot].Add(ub); bk.uv[slot].Add(uc);
            bk.t[slot].Add(i); bk.t[slot].Add(i + 1); bk.t[slot].Add(i + 2);
        }

        /// <summary>A roof face (triangle or quad, eave first), with the tile texture running up the slope.</summary>
        static void RoofFace(Bucket bk, int slot, float tile, params Vector3[] q)
        {
            var e = (q[1] - q[0]).normalized; var n = Vector3.Cross(q[1] - q[0], q[q.Length - 1] - q[0]).normalized;
            if (n.y < 0) n = -n;
            var s = Vector3.Cross(e, n).normalized; if (Vector3.Dot(s, (q[q.Length - 1] - q[0])) < 0) s = -s;
            Vector2 U(Vector3 p) => new Vector2(Vector3.Dot(p, e) / tile, Vector3.Dot(p - q[0], s) / tile);
            Tri(bk, slot, q[0], q[1], q[2], U(q[0]), U(q[1]), U(q[2]), true);
            if (q.Length == 4) Tri(bk, slot, q[0], q[2], q[3], U(q[0]), U(q[2]), U(q[3]), true);
        }

        /// <summary>A hip roof on a rectangle (center c, long axis u, half sizes a and b) at height y, with a fascia band.</summary>
        static void HipRoof(Bucket bk, int slot, Vector2 c, Vector2 u, float a, float b, float y, float overhang, float pitchDeg, float tile = 2f)
        {
            float A = a + overhang, B = b + overhang, rise = B * Mathf.Tan(pitchDeg * Mathf.Deg2Rad), ridge = Mathf.Max(0f, A - B);
            var w = new Vector2(-u.y, u.x);
            Vector3 P(Vector2 q, float h) => new Vector3(q.x, h, q.y);
            Vector3 p00 = P(c - u * A - w * B, y), p10 = P(c + u * A - w * B, y), p11 = P(c + u * A + w * B, y), p01 = P(c - u * A + w * B, y);
            Vector3 r0 = P(c - u * ridge, y + rise), r1 = P(c + u * ridge, y + rise);
            RoofFace(bk, slot, tile, p00, p10, r1, r0);
            RoofFace(bk, slot, tile, p11, p01, r0, r1);
            RoofFace(bk, slot, tile, p10, p11, r1);
            RoofFace(bk, slot, tile, p01, p00, r0);
            // Fascia board under the eave, all the way round, and the soffit seen from below.
            var ring = new[] { p00, p10, p11, p01 };
            for (int i = 0; i < 4; i++)
            {
                var e0 = ring[i]; var e1 = ring[(i + 1) % 4]; var dn = Vector3.down * .22f;
                Quad(bk, CTrim, e0 + dn, e0, e1 + dn, e1);
            }
            Tri(bk, CTrim, p00 + Vector3.down * .22f, p10 + Vector3.down * .22f, p11 + Vector3.down * .22f, Vector2.zero, Vector2.zero, Vector2.zero, false);
            Tri(bk, CTrim, p00 + Vector3.down * .22f, p11 + Vector3.down * .22f, p01 + Vector3.down * .22f, Vector2.zero, Vector2.zero, Vector2.zero, false);
        }

        /// <summary>A wall quad: bottom-left, top-left, bottom-right, top-right as seen from outside.</summary>
        static void Quad(Bucket bk, int slot, Vector3 bl, Vector3 tl, Vector3 br, Vector3 tr)
        {
            var v = bk.v[slot]; int n = v.Count;
            v.Add(bl); v.Add(tl); v.Add(br); v.Add(tr);
            float len = (br - bl).magnitude, h = (tl - bl).magnitude;
            bk.uv[slot].Add(new Vector2(0, 0)); bk.uv[slot].Add(new Vector2(0, h)); bk.uv[slot].Add(new Vector2(len, 0)); bk.uv[slot].Add(new Vector2(len, h));
            bk.t[slot].AddRange(new[] { n, n + 1, n + 2, n + 2, n + 1, n + 3 });
        }

        /// <summary>Walls whose texture is one row of an atlas: the row's full height is one story (or the whole wall).</summary>
        static void AtlasWalls(Bucket bk, int slot, List<Vector2> pts, float y0, float y1, Func<int, int> rowOf, float texW, float uStart)
        {
            float run = uStart;
            for (int i = 0; i < pts.Count; i++)
            {
                var a = pts[i]; var c = pts[(i + 1) % pts.Count]; float len = (c - a).magnitude;
                int row = rowOf(i);
                float v0 = (row + .004f) / AtlasRows, v1 = (row + .996f) / AtlasRows;
                var v = bk.v[slot]; var uv = bk.uv[slot]; int n = v.Count;
                v.Add(new Vector3(a.x, y0, a.y)); v.Add(new Vector3(a.x, y1, a.y)); v.Add(new Vector3(c.x, y0, c.y)); v.Add(new Vector3(c.x, y1, c.y));
                float u0 = run / texW, u1 = (run + len) / texW;
                uv.Add(new Vector2(u0, v0)); uv.Add(new Vector2(u0, v1)); uv.Add(new Vector2(u1, v0)); uv.Add(new Vector2(u1, v1));
                bk.t[slot].AddRange(new[] { n, n + 1, n + 2, n + 2, n + 1, n + 3 });
                run += len;
            }
        }

        static List<Vector2> RectPts(Vector2 c, Vector2 u, float a, float b)
        {
            var w = new Vector2(-u.y, u.x);
            var r = new List<Vector2> { c - u * a - w * b, c + u * a - w * b, c + u * a + w * b, c - u * a + w * b };
            if (PolyArea(r) < 0) r.Reverse();
            return r;
        }

        // Rooftop units on a flat roof: Miami's roofs are covered in AC condensers and air handlers.
        static void RoofUnits(Bucket bk, List<Vector2> pts, Vector2 c, Vector2 u, float a, float b, float y, float area, System.Random r)
        {
            int n = Mathf.Clamp(Mathf.RoundToInt(area / 160f), area > 50f ? 1 : 0, 14);
            var rot = Quaternion.LookRotation(new Vector3(u.x, 0, u.y), Vector3.up);
            var w = new Vector2(-u.y, u.x);
            for (int i = 0, tries = 0; i < n && tries < n * 6; tries++)
            {
                var q = c + u * a * ((float)r.NextDouble() * 1.6f - .8f) + w * b * ((float)r.NextDouble() * 1.6f - .8f);
                if (!Inside(pts, q)) continue;
                bool big = r.NextDouble() < .25;
                var size = big ? new Vector3(2.2f, 1.5f, 1.6f) : new Vector3(1f + (float)r.NextDouble() * .4f, .9f, 1f);
                Cuboid(bk.v[CMetal], bk.uv[CMetal], bk.t[CMetal], new Vector3(q.x, y + size.y / 2f, q.y), rot, size);
                i++;
            }
        }

        /// <summary>A flat roof inside a parapet: the walls rise past the roof line, so the roof sits a little lower.</summary>
        static void FlatRoof(Bucket bk, List<Vector2> pts, float y, float area, System.Random r)
        {
            Fan(bk, CMembrane, pts, y, 2f);
            Obb(pts, out var c, out var u, out var a, out var b);
            RoofUnits(bk, pts, c, u, a, b, y, area, r);
        }

        static void Apron(Bucket bk, List<Vector2> pts, Vector2 mid, float width)
        {
            float rad = pts.Average(q => (q - mid).magnitude);
            Fan(bk, CPave, pts.Select(q => mid + (q - mid) * (1f + width / Mathf.Max(3f, rad))).ToList(), -0.14f, 4f);
        }

        static readonly Dictionary<string, string[]> SignFor = new Dictionary<string, string[]>
        {
            ["food"] = new[] { "CUBAN CAFE", "PIZZA", "CHINESE FOOD", "WINGS", "BAKERY" }, ["bakery"] = new[] { "BAKERY" },
            ["pharmacy"] = new[] { "FARMACIA" }, ["barber"] = new[] { "BARBER SHOP" }, ["nails"] = new[] { "NAILS", "BEAUTY SALON" },
            ["laundry"] = new[] { "COIN LAUNDRY" }, ["tax"] = new[] { "TAX SERVICE" }, ["liquor"] = new[] { "LIQUORS" }, ["cash"] = new[] { "CHECK CASHING" },
            ["dental"] = new[] { "DENTAL" }, ["phones"] = new[] { "CELL PHONES" }, ["insurance"] = new[] { "SEGUROS", "AUTO INSURANCE" },
            ["smoke"] = new[] { "SMOKE SHOP" }, ["clinic"] = new[] { "MEDICAL CENTER" }, ["pawn"] = new[] { "PAWN" }, ["discount"] = new[] { "DISCOUNT" },
            ["grocery"] = new[] { "MERCADO" }, ["convenience"] = new[] { "MERCADO" }, ["furniture"] = new[] { "FURNITURE", "MATTRESS" }, ["travel"] = new[] { "TRAVEL & ENVIOS" },
        };

        /// <summary>Starts the storefront texture at a shop that matches a real business in the building, when there is one.</summary>
        static float ShopStart(List<string> kinds, System.Random r, out int set)
        {
            foreach (var k in kinds)
                if (SignFor.TryGetValue(k, out var words))
                {
                    var word = words[r.Next(words.Length)];
                    for (int s = 0; s < 4; s++) { int u = Array.IndexOf(ShopWords[s], word); if (u >= 0) { set = s; return u * ShopUnit; } }
                }
            set = r.Next(4);
            return r.Next(7) * ShopUnit;
        }

        static int bkHouse, bkApt, bkShop, bkWare, bkOffice, bkTower, bkCivic, bkShed, driveways, lotsMade;

        /// <summary>
        /// A photographed surface (Poly Haven) turned into a detail layer: its grain and blotches kept, its color removed,
        /// centered on mid grey so it darkens and lightens the painted wall underneath without changing its color.
        /// </summary>
        static Texture2D DetailFromPhoto(string texSet, string name, float contrast)
        {
            var file = $"{PH}/Textures/{texSet}/{texSet}_Diffuse.jpg";
            if (!File.Exists(file)) return null;
            var src = new Texture2D(2, 2); src.LoadImage(File.ReadAllBytes(file));
            const int S = 1024; var px = new Color[S * S]; double sum = 0;
            var lum = new float[S * S];
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++) { float l = src.GetPixelBilinear((x + .5f) / S, (y + .5f) / S).grayscale; lum[y * S + x] = l; sum += l; }
            float mean = (float)(sum / lum.Length);
            for (int i = 0; i < px.Length; i++) { float v = Mathf.Clamp01(.5f + (lum[i] / Mathf.Max(.01f, mean) - 1f) * .5f * contrast); px[i] = new Color(v, v, v, 1f); }
            UnityEngine.Object.DestroyImmediate(src);
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, true); tex.SetPixels(px); tex.Apply();
            return SaveTexture(tex, name);
        }

        static void AddDetail(Material m, Texture2D detail, string normalSet, float tiles)
        {
            if (detail == null) return;
            m.SetTexture("_DetailAlbedoMap", detail); m.SetTextureScale("_DetailAlbedoMap", new Vector2(tiles, tiles));
            m.SetFloat("_DetailAlbedoMapScale", 1f);
            var n = Tex($"{PH}/Textures/{normalSet}/{normalSet}_nor_gl.jpg", true);
            if (n != null) { m.SetTexture("_DetailNormalMap", n); m.SetFloat("_DetailNormalMapScale", .8f); }
            m.EnableKeyword("_DETAIL_MULX2");
        }

        static Material PhotoRoof(string name, string texSet, float metersPerRepeat, Color tint, Material fallback, float bump = 1f)
        {
            var b = $"{PH}/Textures/{texSet}/{texSet}_";
            if (!File.Exists(b + "Diffuse.jpg")) return fallback;
            var mt = Lit(name, tint, .12f, texSet);
            // Roof faces measure their texture in 2 m steps; the photo covers metersPerRepeat.
            mt.SetTextureScale("_BaseMap", Vector2.one * (2f / metersPerRepeat));
            // Deeper tile relief so the rows of tiles throw their own little shadows in the low sun.
            mt.SetFloat("_BumpScale", bump);
            return mt;
        }

        static Material[] CityMaterials()
        {
            var plaster = DetailFromPhoto("grey_plaster", "PlasterDetail", .9f);
            var concrete = DetailFromPhoto("concrete_wall_004", "ConcreteDetail", 1.2f);
            // Each atlas is laid out so one texture repeat is the same size across and up, so the detail stays square.
            Material Atlas(string name, Texture2D t, float metersPerRepeat, Texture2D detail, string normalSet, float detailMeters)
            {
                var mt = Lit(name, Color.white, .12f); mt.SetTexture("_BaseMap", t);
                AddDetail(mt, detail, normalSet, metersPerRepeat / detailMeters);
                return mt;
            }
            Material Roof(string name, Texture2D t, float smooth) { var mt = Lit(name, Color.white, smooth); mt.SetTexture("_BaseMap", t); return mt; }
            var lotTex = AssetDatabase.LoadAssetAtPath<Texture2D>(Gen + "/Materials/ParkingStripes.png");
            var lot = Lit("CityLot", Color.white, .15f); if (lotTex != null) lot.SetTexture("_BaseMap", lotTex);
            return new[]
            {
                Atlas("HouseWalls", MakeHouseAtlas(), HouseTexW, plaster, "grey_plaster", 2.5f), Atlas("AptWalls", MakeAptAtlas(), AptTexW, plaster, "grey_plaster", 2.5f),
                Atlas("ShopWalls", MakeShopAtlas(), ShopTexW, plaster, "grey_plaster", 2.5f), Atlas("WareWalls", MakeWareAtlas(), WareTexW, concrete, "concrete_wall_004", 3.5f),
                Facade("FacadeGlassBlue", new Color(.3f, .37f, .44f), new Color(.16f, .28f, .4f), 0, 1, .8f),
                Facade("FacadeGlassGreen", new Color(.32f, .4f, .4f), new Color(.17f, .33f, .35f), 0, 2, .8f),
                Facade("FacadeGlassDark", new Color(.2f, .22f, .26f), new Color(.1f, .13f, .18f), 0, 3, .85f),
                Facade("FacadeBandWhite", new Color(.84f, .84f, .8f), new Color(.2f, .3f, .4f), 2, 4, .45f),
                Facade("FacadeBandTan", new Color(.72f, .66f, .56f), new Color(.17f, .23f, .3f), 2, 5, .35f),
                Facade("FacadeCondo", new Color(.88f, .86f, .79f), new Color(.2f, .27f, .35f), 1, 6, .3f),
                // Clay barrel tile, the old-Florida Spanish roof; flat concrete tile, toned grey-brown and darker for Florida's mildew.
                PhotoRoof("RoofBarrel", "clay_roof_tiles_02", 4.4f, new Color(.8f, .72f, .68f), null, 2.2f) ?? Roof("RoofBarrel", MakeBarrelTile(), .15f),
                PhotoRoof("RoofFlat", "grey_roof_tiles", 4.8f, new Color(.92f, .9f, .88f), null) ?? Roof("RoofFlat", MakeFlatTile(), .1f), Roof("RoofShingle", MakeShingle(), .08f), Roof("RoofWhite", MakeMembrane(), .12f),
                Lit("RoofUnit", new Color(.74f, .75f, .74f), .35f),
                Textured("CityPave", MakeConcrete("CityPaveTex", new Color(.52f, .51f, .48f), 0), .1f),
                Lit("CityTrim", new Color(.92f, .91f, .88f), .2f),
                lot,
            };
        }

        static void BuildBuildings(RoutePath p, Func<float, float> south, Mats m)
        {
            occupied.Clear();
            var file = $"Tools/Data/{RouteId}_buildings.json";
            if (!File.Exists(file)) { Debug.LogError("[FloridaSlice] no building data"); return; }
            var data = JsonUtility.FromJson<BuildingFile>(File.ReadAllText(file));
            LoadLand();
            var mats = CityMaterials();
            var rnd = new System.Random(305);
            var map = new Dictionary<(int, int), Bucket>();
            int built = 0, dropped = 0, solid = 0;
            bkHouse = bkApt = bkShop = bkWare = bkOffice = bkTower = bkCivic = bkShed = driveways = lotsMade = 0;
            yards.Clear(); lotEdges.Clear(); footGrid.Clear();
            foreach (var b in data.items)
            {
                var pts = Pts(b.p);
                if (pts.Count < 3 || pts.Any(q => OnRoad(q) || NearExit(q, 12f)) || exitCorridor.Any(c => Inside(pts, c))) { dropped++; continue; }
                float area = PolyArea(pts);
                if (area < 0) { pts.Reverse(); area = -area; }
                var mid = new Vector2(pts.Average(q => q.x), pts.Average(q => q.y));
                // Every building gets its own steady dice, so rebuilding never reshuffles the city.
                var r = new System.Random((int)(mid.x * 7.1f) ^ (int)(mid.y * 13.3f) ^ 0x5f3);
                var shops = ShopsIn(pts, mid, 0f);
                var kind = Classify(b.k ?? "", area, b.h, ZoneAt(mid), shops.Count);
                var bk = Get(map, mid, 900f, CitySlots);
                Obb(pts, out var oc, out var ou, out var oa, out var ob);
                bool rect = area / Mathf.Max(1f, 4f * oa * ob) > .8f && ob > 2.5f;
                float top;
                switch (kind)
                {
                    case BKind.House: top = House(bk, pts, mid, area, b.h, rect, oc, ou, oa, ob, r); bkHouse++; break;
                    case BKind.Apartment: top = Apartment(bk, pts, area, b.h, rect, oc, ou, oa, ob, r); bkApt++; break;
                    case BKind.Shop: top = Shop(bk, pts, mid, area, b.h, shops, r); bkShop++; break;
                    case BKind.Warehouse: top = Warehouse(bk, pts, mid, area, b.h, r); bkWare++; break;
                    case BKind.Shed:
                        top = 2.6f; AtlasWalls(bk, CWare, pts, -0.3f, top, i => r.Next(4) + 4, WareTexW * .4f, 0f); Fan(bk, CMembrane, pts, top, 2f); bkShed++; break;
                    default:
                        top = Tall(bk, pts, mid, area, b.h, kind, r);
                        if (kind == BKind.Tower) bkTower++; else if (kind == BKind.Civic) bkCivic++; else bkOffice++;
                        break;
                }
                AddFootprint(pts);
                float minX = pts.Min(q => q.x), maxX = pts.Max(q => q.x), minZ = pts.Min(q => q.y), maxZ = pts.Max(q => q.y);
                for (int ix = (int)(minX / 30f); ix <= (int)(maxX / 30f); ix++) for (int iz = (int)(minZ / 30f); iz <= (int)(maxZ / 30f); iz++) occupied.Add((ix, iz));
                if (Locate(mid, out var cs, out var clat) && Mathf.Abs(clat) < 500f)
                {
                    // Small blocks that follow the real footprint; one box around an angled building can span the highway.
                    var tan = p.Tangent(cs); var right = p.Right(cs);
                    for (float gx = minX + 4f; gx < maxX; gx += 9f) for (float gz = minZ + 4f; gz < maxZ; gz += 9f)
                    {
                        if (!Inside(pts, new Vector2(gx, gz))) continue;
                        var d = new Vector3(gx - mid.x, 0, gz - mid.y);
                        float bs = cs + Vector3.Dot(d, tan), bl = clat + Vector3.Dot(d, right);
                        if (bl - 5f < p.HalfWidth(bs) + 5f && bl + 5f > south(bs) - p.HalfWidth(bs) - 5f) continue;
                        Solid(bs, bl, 5f, 5f, "building"); solid++;
                    }
                }
                built++;
            }
            int infill = Infill(p, south, map);
            int streetFill = StreetInfill(map);
            Debug.Log($"[FloridaSlice] street infill houses where a built-up neighborhood has an empty gap and no zone: {streetFill}");
            int chunks = Flush(map, "City", mats, CitySlots, true);
            Debug.Log($"[FloridaSlice] infill buildings where the map has a zone but no footprints: {infill}");
            Debug.Log($"[FloridaSlice] buildings placed {built} (houses {bkHouse}, apartments {bkApt}, shops {bkShop}, warehouses {bkWare}, offices {bkOffice}, towers {bkTower}, civic {bkCivic}, sheds {bkShed}), driveways {driveways}, shop lots {lotsMade}, removed from roadway {dropped}, solid {solid}, chunks {chunks}");
            BuildFarCity(p, south, mats);
        }

        /// <summary>
        /// OpenStreetMap is missing footprints in places (whole blocks in parts of Miami). Where the map marks land as
        /// homes, shops or industry but nothing stands, add what the zone would have: a house facing its street every
        /// lot or so, or a low commercial or industrial building. Parks, water and highway land stay open.
        /// </summary>
        static int Infill(RoutePath p, Func<float, float> south, Dictionary<(int, int), Bucket> map)
        {
            int n = 0;
            foreach (var z in zones)
            {
                if (z.k != "home" && z.k != "shop" && z.k != "industry") continue;
                float step = z.k == "home" ? 24f : 45f;
                for (float x = z.min.x + step / 2f; x < z.max.x; x += step)
                    for (float y = z.min.y + step / 2f; y < z.max.y; y += step)
                    {
                        var q = new Vector2(x, y);
                        if (!Inside(z.pts, q) || InBuilding(q, z.k == "home" ? 12f : 20f) || OnRoad(q, 25f) || NearExit(q, 20f) || InWater(q)) continue;
                        if (osmLots.Any(l => Inside(l, q))) continue;
                        float d = StreetDistance(q, 60f, out var at, out var sw);
                        if (d > 55f || d < sw / 2f + 7f) continue;
                        var toStreet = (at - q).normalized; var u = new Vector2(-toStreet.y, toStreet.x);
                        var r = new System.Random((int)(x * 3.1f) ^ (int)(y * 7.7f) ^ 0x77);
                        float a, b;
                        if (z.k == "home") { a = 6f + (float)r.NextDouble() * 3f; b = 4.5f + (float)r.NextDouble() * 2f; }
                        else { a = 12f + (float)r.NextDouble() * 10f; b = 9f + (float)r.NextDouble() * 6f; }
                        // Set back from the street the way real lots are.
                        var c = at - toStreet * (sw / 2f + (z.k == "home" ? 7.5f : 14f) + b);
                        var foot = RectPts(c, u, a, b);
                        if (foot.Any(v => InBuilding(v, 1.5f) || OnRoad(v, 20f) || StreetDistance(v, 8f, out _, out var sw2) < sw2 / 2f + 2f)) continue;
                        var bk = Get(map, c, 900f, CitySlots);
                        float area = 4f * a * b;
                        if (z.k == "home") House(bk, foot, c, area, 0f, true, c, u, a, b, r);
                        else if (z.k == "shop") Shop(bk, foot, c, area, 0f, new List<string>(), r);
                        else Warehouse(bk, foot, c, area, 0f, r);
                        AddFootprint(foot);
                        if (Locate(c, out var cs, out var clat) && Mathf.Abs(clat) < 500f) Solid(cs, clat, Mathf.Min(a, b), Mathf.Min(a, b), "building");
                        n++;
                    }
            }
            return n;
        }

        /// <summary>
        /// The zoning map covers only part of Miami as homes, so whole blocks with streets but no footprints were left as
        /// empty fields. Along side streets (7 m), where real buildings stand all around but a stretch is empty and the map
        /// says nothing else is there (no park, school, water, shop or industry), add houses a lot apart facing the street.
        /// </summary>
        static int StreetInfill(Dictionary<(int, int), Bucket> map)
        {
            bool Occupied(int x, int y) => footGrid.TryGetValue((x, y), out var l) && l.Count > 0;
            bool NearFoot(Vector2 q, float rad)
            {
                int r = Mathf.CeilToInt(rad / FootCell), cx = (int)Mathf.Floor(q.x / FootCell), cy = (int)Mathf.Floor(q.y / FootCell);
                for (int a = -r; a <= r; a++) for (int b = -r; b <= r; b++)
                    if (footGrid.TryGetValue((cx + a, cy + b), out var l))
                        foreach (var pts in l) foreach (var v in pts) if ((v - q).sqrMagnitude < rad * rad) return true;
                return false;
            }
            int Neighbors(Vector2 q)
            {
                int n = 0, cx = (int)Mathf.Floor(q.x / FootCell), cy = (int)Mathf.Floor(q.y / FootCell);
                for (int a = -6; a <= 6; a++) for (int b = -6; b <= 6; b++) if (Occupied(cx + a, cy + b)) n++;
                return n;
            }
            // Pick the sites against the real buildings first, so new houses don't block each other.
            var sites = new List<(Vector2 c, Vector2 u, Vector2 toStreet, float a, float b, int seed)>();
            foreach (var (sa, sb, w) in streetGrid.Values.SelectMany(x => x).Distinct())
            {
                if (w > 7.5f) continue;
                var d = sb - sa; float len = d.magnitude; if (len < 20f) continue;
                var t = d / len; var n = new Vector2(-t.y, t.x);
                for (float x = 11f; x < len - 8f; x += 22f)
                    for (int side = -1; side <= 1; side += 2)
                    {
                        var at = sa + t * x;
                        var r = new System.Random((int)(at.x * 3.1f) ^ (int)(at.y * 7.7f) ^ side * 0x2F1 ^ 0x5EED);
                        float a = 6f + (float)r.NextDouble() * 3f, b = 4.5f + (float)r.NextDouble() * 2f;
                        var c = at + n * side * (w / 2f + 7.5f + b);
                        var z = ZoneAt(c); if (z != null && z != "home") continue;
                        if (NearFoot(c, 28f) || Neighbors(c) < 25 || InWater(c) || OnRoad(c, 25f) || NearExit(c, 20f)) continue;
                        if (osmLots.Any(l => Inside(l, c))) continue;
                        // The house sits square to its street: long side along it.
                        var foot = RectPts(c, t, a, b);
                        if (foot.Any(v => OnRoad(v, 20f) || StreetDistance(v, 8f, out _, out var sw2) < sw2 / 2f + 2f)) continue;
                        if (StreetDistance(c, 40f, out var near, out _) < w / 2f + 7.5f + b - 1f && (near - at).sqrMagnitude > 4f) continue; // closer to another street: a corner, leave it
                        if (sites.Any(o => (o.c - c).sqrMagnitude < 18f * 18f)) continue;
                        sites.Add((c, t, -n * side, a, b, r.Next()));
                    }
            }
            foreach (var (c, u, toStreet, a, b, seed) in sites)
            {
                var r = new System.Random(seed);
                var foot = RectPts(c, u, a, b);
                House(Get(map, c, 900f, CitySlots), foot, c, 4f * a * b, 0f, true, c, u, a, b, r);
                AddFootprint(foot);
                if (Locate(c, out var cs, out var clat) && Mathf.Abs(clat) < 500f) Solid(cs, clat, Mathf.Min(a, b), Mathf.Min(a, b), "building");
            }
            return sites.Count;
        }

        static float House(Bucket bk, List<Vector2> pts, Vector2 mid, float area, float h, bool rect, Vector2 c, Vector2 u, float a, float b, System.Random r)
        {
            int stories = h > 6.6f ? 2 : h > 2f ? 1 : (r.NextDouble() < .1 ? 2 : 1);
            float wall = stories * HouseStory;
            int row = r.Next(AtlasRows); float u0 = (float)r.NextDouble() * HouseTexW;
            var foot = rect ? RectPts(c, u, a, b) : pts;
            for (int s = 0; s < stories; s++) AtlasWalls(bk, CHouse, foot, s == 0 ? -0.3f : s * HouseStory, (s + 1) * HouseStory, i => row, HouseTexW, u0 + s * 3.7f);
            if (rect)
            {
                double roll = r.NextDouble();
                int slot = roll < .5 ? CBarrel : roll < .82 ? CFlatTile : CShingle;
                HipRoof(bk, slot, c, u, a, b, wall, .55f, slot == CBarrel ? 22f : 24f);
            }
            else
            {
                // Flat-roofed, as many mid-century Miami houses are: a low parapet and a white roof.
                AtlasWalls(bk, CHouse, foot, wall, wall + .45f, i => row, HouseTexW, u0);
                Fan(bk, CMembrane, foot, wall + .3f, 2f);
                if (r.NextDouble() < .6) RoofUnits(bk, foot, c, u, a, b, wall + .3f, Mathf.Min(area, 160f), r);
            }
            // A driveway from the house to its street.
            float d = StreetDistance(mid, 30f, out var at, out var sw);
            var toStreet = (at - mid).normalized;
            if (rect) yards.Add(new Yard { mid = mid, c = c, u = u, a = a, b = b, toStreet = toStreet, hasStreet = d < 30f && d > 3f });
            else { Obb(pts, out var c2, out var u2, out var a2, out var b2); yards.Add(new Yard { mid = mid, c = c2, u = u2, a = a2, b = b2, toStreet = toStreet, hasStreet = d < 30f && d > 3f }); }
            if (d < 30f && d > 3f)
            {
                var dir = toStreet; var side = new Vector2(-dir.y, dir.x);
                var start = mid + side * (Mathf.Min(a, b) * .5f * (r.NextDouble() < .5 ? -1f : 1f));
                var end = start + dir * Mathf.Max(0f, d - sw / 2f);
                if (!OnRoad(end, 2f) && !NearExit(end, 6f))
                {
                    Strip(bk, CPave, start, end, side * 1.6f, -0.2f, 3.2f);
                    driveways++;
                }
            }
            return wall;
        }

        static float Apartment(Bucket bk, List<Vector2> pts, float area, float h, bool rect, Vector2 c, Vector2 u, float a, float b, System.Random r)
        {
            int stories = h > 2f ? Mathf.Clamp(Mathf.RoundToInt(h / AptStory), 2, 12) : 2 + r.Next(2);
            int row = r.Next(AtlasRows); float u0 = (float)r.NextDouble() * AptTexW;
            for (int s = 0; s < stories; s++) AtlasWalls(bk, CApt, pts, s == 0 ? -0.3f : s * AptStory, (s + 1) * AptStory, i => row, AptTexW, u0 + s * 1.6f);
            float wall = stories * AptStory;
            if (rect && stories <= 4 && r.NextDouble() < .45)
                HipRoof(bk, r.NextDouble() < .7 ? CBarrel : CFlatTile, c, u, a, b, wall, .7f, 22f);
            else
            {
                AtlasWalls(bk, CApt, pts, wall, wall + .6f, i => row, AptTexW, u0);
                FlatRoof(bk, pts, wall + .4f, area, r);
            }
            return wall;
        }

        static int FrontEdge(List<Vector2> pts, out float streetGap)
        {
            int best = 0; float score = float.MinValue; streetGap = 99f;
            for (int i = 0; i < pts.Count; i++)
            {
                var a = pts[i]; var c = pts[(i + 1) % pts.Count]; var d = c - a; float len = d.magnitude; if (len < 4f) continue;
                var n = new Vector2(d.y, -d.x) / len; var m = (a + c) / 2f;
                float gap = StreetDistance(m + n * 2f, 90f, out _, out _);
                float sc = len - gap * .6f;
                if (sc > score) { score = sc; best = i; streetGap = gap; }
            }
            return best;
        }

        static float Shop(Bucket bk, List<Vector2> pts, Vector2 mid, float area, float h, List<string> shops, System.Random r)
        {
            float wall = h > 3.5f && h < 12f ? Mathf.Max(h, 4.6f) : ShopH;
            int front = FrontEdge(pts, out float gap);
            float u0 = ShopStart(shops, r, out int set);
            // The front's texture starts at the matching shop; the other walls use the plain back of the same row.
            float run = 0; for (int i = 0; i < front; i++) run += (pts[(i + 1) % pts.Count] - pts[i]).magnitude;
            AtlasWalls(bk, CShop, pts, -0.3f, wall, i => i == front ? set : set + 4, ShopTexW, u0 - run);
            Fan(bk, CMembrane, pts, wall - .45f, 2f);
            Obb(pts, out var c, out var u, out var a, out var b);
            RoofUnits(bk, pts, c, u, a, b, wall - .45f, area, r);
            // A canopy over the walk along the front, and the parking lot between it and the street.
            var fa = pts[front]; var fc = pts[(front + 1) % pts.Count]; var fd = (fc - fa).normalized; var n = new Vector2(fd.y, -fd.x);
            var e0 = fa + fd * .2f; var e1 = fc - fd * .2f;
            Vector3 P(Vector2 q, float y) => new Vector3(q.x, y, q.y);
            float cy = wall * .61f;
            Quad(bk, CTrim, P(e0 + n * 2.4f, cy - .45f), P(e0 + n * 2.4f, cy), P(e1 + n * 2.4f, cy - .45f), P(e1 + n * 2.4f, cy));
            Tri(bk, CTrim, P(e0, cy), P(e1, cy), P(e1 + n * 2.4f, cy), Vector2.zero, Vector2.zero, Vector2.zero, true);
            Tri(bk, CTrim, P(e0, cy), P(e1 + n * 2.4f, cy), P(e0 + n * 2.4f, cy), Vector2.zero, Vector2.zero, Vector2.zero, true);
            Tri(bk, CTrim, P(e0, cy - .45f), P(e1, cy - .45f), P(e1 + n * 2.4f, cy - .45f), Vector2.zero, Vector2.zero, Vector2.zero, false);
            Tri(bk, CTrim, P(e0, cy - .45f), P(e1 + n * 2.4f, cy - .45f), P(e0 + n * 2.4f, cy - .45f), Vector2.zero, Vector2.zero, Vector2.zero, false);
            Strip(bk, CPave, e0 + n * 1.4f, e1 + n * 1.4f, new Vector2(-fd.y, fd.x) * 1.4f, -0.13f, 2.8f);
            float depth = Mathf.Min(26f, gap - 6f);
            var lotMid = (fa + fc) / 2f + n * (2.8f + depth / 2f);
            if (depth > 9f && !OnRoad(lotMid, 3f) && !NearExit(lotMid, 8f) && !osmLots.Any(l => Inside(l, lotMid)))
            {
                var l0 = fa + n * 2.8f; var l1 = fc + n * 2.8f; var l2 = fc + n * (2.8f + depth); var l3 = fa + n * (2.8f + depth);
                if (!OnRoad(l2, 2f) && !OnRoad(l3, 2f))
                {
                    var v = bk.v[CLot]; int k = v.Count; float len = (fc - fa).magnitude;
                    v.Add(P(l0, -0.155f)); v.Add(P(l1, -0.155f)); v.Add(P(l2, -0.155f)); v.Add(P(l3, -0.155f));
                    bk.uv[CLot].Add(new Vector2(0, 0)); bk.uv[CLot].Add(new Vector2(len / 2.75f, 0)); bk.uv[CLot].Add(new Vector2(len / 2.75f, depth / 5.4f)); bk.uv[CLot].Add(new Vector2(0, depth / 5.4f));
                    var tri = new[] { k, k + 2, k + 1, k, k + 3, k + 2 };
                    if (Vector3.Cross(v[k + 2] - v[k], v[k + 1] - v[k]).y < 0) tri = new[] { k, k + 1, k + 2, k, k + 2, k + 3 };
                    bk.t[CLot].AddRange(tri);
                    lotsMade++;
                    lotEdges.Add((l3 + n * 1.2f, l2 + n * 1.2f));
                }
            }
            return wall;
        }

        static float Warehouse(Bucket bk, List<Vector2> pts, Vector2 mid, float area, float h, System.Random r)
        {
            float wall = h > 2f ? Mathf.Clamp(h, 5.5f, 15f) : 7.5f + (float)r.NextDouble() * 3f;
            int front = FrontEdge(pts, out _);
            int c = r.Next(4);
            AtlasWalls(bk, CWare, pts, -0.3f, wall, i => i == front ? c : c + 4, WareTexW, (float)r.NextDouble() * WareTexW);
            Fan(bk, CMembrane, pts, wall - .5f, 2f);
            Obb(pts, out var oc, out var u, out var a, out var b);
            RoofUnits(bk, pts, oc, u, a, b, wall - .5f, area * .35f, r);
            Apron(bk, pts, mid, 5f);
            return wall;
        }

        static float Tall(Bucket bk, List<Vector2> pts, Vector2 mid, float area, float h, BKind kind, System.Random r)
        {
            if (h <= 2f) h = kind == BKind.Civic ? 7f + (float)r.NextDouble() * 4f : 12f + (float)r.NextDouble() * 10f;
            int style = kind == BKind.Tower ? (r.NextDouble() < .25 ? 5 : r.Next(3)) : kind == BKind.Civic ? 3 + r.Next(2) : 3 + r.Next(3);
            Walls(bk, CFac + style, pts, -0.3f, h, (float)r.NextDouble() * 3f);
            Walls(bk, CFac + style, pts, h, h + .9f, .5f);
            FlatRoof(bk, pts, h + .6f, area, r);
            Apron(bk, pts, mid, 2.4f);
            if (h > 30f)
            {
                var core = pts.Select(q => mid + (q - mid) * .48f).ToList();
                float h2 = h + 3f + h * .05f;
                Walls(bk, CFac + style, core, h, h2, 1f);
                Fan(bk, CMembrane, core, h2, 2f);
            }
            return h;
        }

        /// <summary>
        /// Past the mapped band (about 420 m each side) the city carries on: low houses with tile and white roofs to 1.2 km,
        /// then blocks of mixed low buildings to 2.6 km, on a grid that follows the highway. No shadows, and haze softens them.
        /// </summary>
        static void BuildFarCity(RoutePath p, Func<float, float> south, Material[] mats)
        {
            var map = new Dictionary<(int, int), Bucket>();
            int houses = 0, blocks = 0;
            for (float s = 0f; s < p.Length; s += 26f)
            {
                var tan = p.Tangent(s); var u = new Vector2(tan.x, tan.z).normalized;
                foreach (int sideSign in new[] { 1, -1 })
                    for (float off = 470f; off < 2600f; off += off < 1200f ? 32f : 70f)
                    {
                        float lat = sideSign > 0 ? p.HalfWidth(s) + off : south(s) - p.HalfWidth(s) - off;
                        var at3 = p.Point(s, lat); var at = new Vector2(at3.x, at3.z);
                        var r = new System.Random((int)(s * 3.7f) ^ (int)(off * 11.1f) ^ (sideSign * 977));
                        if (off < 1200f && ((int)(off / 32f)) % 5 == 0) continue; // a street every fifth row
                        if (!FarFromRoute(at, 455f)) continue;
                        var z = ZoneAt(at); if (z == "water" || z == "park" || z == "wetland") continue;
                        var bk = Get(map, at, 1200f, CitySlots);
                        if (off < 1200f)
                        {
                            if (r.NextDouble() < .12) continue;
                            float a = 6f + (float)r.NextDouble() * 3f, b = 4.5f + (float)r.NextDouble() * 2f;
                            var cc = at + u * (float)(r.NextDouble() * 4 - 2);
                            var foot = RectPts(cc, u, a, b);
                            int row = r.Next(AtlasRows);
                            AtlasWalls(bk, CHouse, foot, -0.3f, HouseStory, i => row, HouseTexW, (float)r.NextDouble() * HouseTexW);
                            double roll = r.NextDouble();
                            if (roll < .78) HipRoof(bk, roll < .45 ? CBarrel : roll < .68 ? CFlatTile : CShingle, cc, u, a, b, HouseStory, .5f, 22f, 2f);
                            else Fan(bk, CMembrane, foot, HouseStory, 2f);
                            houses++;
                        }
                        else
                        {
                            if (r.NextDouble() < .35) continue;
                            float a = 10f + (float)r.NextDouble() * 18f, b = 8f + (float)r.NextDouble() * 12f, hgt = r.NextDouble() < .15 ? 12f + (float)r.NextDouble() * 18f : 4f + (float)r.NextDouble() * 6f;
                            var foot = RectPts(at, u, a, b);
                            int row = r.Next(AtlasRows);
                            if (hgt > 10f) Walls(bk, CFac + 3 + r.Next(3), foot, -0.3f, hgt, 0f);
                            else AtlasWalls(bk, r.NextDouble() < .5 ? CApt : CWare, foot, -0.3f, hgt, i => row, AptTexW, 0f);
                            Fan(bk, r.NextDouble() < .6 ? CMembrane : CBarrel, foot, hgt, 2f);
                            blocks++;
                        }
                    }
            }
            Flush(map, "FarCity", mats, CitySlots, false);
            Debug.Log($"[FloridaSlice] far city: houses {houses}, blocks {blocks}");
        }

        /// <summary>True when no part of the route passes within dist of the point (checked on the route's coarse grid).</summary>
        static bool FarFromRoute(Vector2 q, float dist)
        {
            int cx = (int)Mathf.Floor(q.x / 150f), cz = (int)Mathf.Floor(q.y / 150f), ring = Mathf.CeilToInt(dist / 150f) + 1;
            float d2 = dist * dist;
            for (int a = -ring; a <= ring; a++) for (int b = -ring; b <= ring; b++)
                if (cells.TryGetValue((cx + a, cz + b), out var list))
                    foreach (var ss in list) { var c = lookupPath.Center(ss); if ((c.x - q.x) * (c.x - q.x) + (c.z - q.y) * (c.z - q.y) < d2) return false; }
            return true;
        }
    }
}
