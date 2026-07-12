using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A terrain-draped ribbon along the herd's transhumance route (GZHerdRunner.waypoints).
/// The GreatZimbabwe/RouteGlow shader scrolls soft dashes along it toward the destination
/// while _GZFX_RouteGlow is live — "the herds moved seasonally between pastures" drawn on
/// the very ground the live herd is walking. Mesh verts are baked in world space (the
/// transform stays at origin); one draw call; the shader collapses everything when idle.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class GZRouteRibbon : MonoBehaviour
{
    [Header("Wiring (set by GZFactFXSetup)")]
    public GZHerdRunner herd;

    public float width = 3.2f;
    [Tooltip("Sampling step along the route, metres.")]
    public float step = 5f;
    [Tooltip("Metres above the sampled terrain.")]
    public float groundOffset = 0.35f;
    public Material ribbonMaterial;

    void OnEnable()
    {
        var mf = GetComponent<MeshFilter>();
        if (mf.sharedMesh == null) BuildMesh();
        var mr = GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        if (ribbonMaterial != null && mr.sharedMaterial != ribbonMaterial)
            mr.sharedMaterial = ribbonMaterial;
    }

    [ContextMenu("Rebuild Ribbon Mesh")]
    public void BuildMesh()
    {
        if (herd == null || herd.waypoints == null || herd.waypoints.Length < 2) return;
        var wp = herd.waypoints;

        // Flatten the polyline and measure it (same convention as GZHerdRunner).
        float total = 0f;
        var lens = new float[wp.Length - 1];
        for (int i = 1; i < wp.Length; i++)
        {
            Vector3 a = wp[i - 1], b = wp[i];
            a.y = 0f; b.y = 0f;
            lens[i - 1] = Vector3.Distance(a, b);
            total += lens[i - 1];
        }
        if (total < 1f) return;

        var terrain = Terrain.activeTerrain;
        var verts = new List<Vector3>();
        var uv = new List<Vector2>(); // x: 0..1 along the route, y: 0/1 across
        var tris = new List<int>();

        int samples = Mathf.Max(2, Mathf.CeilToInt(total / Mathf.Max(1f, step)));
        for (int s = 0; s <= samples; s++)
        {
            float d = total * s / samples;
            SamplePath(wp, lens, d, out Vector3 pos, out Vector3 dir);
            Vector3 side = Vector3.Cross(Vector3.up, dir).normalized * (width * 0.5f);

            Vector3 l = pos - side, r = pos + side;
            if (terrain != null)
            {
                l.y = terrain.SampleHeight(l) + terrain.transform.position.y + groundOffset;
                r.y = terrain.SampleHeight(r) + terrain.transform.position.y + groundOffset;
            }
            float u = (float)s / samples;
            verts.Add(l); uv.Add(new Vector2(u, 0f));
            verts.Add(r); uv.Add(new Vector2(u, 1f));
            if (s > 0)
            {
                int b = verts.Count - 4;
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
            }
        }

        var mesh = new Mesh { name = "GZ_RouteRibbon (generated)" };
        mesh.hideFlags = HideFlags.DontSave;
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        GetComponent<MeshFilter>().sharedMesh = mesh;
    }

    static void SamplePath(Vector3[] wp, float[] lens, float meters, out Vector3 pos, out Vector3 dir)
    {
        pos = wp[0];
        dir = Vector3.forward;
        float remaining = meters;
        for (int i = 1; i < wp.Length; i++)
        {
            float seg = lens[i - 1];
            Vector3 a = wp[i - 1], b = wp[i];
            Vector3 flat = b - a; flat.y = 0f;
            if (remaining <= seg || i == wp.Length - 1)
            {
                float t = seg <= 0f ? 0f : Mathf.Clamp01(remaining / seg);
                pos = Vector3.Lerp(a, b, t);
                dir = flat.normalized;
                return;
            }
            remaining -= seg;
        }
    }
}
