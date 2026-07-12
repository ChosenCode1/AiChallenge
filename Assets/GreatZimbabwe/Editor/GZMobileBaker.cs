using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Bakes the Great Zimbabwe Terrain into a mobile-ready scene:
///  1. Captures a 4K "lit albedo" (ortho + sun shadows + AO baked in) from the live
///     PC scene, with cloud cookie and ambient life temporarily hidden.
///  2. Generates chunked, zone-aware decimated meshes straight from the heightmap —
///     fine grid over the monuments (walls live in the DSM!), medium on high-relief
///     ground (hill cliffs), coarse on the flat valley. Skirts hide seam cracks.
///  3. Builds Assets/Scenes/GreatZimbabwe_Mobile.unity: unlit terrain chunks with
///     scrolling cloud shadows in-shader, bird flock, herd, 60 fps cap.
/// The source GreatZimbabwe scene and its TerrainData are never modified.
/// Run via: eval --code 'return GZMobileBaker.BakeAll();'
/// </summary>
public static class GZMobileBaker
{
    const string MobileDir = "Assets/GreatZimbabwe/Mobile";
    const string AlbedoPath = MobileDir + "/GZ_MobileLitAlbedo.png";
    const string ChunksPath = MobileDir + "/GZ_MobileChunks.asset";
    const string MatPath = MobileDir + "/GZ_MobileTerrain.mat";
    const string ScenePath = "Assets/Scenes/GreatZimbabwe_Mobile.unity";
    const string CookiePath = "Assets/GreatZimbabwe/GZ_CloudCookie.png";
    const string BirdMatPath = "Assets/GreatZimbabwe/GZ_BirdFlock.mat";
    const string HerdMatPath = "Assets/GreatZimbabwe/GZ_HerdPlaceholder.mat";
    const string RPAssetPath = "Assets/Settings/PC_RPAsset.asset";

    const int Chunks = 8;                 // 8x8 grid over the site
    const float TierGreatEnclosure = 1.0f;// GE has the tallest, sharpest wall crests
    const float TierMonument = 1.4f;      // metres between verts near other monuments
    const float TierRelief = 3.5f;        // steep ground (hill cliffs)
    const float TierFlat = 7.0f;          // open valley floor
    const float ReliefThresholdM = 5.5f;  // max height delta between coarse samples
    const float SkirtDrop = 2.0f;
    const float CrestCellReliefM = 1.2f;  // cells steeper than this get crest-max sampling

