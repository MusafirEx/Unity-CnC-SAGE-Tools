Shader "MusafirEx/SAGE Terrain Atlas Blend"
{
    Properties
    {
        _MainTex ("SAGE Terrain Base Atlas", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" }
        Pass
        {
            Cull Back
            ZTest LEqual
            ZWrite On
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uvBase : TEXCOORD0;
                float2 uvSecond : TEXCOORD1;
                float2 uvThird : TEXCOORD2;
                float4 blendInfo : TEXCOORD3;
                float2 cellFrac : TEXCOORD4;
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float2 uvBase : TEXCOORD0;
                float2 uvSecond : TEXCOORD1;
                float2 uvThird : TEXCOORD2;
                float4 blendInfo : TEXCOORD3;
                float2 cellFrac : TEXCOORD4;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.uvBase = v.uvBase;
                o.uvSecond = v.uvSecond;
                o.uvThird = v.uvThird;
                o.blendInfo = v.blendInfo;
                o.cellFrac = v.cellFrac;
                return o;
            }

            // Mirror OpenSAGE Terrain.frag CalculateBlendFactor:
            // 1 = right, 2 = top, 4 = top-right, 8 = top-left.
            // flags bit 0 = flipped; bit 1 = corner two-sided.
            float BlendFactor(float direction, float flags, float2 fraction)
            {
                bool flipped = fmod(flags, 2.0) >= 0.5;
                bool twoSided = flags >= 2.0;
                if (flipped)
                {
                    if (direction < 1.5)
                        fraction.x = 1.0 - fraction.x;
                    else
                        fraction.y = 1.0 - fraction.y;
                }

                if (abs(direction - 1.0) < 0.1) return saturate(fraction.x);
                if (abs(direction - 2.0) < 0.1) return saturate(fraction.y);
                if (abs(direction - 4.0) < 0.1)
                    fraction = 1.0 - fraction;
                else if (abs(direction - 8.0) < 0.1)
                    fraction.y = 1.0 - fraction.y;
                else return 0.0;

                float sum = fraction.x + fraction.y;
                return twoSided ? 1.0 - saturate(sum - 1.0)
                                : saturate(1.0 - sum);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 fraction = saturate(i.cellFrac);
                float3 baseColor = tex2D(_MainTex, i.uvBase).rgb;
                float firstFactor = BlendFactor(i.blendInfo.x, i.blendInfo.y, fraction);
                float secondFactor = BlendFactor(i.blendInfo.z, i.blendInfo.w, fraction);

                // Branches prevent unnecessary atlas lookups in cells with no
                // active blend description. Both samples use the same atlas.
                float3 result = baseColor;
                if (firstFactor > 0.0001)
                {
                    float3 secondary = tex2D(_MainTex, i.uvSecond).rgb;
                    result = lerp(result, secondary, firstFactor);
                }
                if (secondFactor > 0.0001)
                {
                    float3 tertiary = tex2D(_MainTex, i.uvThird).rgb;
                    result = lerp(result, tertiary, secondFactor);
                }
                return float4(result, 1.0);
            }
            ENDCG
        }
    }
    Fallback "Unlit/Texture"
}
