using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Ghost "study model" of the Conical Tower at its real proportions (about ten metres
/// tall, five across at the base, tapering as it rises). The GreatZimbabwe/TowerScan
/// shader sweeps a horizontal cut plane up the drum as _GZFX_TowerScan rises: below the
/// plane the stone renders as solid concentric coursework, and a moving cut-disc shows
/// the cross-section — no door, no chamber, no stair, stone all the way through. The
/// disc is part of this same mesh (flagged in uv0.x) and the vertex shader lifts it to
/// the scan height. One mesh, one draw call; all verts collapse while the cue is idle.
/// Must read as a hologram beside the real tower, never a reconstruction (FX_PLAN rule 2).
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class GZTowerFX : MonoBehaviour
{
    [Tooltip("Object-space dimensions; the fact sheet's real numbers. Pushed to the shader.")]
    public float height = 10f;
    public float baseRadius = 2.6f;
    public float topRadius = 1.55f;
    [Range(12, 96)] public int segments = 48;
    public Material towerMaterial;

    void OnEnable()
    {
        var mf = GetComponent<MeshFilter>();
        if (mf.sharedMesh == null) BuildMesh();
        var mr = GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        if (towerMaterial != null && mr.sharedMaterial != towerMaterial)
            mr.sharedMaterial = towerMaterial;
        PushMaterialParams();
    }

    /// <summary>The scan shader needs the drum's proportions (single instance, shared material).</summary>
    public void PushMaterialParams()
    {
        if (towerMaterial == null) return;
        towerMaterial.SetFloat("_Height", height);
        towerMaterial.SetFloat("_BaseRadius", baseRadius);
        towerMaterial.SetFloat("_TopRadius", topRadius);
    }

    [ContextMenu("Rebuild Tower Mesh")]
    public void BuildMesh()
    {
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var uv0 = new List<Vector2>(); // x: flag (0 = shell, 2 = scan disc), y: heightNorm | radialNorm
        var tris = new List<int>();

        float slope = (baseRadius - topRadius) / Mathf.Max(height, 0.01f);
        int ringVerts = segments + 1;

        // ---- shell: one frustum strip, smooth outward normals ----
        for (int ring = 0; ring < 2; ring++)
        {
            float y = ring * height;
            float r = ring == 0 ? baseRadius : topRadius;
            for (int s = 0; s <= segments; s++)
            {
                float a = Mathf.PI * 2f * s / segments;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                verts.Add(dir * r + Vector3.up * y);
                normals.Add((dir + Vector3.up * slope).normalized);
                uv0.Add(new Vector2(0f, ring));
            }
        }
        for (int s = 0; s < segments; s++)
        {
            int b0 = s, b1 = s + 1, t0 = ringVerts + s, t1 = ringVerts + s + 1;
            tris.Add(b0); tris.Add(t0); tris.Add(b1);
            tris.Add(b1); tris.Add(t0); tris.Add(t1);
        }

        // ---- top cap ----
        int capCenter = verts.Count;
        verts.Add(Vector3.up * height);
        normals.Add(Vector3.up);
        uv0.Add(new Vector2(0f, 1f));
        int capRing = verts.Count;
        for (int s = 0; s <= segments; s++)
        {
            float a = Mathf.PI * 2f * s / segments;
            verts.Add(new Vector3(Mathf.Cos(a) * topRadius, height, Mathf.Sin(a) * topRadius));
            normals.Add(Vector3.up);
            uv0.Add(new Vector2(0f, 1f));
        }
        for (int s = 0; s < segments; s++)
        {
            tris.Add(capCenter); tris.Add(capRing + s + 1); tris.Add(capRing + s);
        }

        // ---- scan disc: authored at y=0 / base radius; the shader lifts and shrinks it ----
        int discCenter = verts.Count;
        verts.Add(Vector3.zero);
        normals.Add(Vector3.up);
        uv0.Add(new Vector2(2f, 0f));
        int discRing = verts.Count;
        for (int s = 0; s <= segments; s++)
        {
            float a = Mathf.PI * 2f * s / segments;
            verts.Add(new Vector3(Mathf.Cos(a) * baseRadius, 0f, Mathf.Sin(a) * baseRadius));
            normals.Add(Vector3.up);
            uv0.Add(new Vector2(2f, 1f));
        }
        for (int s = 0; s < segments; s++)
        {
            tris.Add(discCenter); tris.Add(discRing + s + 1); tris.Add(discRing + s);
        }

        var mesh = new Mesh { name = "GZ_TowerFX (generated)" };
        mesh.hideFlags = HideFlags.DontSave;
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uv0);
        mesh.SetTriangles(tris, 0);
        mesh.bounds = new Bounds(Vector3.up * height * 0.5f,
                                 new Vector3(baseRadius * 2.4f, height + 2f, baseRadius * 2.4f));
        GetComponent<MeshFilter>().sharedMesh = mesh;
    }
}
