// "Walking wealth": the herd's everyday look is the same dark matte as the old URP/Lit
// placeholder material, but while _GZFX_HerdGold is live a golden fresnel rim ignites —
// cattle literally rimmed in gold as the guide speaks the wealth fact. GZFactFXSetup
// swaps this shader onto GZ_HerdPlaceholder.mat (the _BaseColor property carries over)
// and RemoveFactFX restores URP/Lit. Works unchanged on any future animal prefab that
// keeps the material. Opaque + ShadowCaster so the animals keep their ground shadows.
Shader "GreatZimbabwe/HerdGold"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (0.16, 0.12, 0.09, 1)
        _RimPower("Rim Power", Float) = 2.0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "HerdGoldForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "GZFactFXCommon.hlsl"

            float _GZFX_HerdGold; // envelope-shaped channel (FX_PLAN.md §3)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _RimPower;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionHCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                Light mainLight = GetMainLight();
                float ndl = dot(n, mainLight.direction) * 0.5 + 0.5; // half-Lambert
                half3 col = _BaseColor.rgb * (mainLight.color.rgb * ndl + SampleSH(n));

                // The wealth rim: gated to the cattle cue's focus so a shared answer
                // elsewhere never gilds the herd.
                float3 viewDir = _WorldSpaceCameraPos - i.positionWS;
                float rim = GZFresnel(n, viewDir, _RimPower);
                col += _GZFX_Color.rgb * (rim * _GZFX_HerdGold * GZFocusGate(i.positionWS) * 1.4);

                return half4(col, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            Varyings shadowVert(Attributes v)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionHCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    o.positionHCS.z = min(o.positionHCS.z, o.positionHCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    o.positionHCS.z = max(o.positionHCS.z, o.positionHCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif
                return o;
            }

            half4 shadowFrag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
