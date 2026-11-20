using System.Collections.Generic;
using UnityEngine;

namespace SurviveI95.Game
{
    /// <summary>
    /// Keeps the pond fountains alive: each plume surges and settles as the pump's jets fluctuate and turns slowly, so
    /// the spray never looks frozen. Only fountains within about 500 m of the camera are touched.
    /// </summary>
    public class Fountains : MonoBehaviour
    {
        readonly List<(Transform t, Vector3 scale, float phase)> list = new List<(Transform, Vector3, float)>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var root = GameObject.Find("Fountains");
            if (root != null && root.GetComponent<Fountains>() == null) root.AddComponent<Fountains>();
        }

        void Start()
        {
            int i = 0;
            foreach (Transform t in transform) list.Add((t, t.localScale, i++ * 2.39f));
        }

        void Update()
        {
            var cam = Camera.main; if (cam == null) return;
            var at = cam.transform.position; float time = Time.time;
            foreach (var (t, scale, phase) in list)
            {
                if ((t.position - at).sqrMagnitude > 500f * 500f) continue;
                float surge = 1f + .07f * Mathf.Sin(time * 1.6f + phase) + .035f * Mathf.Sin(time * 4.7f + phase * 1.7f);
                float puff = 1f + .04f * Mathf.Sin(time * 2.3f + phase * .6f);
                t.localScale = new Vector3(scale.x * puff, scale.y * surge, scale.z * puff);
                t.Rotate(0f, 9f * Time.deltaTime, 0f, Space.World);
            }
        }
    }
}
