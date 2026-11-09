using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace SurviveI95.Game
{
    /// <summary>Live mirror images for the cockpit view. Cameras only run while the player is inside the car.</summary>
    public class MirrorRig : MonoBehaviour
    {
        Camera[] cams;

        void Awake()
        {
            cams = new[]
            {
                Make("Mirror_L", 256, 256, 12f, 0.5f, null),
                Make("Mirror_R", 256, 256, -12f, 0.5f, null),
                Make("Mirror_Rear", 512, 192, 0f, 0.2f, new Vector3(0, 1.1f, -2.45f)),
            };
            SetActive(true);
        }

        Camera Make(string part, int w, int h, float yaw, float near, Vector3? position)
        {
            var target = transform.Find("Model/" + part);
            if (target == null) return null;
            var r = target.GetComponent<MeshRenderer>();
            var rt = new RenderTexture(w, h, 16, RenderTextureFormat.ARGB32) { name = part };
            var go = new GameObject(part + "Camera", typeof(Camera));
            go.transform.SetParent(transform, false);
            go.transform.localPosition = position ?? transform.InverseTransformPoint(r.bounds.center);
            go.transform.localRotation = Quaternion.Euler(0, 180f + yaw, 0);
            var cam = go.GetComponent<Camera>();
            cam.targetTexture = rt; cam.fieldOfView = 34f; cam.nearClipPlane = near; cam.farClipPlane = 1500f; cam.depth = -10; cam.allowHDR = false; cam.allowMSAA = false;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderShadows = false; data.requiresDepthTexture = false; data.requiresColorTexture = false;
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mat.SetTexture("_BaseMap", rt);
            mat.SetColor("_BaseColor", new Color(.86f, .88f, .9f));
            r.sharedMaterial = mat;
            return cam;
        }

        public void SetActive(bool on)
        {
            foreach (var c in cams) if (c != null) c.enabled = on;
        }
    }
}
