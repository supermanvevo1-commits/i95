using System.Collections.Generic;
using SurviveI95.Data;
using UnityEngine;

namespace SurviveI95.Game
{
    /// <summary>Placeholder vehicle shapes generated from the catalog's sizes. Generic on purpose: no real makes.</summary>
    public static class TrafficModels
    {
        public const int Paint = 0, Glass = 1, Dark = 2, Trim = 3, Head = 4, Tail = 5, SubCount = 6;

        public class Set { public Mesh Near, Far, SignalLeft, SignalRight, Brake, HighBeam; }

        class B
        {
            readonly List<Vector3> v = new List<Vector3>();
            readonly List<int>[] t = new List<int>[SubCount];
            public B() { for (int i = 0; i < SubCount; i++) t[i] = new List<int>(); }

            // Faces are turned outward from a known interior point, so shapes cannot end up inside out.
            public void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 inside)
            {
                int i = v.Count;
                bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c + d) / 4f - inside) < 0f;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                var list = t[sub];
                if (flip) { list.Add(i); list.Add(i + 2); list.Add(i + 1); list.Add(i); list.Add(i + 3); list.Add(i + 2); }
                else { list.Add(i); list.Add(i + 1); list.Add(i + 2); list.Add(i); list.Add(i + 2); list.Add(i + 3); }
            }

            public void Loft(int sub, params float[][] s) // each row: z, half width low, y low, y high, half width high
            {
                Vector3 inside = Vector3.zero;
                foreach (var r in s) inside += new Vector3(0, (r[2] + r[3]) / 2f, r[0]);
                inside /= s.Length;
                Vector3[] Ring(float[] r) => new[] { new Vector3(-r[1], r[2], r[0]), new Vector3(r[1], r[2], r[0]), new Vector3(r[4], r[3], r[0]), new Vector3(-r[4], r[3], r[0]) };
                var prev = Ring(s[0]);
                Quad(sub, prev[0], prev[1], prev[2], prev[3], inside);
                for (int i = 1; i < s.Length; i++)
                {
                    var cur = Ring(s[i]);
                    for (int e = 0; e < 4; e++) { int n = (e + 1) % 4; Quad(sub, prev[e], prev[n], cur[n], cur[e], inside); }
                    prev = cur;
                }
                Quad(sub, prev[0], prev[1], prev[2], prev[3], inside);
            }

            public void Box(int sub, Vector3 center, Vector3 size)
            {
                float hx = size.x / 2f, hy = size.y / 2f, z0 = center.z - size.z / 2f, z1 = center.z + size.z / 2f;
                Loft(sub, new[] { z0, hx, center.y - hy, center.y + hy, hx }, new[] { z1, hx, center.y - hy, center.y + hy, hx });
                for (int i = v.Count - 24; i < v.Count; i++) v[i] += new Vector3(center.x, 0, 0);
            }

            // Cylinder along X (wheels) or Z (tanks).
            public void Cylinder(int sub, Vector3 center, float radius, float length, bool alongX, int sides = 10)
            {
                Vector3 axis = alongX ? Vector3.right : Vector3.forward, u = Vector3.up, w = alongX ? Vector3.forward : Vector3.right;
                Vector3 a = center - axis * length / 2f, b = center + axis * length / 2f;
                for (int i = 0; i < sides; i++)
                {
                    float p = i * Mathf.PI * 2f / sides, q = (i + 1) * Mathf.PI * 2f / sides;
                    Vector3 e0 = (u * Mathf.Cos(p) + w * Mathf.Sin(p)) * radius, e1 = (u * Mathf.Cos(q) + w * Mathf.Sin(q)) * radius;
                    Quad(sub, a + e0, a + e1, b + e1, b + e0, center);
                    Tri(sub, a, a + e0, a + e1, center);
                    Tri(sub, b, b + e0, b + e1, center);
                }
            }

            void Tri(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 inside)
            {
                int i = v.Count;
                bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c) / 3f - inside) < 0f;
                v.Add(a); v.Add(b); v.Add(c);
                t[sub].Add(i); t[sub].Add(flip ? i + 2 : i + 1); t[sub].Add(flip ? i + 1 : i + 2);
            }

            public Mesh Done(string name, int subs = SubCount)
            {
                // Split vertices per face already, so normals come out flat and read as hard panels.
                var m = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt16 };
                m.SetVertices(v);
                m.subMeshCount = subs;
                for (int i = 0; i < subs; i++) m.SetTriangles(t[i], i, false);
                m.RecalculateNormals();
                m.RecalculateBounds();
                return m;
            }
        }

        struct Cab { public float H, Shoulder, RearBase, RearRoof, FrontRoof, FrontBase; public bool Open; }

        static void Wheels(B b, float radius, float track, float width, params float[] zs)
        {
            foreach (float z in zs)
                foreach (float side in new[] { -1f, 1f })
                {
                    b.Cylinder(Dark, new Vector3(side * track, radius, z), radius, width, true);
                    b.Cylinder(Trim, new Vector3(side * (track + width * .51f), radius, z), radius * .55f, .01f, true, 8);
                }
        }

        static float[] R(float z, float low, float y0, float y1, float high) => new[] { z, low, y0, y1, high };

        // A passenger shape: lower body, glasshouse and roof, sized from fractions of its length.
        static void Passenger(B b, float L, float W, Cab c, float z = 0f)
        {
            float hw = W / 2f, zr = z - L / 2f, zf = z + L / 2f;
            b.Loft(Paint,
                R(zr, hw * .84f, .34f, c.Shoulder - .08f, hw * .74f),
                R(zr + .22f, hw * .97f, .27f, c.Shoulder, hw * .9f),
                R(zf - .62f, hw, .27f, c.Shoulder - .04f, hw * .9f),
                R(zf - .1f, hw * .88f, .3f, c.Shoulder - .22f, hw * .76f),
                R(zf, hw * .78f, .38f, c.Shoulder - .32f, hw * .68f));
            b.Box(Dark, new Vector3(0, .2f, z), new Vector3(W * .86f, .16f, L * .9f));
            b.Box(Head, new Vector3(-hw * .66f, c.Shoulder - .34f, zf + .005f), new Vector3(hw * .42f, .12f, .02f));
            b.Box(Head, new Vector3(hw * .66f, c.Shoulder - .34f, zf + .005f), new Vector3(hw * .42f, .12f, .02f));
            b.Box(Tail, new Vector3(-hw * .66f, c.Shoulder - .2f, zr - .005f), new Vector3(hw * .44f, .13f, .02f));
            b.Box(Tail, new Vector3(hw * .66f, c.Shoulder - .2f, zr - .005f), new Vector3(hw * .44f, .13f, .02f));
            b.Box(Dark, new Vector3(0, .5f, zf - .01f), new Vector3(hw * .8f, .16f, .04f));

            float zA = zr + L * c.RearBase, zB = zr + L * c.RearRoof, zC = zr + L * c.FrontRoof, zD = zr + L * c.FrontBase;
            float y = c.Shoulder - .03f;
            if (c.Open)
            {
                b.Loft(Glass, R(zC, hw * .86f, y, y + .02f, hw * .84f), R(zD, hw * .88f, y, c.H, hw * .74f));
                b.Box(Dark, new Vector3(0, y + .24f, zA + (zC - zA) * .3f), new Vector3(W * .72f, .48f, .2f));
                b.Box(Dark, new Vector3(0, y + .02f, (zA + zD) / 2f), new Vector3(W * .78f, .05f, zD - zA));
                return;
            }
            b.Loft(Glass,
                R(zA, hw * .9f, y, y + .03f, hw * .84f),
                R(zB, hw * .9f, y, c.H - .03f, hw * .74f),
                R(zC, hw * .9f, y, c.H - .03f, hw * .74f),
                R(zD, hw * .9f, y, y + .03f, hw * .84f));
            b.Loft(Paint, R(zB - .04f, hw * .76f, c.H - .04f, c.H, hw * .7f), R(zC + .04f, hw * .76f, c.H - .04f, c.H, hw * .7f));
            float pillar = (zB + zC) / 2f;
            b.Box(Dark, new Vector3(-hw * .85f, (y + c.H) / 2f, pillar), new Vector3(.05f, c.H - y, .1f));
            b.Box(Dark, new Vector3(hw * .85f, (y + c.H) / 2f, pillar), new Vector3(.05f, c.H - y, .1f));
        }

        static void TruckCab(B b, float W, float zRear, float length, float height, int paint = Paint)
        {
            float hw = W / 2f, zFront = zRear + length;
            b.Loft(paint, R(zRear, hw * .98f, .55f, height, hw * .92f), R(zFront - .28f, hw, .55f, height, hw * .9f), R(zFront, hw * .92f, .55f, height * .62f, hw * .84f));
            b.Box(Glass, new Vector3(0, height * .74f, zFront - .17f), new Vector3(W * .82f, height * .3f, .28f));
            b.Box(Glass, new Vector3(0, height * .75f, zFront - .85f), new Vector3(W + .02f, height * .24f, .85f));
            b.Box(Trim, new Vector3(0, .85f, zFront + .02f), new Vector3(W * .6f, .6f, .05f));
            foreach (float s in new[] { -1f, 1f })
                b.Box(Head, new Vector3(s * hw * .74f, .82f, zFront + .01f), new Vector3(.34f, .2f, .03f));
        }

        static void RearPanel(B b, float W, float y, float z)
        {
            foreach (float s in new[] { -1f, 1f })
                b.Box(Tail, new Vector3(s * W * .36f, y, z - .01f), new Vector3(.34f, .16f, .03f));
        }

        static void Build(B b, VehicleDefinition d, bool wheels)
        {
            float L = d.lengthM, W = d.widthM, hw = W / 2f, zr = -L / 2f, zf = L / 2f;
            switch (d.id)
            {
                case "coupe": Passenger(b, L, W, new Cab { H = 1.32f, Shoulder = .82f, RearBase = .13f, RearRoof = .33f, FrontRoof = .55f, FrontBase = .7f }); break;
                case "hatchback": Passenger(b, L, W, new Cab { H = 1.5f, Shoulder = .9f, RearBase = .02f, RearRoof = .12f, FrontRoof = .55f, FrontBase = .72f }); break;
                case "convertible": Passenger(b, L, W, new Cab { H = 1.24f, Shoulder = .84f, RearBase = .2f, RearRoof = .3f, FrontRoof = .55f, FrontBase = .68f, Open = true }); break;
                case "station_wagon": Passenger(b, L, W, new Cab { H = 1.5f, Shoulder = .88f, RearBase = .02f, RearRoof = .08f, FrontRoof = .6f, FrontBase = .74f }); break;
                case "luxury": Passenger(b, L, W, new Cab { H = 1.5f, Shoulder = .93f, RearBase = .14f, RearRoof = .28f, FrontRoof = .6f, FrontBase = .72f }); break;
                case "suv": Passenger(b, L, W, new Cab { H = 1.82f, Shoulder = 1.1f, RearBase = .02f, RearRoof = .07f, FrontRoof = .62f, FrontBase = .74f }); break;
                case "minivan": Passenger(b, L, W, new Cab { H = 1.8f, Shoulder = 1.02f, RearBase = .02f, RearRoof = .06f, FrontRoof = .68f, FrontBase = .84f }); break;
                case "delivery_van": Passenger(b, L, W, new Cab { H = 2.45f, Shoulder = 1.2f, RearBase = .01f, RearRoof = .02f, FrontRoof = .78f, FrontBase = .86f }); b.Box(Paint, new Vector3(0, 1.75f, -.72f), new Vector3(W * .94f, 1.36f, L * .72f)); break;
                case "taxi":
                    Passenger(b, L, W, new Cab { H = 1.48f, Shoulder = .92f, RearBase = .13f, RearRoof = .28f, FrontRoof = .6f, FrontBase = .73f });
                    b.Box(Trim, new Vector3(0, 1.57f, .2f), new Vector3(.8f, .18f, .28f));
                    break;
                case "pickup":
                    Passenger(b, L, W, new Cab { H = 1.88f, Shoulder = 1.16f, RearBase = .4f, RearRoof = .42f, FrontRoof = .64f, FrontBase = .73f });
                    b.Box(Dark, new Vector3(0, 1.16f, zr + L * .2f), new Vector3(W * .78f, .03f, L * .34f));
                    break;
                case "semi":
                case "tanker_gasoline":
                case "tanker_diesel":
                {
                    float cabLength = 5.6f, rearEnd = zr + (L - cabLength - .7f);
                    TruckCab(b, W, zf - cabLength, cabLength * .62f + .2f, 3.25f);
                    b.Box(Paint, new Vector3(0, 3.5f, zf - cabLength + .95f), new Vector3(W * .92f, .55f, 1.8f));
                    b.Box(Dark, new Vector3(0, .7f, zf - cabLength * .55f), new Vector3(W * .8f, .28f, cabLength));
                    b.Box(Paint, new Vector3(0, 1f, zf - .55f), new Vector3(W * .94f, .55f, 1.1f));
                    if (d.id == "semi") b.Box(Trim, new Vector3(0, 2.55f, (zr + rearEnd) / 2f), new Vector3(W, 2.75f, rearEnd - zr));
                    else
                    {
                        b.Cylinder(Trim, new Vector3(0, 2.05f, (zr + rearEnd) / 2f), 1.15f, rearEnd - zr, false, 16);
                        b.Box(Dark, new Vector3(0, 3.22f, (zr + rearEnd) / 2f), new Vector3(.5f, .08f, rearEnd - zr - 1f));
                        b.Box(Dark, new Vector3(0, .95f, (zr + rearEnd) / 2f), new Vector3(W * .6f, .25f, rearEnd - zr));
                    }
                    RearPanel(b, W, 1.15f, zr);
                    if (wheels) Wheels(b, .52f, hw - .24f, .46f, zr + 1.6f, zr + 2.9f, zf - cabLength + 1f, zf - cabLength + 2.2f, zf - 1.2f);
                    return;
                }
                case "box_truck":
                    TruckCab(b, W, zf - 2.4f, 2.4f, 2.75f);
                    b.Box(Trim, new Vector3(0, 2.15f, zr + (L - 2.5f) / 2f), new Vector3(W, 2.6f, L - 2.5f));
                    b.Box(Dark, new Vector3(0, .65f, 0f), new Vector3(W * .76f, .28f, L - .5f));
                    RearPanel(b, W, 1f, zr);
                    if (wheels) Wheels(b, .48f, hw - .3f, .4f, zr + 1.9f, zf - 1.35f);
                    return;
                case "motorhome":
                    b.Box(Trim, new Vector3(0, 2.05f, 0f), new Vector3(W, 2.65f, L - .2f));
                    b.Box(Paint, new Vector3(0, 1.1f, 0f), new Vector3(W + .03f, .45f, L - .1f));
                    b.Box(Glass, new Vector3(0, 2.25f, zf - .1f), new Vector3(W * .86f, 1f, .05f));
                    b.Box(Glass, new Vector3(0, 2.4f, -.8f), new Vector3(W + .04f, .55f, L * .46f));
                    b.Box(Dark, new Vector3(0, .6f, 0), new Vector3(W * .8f, .3f, L - .6f));
                    foreach (float s in new[] { -1f, 1f }) b.Box(Head, new Vector3(s * hw * .72f, 1.05f, zf - .08f), new Vector3(.4f, .2f, .04f));
                    RearPanel(b, W, 1.1f, zr + .1f);
                    if (wheels) Wheels(b, .5f, hw - .3f, .34f, zr + 2.8f, zf - 2f);
                    return;
                case "fifth_wheel":
                    Passenger(b, 5.8f, 2.05f, new Cab { H = 1.88f, Shoulder = 1.16f, RearBase = .4f, RearRoof = .42f, FrontRoof = .64f, FrontBase = .73f }, zf - 2.9f);
                    b.Box(Trim, new Vector3(0, 2.1f, zr + (L - 3.8f) / 2f), new Vector3(W, 2.5f, L - 3.8f));
                    b.Box(Glass, new Vector3(0, 2.4f, zr + (L - 3.8f) / 2f), new Vector3(W + .04f, .5f, (L - 3.8f) * .4f));
                    b.Box(Dark, new Vector3(0, .68f, zr + (L - 3.8f) / 2f), new Vector3(W * .6f, .25f, L - 3.8f));
                    RearPanel(b, W, .95f, zr);
                    if (wheels) Wheels(b, .4f, hw - .28f, .3f, zr + 3.3f, zr + 4.3f, zf - 1.2f, zf - 4.5f);
                    return;
                case "suv_boat":
                    Passenger(b, 5f, 2f, new Cab { H = 1.82f, Shoulder = 1.1f, RearBase = .02f, RearRoof = .07f, FrontRoof = .62f, FrontBase = .74f }, zf - 2.5f);
                    b.Box(Dark, new Vector3(0, .5f, zr + 3.9f), new Vector3(.55f, .12f, 7.8f));
                    b.Loft(Trim, R(zr + .2f, W * .36f, .8f, 1.65f, W * .46f), R(zr + 4.8f, W * .32f, .75f, 1.72f, W * .46f), R(zr + 7.4f, .12f, 1.2f, 1.82f, .25f));
                    RearPanel(b, W * .8f, .75f, zr);
                    if (wheels) Wheels(b, .36f, hw - .2f, .24f, zr + 2.6f, zf - 1.1f, zf - 3.85f);
                    return;
                case "motorcycle_cruiser":
                case "motorcycle_sport":
                {
                    bool sport = d.id == "motorcycle_sport";
                    b.Loft(Paint, R(zr + .35f, .1f, .55f, sport ? 1f : .86f, .08f), R(0f, .17f, .42f, sport ? .88f : .9f, .16f), R(zf - .5f, .12f, .55f, sport ? 1.14f : 1.02f, .08f));
                    b.Box(Dark, new Vector3(0, 1.13f, sport ? 0f : -.12f), new Vector3(.4f, .68f, .32f));
                    b.Cylinder(Dark, new Vector3(0, 1.62f, sport ? .22f : -.08f), .15f, .3f, false, 8);
                    b.Box(Head, new Vector3(0, .98f, zf - .42f), new Vector3(.16f, .12f, .03f));
                    b.Box(Tail, new Vector3(0, .84f, zr + .28f), new Vector3(.14f, .07f, .03f));
                    if (wheels)
                        foreach (float z in new[] { zr + .36f, zf - .38f })
                        {
                            b.Cylinder(Dark, new Vector3(0, .33f, z), .33f, .13f, true);
                            b.Cylinder(Trim, new Vector3(0, .33f, z), .18f, .15f, true, 8);
                        }
                    return;
                }
                default: Passenger(b, L, W, new Cab { H = 1.46f, Shoulder = .92f, RearBase = .13f, RearRoof = .28f, FrontRoof = .6f, FrontBase = .73f }); break;
            }
            if (wheels)
            {
                float r = d.id == "pickup" || d.id == "suv" ? .4f : d.id == "delivery_van" ? .38f : .33f;
                Wheels(b, r, hw - .1f, .23f, zr + L * .2f, zf - L * .2f);
            }
        }

        static Mesh Lamp(VehicleDefinition d, params (float x, float y, float z, float sx, float sy)[] lamps)
        {
            var b = new B();
            foreach (var l in lamps) b.Box(0, new Vector3(l.x, l.y, l.z), new Vector3(l.sx, l.sy, .03f));
            return b.Done(d.id + "_lamp", 1);
        }

        static float LampHeight(VehicleDefinition d) => d.category == "motorcycle" ? .86f : d.lengthM > 6.8f ? 1.05f : d.id == "pickup" || d.id == "suv" || d.id == "minivan" ? .9f : .72f;
        static float FrontLampHeight(VehicleDefinition d) => d.category == "motorcycle" ? .98f : d.lengthM > 6.8f && d.id != "suv_boat" && d.id != "fifth_wheel" ? .84f : d.id == "pickup" || d.id == "suv" || d.id == "minivan" || d.id == "suv_boat" || d.id == "fifth_wheel" ? .78f : .58f;

        public static Set Create(VehicleDefinition d)
        {
            var near = new B(); Build(near, d, true);
            var far = new B(); Build(far, d, false);
            float x = d.widthM * (d.category == "motorcycle" ? .34f : .42f), y = LampHeight(d), f = FrontLampHeight(d);
            float back = -d.lengthM / 2f - .03f, front = d.lengthM / 2f + .04f;
            return new Set
            {
                Near = near.Done(d.id), Far = far.Done(d.id + "_far"),
                SignalLeft = Lamp(d, (-x, y, back, .2f, .13f), (-x, f, front, .2f, .1f)),
                SignalRight = Lamp(d, (x, y, back, .2f, .13f), (x, f, front, .2f, .1f)),
                Brake = d.category == "motorcycle" ? Lamp(d, (0f, y, back, .2f, .08f)) : Lamp(d, (-x * .78f, y, back, .34f, .15f), (x * .78f, y, back, .34f, .15f)),
                HighBeam = d.category == "motorcycle" ? Lamp(d, (0f, f, front, .22f, .16f)) : Lamp(d, (-x * .82f, f, front, .42f, .18f), (x * .82f, f, front, .42f, .18f))
            };
        }
    }
}
