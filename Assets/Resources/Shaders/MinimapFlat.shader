// Flat vertex colors with no light and no fog, for the minimap's own camera.
Shader "Hidden/SurviveI95/MinimapFlat"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 position : POSITION; float4 color : COLOR; };
            struct Varyings { float4 position : SV_POSITION; float4 color : COLOR; };
            Varyings vert(Attributes a) { Varyings v; v.position = TransformObjectToHClip(a.position.xyz); v.color = a.color; return v; }
            half4 frag(Varyings v) : SV_Target { return v.color; }
            ENDHLSL
        }
    }
}
