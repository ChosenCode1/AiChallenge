// Shared helpers + the global FX channels written by GZFactFXDirector every frame.
// Include this from every Fact-FX shader; do not redeclare the globals.
// Channel registry and palette semantics: FX_PLAN.md §3.
#ifndef GZ_FACTFX_COMMON_INCLUDED
#define GZ_FACTFX_COMMON_INCLUDED

// ---- globals (set via Shader.SetGlobal*, deliberately outside any CBUFFER) ----
float _GZFX_Pulse;    // intensity envelope of the active cue, 0 -> 1 -> 0
float _GZFX_PulseT;   // eased normalized cue time 0 -> 1 (drives expansions/sweeps)
half4 _GZFX_Color;    // active cue theme color
float4 _GZFX_Focus;   // xyz = POI focus world pos (y = ground), w = FX radius in metres

float GZHash21(float2 p)
{
    p = frac(p * float2(234.34, 435.345));
    p += dot(p, p + 34.23);
    return frac(p.x * p.y);
}

// Cheap value noise; feed world-space coords so effects have no UV seams.
float GZValueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = GZHash21(i);
    float b = GZHash21(i + float2(1, 0));
    float c = GZHash21(i + float2(0, 1));
    float d = GZHash21(i + float2(1, 1));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// Gaussian-ish band centered at 'center'; width in the same units as x.
float GZSoftBand(float x, float center, float width)
{
    float d = (x - center) / max(width, 1e-4);
    return exp(-d * d);
}

// Ghost-model edge glow.
float GZFresnel(float3 normalWS, float3 viewDirWS, float power)
{
    return pow(1.0 - saturate(dot(normalize(normalWS), normalize(viewDirWS))), power);
}

// Spatial gate for FX that share a channel across POIs (e.g. _GZFX_WallAssembly drives
// study models at both the Great Enclosure and the East Ruins): full inside the active
// cue's FX radius (_GZFX_Focus.w), fading to nothing one radius further out. When no cue
// is playing _GZFX_Focus is zero and everything is gated shut.
float GZFocusGate(float3 positionWS)
{
    float d = distance(positionWS.xz, _GZFX_Focus.xz);
    return saturate(1.0 - (d - _GZFX_Focus.w) / max(_GZFX_Focus.w, 1.0));
}

#endif
