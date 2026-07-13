using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The always-flying aerial tour camera. It is permanently on an orbit track
/// around some focus (the "grand tour" is simply the overview POI's big ring);
/// <see cref="FlyTo"/> re-targets it and smooth-damped pursuit produces the
/// transition flight — there are no cuts and no explicit transition splines.
/// A terrain-clearance clamp (with velocity lookahead) guarantees the camera
/// never gets close enough to the ground to expose photogrammetry artifacts,
/// including while transiting across the Hill Complex.
/// </summary>
public class GZTourCamera : MonoBehaviour
{
    [Header("Motion feel")]
    [Tooltip("SmoothDamp time for position pursuit. Higher = lazier, more cinematic.")]
    public float smoothTime = 2.4f;
    [Tooltip("Speed ceiling for transit flights, m/s.")]
    public float maxSpeed = 90f;
    [Tooltip("Exponential responsiveness of the look-at rotation.")]
    public float lookResponsiveness = 2.0f;
    [Tooltip("How quickly the camera climbs when the terrain clamp pushes it up.")]
    public float climbResponsiveness = 3.0f;

    [Header("Live steering (safe to change at runtime)")]
    [Tooltip("Extra metres added to every altitude — a global 'fly higher/lower' knob.")]
    public float altitudeBias = 0f;
    [Tooltip("Multiplies all orbit speeds.")]
    public float speedMultiplier = 1f;
    [Tooltip("+1 = counter-clockwise orbits, -1 = clockwise.")]
    public float orbitSign = 1f;

    [Header("Quality floor (asset-friendly heights)")]
    [Tooltip("Hard floor on orbit height above the focus ground, metres. No POI tuning or 'low'/'close' view hint can take the camera below this.")]
    public float minAltitude = 70f;
    [Tooltip("Hard floor on terrain clearance everywhere (orbits and transits), metres. Overrides any smaller per-POI clearance.")]
    public float minClearanceFloor = 50f;

    public GZTourPOI CurrentPoi { get; private set; }

    /// <summary>True once the camera has settled onto the current POI's orbit ring.
    /// Latched until the next FlyTo so label consumers never see it flicker.</summary>
    public bool OnStation { get; private set; }

    List<GZTourPOI> _pois;
    GZTourPOI _overview;
    GZHerdRunner _herd;
    Terrain _terrain;

    float _angleDeg;             // current azimuth on the orbit ring
    float _altMul = 1f, _radMul = 1f, _spdMul = 1f;
    Vector3 _velocity;           // SmoothDamp state
    Vector3 _smoothedFocus;
    bool _focusInitialised;

    /// <summary>Called once by the director before play begins in earnest.</summary>
    public void Configure(List<GZTourPOI> pois, GZTourPOI overview, GZHerdRunner herd)
    {
        _pois = pois;
        _overview = overview;
        _herd = herd;
        _terrain = Terrain.activeTerrain;
        if (CurrentPoi == null && overview != null) FlyTo(overview, null, null);
    }

    /// <summary>
    /// Re-target the tour. View hints scale the POI's own tuned numbers:
    /// view high/low/close changes altitude+radius, orbit slow/fast changes speed.
    /// Null/empty hints keep the POI defaults.
    /// </summary>
    public void FlyTo(GZTourPOI poi, string view, string orbit)
    {
        if (poi == null) return;
        bool samePoi = CurrentPoi == poi;
        CurrentPoi = poi;
        OnStation = false;

        switch ((view ?? "").Trim().ToLowerInvariant())
        {
            case "high":  _altMul = 1.7f;  _radMul = 1.25f; break;
            case "low":   _altMul = 0.45f; _radMul = 0.75f; break;
            case "close": _altMul = 0.8f;  _radMul = 0.55f; break;
            default:      _altMul = 1f;    _radMul = 1f;    break;
        }
        switch ((orbit ?? "").Trim().ToLowerInvariant())
        {
            case "slow": _spdMul = 0.55f; break;
            case "fast": _spdMul = 1.8f;  break;
            default:     _spdMul = 1f;    break;
        }

        // Enter the new ring on the side we are already on: no orbit snap.
        if (!samePoi)
        {
            Vector3 focus = ResolveFocus(poi, out _);
            Vector3 off = transform.position - focus;
            if (new Vector2(off.x, off.z).sqrMagnitude > 1f)
                _angleDeg = Mathf.Atan2(off.x, off.z) * Mathf.Rad2Deg;
            _smoothedFocus = focus;
            _focusInitialised = true;
        }
    }

