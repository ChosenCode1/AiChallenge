// Ghost dry-stone wall study model: individual blocks fly in from a scatter cloud and
// seat themselves course by course (bottom first) as _GZFX_WallAssembly rises, then
// return to the wind as it falls — mortarless building made visible, clearly a hologram.
// The chevron crest quad draws its zigzag only while _GZFX_Chevron is live. Both effects
// are gated to the active cue's POI by GZFocusGate so the shared channel never lights a
// study model at another site. All per-block data comes from mesh UVs (GZWallAssembly.cs).
// While no cue is live every vertex collapses to the origin: idle cost is zero fragments.
Shader "GreatZimbabwe/WallAssembly"
{
    Properties
    {
        _Intensity("Intensity", Float) = 0.45
        _ScatterRadius("Scatter Radius (m, object space)", Float) = 12
        _AssembleWindow("Per-Block Assemble Window", Float) = 0.33
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "WallAssembly"
            Blend One One
            // ZWrite ON is deliberate for an additive effect: only the nearest block face
            // glows per pixel, so the stacked layers of a 4-deep ghost wall never
            // accumulate to white. Near-black pixels clip() so they don't write depth.
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "GZFactFXCommon.hlsl"

            // Per-effect channels (envelope-shaped by GZFactFXDirector, see FX_PLAN.md §3).
            float _GZFX_WallAssembly;
            float _GZFX_Chevron;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv0 : TEXCOORD0;   // x: courseNorm (2 = chevron crest), y: block seed
                float2 uv1 : TEXCOORD1;   // block center XY | chevron strip UV
                float2 uv2 : TEXCOORD2;   // block center Z
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 data : TEXCOORD2;  // x: flag/courseNorm, y: seed, zw: strip UV
            };

            CBUFFER_START(UnityPerMaterial)
                float _Intensity;
                float _ScatterRadius;
                float _AssembleWindow;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                Varyings o;
                float flag = v.uv0.x;
                float seed = v.uv0.y;
                float3 posOS = v.positionOS.xyz;
                float3 nOS = v.normalOS;

                // The chevron cue also needs the wall standing beneath its crest.
                float drive = max(_GZFX_WallAssembly, _GZFX_Chevron * 0.85);

                if (flag < 1.5)
                {
                    float3 center = float3(v.uv1.x, v.uv1.y, v.uv2.x);
                    // Bottom courses seat first; per-block jitter breaks the wave up.
                    float start = flag * 0.55 + seed * 0.12;
                    float p = saturate((drive - start) / max(_AssembleWindow, 1e-3));
                    p = p * p * (3.0 - 2.0 * p);

                    float a = seed * 39.0;
                    float rr = _ScatterRadius * (0.65 + 0.7 * frac(seed * 7.13));
                    float3 scatter = float3(cos(a) * rr, 3.0 + 6.0 * frac(seed * 3.71), sin(a) * rr);

                    // Rigid per-block motion: tumble (yaw) unwinds as the block seats.
                    float3 local = posOS - center;
                    float ang = (1.0 - p) * (seed - 0.5) * 7.0;
                    float s, c;
                    sincos(ang, s, c);
                    local.xz = float2(local.x * c - local.z * s, local.x * s + local.z * c);
                    nOS.xz = float2(nOS.x * c - nOS.z * s, nOS.x * s + nOS.z * c);

                    posOS = center + scatter * (1.0 - p) + local;
                }

                // Inert between cues: collapse to zero-area triangles, zero fragments.
                posOS *= step(0.002, drive);

                o.positionWS = TransformObjectToWorld(posOS);
                o.positionHCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(nOS);
                o.data = float4(flag, seed, v.uv1.x, v.uv1.y);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float gate = GZFocusGate(i.positionWS);
                float glow;

                if (i.data.x > 1.5)
                {
                    // Chevron crest: SDF zigzag, wiped left to right as the cue rises.
                    float2 suv = i.data.zw;
                    float zig = abs(frac(suv.x * 14.0) * 2.0 - 1.0);
                    float stroke = 1.0 - saturate(abs(suv.y - zig) / 0.2);
                    stroke *= stroke;
                    float wipe = saturate(_GZFX_Chevron * 2.4 - suv.x * 1.3);
                    glow = stroke * wipe * _GZFX_Chevron * 1.7;
                }
                else
                {
                    // Ghost masonry: fresnel edges dominate so the coursing reads block
                    // by block; faint faces keep the silhouette legible.
                    float3 viewDir = _WorldSpaceCameraPos - i.positionWS;
                    float fres = GZFresnel(i.normalWS, viewDir, 2.6);
                    float vis = max(_GZFX_WallAssembly, _GZFX_Chevron * 0.85);
                    float shimmer = 0.85 + 0.3 * GZValueNoise(i.positionWS.xy * 0.7
                                                              + float2(0.0, _Time.y * 0.35));
                    glow = (0.12 + 0.9 * fres) * vis * shimmer;
                }

                glow *= gate * _Intensity;
                clip(glow - 0.004); // don't depth-occlude other FX with invisible pixels
                return half4(_GZFX_Color.rgb * glow, 1.0);
            }
            ENDHLSL
        }
    }
}
