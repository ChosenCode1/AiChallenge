// Transhumance route glow: soft ochre dashes flow along the herd's waypoint ribbon
// (GZRouteRibbon.cs) in the direction the animals walk while _GZFX_RouteGlow is live.
// Deliberately NOT gated by GZFocusGate: the whole point is showing the full seasonal
// route, which runs far beyond the cattle POI's FX radius; this channel has no other
// listener so there is nothing to cross-light. Collapses to nothing when idle.
Shader "GreatZimbabwe/RouteGlow"
{
    Properties
    {
        _Intensity("Intensity", Float) = 0.8
        _DashCount("Dashes Along Route", Float) = 26
        _FlowSpeed("Flow Speed (dashes/s)", Float) = 1.2
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "RouteGlow"
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "GZFactFXCommon.hlsl"

            float _GZFX_RouteGlow; // envelope-shaped channel (FX_PLAN.md §3)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;    // x: 0..1 along the route, y: 0/1 across
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float _Intensity;
                float _DashCount;
                float _FlowSpeed;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 posOS = v.positionOS.xyz * step(0.002, _GZFX_RouteGlow); // idle collapse
                o.positionHCS = TransformObjectToHClip(posOS);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // Dashes drift toward increasing u — the same direction the herd walks.
                float phase = i.uv.x * _DashCount - _Time.y * _FlowSpeed;
                float dash = pow(0.5 + 0.5 * sin(phase * 6.2831853), 3.0);

                float across = smoothstep(1.0, 0.15, abs(i.uv.y * 2.0 - 1.0));
                float tips = smoothstep(0.0, 0.05, i.uv.x) * smoothstep(1.0, 0.95, i.uv.x);

                float glow = (0.12 + dash) * across * tips * _GZFX_RouteGlow * _Intensity;
                return half4(_GZFX_Color.rgb * glow, 1.0);
            }
            ENDHLSL
        }
    }
}
