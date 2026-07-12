// Minimal additive billboard shader for Fact-FX particle systems (the granary grain
// pour). Color comes entirely from the ParticleSystem's start color / color-over-lifetime
// (set from GZFactFXDirector.ThemeColor in setup — palette rule 3), so this shader stays
// a dumb tinted-sprite: no URP particle material GUI plumbing to fight.
Shader "GreatZimbabwe/FXParticleAdd"
{
    Properties
    {
        _BaseMap("Particle Texture", 2D) = "white" {}
        _Intensity("Intensity", Float) = 1.0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "FXParticleAdd"
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float _Intensity;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                half3 col = i.color.rgb * tex.rgb * (tex.a * i.color.a * _Intensity);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