    public static string BakeAll()
    {
        var srcScene = SceneManager.GetActiveScene();
        if (!srcScene.path.EndsWith("GreatZimbabwe.unity"))
            return "ABORT: open Assets/Scenes/GreatZimbabwe.unity first (active: " + srcScene.path + ")";
        var terrain = Terrain.activeTerrain;
        if (terrain == null) return "ABORT: no active terrain in scene.";
        var td = terrain.terrainData;
        Vector3 tsize = td.size;

        Directory.CreateDirectory(MobileDir);
        var sb = new StringBuilder();

        // ---- 1. lit albedo from the live scene (reads only; settings restored) ----
        sb.Append(CaptureLitAlbedo(tsize)).Append("\n");

        // ---- 2. chunk meshes from the heightmap ----
        var meshes = BuildChunkMeshes(td, tsize, sb);

        if (AssetDatabase.LoadAssetAtPath<Object>(ChunksPath) != null)
            AssetDatabase.DeleteAsset(ChunksPath);
        var container = new Mesh { name = "GZ_MobileChunks_Container" };
        AssetDatabase.CreateAsset(container, ChunksPath);
        foreach (var m in meshes) AssetDatabase.AddObjectToAsset(m, ChunksPath);
        AssetDatabase.ImportAsset(ChunksPath);

        // ---- 3. material ----
        var shader = Shader.Find("GreatZimbabwe/MobileTerrainUnlit");
        if (shader == null) return "ABORT: MobileTerrainUnlit shader not found (compile error?)";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, MatPath);
        }
        mat.shader = shader;
        mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(AlbedoPath));
        mat.SetTexture("_CloudTex", AssetDatabase.LoadAssetAtPath<Texture2D>(CookiePath));
        mat.SetFloat("_CloudStrength", 0.5f);
        mat.SetFloat("_CloudTileMeters", 1400f);
        EditorUtility.SetDirty(mat);

        // ---- 4. build the mobile scene ----
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        PopulateMobileScene(mat, meshes);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        sb.Append("Scene saved: ").Append(ScenePath).Append(" (left open in editor)");
        return sb.ToString();
    }

    // ---------- lit albedo ----------

    static string CaptureLitAlbedo(Vector3 tsize)
    {
        // Temporarily raise shadow reach/quality so the whole site self-shadows in
        // the top-down capture, hide animated/ambient content, then restore all.
        var rp = AssetDatabase.LoadMainAssetAtPath(RPAssetPath);
        var so = new SerializedObject(rp);
        int oldRes = so.FindProperty("m_MainLightShadowmapResolution").intValue;
        int oldCasc = so.FindProperty("m_ShadowCascadeCount").intValue;
        float oldDist = so.FindProperty("m_ShadowDistance").floatValue;
        so.FindProperty("m_MainLightShadowmapResolution").intValue = 4096;
        so.FindProperty("m_ShadowCascadeCount").intValue = 1;
        so.FindProperty("m_ShadowDistance").floatValue = 1500f;
        so.ApplyModifiedPropertiesWithoutUndo();

        var life = GameObject.Find("GZ_AmbientLife");
        bool lifeWasActive = life != null && life.activeSelf;
        if (life != null) life.SetActive(false);

        Light sun = null;
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional) { sun = l; break; }
        Texture oldCookie = sun != null ? sun.cookie : null;
        if (sun != null) sun.cookie = null;

        var go = new GameObject("GZ_BakeCam");
        RenderTexture rt = null;
        try
        {
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = tsize.z * 0.5f;
            cam.aspect = tsize.x / tsize.z;
            cam.nearClipPlane = 10f;
            cam.farClipPlane = 1000f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.gray;
            go.transform.position = new Vector3(tsize.x * 0.5f, 500f, tsize.z * 0.5f);
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            const int res = 4096;
            rt = new RenderTexture(res, res, 24);
            cam.targetTexture = rt;
            var req = new RenderPipeline.StandardRequest();
            req.destination = rt;
            if (RenderPipeline.SupportsRenderRequest(cam, req))
                RenderPipeline.SubmitRenderRequest(cam, req);
            else
                cam.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(res, res, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, res, res), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            File.WriteAllBytes(AlbedoPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(AlbedoPath);

            var imp = (TextureImporter)AssetImporter.GetAtPath(AlbedoPath);
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.maxTextureSize = 4096;
            imp.anisoLevel = 4;
            imp.mipmapEnabled = true;
            imp.SaveAndReimport();
            return "Lit albedo baked: " + AlbedoPath + " (4096px, sun+shadows, no clouds/animals)";
        }
        finally
        {
            if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
            Object.DestroyImmediate(go);
            if (sun != null) sun.cookie = oldCookie;
            if (life != null) life.SetActive(lifeWasActive);
            so.FindProperty("m_MainLightShadowmapResolution").intValue = oldRes;
            so.FindProperty("m_ShadowCascadeCount").intValue = oldCasc;
            so.FindProperty("m_ShadowDistance").floatValue = oldDist;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    // ---------- chunk meshes ----------

    static List<Mesh> BuildChunkMeshes(TerrainData td, Vector3 tsize, StringBuilder sb)
    {
        int hmRes = td.heightmapResolution;
        float[,] heights = td.GetHeights(0, 0, hmRes, hmRes); // [z, x] normalized
        var meshes = new List<Mesh>();
        long vTot = 0, tTot = 0;
        int nGE = 0, nMon = 0, nRel = 0, nFlat = 0;
        float cw = tsize.x / Chunks, ch = tsize.z / Chunks;

        for (int cz = 0; cz < Chunks; cz++)
            for (int cx = 0; cx < Chunks; cx++)
            {
                float x0 = cx * cw, z0 = cz * ch;
                float step;
                bool crestFix = false;
                if (ChunkTouchesZone(x0, z0, cw, ch, tsize, true)) { step = TierGreatEnclosure; crestFix = true; nGE++; }
                else if (ChunkTouchesZone(x0, z0, cw, ch, tsize, false)) { step = TierMonument; crestFix = true; nMon++; }
                else if (ChunkRelief(heights, hmRes, tsize, x0, z0, cw, ch) > ReliefThresholdM) { step = TierRelief; nRel++; }
                else { step = TierFlat; nFlat++; }

                var m = BuildOneChunk(td, tsize, x0, z0, cw, ch, step, cx, cz, crestFix);
                meshes.Add(m);
                vTot += m.vertexCount;
                tTot += m.triangles.Length / 3;
            }

        sb.Append("Chunks: ").Append(meshes.Count)
          .Append(" | GE ").Append(nGE).Append(" @").Append(TierGreatEnclosure).Append("m+crest")
          .Append(", monument ").Append(nMon).Append(" @").Append(TierMonument).Append("m+crest")
          .Append(", relief ").Append(nRel).Append(" @").Append(TierRelief).Append("m")
          .Append(", flat ").Append(nFlat).Append(" @").Append(TierFlat).Append("m")
          .Append(" | verts ").Append(vTot).Append(" tris ").Append(tTot).Append("\n");
        return meshes;
    }

    /// <summary>Monument no-touch zones from GZVegetationFlattener.IsProtected, dilated so fine geometry extends past the walls. geOnly tests just the Great Enclosure disc (its own finer tier).</summary>
    static bool ChunkTouchesZone(float x0, float z0, float w, float h, Vector3 tsize, bool geOnly)
    {
        for (int i = 0; i <= 5; i++)
            for (int j = 0; j <= 5; j++)
            {
                float u = (x0 + w * i / 5f) / tsize.x;
                float v = (z0 + h * j / 5f) / tsize.z;
                if (geOnly ? IsGreatEnclosureUV(u, v) : IsMonumentUV(u, v)) return true;
            }
        return false;
    }

    static bool IsGreatEnclosureUV(float u, float v)
    {
        return InEllipse(u, v, 0.4514f, 0.2032f, 0.050f, 0.050f); // Great Enclosure (full disc)
    }

    static bool IsMonumentUV(float u, float v)
    {
        if (InEllipse(u, v, 0.490f, 0.725f, 0.115f, 0.052f)) return true;   // Hill Complex SUMMIT enclosures only
        if (InEllipse(u, v, 0.223f, 0.128f, 0.046f, 0.042f)) return true;   // Karanga village
        if (InEllipse(u, v, 0.513f, 0.360f, 0.075f, 0.088f)) return true;   // Valley Ruins
        if (InEllipse(u, v, 0.760f, 0.442f, 0.046f, 0.037f)) return true;   // East Ruins
        if (InEllipse(u, v, 0.462f, 0.148f, 0.048f, 0.026f)) return true;   // Camp Ruins
        if (InEllipse(u, v, 0.425f, 0.278f, 0.030f, 0.042f)) return true;   // Posselt/Philips
        // Hill cliff band demoted to the relief tier: granite domes read fine at
        // 3.5 m; only the summit walls need monument-tier sampling.
        return false;
    }

    static bool InEllipse(float u, float v, float cu, float cv, float ru, float rv)
    {
        float du = (u - cu) / ru, dv = (v - cv) / rv;
        return du * du + dv * dv < 1f;
    }

    static float ChunkRelief(float[,] heights, int hmRes, Vector3 tsize, float x0, float z0, float w, float h)
    {
        // Max height delta between neighbouring coarse samples (~6 m apart).
        const int N = 24;
        float maxDelta = 0f;
        float prevRow = 0f;
        var row = new float[N + 1];
        for (int j = 0; j <= N; j++)
        {
            for (int i = 0; i <= N; i++)
            {
                int hx = Mathf.Clamp(Mathf.RoundToInt((x0 + w * i / N) / tsize.x * (hmRes - 1)), 0, hmRes - 1);
                int hz = Mathf.Clamp(Mathf.RoundToInt((z0 + h * j / N) / tsize.z * (hmRes - 1)), 0, hmRes - 1);
                float y = heights[hz, hx] * tsize.y;
                if (i > 0) maxDelta = Mathf.Max(maxDelta, Mathf.Abs(y - prevRow));
                if (j > 0) maxDelta = Mathf.Max(maxDelta, Mathf.Abs(y - row[i]));
                prevRow = y;
                row[i] = y;
            }
        }
        return maxDelta;
    }

    static Mesh BuildOneChunk(TerrainData td, Vector3 tsize, float x0, float z0, float w, float h, float step, int cx, int cz, bool crestFix)
    {
        int nx = Mathf.CeilToInt(w / step) + 1;
        int nz = Mathf.CeilToInt(h / step) + 1;
        int main = nx * nz;
        int skirt = 2 * nx + 2 * nz;
        var verts = new Vector3[main + skirt];
        var uvs = new Vector2[main + skirt];
        var norms = new Vector3[main + skirt];
        float su = step / tsize.x, sv = step / tsize.z;

        for (int iz = 0; iz < nz; iz++)
            for (int ix = 0; ix < nx; ix++)
            {
                float wx = x0 + Mathf.Min(ix * step, w);
                float wz = z0 + Mathf.Min(iz * step, h);
                float u = wx / tsize.x, v = wz / tsize.z;
                int vi = iz * nx + ix;
                verts[vi] = new Vector3(wx, SampleHeight(td, u, v, su, sv, crestFix), wz);
                uvs[vi] = new Vector2(u, v);
                norms[vi] = td.GetInterpolatedNormal(u, v);
            }

        var tris = new List<int>((nx - 1) * (nz - 1) * 6 + (nx + nz) * 12);
        for (int iz = 0; iz < nz - 1; iz++)
            for (int ix = 0; ix < nx - 1; ix++)
            {
                int a = iz * nx + ix, b = a + 1, c = a + nx, d = c + 1;
                tris.Add(a); tris.Add(c); tris.Add(b);
                tris.Add(b); tris.Add(c); tris.Add(d);
            }

        // Skirts: duplicate each perimeter vertex, dropped by SkirtDrop, and stitch
        // quads so cracks between different-resolution chunks are hidden.
        int si = main;
        AddSkirtEdge(verts, uvs, norms, tris, ref si, EdgeIndices(nx, nz, 0));
        AddSkirtEdge(verts, uvs, norms, tris, ref si, EdgeIndices(nx, nz, 1));
        AddSkirtEdge(verts, uvs, norms, tris, ref si, EdgeIndices(nx, nz, 2));
        AddSkirtEdge(verts, uvs, norms, tris, ref si, EdgeIndices(nx, nz, 3));

        var mesh = new Mesh { name = "GZ_MobileChunk_" + cx + "_" + cz };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetNormals(norms);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// Grid sampling aliases sharp wall crests into a sawtooth: vertices land
    /// alternately on the crest and on the slope, so the crest line dips between
    /// them. In steep cells of monument chunks we take the MAX of five sub-samples,
    /// which keeps the crest continuous (at the cost of ~0.3-0.5 m wall widening).
    /// Flat cells keep the exact centre sample so open ground is unaffected.
    /// </summary>
    static float SampleHeight(TerrainData td, float u, float v, float su, float sv, bool crestFix)
    {
        float h0 = td.GetInterpolatedHeight(u, v);
        if (!crestFix) return h0;
        const float o = 0.4f;
        float h1 = td.GetInterpolatedHeight(Mathf.Clamp01(u + su * o), v);
        float h2 = td.GetInterpolatedHeight(Mathf.Clamp01(u - su * o), v);
        float h3 = td.GetInterpolatedHeight(u, Mathf.Clamp01(v + sv * o));
        float h4 = td.GetInterpolatedHeight(u, Mathf.Clamp01(v - sv * o));
        float mx = Mathf.Max(h0, Mathf.Max(Mathf.Max(h1, h2), Mathf.Max(h3, h4)));
        float mn = Mathf.Min(h0, Mathf.Min(Mathf.Min(h1, h2), Mathf.Min(h3, h4)));
        return (mx - mn) > CrestCellReliefM ? mx : h0;
    }

    static int[] EdgeIndices(int nx, int nz, int edge)
    {
        int n = edge < 2 ? nx : nz;
        var idx = new int[n];
        for (int i = 0; i < n; i++)
            idx[i] = edge == 0 ? i                     // south row
                   : edge == 1 ? (nz - 1) * nx + i     // north row
                   : edge == 2 ? i * nx                // west column
                   : i * nx + (nx - 1);                // east column
        return idx;
    }

    static void AddSkirtEdge(Vector3[] verts, Vector2[] uvs, Vector3[] norms, List<int> tris, ref int si, int[] edge)
    {
        int start = si;
        for (int i = 0; i < edge.Length; i++)
        {
            verts[si] = verts[edge[i]] + Vector3.down * SkirtDrop;
            uvs[si] = uvs[edge[i]];
            norms[si] = norms[edge[i]];
            si++;
        }
        for (int i = 0; i < edge.Length - 1; i++)
        {
            int a = edge[i], b = edge[i + 1], a2 = start + i, b2 = start + i + 1;
            tris.Add(a); tris.Add(a2); tris.Add(b);
            tris.Add(b); tris.Add(a2); tris.Add(b2);
        }
    }

    // ---------- scene assembly ----------

    static void PopulateMobileScene(Material terrainMat, List<Mesh> meshes)
    {
        var cam = Camera.main;
        if (cam != null)
        {
            cam.transform.position = new Vector3(430f, 62f, 110f);
            cam.transform.LookAt(new Vector3(520f, 60f, 450f));
            cam.farClipPlane = 2000f;
        }

        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional)
            {
                l.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                l.color = new Color(1f, 0.9568627f, 0.8392157f);
                l.shadows = LightShadows.None; // terrain shadows are baked; nothing else needs them
            }

        var root = new GameObject("GZ_MobileTerrain");
        foreach (var m in meshes)
        {
            var go = new GameObject(m.name);
            go.transform.SetParent(root.transform, false);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = terrainMat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            GameObjectUtility.SetStaticEditorFlags(go, (StaticEditorFlags)~0);
        }

        var life = new GameObject("GZ_AmbientLife");

        var flockGO = new GameObject("GZ_BirdFlock_HillComplex");
        flockGO.transform.SetParent(life.transform, false);
        flockGO.transform.position = new Vector3(555f, 171f, 872f);
        var flock = flockGO.AddComponent<GZBirdFlock>();
        flock.flockMaterial = AssetDatabase.LoadAssetAtPath<Material>(BirdMatPath);
        flock.orbitRadius = 95f;
        flock.radiusJitter = 30f;
        flock.baseAltitude = 30f;
        flock.birdCount = 28;
        flock.BuildMesh();
        flock.PushMaterialParams();
        if (flock.flockMaterial != null)
            flockGO.GetComponent<MeshRenderer>().sharedMaterial = flock.flockMaterial;

        var herdGO = new GameObject("GZ_Herd_ValleyRun");
        herdGO.transform.SetParent(life.transform, false);
        var herd = herdGO.AddComponent<GZHerdRunner>();
        herd.waypoints = new[]
        {
            new Vector3(300f, 43f, 180f),
            new Vector3(330f, 47f, 300f),
            new Vector3(360f, 55f, 420f),
            new Vector3(400f, 56f, 550f),
            new Vector3(430f, 66f, 650f),
        };
        herd.count = 9;
        herd.placeholderMaterial = AssetDatabase.LoadAssetAtPath<Material>(HerdMatPath);
        herd.RebuildAgents();

        var cap = new GameObject("GZ_FrameCap");
        cap.AddComponent<GZFrameRateCap>().targetFps = 60;
    }
}
