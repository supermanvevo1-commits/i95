// Heat shimmer: light grazing low over hot ground bends in the rising air, so distant things near the horizon
// wobble. The wobble grows with distance past about 80 m and only where the line of sight skims the ground.
Shader "Hidden/SurviveI95/HeatShimmer"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off
        Pass
        {
            Name "HeatShimmer"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            float _Strength;   // largest shift, in pixels at 1080 lines
            float _Heat;       // 0 cool morning to 1 midday summer

            float Weight(float2 uv)
            {
                float raw = SampleSceneDepth(uv);
                float3 world = ComputeWorldSpacePosition(uv, raw, UNITY_MATRIX_I_VP);
                float3 ray = world - _WorldSpaceCameraPos;
                float dist = min(length(ray), 3000.0);
                float level = abs(ray.y) / max(dist, 1.0);
                return smoothstep(80.0, 400.0, dist) * (1.0 - smoothstep(.004, .026, level));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float w = Weight(uv) * _Heat;
                if (w < .002) return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                float t = _Time.y;
                // Fine cells of rising air: bands that boil upward, broken up across the view.
                float2 p = uv * float2(_ScreenParams.x / _ScreenParams.y, 1.0) * 1080.0;
                float a = sin(p.y * .55 + t * 11.0 + sin(p.x * .045 + t * 1.7) * 3.0);
                float b = sin(p.y * .9 - t * 7.0 + sin(p.x * .11 - t * 2.3) * 2.0);
                float c = sin(p.x * .07 + p.y * .2 + t * 5.0);
                float2 shift = float2(c * .35, a * .6 + b * .4) * _Strength * w / 1080.0;
                // Keep the sample on the same side of the depth edge, so near objects don't smear into it.
                float2 uv2 = uv + shift;
                if (Weight(uv2) * _Heat < w * .5) uv2 = uv;
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv2);
            }
            ENDHLSL
        }
    }
}
