using UnityEngine;

/// <summary>
/// A flock of soaring birds rendered as a single procedural mesh whose motion
/// (orbit, bank, flap/glide) runs entirely in the GreatZimbabwe/BirdFlock vertex
/// shader. One draw call, no skinned meshes, no Animators. Place the GameObject
/// at the point the flock should circle (e.g. the Hill Complex summit).
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class GZBirdFlock : MonoBehaviour
{
    [Range(1, 256)] public int birdCount = 28;
    public float orbitRadius = 95f;
    public float radiusJitter = 30f;
    [Tooltip("Height of the flock above this transform, in metres.")]
    public float baseAltitude = 30f;
    public float altitudeJitter = 14f;
    public float wingspan = 2.1f;
    public int seed = 20260707;
    public Material flockMaterial;

    void OnEnable()
    {
        var mf = GetComponent<MeshFilter>();
        if (mf.sharedMesh == null) BuildMesh();
        var mr = GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        if (flockMaterial != null && mr.sharedMaterial != flockMaterial)
            mr.sharedMaterial = flockMaterial;
        PushMaterialParams();
    }

    /// <summary>Keeps the shader's orbit params in sync with this component.</summary>
    public void PushMaterialParams()
    {
        if (flockMaterial == null) return;
        flockMaterial.SetFloat("_OrbitRadius", orbitRadius);
        flockMaterial.SetFloat("_BaseAltitude", baseAltitude);
        flockMaterial.SetFloat("_Wingspan", wingspan);
    }

    [ContextMenu("Rebuild Flock Mesh")]
    public void BuildMesh()
    {
        var rng = new System.Random(seed);
        int n = Mathf.Max(1, birdCount);
        var verts = new Vector3[n * 4];
        var uv0 = new Vector2[n * 4]; // orbit phase, flap phase
        var uv1 = new Vector2[n * 4]; // radius multiplier, altitude offset
        var uv2 = new Vector2[n * 4]; // size multiplier, speed multiplier
        var tris = new int[n * 6];

        // Unit-space bird: spine (nose/tail) plus two wingtips. The shader scales by
        // wingspan, flaps the tips (|x| == 1), and places the bird on its orbit.
        Vector3[] corners =
        {
            new Vector3( 0f,    0f,    1f),    // nose
            new Vector3( 0f,    0.06f, -0.85f),// tail
            new Vector3(-1f,    0f,   -0.15f), // left wingtip
            new Vector3( 1f,    0f,   -0.15f), // right wingtip
        };

        for (int i = 0; i < n; i++)
        {
            float orbitPhase = (float)rng.NextDouble();
            float flapPhase = (float)rng.NextDouble();
            float radiusMul = Mathf.Lerp(1f - radiusJitter / Mathf.Max(1f, orbitRadius),
                                         1f + radiusJitter / Mathf.Max(1f, orbitRadius),
                                         (float)rng.NextDouble());
            float altOff = Mathf.Lerp(-altitudeJitter, altitudeJitter, (float)rng.NextDouble());
            float sizeMul = Mathf.Lerp(0.8f, 1.3f, (float)rng.NextDouble());
            float speedMul = Mathf.Lerp(0.85f, 1.15f, (float)rng.NextDouble());

            for (int c = 0; c < 4; c++)
            {
                int vi = i * 4 + c;
                verts[vi] = corners[c];
                uv0[vi] = new Vector2(orbitPhase, flapPhase);
                uv1[vi] = new Vector2(radiusMul, altOff);
                uv2[vi] = new Vector2(sizeMul, speedMul);
            }

            int b = i * 4, ti = i * 6;
            tris[ti + 0] = b + 0; tris[ti + 1] = b + 2; tris[ti + 2] = b + 1; // nose, tipL, tail
            tris[ti + 3] = b + 0; tris[ti + 4] = b + 1; tris[ti + 5] = b + 3; // nose, tail, tipR
        }

        var mesh = new Mesh { name = "GZ_BirdFlock (generated)" };
        mesh.hideFlags = HideFlags.DontSave;
        mesh.vertices = verts;
        mesh.uv = uv0;
        mesh.uv2 = uv1;
        mesh.uv3 = uv2;
        mesh.triangles = tris;
        // Verts live near the origin but the shader moves them onto the orbit —
        // override bounds so the flock is never frustum-culled mid-flight.
        float ext = orbitRadius + radiusJitter + 40f;
        mesh.bounds = new Bounds(new Vector3(0f, baseAltitude, 0f),
                                 new Vector3(ext * 2f, (baseAltitude + altitudeJitter) * 2f + 40f, ext * 2f));

        GetComponent<MeshFilter>().sharedMesh = mesh;
    }
}
