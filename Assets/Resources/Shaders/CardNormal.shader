// Used only while building: records which way each surface of a plant faces, as seen from the capture camera,
// so the plant's flat "cards" can be lit by the scene's sun like the real model.
Shader "Hidden/SurviveI95/CardNormal"
{
    Properties
    {
        _MainTex ("Base", 2D) = "white" {}
        _Cutoff ("Cutoff", Float) = 0.5
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float _Cutoff;
            struct A { float4 pos : POSITION; float3 n : NORMAL; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float2 uv : TEXCOORD1; };
            V vert(A i)
            {
                V o; o.pos = TransformObjectToHClip(i.pos.xyz);
                o.n = TransformWorldToViewDir(TransformObjectToWorldNormal(i.n)); o.uv = i.uv; return o;
            }
            half4 frag(V i, bool front : SV_IsFrontFace) : SV_Target
            {
                half a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a;
                clip(a - _Cutoff);
                float3 n = normalize(i.n); if (!front) n = -n;
                // In view space the camera looks down -z, so a surface facing it already points +z, as a normal map expects.
                if (n.z < 0) n.z = 0.05;
                return half4(normalize(n) * 0.5 + 0.5, 1);
            }
            ENDHLSL
        }
    }
}
