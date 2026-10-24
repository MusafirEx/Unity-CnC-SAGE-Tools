Shader "MusafirEx/SAGE Water Preview"
{
    Properties
    {
        _Color ("Water Tint", Color) = (0.12, 0.35, 0.46, 0.62)
        _Scroll ("Water Ripple Scroll", Float) = 0.01
        _WaveStrength ("Ripple Strength", Range(0, 1)) = 0.18
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _Scroll;
            float _WaveStrength;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;
                float phase = _Time.y * _Scroll * 6.2831853;
                float rippleA = sin((uv.x + uv.y * 0.3) * 6.2831853 + phase);
                float rippleB = sin((uv.y - uv.x * 0.2) * 9.424778 - phase * 0.7);
                float ripple = (rippleA * rippleB) * _WaveStrength;
                float shading = saturate(0.87 + ripple);
                return fixed4(_Color.rgb * shading, _Color.a);
            }
            ENDCG
        }
    }
    Fallback "Unlit/Transparent"
}
