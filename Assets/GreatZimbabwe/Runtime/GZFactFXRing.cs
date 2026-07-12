using UnityEngine;

/// <summary>
/// The universal "the guide means HERE" pulse: a terrain-conforming disc whose expanding
/// ring is drawn by the GreatZimbabwe/FactFXRing shader from the _GZFX_* globals that
/// <see cref="GZFactFXDirector"/> writes. This component only builds the disc mesh, snaps
/// its vertices to the terrain at pulse start, and re-snaps while following the herd.
/// One draw call, additive, renderer disabled between pulses.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class GZFactFXRing : MonoBehaviour
{
    [Range(12, 128)] public int angularSegments = 64;
    [Range(4, 48)] public int radialSegments = 18;
    [Tooltip("Metres above the sampled terrain, so slopes never clip the ring.")]
    public float groundOffset = 0.45f;
    public Material ringMaterial;
    public GZTourCamera tourCamera;

    Mesh _mesh;
    Vector3[] _verts;
    GZTourPOI _poi;
    float _radius;

    /// <summary>Canonical FX radius for a POI — also what _GZFX_Focus.w carries.</summary>
    public static float RadiusFor(GZTourPOI poi)
    {
        return poi == null ? 60f : Mathf.Max(30f, poi.orbitRadius * 0.55f);
    }

    void OnEnable()
    {
        var mr = GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        if (ringMaterial != null && mr.sharedMaterial != ringMaterial)
            mr.sharedMaterial = ringMaterial;
        mr.enabled = false;
        EnsureMesh();
    }

    /// <summary>Show the ring at a POI's focus. The shader animates it from the globals.</summary>
    public void BeginPulse(GZTourPOI poi)
    {
        _poi = poi;
        if (poi == null || tourCamera == null) return;
        _radius = RadiusFor(poi);
        EnsureMesh();
        Snap();
        GetComponent<MeshRenderer>().enabled = true;
    }

    /// <summary>Called each frame while a cue plays; re-snaps when the focus moves (the herd).</summary>
    public void Tick()
    {
        if (_poi != null && _poi.focusKind == GZFocusKind.HerdCentroid) Snap();
    }

    public void EndPulse()
    {
        _poi = null;
        var mr = GetComponent<MeshRenderer>();
        if (mr != null) mr.enabled = false;
    }

    void EnsureMesh()
    {
        int va = angularSegments + 1, vr = radialSegments + 1;
        int count = va * vr;
        if (_mesh != null && _verts != null && _verts.Length == count) return;

        _mesh = new Mesh { name = "GZ_FactFXRing (generated)" };
        _mesh.hideFlags = HideFlags.DontSave;
        _mesh.MarkDynamic();
        _verts = new Vector3[count];
        var uv = new Vector2[count]; // x = radial 0..1, y = angle 0..1
        var tris = new int[angularSegments * radialSegments * 6];

        for (int r = 0; r < vr; r++)
            for (int a = 0; a < va; a++)
                uv[r * va + a] = new Vector2((float)r / radialSegments, (float)a / angularSegments);

        int t = 0;
        for (int r = 0; r < radialSegments; r++)
            for (int a = 0; a < angularSegments; a++)
            {
                int i0 = r * va + a;
                int i1 = i0 + 1;
                int i2 = i0 + va;
                int i3 = i2 + 1;
                tris[t++] = i0; tris[t++] = i2; tris[t++] = i1;
                tris[t++] = i1; tris[t++] = i2; tris[t++] = i3;
            }

        _mesh.vertices = _verts;
        _mesh.uv = uv;
        _mesh.triangles = tris;
        GetComponent<MeshFilter>().sharedMesh = _mesh;
    }

    /// <summary>Positions the disc at the POI focus and drapes every vertex over the terrain.</summary>
    void Snap()
    {
        if (_poi == null || tourCamera == null || _mesh == null) return;
        Vector3 focus = tourCamera.FocusOf(_poi, out float groundY);
        Vector3 center = new Vector3(focus.x, groundY, focus.z);
        transform.SetPositionAndRotation(center, Quaternion.identity);
        transform.localScale = Vector3.one;

        var terrain = Terrain.activeTerrain;
        int va = angularSegments + 1;
        for (int r = 0; r <= radialSegments; r++)
        {
            float rad = _radius * r / radialSegments;
            for (int a = 0; a <= angularSegments; a++)
            {
                float ang = Mathf.PI * 2f * a / angularSegments;
                Vector3 w = center + new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad);
                float y = terrain != null
                    ? terrain.SampleHeight(w) + terrain.transform.position.y
                    : center.y;
                _verts[r * va + a] = new Vector3(w.x - center.x, y - center.y + groundOffset, w.z - center.z);
            }
        }
        _mesh.vertices = _verts;
        _mesh.RecalculateBounds();
    }
}
