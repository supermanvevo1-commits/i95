using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace SurviveI95.Game
{
    /// <summary>
    /// Heat shimmer over the road and land ahead on a hot day: a slight boiling wobble in the distance near the horizon,
    /// on the main game camera only (not the mirrors, the minimap or screenshot cameras).
    /// </summary>
    public class HeatShimmer : MonoBehaviour
    {
        // Largest shift in pixels at 1080 lines, and how hot it is (1 = summer midday; the time of day will drive it later).
        public static float Strength = 2.2f, Heat = 1f;

        Material mat; Pass pass;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindAnyObjectByType<PlayerCar>() == null || FindAnyObjectByType<HeatShimmer>() != null) return;
            new GameObject("HeatShimmer").AddComponent<HeatShimmer>();
        }

        void OnEnable()
        {
            var sh = Resources.Load<Shader>("Shaders/HeatShimmer");
            if (sh == null) { enabled = false; return; }
            mat = new Material(sh) { hideFlags = HideFlags.DontSave };
            pass = new Pass(mat) { renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing };
            RenderPipelineManager.beginCameraRendering += Begin;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= Begin;
            if (mat != null) Destroy(mat);
        }

        void Begin(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam.cameraType != CameraType.Game || cam.targetTexture != null || cam != Camera.main || Heat <= 0f || Strength <= 0f) return;
            mat.SetFloat("_Strength", Strength); mat.SetFloat("_Heat", Heat);
            cam.GetUniversalAdditionalCameraData().scriptableRenderer.EnqueuePass(pass);
        }

        class Pass : ScriptableRenderPass
        {
            readonly Material mat;
            public Pass(Material m) { mat = m; requiresIntermediateTexture = true; ConfigureInput(ScriptableRenderPassInput.Depth); }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
            {
                var res = frame.Get<UniversalResourceData>();
                if (res.isActiveTargetBackBuffer) return;
                var src = res.activeColorTexture;
                var desc = graph.GetTextureDesc(src); desc.name = "HeatShimmer"; desc.clearBuffer = false;
                var dst = graph.CreateTexture(desc);
                graph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(src, dst, mat, 0), "HeatShimmer");
                res.cameraColor = dst;
            }
        }
    }
}
