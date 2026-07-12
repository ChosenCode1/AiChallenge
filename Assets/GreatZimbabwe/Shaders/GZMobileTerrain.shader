// Mobile terrain: pre-lit albedo (sun + shadows baked offline by GZMobileBaker),
// so no realtime lighting, no shadowmaps, no normal maps — one texture sample plus
// a scrolling cloud-shadow multiply. Fragment cost is as close to free as URP gets.
Shader "GreatZimbabwe/MobileTerrainUnlit"
{
    Properties
    {
        _BaseMap("Lit Albedo (baked)", 2D) = "white" {}
        _CloudTex("Cloud Shadow Map", 2D) = "white" {}
        _CloudStrength("Cloud Strength", Range(0, 1)) = 0.5
        _CloudTileMeters("Cloud Tile (m)", Float) = 1400
        _WindDirDeg("Wind Direction (deg)", Float) = 25
        _WindSpeed("Wind Speed (m/s)", Float) = 9
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "MobileTerrainUnlit"
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);      SAMPLER(sampler_BaseMap);
            TEXTURE2D(_CloudTex);     SAMPLER(sampler_CloudTex);

            CBUFFER_START(UnityPerMaterial)
                float _CloudStrength;
                float _CloudTileMeters;
                float _WindDirDeg;
                float _WindSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 worldXZ : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionHCS = TransformWorldToHClip(posWS);
                o.uv = v.uv;
                o.worldXZ = posWS.xz;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 col = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                float rad = radians(_WindDirDeg);
                float2 wind = float2(cos(rad), sin(rad)) * (_WindSpeed * _Time.y);
                float2 cuv = (i.worldXZ + wind) / _CloudTileMeters;
                half cloud = SAMPLE_TEXTURE2D(_CloudTex, sampler_CloudTex, cuv).r;
                half atten = 1.0h - _CloudStrength * (1.0h - cloud);
                col.rgb *= atten;
                return col;
            }
            ENDHLSL
        }
    }
}
