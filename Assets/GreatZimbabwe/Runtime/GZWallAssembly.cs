using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A ghost "study model" of the Great Enclosure's dry-stone masonry: ~350 individual
/// granite blocks in running-bond courses along a CURVED arc segment (the real wall is an
/// ellipse — no corners), with the inward lean and tapering thickness of the original.
/// All motion runs in the GreatZimbabwe/WallAssembly vertex shader: when the guide speaks
/// the no-mortar fact, _GZFX_WallAssembly rises and the blocks fly in and seat themselves
/// course by course, bottom first — gravity and skill made visible. Per-block data is
/// packed into the mesh UVs exactly like GZBirdFlock. One mesh, one draw call, and the
/// shader collapses all vertices while no cue is live so the idle cost is nil.
/// The arc reads from every azimuth of the orbiting tour camera (a flat slab is edge-on
/// half the time). Placement + scale come from GZFactFXSetup; the model must read as a
/// hologram, never as a reconstruction (FX_PLAN.md rule 2).
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class GZWallAssembly : MonoBehaviour
{
    [Range(4, 30)] public int courses = 14;
    [Range(4, 60)] public int blocksPerCourse = 24;
    [Tooltip("Radius of the wall's arc, object-space metres. Local +Z is the convex (outer) side.")]
    public float arcRadius = 15f;
    [Tooltip("Angular span of the arc segment, degrees.")]
    public float arcDegrees = 70f;
    public float wallHeight = 6f;
    [Tooltip("Block depth at the base; tapers toward the top like the real wall.")]
    public float blockDepth = 0.55f;
    [Tooltip("Inward lean of the wall as it rises, degrees (the real batter).")]
    public float leanDegrees = 4f;
    [Tooltip("How far blocks scatter before assembly, object-space metres. Pushed to the shader.")]
    public float scatterRadius = 12f;
    [Tooltip("Add the chevron crest band (drawn only while _GZFX_Chevron is live).")]
    public bool chevronCrest = true;
    public int seed = 20260710;
    public Material wallMaterial;

    void OnEnable()
    {
        var mf = GetComponent<MeshFilter>();
        if (mf.sharedMesh == null) BuildMesh();
        var mr = GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        if (wallMaterial != null && mr.sharedMaterial != wallMaterial)
            mr.sharedMaterial = wallMaterial;
        PushMaterialParams();
    }

    /// <summary>Keeps the shader's scatter range in sync (material is shared by all instances).</summary>
    public void PushMaterialParams()
    {
        if (wallMaterial == null) return;
        wallMaterial.SetFloat("_ScatterRadius", scatterRadius);
    }

    [ContextMenu("Rebuild Wall Mesh")]
    public void BuildMesh()
    {
        var rng = new System.Random(seed);
        var verts = new List<Vector3>(9000);
        var normals = new List<Vector3>(9000);
        var uv0 = new List<Vector2>(9000); // x: courseNorm (2 = chevron crest), y: per-block seed
        var uv1 = new List<Vector2>(9000); // block center XY | chevron strip UV
        var uv2 = new List<Vector2>(9000); // block center Z, free
        var tris = new List<int>(14000);

        float courseH = wallHeight / courses;
        float shear = Mathf.Tan(leanDegrees * Mathf.Deg2Rad);
        float arcRad = arcDegrees * Mathf.Deg2Rad;
        float baseW = arcRadius * arcRad / blocksPerCourse;

        for (int c = 0; c < courses; c++)
        {
            float courseNorm = courses <= 1 ? 0f : (float)c / (courses - 1);
            float y0 = c * courseH;
            float yMid = y0 + courseH * 0.5f;
            float halfH = courseH * 0.47f;                                    // 6% seam shows the coursing
            float depth = blockDepth * Mathf.Lerp(1.15f, 0.72f, courseNorm);  // taper as it rises
            float rCourse = arcRadius - shear * yMid;                         // the inward lean

            float theta = -arcRad * 0.5f;
            bool firstBlock = true;
            while (theta < arcRad * 0.5f - (baseW * 0.2f) / arcRadius)
            {
                float w = baseW * Mathf.Lerp(0.8f, 1.3f, (float)rng.NextDouble());
                if (firstBlock && (c & 1) == 1) w *= 0.55f;                   // running bond offset
                firstBlock = false;
                float dTheta = w / arcRadius;
                if (theta + dTheta > arcRad * 0.5f) dTheta = arcRad * 0.5f - theta;

                float mid = theta + dTheta * 0.5f;
                Vector3 tangent = new Vector3(Mathf.Cos(mid), 0f, -Mathf.Sin(mid));
                Vector3 radial = new Vector3(Mathf.Sin(mid), 0f, Mathf.Cos(mid));
                Vector3 center = new Vector3(Mathf.Sin(mid) * rCourse, yMid,
                                             Mathf.Cos(mid) * rCourse - arcRadius);
                float halfW = Mathf.Max(0.05f, dTheta * arcRadius * 0.5f - 0.03f); // dry joint

                AddOrientedBlock(verts, normals, uv0, uv1, uv2, tris,
                                 center, tangent, radial, halfW, halfH, depth * 0.5f,
                                 courseNorm, (float)rng.NextDouble());
                theta += dTheta;
            }
        }

        if (chevronCrest) AddChevronStrip(verts, normals, uv0, uv1, uv2, tris, shear, arcRad);

        var mesh = new Mesh { name = "GZ_WallAssembly (generated)" };
        mesh.hideFlags = HideFlags.DontSave;
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uv0);
        mesh.SetUVs(1, uv1);
        mesh.SetUVs(2, uv2);
        mesh.SetTriangles(tris, 0);
        // Verts sit at their seated positions but the shader flings them out to the
        // scatter radius — override bounds so mid-flight blocks are never culled.
        float ext = scatterRadius + 8f;
        mesh.bounds = new Bounds(new Vector3(0f, wallHeight * 0.5f + 4f, 0f),
                                 new Vector3((arcRadius + ext) * 2f, wallHeight + 18f + ext, (arcRadius + ext) * 2f));
        GetComponent<MeshFilter>().sharedMesh = mesh;
    }

    void AddOrientedBlock(List<Vector3> verts, List<Vector3> normals, List<Vector2> uv0,
                          List<Vector2> uv1, List<Vector2> uv2, List<int> tris,
                          Vector3 center, Vector3 tangent, Vector3 radial,
                          float halfW, float halfH, float halfD,
                          float courseNorm, float blockSeed)
    {
        var d0 = new Vector2(courseNorm, blockSeed);
        var d1 = new Vector2(center.x, center.y);
        var d2 = new Vector2(center.z, 0f);
        Vector3 up = Vector3.up;

        // Corner helper: sx along the tangent, sy up, sz along the outward radial.
        Vector3 C(float sx, float sy, float sz)
            => center + tangent * (sx * halfW) + up * (sy * halfH) + radial * (sz * halfD);

        // 6 faces with hard normals so the fresnel edge glow reads block by block.
        Face(verts, normals, uv0, uv1, uv2, tris, d0, d1, d2, radial,
             C(-1, -1, 1), C(1, -1, 1), C(1, 1, 1), C(-1, 1, 1));
        Face(verts, normals, uv0, uv1, uv2, tris, d0, d1, d2, -radial,
             C(1, -1, -1), C(-1, -1, -1), C(-1, 1, -1), C(1, 1, -1));
        Face(verts, normals, uv0, uv1, uv2, tris, d0, d1, d2, tangent,
             C(1, -1, 1), C(1, -1, -1), C(1, 1, -1), C(1, 1, 1));
        Face(verts, normals, uv0, uv1, uv2, tris, d0, d1, d2, -tangent,
             C(-1, -1, -1), C(-1, -1, 1), C(-1, 1, 1), C(-1, 1, -1));
        Face(verts, normals, uv0, uv1, uv2, tris, d0, d1, d2, up,
             C(-1, 1, 1), C(1, 1, 1), C(1, 1, -1), C(-1, 1, -1));
        Face(verts, normals, uv0, uv1, uv2, tris, d0, d1, d2, -up,
             C(-1, -1, -1), C(1, -1, -1), C(1, -1, 1), C(-1, -1, 1));
    }

    void AddChevronStrip(List<Vector3> verts, List<Vector3> normals, List<Vector2> uv0,
                         List<Vector2> uv1, List<Vector2> uv2, List<int> tris,
                         float shear, float arcRad)
    {
        // A curved ribbon floating on the crest line; the zigzag itself is an SDF in the
        // fragment shader, masked by _GZFX_Chevron and gated to the answer POI.
        float y0 = wallHeight + 0.06f, y1 = wallHeight + 0.72f;
        float r = arcRadius - shear * wallHeight;
        const int segments = 28;

        for (int s = 0; s < segments; s++)
        {
            float u0 = (float)s / segments, u1 = (float)(s + 1) / segments;
            float t0 = Mathf.Lerp(-arcRad * 0.5f, arcRad * 0.5f, u0);
            float t1 = Mathf.Lerp(-arcRad * 0.5f, arcRad * 0.5f, u1);
            Vector3 p0 = new Vector3(Mathf.Sin(t0) * r, 0f, Mathf.Cos(t0) * r - arcRadius);
            Vector3 p1 = new Vector3(Mathf.Sin(t1) * r, 0f, Mathf.Cos(t1) * r - arcRadius);
            Vector3 n = new Vector3(Mathf.Sin((t0 + t1) * 0.5f), 0f, Mathf.Cos((t0 + t1) * 0.5f));

            int b = verts.Count;
            verts.Add(p0 + Vector3.up * y0); uv1.Add(new Vector2(u0, 0f));
            verts.Add(p1 + Vector3.up * y0); uv1.Add(new Vector2(u1, 0f));
            verts.Add(p1 + Vector3.up * y1); uv1.Add(new Vector2(u1, 1f));
            verts.Add(p0 + Vector3.up * y1); uv1.Add(new Vector2(u0, 1f));
            for (int i = 0; i < 4; i++)
            {
                normals.Add(n);
                uv0.Add(new Vector2(2f, 0f)); // flag: chevron crest
                uv2.Add(Vector2.zero);
            }
            tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
            tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
        }
    }

    static void Face(List<Vector3> verts, List<Vector3> normals, List<Vector2> uv0,
                     List<Vector2> uv1, List<Vector2> uv2, List<int> tris,
                     Vector2 d0, Vector2 d1, Vector2 d2, Vector3 normal,
                     Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3)
    {
        int b = verts.Count;
        verts.Add(v0); verts.Add(v1); verts.Add(v2); verts.Add(v3);
        for (int i = 0; i < 4; i++)
        {
            normals.Add(normal);
            uv0.Add(d0); uv1.Add(d1); uv2.Add(d2);
        }
        tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
        tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
    }
}
