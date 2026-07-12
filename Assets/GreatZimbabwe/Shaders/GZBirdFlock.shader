// Soaring-bird flock: all motion (orbit, bank, intermittent flap/glide) computed
// in the vertex shader from per-bird phases packed into the mesh UV channels by
// GZBirdFlock.cs. Unlit dark silhouette, URP + SRP-batcher compatible.
Shader "GreatZimbabwe/BirdFlock"
{
    Properties
    {
        _Color("Silhouette Color", Color) = (0.10, 0.09, 0.08, 1)
        _OrbitRadius("Orbit Radius (m)", Float) = 95
        _OrbitSpeed("Orbit Speed (rad/s)", Float) = 0.12
        _BaseAltitude("Base Altitude (m)", Float) = 30
        _Wingspan("Wingspan (m)", Float) = 2.1
        _FlapSpeed("Flap Speed", Float) = 7
        _GlideCycle("Glide Cycle", Float) = 0.35
        _Bank("Bank (rad)", Float) = 0.25
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "BirdFlockUnlit"
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define GZ_TWO_PI 6.28318530718

            struct Attributes
            {
                float4 positionOS : POSITION;  // unit bird corner (|x|==1 -> wingtip)
                float2 uv0 : TEXCOORD0;        // orbit phase, flap phase (0..1)
                float2 uv1 : TEXCOORD1;        // radius multiplier, altitude offset (m)
                float2 uv2 : TEXCOORD2;        // size multiplier, speed multiplier
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _OrbitRadius;
                float _OrbitSpeed;
                float _BaseAltitude;
                float _Wingspan;
                float _FlapSpeed;
                float _GlideCycle;
                float _Bank;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                Varyings o;
                float t = _Time.y;

                float orbitPhase = v.uv0.x * GZ_TWO_PI;
                float flapPhase  = v.uv0.y * GZ_TWO_PI;
                float radiusMul  = v.uv1.x;
                float altOffset  = v.uv1.y;
                float sizeMul    = v.uv2.x;
                float speedMul   = v.uv2.y;

                // Orbit around the flock origin (object space).
                float ang = orbitPhase + t * _OrbitSpeed * speedMul;
                float r = _OrbitRadius * radiusMul;
                float3 center = float3(cos(ang) * r,
                                       _BaseAltitude + altOffset + sin(ang * 2.7 + flapPhase) * 3.0,
                                       sin(ang) * r);

                // Flight frame: forward along the orbit tangent, banked into the turn.
                float3 fwd = float3(-sin(ang), 0, cos(ang));
                float3 up = float3(0, 1, 0);
                float3 right = normalize(cross(up, fwd));
                float cb = cos(_Bank), sb = sin(_Bank);
                float3 rightB = right * cb + up * sb;
                float3 upB = up * cb - right * sb;

                // Intermittent flapping: soar-glide envelope, then a dihedral hold.
                float glide = smoothstep(0.2, 0.8, 0.5 + 0.5 * sin(t * _GlideCycle * speedMul + flapPhase));
                float flap = sin(t * _FlapSpeed * speedMul + flapPhase) * (0.2 + 0.8 * glide);
                float half_span = _Wingspan * sizeMul * 0.5;
                float flapWeight = abs(v.positionOS.x); // 1 at wingtips, 0 on the spine

                float3 local = v.positionOS.xyz * half_span;
                local.y += flap * flapWeight * half_span * 0.6;
                local.y += flapWeight * (1.0 - glide) * half_span * 0.25; // glide dihedral

                float3 posOS = center + rightB * local.x + upB * local.y + fwd * local.z;
                o.positionHCS = TransformObjectToHClip(posOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                return _Color;
            }
            ENDHLSL
        }
    }
}
