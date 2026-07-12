// Universal fact-cue ground pulse: an expanding ring draped over the terrain by
// GZFactFXRing.cs. All animation comes from the _GZFX_* globals (GZFactFXCommon.hlsl)
// written by GZFactFXDirector — this shader just draws the front, an echo, and a faint
// interior wash in the cue's theme color. Additive, URP + SRP-batcher compatible.
Shader "GreatZimbabwe/FactFXRing"
{
    Properties
    {
        _Intensity("Intensity", Float) = 0.85
        _BandWidth("Band Width (0-1 radial)", Float) = 0.035
        _ShimmerScale("Shimmer Scale (1/m)", Float) = 0.14
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "FactFXRing"
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "GZFactFXCommon.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;         // x = radial 0..1, y = angle 0..1
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                float _Intensity;
                float _BandWidth;
                float _ShimmerScale;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionHCS = TransformWorldToHClip(o.positionWS);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float r = i.uv.x;
                float front = lerp(0.05, 1.0, _GZFX_PulseT);

                float band = GZSoftBand(r, front, _BandWidth);
                float echo = GZSoftBand(r, front * 0.8, _BandWidth * 1.6) * 0.22;
                float wash = smoothstep(front, front * 0.2, r) * 0.025; // faint fill behind the front

                // World-space shimmer: no angular seam, stays put while the ring expands.
                float shimmer = 0.7 + 0.3 * GZValueNoise(i.positionWS.xz * _ShimmerScale
                                                         + float2(0.0, _Time.y * 0.6));
                float edgeFade = smoothstep(1.0, 0.9, r); // never hard-clip at the mesh rim

                float glow = ((band + echo) * shimmer + wash) * edgeFade * _GZFX_Pulse * _Intensity;
                return half4(_GZFX_Color.rgb * glow, 1.0);
            }
            ENDHLSL
        }
    }
}
