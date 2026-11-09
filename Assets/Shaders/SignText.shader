// Depth-tested text so road signs do not draw through overpasses and trees.
Shader "SurviveI95/SignText"
{
    Properties { _MainTex ("Font", 2D) = "white" {} _Color ("Color", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex; float4 _Color;
            struct a2v { float4 v : POSITION; float2 uv : TEXCOORD0; fixed4 c : COLOR; };
            struct v2f { float4 p : SV_POSITION; float2 uv : TEXCOORD0; fixed4 c : COLOR; };
            v2f vert(a2v i) { v2f o; o.p = UnityObjectToClipPos(i.v); o.uv = i.uv; o.c = i.c; return o; }
            fixed4 frag(v2f i) : SV_Target { fixed4 c = i.c * _Color; c.a *= tex2D(_MainTex, i.uv).a; return c; }
            ENDHLSL
        }
    }
}
