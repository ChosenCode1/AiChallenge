// Conical Tower solidity scan: a horizontal cut plane sweeps up the ghost drum as
// _GZFX_TowerScan rises. Below the plane the stone reads as solid coursework; the moving
// cut-disc (flagged verts, lifted in the vertex shader) shows the cross-section —
// no door, no chamber, no stair, granite all the way through. ZWrite On + clip() per the
// Phase 2 as-built note so stacked ghost surfaces never blow out to white.
Shader "GreatZimbabwe/TowerScan"
{
    Properties
    {
        _Intensity("Intensity", Float) = 0.5
        _Height("Tower Height (m, object space)", Float) = 10
        _BaseRadius("Base Radius (m)", Float) = 2.6
        _TopRadius("Top Radius (m)", Float) = 1.55
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "TowerScan"
            Blend One One
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "GZFactFXCommon.hlsl"

            float _GZFX_TowerScan; // envelope-shaped channel (FX_PLAN.md §3)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv0 : TEXCOORD0;   // x: flag (0 shell, 2 scan disc), y: heightNorm | radialNorm
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 data : TEXCOORD2;  // x: flag, y: uv0.y, z: object-space Y
            };

            CBUFFER_START(UnityPerMaterial)
                float _Intensity;
                float _Height;
                float _BaseRadius;
                float _TopRadius;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 posOS = v.positionOS.xyz;
                float scan = saturate(_GZFX_TowerScan);

                if (v.uv0.x > 1.5)
                {
                    // The cut disc rides the scan plane, shrinking to the drum's taper.
                    float rNow = lerp(_BaseRadius, _TopRadius, scan) * 0.99;
                    posOS.xz *= rNow / max(_BaseRadius, 0.01);
                    posOS.y = scan * _Height + 0.015;
                }

                // Inert between cues: collapse to zero-area triangles.
                posOS *= step(0.002, _GZFX_TowerScan);

                o.positionWS = TransformObjectToWorld(posOS);
                o.positionHCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.data = float4(v.uv0.x, v.uv0.y, posOS.y, 0);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float gate = GZFocusGate(i.positionWS);
                float scan = saturate(_GZFX_TowerScan);
                float scanY = scan * _Height;
                float noise = GZValueNoise(i.positionWS.xz * 2.7 + i.positionWS.y * 1.3);
                float glow;

                if (i.data.x > 1.5)
                {
                    // Cut face: solid concentric coursework across the whole section.
                    float rings = abs(frac(i.data.y * 8.0) * 2.0 - 1.0);
                    float ringLine = smoothstep(0.55, 0.95, rings);
                    glow = 0.42 * (0.72 + 0.28 * noise) * (0.75 + 0.45 * ringLine);
                }
                else
                {
                    float objY = i.data.z;
                    float3 viewDir = _WorldSpaceCameraPos - i.positionWS;
                    float fres = GZFresnel(i.normalWS, viewDir, 2.4);

                    // Below the plane: revealed solid masonry (course bands). Above: ghost shell.
                    float course = smoothstep(0.3, 0.7, abs(frac(objY * 1.35) * 2.0 - 1.0));
                    float solid = 0.26 * (0.65 + 0.35 * course) * (0.75 + 0.25 * noise) + 0.4 * fres;
                    float ghost = 0.05 + 0.55 * fres;
                    glow = lerp(ghost, solid, step(objY, scanY));

                    // The scan line itself burns brightest.
                    glow += GZSoftBand(objY, scanY, 0.16) * 0.9;
                }

                glow *= saturate(scan * 2.5) * gate * _Intensity;
                clip(glow - 0.004); // don't depth-occlude other FX with invisible pixels
                return half4(_GZFX_Color.rgb * glow, 1.0);
            }
            ENDHLSL
        }
    }
}
