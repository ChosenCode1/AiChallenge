// Procedural savanna skybox for the Great Zimbabwe tour. Pure math (no textures):
// a deep highveld-blue zenith falling into a warm dust-haze horizon that echoes the
// granite ochres of the walls, a sun disc + glow that tracks the scene's directional
// light via _MainLightPosition (so lighting and sky never disagree), and two oct-cheap
// FBM cloud layers drifting on the wind. Cheap enough for the mobile build: ~5 noise
// octaves per pixel, no branches, no samplers.
Shader "GreatZimbabwe/Skybox"
{
    Properties
    {
        [Header(Sky)]
        _ZenithColor("Zenith Color", Color) = (0.145, 0.286, 0.502, 1)
        _MidColor("Mid Sky Color", Color) = (0.42, 0.56, 0.75, 1)
        _HorizonColor("Horizon Haze Color", Color) = (0.93, 0.74, 0.51, 1)
        _GroundColor("Ground Color (below horizon)", Color) = (0.34, 0.27, 0.21, 1)
        _HazeHeight("Haze Height", Range(1, 16)) = 5.5
        _SunWarmth("Sun-side Warmth", Range(0, 2)) = 0.9

        [Header(Sun)]
        _SunColor("Sun Color", Color) = (1.0, 0.93, 0.78, 1)
        _SunSize("Sun Disc Size", Range(0.0002, 0.01)) = 0.001
        _SunGlow("Sun Glow Intensity", Range(0, 3)) = 0.9

        [Header(Clouds)]
        _CloudLitColor("Cloud Lit Color", Color) = (1.0, 0.96, 0.90, 1)
        _CloudShadeColor("Cloud Shade Color", Color) = (0.66, 0.66, 0.72, 1)
        _CloudCoverage("Cloud Coverage", Range(0, 1)) = 0.38
        _CloudSoftness("Cloud Softness", Range(0.05, 0.6)) = 0.32
        _CloudScale("Cloud Scale", Range(0.5, 8)) = 2.2
        _CloudSpeed("Cloud Drift Speed", Range(0, 0.1)) = 0.008
        _CloudOpacity("Cloud Opacity", Range(0, 1)) = 0.85
    }
    SubShader
    {
        Tags { "RenderType" = "Background" "Queue" = "Background" "RenderPipeline" = "UniversalPipeline" "PreviewType" = "Skybox" }
        Pass
        {
            Name "GZSkybox"
            Cull Off
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ZenithColor;
                half4 _MidColor;
                half4 _HorizonColor;
                half4 _GroundColor;
                float _HazeHeight;
                half _SunWarmth;
                half4 _SunColor;
                float _SunSize;
                half _SunGlow;
                half4 _CloudLitColor;
                half4 _CloudShadeColor;
                float _CloudCoverage;
                float _CloudSoftness;
                float _CloudScale;
                float _CloudSpeed;
                half _CloudOpacity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 viewDirOS : TEXCOORD0; // skybox mesh is camera-centered, so OS pos == view ray
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                o.viewDirOS = v.positionOS.xyz;
                return o;
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            // 3 octaves: enough billow for cumulus at this projection, cheap on mobile.
            float fbm(float2 p)
            {
                float v = 0.53 * vnoise(p);
                p = p * 2.13 + 19.7;
                v += 0.28 * vnoise(p);
                p = p * 2.31 + 7.3;
                v += 0.19 * vnoise(p);
                return v;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 dir = normalize(i.viewDirOS);
                float3 sunDir = normalize(_MainLightPosition.xyz);
                float sd = dot(dir, sunDir); // -1..1, 1 = looking at the sun

                // --- sky gradient: zenith -> mid -> warm dust haze at the horizon ---
                float h = saturate(dir.y);
                half3 sky = lerp(_MidColor.rgb, _ZenithColor.rgb, pow(h, 0.75));
                float haze = pow(1.0 - h, _HazeHeight);
                // haze warms toward the sun's azimuth (dusty savanna air scatters warm light)
                half warmth = pow(saturate(sd) * 0.5 + 0.5, 3.0) * _SunWarmth;
                half3 hazeCol = _HorizonColor.rgb * (1.0 + warmth * half3(0.25, 0.08, -0.08));
                sky = lerp(sky, hazeCol, saturate(haze));

                // --- sun disc + glow (tracks the scene's directional light) ---
                float disc = smoothstep(1.0 - _SunSize, 1.0 - _SunSize * 0.35, sd);
                float glow = pow(saturate(sd), 48.0) * _SunGlow
                           + pow(saturate(sd), 8.0) * _SunGlow * 0.15;
                sky += _SunColor.rgb * (disc * 2.0 + glow);

                // --- clouds: FBM on a plane projection, drifting on the wind ---
                float horizFade = smoothstep(0.02, 0.18, dir.y);
                float2 cuv = dir.xz / max(dir.y, 0.02) * _CloudScale;
                cuv += _Time.y * _CloudSpeed * float2(13.0, 4.0);
                float dens = fbm(cuv);
                float cloud = smoothstep(1.0 - _CloudCoverage, 1.0 - _CloudCoverage + _CloudSoftness, dens);
                // second, cheaper offset sample toward the sun fakes a lit rim / shaded base
                float lit = fbm(cuv + sunDir.xz * 0.35) - dens;
                half3 cloudCol = lerp(_CloudShadeColor.rgb, _CloudLitColor.rgb, saturate(0.65 - lit * 2.5));
                cloudCol += _SunColor.rgb * pow(saturate(sd), 8.0) * 0.25; // sun-kissed near the disc
                sky = lerp(sky, cloudCol, cloud * _CloudOpacity * horizFade);

                // --- below the horizon: warm earth so nothing ever reads as void ---
                float ground = smoothstep(0.0, 0.08, -dir.y);
                half3 groundCol = lerp(hazeCol, _GroundColor.rgb, saturate(-dir.y * 4.0));
                half3 col = lerp(sky, groundCol, ground);

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
