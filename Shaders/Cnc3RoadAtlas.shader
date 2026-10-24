Shader "MusafirEx/SAGE Road Atlas"
{
    Properties
    {
        _MainTex ("Original SAGE Road Atlas", 2D) = "white" {}
        _Color ("Road Preview Tint", Color) = (0.35,0.35,0.35,1)
        _UseRoadTexture ("Use Source Texture", Float) = 0
        _WidthInTexture ("RoadWidthInTexture", Float) = 0.9
    }
    SubShader
    {
        Tags { "Queue"="Transparent+5" "RenderType"="Transparent"
               "IgnoreProjector"="True" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            fixed4 _Color;
            float _UseRoadTexture, _WidthInTexture;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float2 roadMode : TEXCOORD1;
            };
            struct v2f
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 roadMode : TEXCOORD1;
            };
            v2f vert(appdata input)
            {
                v2f o;
                o.position = UnityObjectToClipPos(input.vertex);
                o.uv = input.uv;
                o.roadMode = input.roadMode;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                if (_UseRoadTexture < 0.5)
                    return _Color;
                float2 sampleUV;
                // Roads.ini 3x3 atlas; the repeating straight road
                // occupies the upper third of the original TGA.
                // Unity V increases upward; top band center = 5/6.
                if (i.roadMode.x < 0.5)
                {
                    float u = frac(i.uv.x);
                    // Clamp away from tile edges to avoid atlas bleed.
                    u = lerp(0.001, 0.999, u);
                    sampleUV = float2(u,
                        0.8333333 + (i.uv.y - 0.5) *
                        (0.25 * _WidthInTexture));
                }
                else
                {
                    // Preliminary intersections:
                    // 3-way top-middle tile, 4-way top-right tile.
                    // Orientation-specific T/Y mappings await phase 5B.
                    float uTile = i.roadMode.x >= 1.5 ? 2.0/3.0 : 1.0/3.0;
                    sampleUV = float2(uTile + saturate(i.uv.x) / 3.0,
                        2.0/3.0 + saturate(i.uv.y) / 3.0);
                }
                fixed4 sampled = tex2D(_MainTex, sampleUV);
                return sampled * _Color;
            }
            ENDCG
        }
    }
    Fallback "Unlit/Transparent"
}
