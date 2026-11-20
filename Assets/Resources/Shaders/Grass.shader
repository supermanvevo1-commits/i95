// Roadside grass: blade cards colored from a dark root to a sunlit tip, with dry straw patches, swaying in the
// trade wind (a steady lean, a flutter, and gusts that roll across the field). Lit by the sun with shadows and
// passing cloud shadows, and the sky. Thins out with distance into the lawn texture underneath.
Shader "SurviveI95/Grass"
{
    Properties
    {
        _BaseMap ("Blades", 2D) = "white" {}
        _Wind ("Wind", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST; float _Wind;
        CBUFFER_END

        struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };

        // Trade wind from the east-southeast, blowing toward the west.
        static const float2 WindDir = float2(-0.989, 0.148);

        float3 Sway(float3 world, float tip, float height)
        {
            float t = _Time.y;
            float gust = sin(dot(world.xz, WindDir) * 0.09 - t * 2.1) * 0.5 + 0.5;
            gust = gust * gust;
            float flutter = sin(t * 5.3 + world.x * 1.7 + world.z * 1.3) * 0.35 + sin(t * 8.1 + world.z * 2.9) * 0.15;
            float lean = (0.25 + gust * 0.9 + flutter * (0.4 + gust)) * _Wind;
            float bend = tip * tip * height * 0.45 * lean;
            return float3(WindDir.x * bend, -abs(bend) * 0.35, WindDir.y * bend);
        }

        float FadeCut(float3 world)
        {
            float d = distance(world, _WorldSpaceCameraPos);
            return lerp(0.45, 1.02, saturate((d - 35.0) / 33.0));
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 world : TEXCOORD1; float fog : TEXCOORD2; float3 tip : TEXCOORD3; float3 sky : TEXCOORD4; UNITY_VERTEX_INPUT_INSTANCE_ID };

            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                float a = frac(sin(dot(i, float2(127.1, 311.7))) * 43758.5), b = frac(sin(dot(i + float2(1, 0), float2(127.1, 311.7))) * 43758.5);
                float c = frac(sin(dot(i + float2(0, 1), float2(127.1, 311.7))) * 43758.5), d = frac(sin(dot(i + float2(1, 1), float2(127.1, 311.7))) * 43758.5);
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            Varyings Vert(Attributes v)
            {
                Varyings o; UNITY_SETUP_INSTANCE_ID(v); UNITY_TRANSFER_INSTANCE_ID(v, o);
                float3 world = TransformObjectToWorld(v.positionOS.xyz);
                float height = length(float3(UNITY_MATRIX_M._m01, UNITY_MATRIX_M._m11, UNITY_MATRIX_M._m21));
                world += Sway(world, v.uv.y, height);
                o.world = world; o.uv = v.uv;
                o.positionCS = TransformWorldToHClip(world);
                o.fog = ComputeFogFactor(o.positionCS.z);
                // March is the dry season: straw-colored patches among the green, and mottling at a few sizes. Worked
                // out per blade corner rather than per pixel: it changes slowly.
                float dry = smoothstep(0.55, 0.8, Noise(world.xz * 0.045) * 0.7 + Noise(world.xz * 0.21) * 0.3);
                float mott = Noise(world.xz * 0.6) * 0.5 + Noise(world.xz * 2.3) * 0.5;
                o.tip = lerp(float3(0.36, 0.44, 0.15), float3(0.6, 0.53, 0.3), dry) * (0.82 + mott * 0.36);
                o.sky = SampleSH(float3(0, 1, 0));
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                clip(tex.a - FadeCut(i.world));
                float3 albedo = lerp(float3(0.07, 0.11, 0.035), i.tip, saturate(i.uv.y * 1.3)) * (tex.r * 0.6 + 0.4);
                Light sun = GetMainLight(TransformWorldToShadowCoord(i.world), i.world, half4(1, 1, 1, 1));
                float3 up = float3(0, 1, 0);
                float wrap = 0.55 + 0.45 * saturate(dot(up, sun.direction));
                float3 lit = albedo * (sun.color * sun.shadowAttenuation * sun.distanceAttenuation * wrap + i.sky);
                lit = MixFog(lit, i.fog);
                return half4(lit, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 world : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            Varyings Vert(Attributes v)
            {
                Varyings o; UNITY_SETUP_INSTANCE_ID(v); UNITY_TRANSFER_INSTANCE_ID(v, o);
                float3 world = TransformObjectToWorld(v.positionOS.xyz);
                float height = length(float3(UNITY_MATRIX_M._m01, UNITY_MATRIX_M._m11, UNITY_MATRIX_M._m21));
                world += Sway(world, v.uv.y, height);
                o.world = world; o.uv = v.uv; o.positionCS = TransformWorldToHClip(world);
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a - FadeCut(i.world));
                return 0;
            }
            ENDHLSL
        }
    }
}
