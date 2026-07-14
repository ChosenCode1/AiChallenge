using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// One-shot setup/teardown for the Great Zimbabwe ambient-animation kit:
/// cloud-shadow light cookie, shader-animated bird flock over the Hill Complex,
/// and a terrain-snapping herd runner across the open valley floor.
/// Everything is reversible via <see cref="RemoveAmbientLife"/>.
/// Drive from the CLI: `eval --code 'return GZAmbientLifeSetup.SetupAmbientLife();'`
/// </summary>
public static class GZAmbientLifeSetup
{
    const string RootName = "GZ_AmbientLife";
    const string CookiePath = "Assets/GreatZimbabwe/GZ_CloudCookie.png";
    const string BirdMatPath = "Assets/GreatZimbabwe/GZ_BirdFlock.mat";
    const string HerdMatPath = "Assets/GreatZimbabwe/GZ_HerdPlaceholder.mat";

    // ---------- optimization ----------

    /// <summary>Disables the URP opaque-texture copy (nothing in the scene samples it). Depth stays on for SSAO.</summary>
    public static string OptimizePipeline()
    {
        var rp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (rp == null) return "No URP asset active.";
        var so = new SerializedObject(rp);
        var prop = so.FindProperty("m_RequireOpaqueTexture");
        bool was = prop.boolValue;
        prop.boolValue = false;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(rp);
        AssetDatabase.SaveAssets();
        return "OpaqueTexture: " + was + " -> false on " + rp.name + " (depth texture left ON for SSAO)";
    }

    /// <summary>Puts the (previously default-positioned) Main Camera at a real vantage point and widens the far plane to cover the site.</summary>
    public static string FrameMainCamera()
    {
        var cam = Camera.main;
        if (cam == null) return "No MainCamera-tagged camera found.";
        Undo.RecordObject(cam.transform, "Frame GZ camera");
        cam.transform.position = new Vector3(430f, 62f, 110f);
        cam.transform.LookAt(new Vector3(520f, 60f, 450f));
        cam.farClipPlane = 2000f;
        EditorUtility.SetDirty(cam);
        EditorSceneManager.MarkSceneDirty(cam.gameObject.scene);
        return "Main Camera -> (430, 62, 110) looking over the Great Enclosure toward the Hill Complex, far plane 2000.";
    }

    // ---------- ambient life ----------

    [MenuItem("Tools/Great Zimbabwe/Setup Ambient Life")]
    static void SetupMenu() { Debug.Log(SetupAmbientLife()); }

    [MenuItem("Tools/Great Zimbabwe/Remove Ambient Life")]
    static void RemoveMenu() { Debug.Log(RemoveAmbientLife()); }

    /// <summary>CLI entry (project closed in the editor):
    /// Unity.exe -batchmode -quit -projectPath "My project" -executeMethod GZAmbientLifeSetup.SetupFromCli</summary>
    public static void SetupFromCli()
    {
        const string scenePath = "Assets/Scenes/GreatZimbabwe.unity";
        if (SceneManager.GetActiveScene().path != scenePath)
            EditorSceneManager.OpenScene(scenePath);
        string result = SetupAmbientLife();
        Debug.Log("[GZAmbientLifeSetup] " + result);
        if (result.StartsWith("ABORT")) throw new System.Exception(result);
    }

