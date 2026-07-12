using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// CLI-driven performance analysis + an "LLM budget mode" for running the scene
/// alongside a local LLM on the same GPU.
/// eval entry points:
///   GZPerfTools.SystemReport()                    — hardware + memory breakdown
///   GZPerfTools.TimeRender(camXYZ, lookXYZ, fov, frames) — real timed GPU frames at 1080p
///   GZPerfTools.SetLLMBudgetMode(true/false)      — apply/restore the budget profile
///   GZPerfTools.SetOrthoMaxSize(4096)             — ortho resolution alone
/// </summary>
public static class GZPerfTools
{
    const string RPAssetPath = "Assets/Settings/PC_RPAsset.asset";
    const string RendererPath = "Assets/Settings/PC_Renderer.asset";
    const string OrthoPath = "Assets/GreatZimbabwe/GZ_Ortho_8192.jpg";
    const string FrameCapName = "GZ_FrameCap";

    // ---------- measurement ----------

    public static string SystemReport()
    {
        var sb = new StringBuilder();
        sb.Append("GPU: ").Append(SystemInfo.graphicsDeviceName)
          .Append(" | VRAM: ").Append(SystemInfo.graphicsMemorySize).Append(" MB")
          .Append(" | RAM: ").Append(SystemInfo.systemMemorySize).Append(" MB")
          .Append(" | CPU: ").Append(SystemInfo.processorType)
          .Append(" x").Append(SystemInfo.processorCount).Append(" threads\n");

        sb.Append("Unity CPU alloc: ").Append(MB(UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong()))
          .Append(" (reserved ").Append(MB(UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong())).Append(")")
          .Append(" | GfxDriver: ").Append(MB(UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver()))
          .Append(" | EditorProcess WS: ").Append(MB(System.Diagnostics.Process.GetCurrentProcess().WorkingSet64)).Append("\n");

        long texTotal = 0, rtTotal = 0, meshTotal = 0;
        var top = new System.Collections.Generic.List<System.Tuple<long, string>>();
        foreach (var t in Resources.FindObjectsOfTypeAll<Texture>())
        {
            long s = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t);
            if (t is RenderTexture) rtTotal += s; else texTotal += s;
            if (s > 2 * 1024 * 1024)
                top.Add(System.Tuple.Create(s, (t is RenderTexture ? "[RT] " : "") + t.name + " (" + t.width + "x" + t.height + ")"));
        }
        foreach (var m in Resources.FindObjectsOfTypeAll<Mesh>())
            meshTotal += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m);

        var td = Terrain.activeTerrain != null ? Terrain.activeTerrain.terrainData : null;
        sb.Append("All textures: ").Append(MB(texTotal))
          .Append(" | RenderTextures: ").Append(MB(rtTotal))
          .Append(" | Meshes: ").Append(MB(meshTotal));
        if (td != null)
        {
            sb.Append(" | TerrainData: ").Append(MB(UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(td)));
            if (td.heightmapTexture != null)
                sb.Append(" (heightmapTex ").Append(MB(UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(td.heightmapTexture))).Append(")");
        }
        sb.Append("\nTop textures >2MB:\n");
        top.Sort((a, b) => b.Item1.CompareTo(a.Item1));
        for (int i = 0; i < Mathf.Min(top.Count, 14); i++)
            sb.Append("  ").Append(MB(top[i].Item1)).Append("  ").Append(top[i].Item2).Append("\n");
        return sb.ToString();
    }

    /// <summary>Times real pipeline renders at 1920x1080 with a forced GPU sync per frame.</summary>
    public static string TimeRender(float camX, float camY, float camZ,
                                    float lookX, float lookY, float lookZ, float fov, int frames)
    {
        var go = new GameObject("GZ_TimingCam");
        RenderTexture rt = null;
        Texture2D sync = null;
        try
        {
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.5f;
            cam.farClipPlane = 2000f;
            go.transform.position = new Vector3(camX, camY, camZ);
            go.transform.LookAt(new Vector3(lookX, lookY, lookZ));
            rt = new RenderTexture(1920, 1080, 24);
            cam.targetTexture = rt;
            sync = new Texture2D(1, 1, TextureFormat.RGBA32, false);

            var req = new RenderPipeline.StandardRequest();
            req.destination = rt;
            bool useReq = RenderPipeline.SupportsRenderRequest(cam, req);

            for (int i = 0; i < 3; i++) RenderOnce(cam, rt, sync, useReq, req); // warm-up

            double best = double.MaxValue, total = 0;
            for (int i = 0; i < frames; i++)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                RenderOnce(cam, rt, sync, useReq, req);
                sw.Stop();
                double ms = sw.Elapsed.TotalMilliseconds;
                total += ms;
                if (ms < best) best = ms;
            }
            double avg = total / frames;
            return "1080p x" + frames + " frames: avg " + avg.ToString("F2") + " ms (best " + best.ToString("F2") +
                   " ms) -> ~" + (1000.0 / avg).ToString("F0") + " fps uncapped; at 60fps cap GPU is ~" +
                   (100.0 * (1.0 - avg / 16.67)).ToString("F0") + "% idle";
        }
        finally
        {
            if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
            if (sync != null) Object.DestroyImmediate(sync);
            Object.DestroyImmediate(go);
        }
    }

    static void RenderOnce(Camera cam, RenderTexture rt, Texture2D sync, bool useReq, RenderPipeline.StandardRequest req)
    {
        if (useReq) RenderPipeline.SubmitRenderRequest(cam, req);
        else cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        sync.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); // forces the GPU to finish the frame
        RenderTexture.active = prev;
    }

    // ---------- LLM budget mode ----------

    /// <summary>
    /// ON:  shadowmap 2048->1024, cascades 4->2, SSAO downsampled + low samples,
    ///      single-layer splat control map 2048->512, 60fps cap object added.
    /// OFF: restores everything. The 8K ortho is deliberately untouched (85 MB is
    ///      cheap on a 16 GB card; use SetOrthoMaxSize to trade it manually).
    /// </summary>
    public static string SetLLMBudgetMode(bool on)
    {
        var sb = new StringBuilder();

        var rp = AssetDatabase.LoadMainAssetAtPath(RPAssetPath);
        var soRp = new SerializedObject(rp);
        soRp.FindProperty("m_MainLightShadowmapResolution").intValue = on ? 1024 : 2048;
        soRp.FindProperty("m_ShadowCascadeCount").intValue = on ? 2 : 4;
        soRp.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(rp);
        sb.Append("Shadows: ").Append(on ? "1024px, 2 cascades" : "2048px, 4 cascades").Append(" | ");

        foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(RendererPath))
        {
            if (sub == null || sub.name != "ScreenSpaceAmbientOcclusion") continue;
            var soF = new SerializedObject(sub);
            var down = soF.FindProperty("m_Settings.Downsample");
            var samples = soF.FindProperty("m_Settings.Samples");
            if (down != null) down.boolValue = on;
            if (samples != null) samples.intValue = on ? 2 : 1; // High=0 Medium=1 Low=2
            soF.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(sub);
            sb.Append("SSAO: ").Append(on ? "downsampled, low samples" : "full-res, medium samples").Append(" | ");
        }

        sb.Append(SetAlphamapResolution(on ? 512 : 2048)).Append(" | ");

        var capGO = GameObject.Find(FrameCapName);
        if (on && capGO == null)
        {
            capGO = new GameObject(FrameCapName);
            capGO.AddComponent<GZFrameRateCap>();
        }
        else if (!on && capGO != null) Object.DestroyImmediate(capGO);
        sb.Append(on ? "60fps cap object added." : "Frame cap removed.");

        AssetDatabase.SaveAssets();
        var scene = SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return sb.ToString();
    }

    /// <summary>
    /// The terrain has exactly one layer, so its splat control map is a constant
    /// weight=1 everywhere — resolution is pure memory cost, no visual meaning.
    /// </summary>
    public static string SetAlphamapResolution(int res)
    {
        var td = Terrain.activeTerrain != null ? Terrain.activeTerrain.terrainData : null;
        if (td == null) return "No active terrain";
        if (td.alphamapLayers != 1) return "ABORT alphamap resize: " + td.alphamapLayers + " layers (expected 1)";
        if (td.alphamapResolution == res) return "Alphamap already " + res;
        td.alphamapResolution = res; // resets weights
        var w = new float[res, res, 1];
        for (int y = 0; y < res; y++) for (int x = 0; x < res; x++) w[y, x, 0] = 1f;
        td.SetAlphamaps(0, 0, w);
        EditorUtility.SetDirty(td);
        return "Alphamap control map -> " + res + " (single layer, weight 1 everywhere)";
    }

    public static string SetOrthoMaxSize(int px)
    {
        var imp = (TextureImporter)AssetImporter.GetAtPath(OrthoPath);
        if (imp == null) return "Ortho importer not found";
        if (imp.maxTextureSize == px) return "Ortho already " + px;
        imp.maxTextureSize = px;
        imp.SaveAndReimport();
        return "Ortho maxTextureSize -> " + px;
    }

    static string MB(long bytes) { return (bytes / 1048576.0).ToString("F1") + " MB"; }
}
