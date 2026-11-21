// Two sky photos (equirectangular panoramas) crossfaded, each turned on its own so its sun sits where the real sun is.
// Used by DayNight to carry the sky from day through sunset and dusk into night.
Shader "SurviveI95/SkyBlend"
{
    Properties
    {
        [NoScaleOffset] _TexA ("Sky A", 2D) = "black" {}
        [NoScaleOffset] _TexB ("Sky B", 2D) = "black" {}
        _RotA ("Rotation A", Range(0, 360)) = 0
        _RotB ("Rotation B", Range(0, 360)) = 0
        _ExpA ("Exposure A", Float) = 1
        _ExpB ("Exposure B", Float) = 1
        _Blend ("Blend", Range(0, 1)) = 0
        _Glow ("City glow", Color) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _TexA, _TexB;
            float _RotA, _RotB, _ExpA, _ExpB, _Blend; float4 _Glow;

            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };

            v2f vert(appdata_base v)
            {
                v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.dir = v.vertex.xyz; return o;
            }

            // Direction to panorama coordinates, the way Unity's own Panoramic sky does it, turned about the vertical.
            float2 ToUV(float3 d, float deg)
            {
                float a = radians(deg), s = sin(a), c = cos(a);
                d = normalize(float3(c * d.x - s * d.z, d.y, s * d.x + c * d.z));
                float lat = acos(clamp(d.y, -1.0, 1.0)), lon = atan2(d.z, d.x);
                return float2(0.5, 1.0) - float2(lon * 0.5 / UNITY_PI, lat / UNITY_PI);
            }

            half4 frag(v2f i) : SV_Target
            {
                float2 ua = ToUV(i.dir, _RotA), ub = ToUV(i.dir, _RotB);
                // No mip seam: sample at the top level (the panoramas have no mipmaps).
                half3 a = tex2Dlod(_TexA, float4(ua, 0, 0)).rgb * _ExpA;
                half3 b = tex2Dlod(_TexB, float4(ub, 0, 0)).rgb * _ExpB;
                // City light glowing up from below the horizon, fading with height.
                float3 d = normalize(i.dir);
                half3 glow = _Glow.rgb * exp(-max(d.y, 0.0) * 6.0) * _Glow.a;
                return half4(lerp(a, b, _Blend) + glow, 1);
            }
            ENDCG
        }
    }
}