    public static string SetupAmbientLife()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.path.Contains("GreatZimbabwe"))
            return "ABORT: active scene is " + scene.path + ", not GreatZimbabwe.";

        var log = new System.Text.StringBuilder();

        // 1. Cloud shadows: generate tileable cloud noise, assign as directional light cookie.
        log.Append(GenerateCloudCookie()).Append(" | ");
        var sun = FindSun();
        if (sun != null)
        {
            var cookie = AssetDatabase.LoadAssetAtPath<Texture2D>(CookiePath);
            sun.cookie = cookie;
            var data = sun.GetComponent<UniversalAdditionalLightData>();
            if (data != null) data.lightCookieSize = new Vector2(1400f, 1400f);
            if (sun.GetComponent<GZCloudShadows>() == null)
                sun.gameObject.AddComponent<GZCloudShadows>();
            EditorUtility.SetDirty(sun.gameObject);
            log.Append("Cloud cookie on '").Append(sun.name).Append("' (900 m tile, drifting). | ");
        }
        else log.Append("WARN: no directional light found. | ");

        // 2. Root object.
        var root = GameObject.Find(RootName);
        if (root == null) root = new GameObject(RootName);

        // 3. Bird flock circling the Hill Complex summit (~171 m ground, birds ~200 m).
        var flockGO = FindOrCreateChild(root.transform, "GZ_BirdFlock_HillComplex");
        flockGO.transform.position = new Vector3(555f, 171f, 872f);
        var flock = flockGO.GetComponent<GZBirdFlock>();
        if (flock == null) flock = flockGO.AddComponent<GZBirdFlock>();
        flock.flockMaterial = LoadOrCreateBirdMaterial();
        flock.orbitRadius = 95f;
        flock.radiusJitter = 30f;
        flock.baseAltitude = 30f;
        flock.birdCount = 28;
        flock.BuildMesh();
        flock.PushMaterialParams();
        flockGO.GetComponent<MeshRenderer>().sharedMaterial = flock.flockMaterial;
        log.Append("Bird flock: 28 birds, 1 draw call, orbiting (555, ~201, 872). | ");

        // 4. Herd running the open valley west of the Valley Ruins (clear of all protected zones).
        var herdGO = FindOrCreateChild(root.transform, "GZ_Herd_ValleyRun");
        herdGO.transform.position = Vector3.zero;
        var herd = herdGO.GetComponent<GZHerdRunner>();
        if (herd == null) herd = herdGO.AddComponent<GZHerdRunner>();
        herd.waypoints = new[]
        {
            new Vector3(300f, 43f, 180f),
            new Vector3(330f, 47f, 300f),
            new Vector3(360f, 55f, 420f),
            new Vector3(400f, 56f, 550f),
            new Vector3(430f, 66f, 650f),
        };
        // 120 animals stays mobile-cheap because the coat shader GPU-instances:
        // every body shares one instanced draw, every head another (plus the
        // instanced shadow pass) instead of 240 individual draw calls.
        herd.count = 120;
        herd.lateralJitter = 12f;
        herd.placeholderMaterial = LoadOrCreateHerdMaterial();
        herd.RebuildAgents();
        log.Append("Herd: 120 instanced agents on a 490 m valley route (assign agentPrefab to use real animals). | ");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        log.Append("Scene saved.");
        return log.ToString();
    }

    /// <summary>Removes everything SetupAmbientLife added (flock, herd, cloud shadows, generated assets). The pipeline optimization and camera framing are kept.</summary>
    public static string RemoveAmbientLife()
    {
        var scene = SceneManager.GetActiveScene();
        var log = new System.Text.StringBuilder();

        var root = GameObject.Find(RootName);
        if (root != null) { Object.DestroyImmediate(root); log.Append("Removed " + RootName + ". | "); }

        var sun = FindSun();
        if (sun != null)
        {
            var comp = sun.GetComponent<GZCloudShadows>();
            if (comp != null) Object.DestroyImmediate(comp);
            sun.cookie = null;
            var data = sun.GetComponent<UniversalAdditionalLightData>();
            if (data != null) data.lightCookieOffset = Vector2.zero;
            EditorUtility.SetDirty(sun.gameObject);
            log.Append("Cleared light cookie + component. | ");
        }

        foreach (var p in new[] { CookiePath, BirdMatPath, HerdMatPath })
            if (AssetDatabase.LoadAssetAtPath<Object>(p) != null) { AssetDatabase.DeleteAsset(p); log.Append("Deleted " + p + ". | "); }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        log.Append("Scene saved.");
        return log.ToString();
    }

    // ---------- helpers ----------

    public static string GenerateCloudCookie()
    {
        const int size = 512;
        const float tiles = 2f;      // noise tiles per cookie -> ~450 m cloud blobs
        // Clouds block most of the sun disk; ambient skylight (which URP keeps) does the
        // fill. Weaker values disappear against the strong ambient in this scene.
        const float strength = 0.80f;

        var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            float v = (float)y / size;
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size;
                // 4-corner cross-fade makes the (non-tileable) Perlin fbm seamless.
                float n00 = Fbm(u * tiles, v * tiles);
                float n10 = Fbm((u - 1f) * tiles, v * tiles);
                float n01 = Fbm(u * tiles, (v - 1f) * tiles);
                float n11 = Fbm((u - 1f) * tiles, (v - 1f) * tiles);
                float n = Mathf.Lerp(Mathf.Lerp(n00, n10, u), Mathf.Lerp(n01, n11, u), v);
                // 3-octave fbm concentrates around ~0.48 — threshold must sit in that band.
                float cloud = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.36f, 0.56f, n));
                byte g = (byte)Mathf.RoundToInt(Mathf.Clamp01(1f - strength * cloud) * 255f);
                px[y * size + x] = new Color32(g, g, g, 255);
            }
        }
        tex.SetPixels32(px);
        Directory.CreateDirectory(Path.GetDirectoryName(CookiePath));
        File.WriteAllBytes(CookiePath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(CookiePath);

        var imp = (TextureImporter)AssetImporter.GetAtPath(CookiePath);
        imp.wrapMode = TextureWrapMode.Repeat;
        imp.sRGBTexture = false;
        imp.mipmapEnabled = true;
        imp.textureCompression = TextureImporterCompression.Uncompressed;
        imp.maxTextureSize = 512;
        imp.SaveAndReimport();
        return "Cloud cookie generated (512px, soft fbm, max " + (int)(strength * 100) + "% darkening).";
    }

    static float Fbm(float x, float y)
    {
        // +100 keeps Perlin inputs positive on the wrapped corners.
        float n = 0f, amp = 0.55f, freq = 1f;
        for (int o = 0; o < 3; o++)
        {
            n += amp * Mathf.PerlinNoise(100f + x * freq, 100f + y * freq);
            amp *= 0.5f; freq *= 2.1f;
        }
        return n;
    }

    static Light FindSun()
    {
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional) return l;
        return null;
    }

    static GameObject FindOrCreateChild(Transform parent, string name)
    {
        var t = parent.Find(name);
        if (t != null) return t.gameObject;
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go;
    }

    static Material LoadOrCreateBirdMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(BirdMatPath);
        if (mat != null) return mat;
        var shader = Shader.Find("GreatZimbabwe/BirdFlock");
        if (shader == null) { Debug.LogError("BirdFlock shader missing"); return null; }
        mat = new Material(shader);
        AssetDatabase.CreateAsset(mat, BirdMatPath);
        return mat;
    }

    static Material LoadOrCreateHerdMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(HerdMatPath);
        if (mat == null)
        {
            // The herd's coat shader (procedural hide + gait); URP/Lit only as a fallback
            // if the GreatZimbabwe shaders are ever stripped from the project.
            var shader = Shader.Find("GreatZimbabwe/HerdGold");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            mat = new Material(shader);
            mat.SetColor("_BaseColor", new Color(0.16f, 0.12f, 0.09f));
            AssetDatabase.CreateAsset(mat, HerdMatPath);
        }
        if (!mat.enableInstancing)
        {
            // Required for the herd to collapse into instanced draws on mobile.
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
        }
        return mat;
    }
}
