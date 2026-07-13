// "Walking wealth", upgraded: this is now the herd's everyday look, not just the FX
// moment. Each animal gets a procedural Sanga/Nguni-style hide — a per-cow coat shade
// (dark brown to red-brown) broken by cream patches from object-space value noise —
// plus real main-light shadows, a soft hide sheen, a warm sun rim, and a subtle
// vertex-level gait bounce so the herd reads alive even as primitive blobs.
// Per-animal variety comes from _CowSeed, set per renderer by GZHerdRunner via
// MaterialPropertyBlock. While _GZFX_HerdGold is live the golden fresnel rim still
// ignites exactly as before — cattle rimmed in gold as the guide speaks the wealth
// fact (FX_PLAN.md §3). Opaque + ShadowCaster so the animals keep their ground
// shadows, and the ShadowCaster applies the same gait offset so shadows stay glued.
Shader "GreatZimbabwe/HerdGold"
{
    Properties
    {
        _BaseColor("Coat A (dark brown)", Color) = (0.16, 0.12, 0.09, 1)
        _CoatColorB("Coat B (red-brown)", Color) = (0.31, 0.16, 0.08, 1)
        _PatchColor("Hide Patch Color", Color) = (0.82, 0.76, 0.66, 1)
        _PatchScale("Patch Scale", Float) = 5.5
        _RimPower("Rim Power", Float) = 2.0
        _GaitSpeed("Gait Speed", Float) = 7.0
        _GaitAmplitude("Gait Bounce (m)", Float) = 0.035
        _CowSeed("Cow Seed (per-agent, set by GZHerdRunner)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _CoatColorB;
            half4 _PatchColor;
            float _PatchScale;
            float _RimPower;
            float _GaitSpeed;
            float _GaitAmplitude;
        CBUFFER_END

        // _CowSeed is per-instance so the whole herd GPU-instances into a handful of
        // draw calls (body mesh + head mesh) at any herd size — the mobile budget for
        // 100+ animals. Falls back to a plain per-renderer uniform when instancing is
        // off, which is the old MaterialPropertyBlock behavior.
        UNITY_INSTANCING_BUFFER_START(GZCow)
            UNITY_DEFINE_INSTANCED_PROP(float, _CowSeed)
        UNITY_INSTANCING_BUFFER_END(GZCow)

        // Rigid world-space gait offset — a function of time + seed only, so the
        // separate body/head renderers of one agent (same _CowSeed) move as a unit.
        // abs(sin) gives the two-beat hoof-fall bounce; the slow drift is ambling sway.
        float3 GZGaitOffset(float cowSeed)
        {
            float phase = _Time.y * _GaitSpeed + cowSeed * 39.77;
            float bob = abs(sin(phase)) * _GaitAmplitude;
            float2 sway = float2(sin(phase * 0.47), sin(phase * 0.53 + 1.7)) * (_GaitAmplitude * 0.4);
            return float3(sway.x, bob, sway.y);
        }
        ENDHLSL

        Pass
        {
            Name "HerdGoldForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "GZFactFXCommon.hlsl"

            float _GZFX_HerdGold; // envelope-shaped channel (FX_PLAN.md §3)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                // Object-space position keeps the hide pattern glued to the moving
                // animal (world-space noise would swim across the coat).
                float3 positionOS : TEXCOORD2;
                float fogFactor : TEXCOORD3;
                // Seed rides a varying so the fragment stage never touches the
                // instance buffer (cheaper, and identical either instancing path).
                float cowSeed : TEXCOORD4;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                float seed = UNITY_ACCESS_INSTANCED_PROP(GZCow, _CowSeed);
                o.cowSeed = seed;
                o.positionOS = v.positionOS.xyz;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz) + GZGaitOffset(seed);
                o.positionHCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fogFactor = ComputeFogFactor(o.positionHCS.z);
                return o;
            }

            // Per-cow hide: coat shade picked by seed, cream patches cut by two
            // octaves of object-space value noise, coverage also varied by seed so
            // some animals run nearly solid and others heavily patched.
            half3 GZCoatAlbedo(float3 posOS, float cowSeed)
            {
                float shade = GZHash21(float2(cowSeed, cowSeed * 1.37 + 0.13));
                half3 coat = lerp(_BaseColor.rgb, _CoatColorB.rgb, shade);

                float2 seedOffset = cowSeed * float2(17.3, 9.1);
                float3 p = posOS * _PatchScale;
                float n = GZValueNoise(p.xy + seedOffset) * 0.62
                        + GZValueNoise(p.zy * 1.9 + seedOffset.yx + 4.7) * 0.38;
                float coverage = lerp(0.74, 0.48, GZHash21(float2(cowSeed * 3.1, 1.0)));
                float patch = smoothstep(coverage, coverage + 0.08, n);
                return lerp(coat, _PatchColor.rgb, patch);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float shadow = lerp(1.0, mainLight.shadowAttenuation, 0.85);

                float halfLambert = dot(n, mainLight.direction) * 0.5 + 0.5;
                float diffuse = halfLambert * halfLambert * shadow; // squared: richer core shading
                half3 albedo = GZCoatAlbedo(i.positionOS, i.cowSeed);
                half3 col = albedo * (mainLight.color.rgb * diffuse + SampleSH(n));

                // Soft hide sheen + a warm sun rim so silhouettes pop against the terrain.
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.positionWS);
                float3 halfDir = normalize(mainLight.direction + viewDir);
                col += mainLight.color.rgb * (pow(saturate(dot(n, halfDir)), 32.0) * 0.15 * shadow);
                float rim = GZFresnel(n, viewDir, _RimPower);
                col += mainLight.color.rgb * albedo * (rim * rim * 0.35 * shadow);

                // The wealth rim: gated to the cattle cue's focus so a shared answer
                // elsewhere never gilds the herd.
                col += _GZFX_Color.rgb * (rim * _GZFX_HerdGold * GZFocusGate(i.positionWS) * 1.4);

                col = MixFog(col, i.fogFactor);
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
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            Varyings shadowVert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz)
                                  + GZGaitOffset(UNITY_ACCESS_INSTANCED_PROP(GZCow, _CowSeed));
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
