using System.Collections.Generic;
using UnityEngine;

namespace SurviveI95.Game
{
    /// <summary>
    /// The palms near the camera sway in the wind. South Florida's trade wind blows from the east at about 10 mph most
    /// of the dry season, so palms lean a little west and rock with the gusts, each at its own pace. Only palms within
    /// 150 m move; the far ones are pictures and too small to tell.
    /// </summary>
    public class PlantSway : MonoBehaviour
    {
        readonly List<(Transform t, Quaternion rest, float phase, float stiff)> palms = new List<(Transform, Quaternion, float, float)>();
        static readonly Vector3 WindTo = new Vector3(-1f, 0f, -.15f); // from the east, a touch from the south

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var root = GameObject.Find("Palms");
            if (root == null || FindAnyObjectByType<PlantSway>() != null) return;
            var s = root.AddComponent<PlantSway>();
            int i = 0;
            foreach (Transform t in root.transform)
            {
                float h = Mathf.Abs(Mathf.Sin(t.position.x * 12.9898f + t.position.z * 78.233f)) * 43758.5453f % 1f;
                s.palms.Add((t, t.rotation, h * 6.28f, .8f + h * .5f)); i++;
            }
        }

        void LateUpdate()
        {
            var cam = Camera.main; if (cam == null) return;
            var eye = cam.transform.position; float time = Time.time;
            var axis = Vector3.Cross(Vector3.up, WindTo.normalized);
            // Gusts roll across the land, so neighbors move together a moment apart.
            foreach (var (t, rest, phase, stiff) in palms)
            {
                var p = t.position;
                if ((p - eye).sqrMagnitude > 150f * 150f) continue;
                float gust = .6f + .4f * Mathf.Sin(time * .23f + p.x * .004f) * Mathf.Sin(time * .37f + p.z * .003f);
                float lean = (1.1f + Mathf.Sin(time * 1.1f / stiff + phase) * .9f + Mathf.Sin(time * 2.7f + phase * 1.7f) * .25f) * gust;
                t.rotation = Quaternion.AngleAxis(lean, axis) * rest;
            }
        }
    }
}