    public void ResumeGrandTour()
    {
        if (_overview != null) FlyTo(_overview, null, null);
    }

    /// <summary>World focus point of a POI as the tour flies it (herd-centroid aware). Used by the Fact-FX layer.</summary>
    public Vector3 FocusOf(GZTourPOI poi, out float groundY) => ResolveFocus(poi, out groundY);

    void LateUpdate()
    {
        if (!Application.isPlaying || CurrentPoi == null) return;
        float dt = Time.deltaTime;
        var poi = CurrentPoi;

        float radius = Mathf.Max(20f, poi.orbitRadius * _radMul);
        float altitude = Mathf.Max(minAltitude, poi.altitude * _altMul + altitudeBias);
        float clearance = Mathf.Max(poi.minClearance, minClearanceFloor);

        // Advance along the ring at constant linear speed.
        float degPerSec = Mathf.Rad2Deg * (poi.cruiseSpeed * _spdMul * Mathf.Max(0.05f, speedMultiplier)) / radius;
        _angleDeg = Mathf.Repeat(_angleDeg + degPerSec * orbitSign * dt, 360f);

        Vector3 rawFocus = ResolveFocus(poi, out float focusGroundY);
        if (!_focusInitialised) { _smoothedFocus = rawFocus; _focusInitialised = true; }
        _smoothedFocus = Vector3.Lerp(_smoothedFocus, rawFocus, 1f - Mathf.Exp(-1.5f * dt));

        Vector3 ringDir = Quaternion.Euler(0f, _angleDeg, 0f) * Vector3.forward;
        Vector3 desired = _smoothedFocus + ringDir * radius;
        desired.y = focusGroundY + altitude;
        desired.y = Mathf.Max(desired.y, TerrainY(desired) + clearance);

        Vector3 pos = Vector3.SmoothDamp(transform.position, desired, ref _velocity, smoothTime, maxSpeed, dt);

        if (!OnStation && (pos - desired).magnitude < Mathf.Max(15f, radius * 0.25f))
            OnStation = true;

        // Terrain clearance clamp with lookahead: only ever pushes the camera UP,
        // so it climbs over the hill during transits instead of clipping the cliff.
        Vector3 ahead = pos + _velocity * 1.2f;
        float minY = Mathf.Max(TerrainY(pos), TerrainY(ahead)) + clearance;
        if (pos.y < minY)
        {
            pos.y = Mathf.Lerp(pos.y, minY, 1f - Mathf.Exp(-climbResponsiveness * dt));
            _velocity.y = Mathf.Max(_velocity.y, 0f);
        }
        transform.position = pos;

        Vector3 gaze = _smoothedFocus - pos;
        if (gaze.sqrMagnitude > 0.01f)
        {
            var want = Quaternion.LookRotation(gaze.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, want, 1f - Mathf.Exp(-lookResponsiveness * dt));
        }
    }

    Vector3 ResolveFocus(GZTourPOI poi, out float groundY)
    {
        if (poi.focusKind == GZFocusKind.HerdCentroid && _herd == null)
            _herd = FindFirstObjectByType<GZHerdRunner>();

        if (poi.focusKind == GZFocusKind.HerdCentroid && _herd != null && _herd.transform.childCount > 0)
        {
            Vector3 sum = Vector3.zero;
            int n = 0;
            foreach (Transform agent in _herd.transform) { sum += agent.position; n++; }
            Vector3 c = sum / n;
            groundY = c.y;
            return c + Vector3.up * poi.focusHeightOffset;
        }

        Vector3 a = poi.anchor;
        groundY = TerrainY(a);
        return new Vector3(a.x, groundY + poi.focusHeightOffset, a.z);
    }

    float TerrainY(Vector3 worldPos)
    {
        if (_terrain == null) _terrain = Terrain.activeTerrain;
        if (_terrain == null) return 0f;
        return _terrain.SampleHeight(worldPos) + _terrain.transform.position.y;
    }
}
