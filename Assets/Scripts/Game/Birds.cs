using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SurviveI95.Game
{
    /// <summary>
    /// Turkey vultures, a South Florida winter fixture: groups ("kettles") of 5 to 14 circling on warm air 60-250 m up,
    /// wings held in a shallow V and rocking side to side instead of flapping. A few kettles hang in the sky around the
    /// player's stretch of road and are moved ahead as the car passes them.
    /// </summary>
    public class Birds : MonoBehaviour
    {
        class Kettle { public Vector3 center; public List<(Transform t, float radius, float phase, float speed, float height, float rock)> birds = new List<(Transform, float, float, float, float, float)>(); }

        const int Kettles = 4;
        readonly List<Kettle> kettles = new List<Kettle>();
        PlayerCar car; Mesh mesh; Material mat;
        readonly System.Random rnd = new System.Random(33);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var car = FindAnyObjectByType<PlayerCar>();
            if (car == null || FindAnyObjectByType<Birds>() != null) return;
            new GameObject("Birds").AddComponent<Birds>().car = car;
        }

        void Start()
        {
            // A dark bird with long wings in a V: 1.8 m span, the pale flight feathers left to the haze.
            var v = new List<Vector3> { new Vector3(0, 0, .35f), new Vector3(0, 0, -.45f), new Vector3(-.9f, .22f, -.05f), new Vector3(-.9f, .22f, -.3f), new Vector3(.9f, .22f, -.05f), new Vector3(.9f, .22f, -.3f), new Vector3(0, 0, -.7f), new Vector3(-.12f, 0, -.55f), new Vector3(.12f, 0, -.55f) };
            var t = new List<int> { 0, 2, 1, 1, 2, 3, 0, 1, 4, 1, 5, 4, 6, 7, 8 };
            mesh = new Mesh { name = "Vulture" }; mesh.SetVertices(v); mesh.SetTriangles(t, 0); mesh.RecalculateNormals();
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); mat.SetColor("_BaseColor", new Color(.07f, .06f, .06f)); mat.SetFloat("_Smoothness", .1f); mat.SetFloat("_Cull", 0f);
            for (int i = 0; i < Kettles; i++) kettles.Add(Make(car.transform.position + car.transform.forward * (300f + i * 700f)));
        }

        Kettle Make(Vector3 near)
        {
            var k = new Kettle();
            float side = rnd.NextDouble() < .5 ? -1f : 1f;
            k.center = near + car.transform.right * side * (150f + (float)rnd.NextDouble() * 600f);
            int n = 5 + rnd.Next(10); float baseH = 60f + (float)rnd.NextDouble() * 120f;
            for (int i = 0; i < n; i++)
            {
                var go = new GameObject("Vulture", typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(transform);
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off;
                float radius = 25f + (float)rnd.NextDouble() * 45f;
                // About 10 m/s through the air, so a circle takes 15-30 s; all in one kettle turn the same way.
                k.birds.Add((go.transform, radius, (float)rnd.NextDouble() * Mathf.PI * 2f, 10f / radius, baseH + (float)rnd.NextDouble() * 70f, (float)rnd.NextDouble() * 10f));
            }
            return k;
        }

        void Update()
        {
            if (car == null) return;
            var fwd = car.transform.forward;
            for (int i = 0; i < kettles.Count; i++)
            {
                var k = kettles[i];
                // Passed and well behind: the kettle drifts on down the road ahead.
                if (Vector3.Dot(k.center - car.transform.position, fwd) < -600f)
                {
                    foreach (var b in k.birds) Destroy(b.t.gameObject);
                    kettles[i] = k = Make(car.transform.position + fwd * (1800f + (float)rnd.NextDouble() * 900f));
                }
                float time = Time.time;
                foreach (var (t, radius, phase, speed, height, rock) in k.birds)
                {
                    float a = phase + time * speed;
                    var pos = k.center + new Vector3(Mathf.Cos(a) * radius, height + Mathf.Sin(time * .07f + phase) * 8f, Mathf.Sin(a) * radius);
                    var dir = new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a));
                    // Banked into the turn, with the vulture's unsteady rock.
                    float bank = 18f + Mathf.Sin(time * 1.3f + rock) * 9f;
                    t.SetPositionAndRotation(pos, Quaternion.LookRotation(dir) * Quaternion.Euler(0, 0, bank));
                }
            }
        }
    }
}
