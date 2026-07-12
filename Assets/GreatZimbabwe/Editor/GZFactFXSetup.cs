using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-shot setup/teardown for the Fact-FX layer (FX_PLAN.md Phase 1): a GZFactFXDirector
/// fed by the tour director, plus the universal ground-ring pulse. Reversible via
/// <see cref="RemoveFactFX"/>. CLI (project must be closed in the editor):
///   Unity.exe -batchmode -quit -projectPath "My project" -executeMethod GZFactFXSetup.SetupFromCli
///   Unity.exe -batchmode -quit -projectPath "My project" -executeMethod GZFactFXSetup.PreviewPulseFromCli
/// The preview writes a still to %GZ_FX_PREVIEW_PATH% (or Temp/gz_fx_preview.png).
/// </summary>
public static class GZFactFXSetup
{
    const string RootName = "GZ_FactFX";
    const string ScenePath = "Assets/Scenes/GreatZimbabwe.unity";
    const string RingMatPath = "Assets/GreatZimbabwe/GZ_FactFXRing.mat";
    const string WallMatPath = "Assets/GreatZimbabwe/GZ_WallAssembly.mat";
    const string TowerMatPath = "Assets/GreatZimbabwe/GZ_TowerScan.mat";
    const string RouteMatPath = "Assets/GreatZimbabwe/GZ_RouteGlow.mat";
    const string GrainMatPath = "Assets/GreatZimbabwe/GZ_GrainPour.mat";
    const string GrainTexPath = "Assets/GreatZimbabwe/GZ_GrainDot.png";
    const string HerdMatPath = "Assets/GreatZimbabwe/GZ_HerdPlaceholder.mat";

    public static string Ping() => "GZFactFX v1 compiled";

    [MenuItem("Tools/Great Zimbabwe/Tour/Setup Fact FX")]
    static void SetupMenu() { Debug.Log(SetupFactFX()); }

    [MenuItem("Tools/Great Zimbabwe/Tour/Remove Fact FX")]
    static void RemoveMenu() { Debug.Log(RemoveFactFX()); }

    public static string SetupFactFX()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.path.Contains("GreatZimbabwe"))
            return "ABORT: active scene is " + scene.path + ", not GreatZimbabwe.";
        var director = UnityEngine.Object.FindFirstObjectByType<GZTourDirector>();
        if (director == null)
            return "ABORT: no GZTourDirector in the scene — run GZTourSetup.SetupTour() first.";
        if (director.tourCamera == null)
            return "ABORT: tour director has no camera wired — re-run GZTourSetup.SetupTour().";

        var log = new System.Text.StringBuilder();
        var root = GameObject.Find(RootName);
        if (root == null) root = new GameObject(RootName);

        var fxGO = FindOrCreateChild(root.transform, "GZ_FactFXDirector");
        var fx = GetOrAdd<GZFactFXDirector>(fxGO);

        var ringGO = FindOrCreateChild(root.transform, "GZ_FactFX_Ring");
        var ring = GetOrAdd<GZFactFXRing>(ringGO);

        fx.tourCamera = director.tourCamera;
        fx.ring = ring;
        ring.tourCamera = director.tourCamera;
        ring.ringMaterial = LoadOrCreateRingMaterial();
        var mr = ringGO.GetComponent<MeshRenderer>();
        if (mr != null && ring.ringMaterial != null) mr.sharedMaterial = ring.ringMaterial;

        director.fxDirector = fx;

        log.Append("FactFX wired: ").Append(GZFactFXCatalog.BuildDefault().Count)
           .Append(" cues in catalogue, ring pulse ready. ");

        // ---------------- Phase 2: wall study models ----------------
        var terrain = Terrain.activeTerrain;
        var wallMat = LoadOrCreateWallMaterial();
        if (terrain != null && wallMat != null)
        {
            // The Great Enclosure hologram: floats just outside the real ellipse, big
            // enough to read from the 115 m orbit. Chevron crest included.
            var ge = director.FindPoi("great_enclosure");
            if (ge != null)
            {
                // South of the real ellipse on open ground, arc concave toward it — the
                // ghost continues the real wall's curve.
                PlaceWall(root.transform, "GZ_FactFX_WallStudy_GE", wallMat, terrain,
                          ge.anchor + new Vector3(20f, 0f, -105f), baseHeight: 8f,
                          yawDegrees: 180f, scale: 2.2f, chevron: true, seed: 20260710);
                log.Append("GE wall study placed. ");
            }

            // East Ruins bonus (er_household): the same craft at two scales, side by side.
            var er = director.FindPoi("east_ruins");
            if (er != null)
            {
                PlaceWall(root.transform, "GZ_FactFX_WallStudy_ER_Grand", wallMat, terrain,
                          er.anchor + new Vector3(-30f, 0f, 10f), baseHeight: 7f,
                          yawDegrees: -15f, scale: 1.6f, chevron: false, seed: 20260711);
                PlaceWall(root.transform, "GZ_FactFX_WallStudy_ER_Household", wallMat, terrain,
                          er.anchor + new Vector3(14f, 0f, 4f), baseHeight: 4f,
                          yawDegrees: -15f, scale: 0.65f, chevron: false, seed: 20260712);
                log.Append("ER two-scale wall pair placed. ");
            }
        }

        // ---------------- Phase 3: Conical Tower (solidity scan + granary pour) ----------------
        var ct = director.FindPoi("conical_tower");
        if (ct != null && terrain != null)
        {
            var towerMat = LoadOrCreateTowerMaterial();
            if (towerMat != null)
            {
                var towerGO = FindOrCreateChild(root.transform, "GZ_FactFX_TowerStudy");
                var tower = GetOrAdd<GZTowerFX>(towerGO);
                tower.towerMaterial = towerMat;
                Vector3 tPos = ct.anchor + new Vector3(17f, 0f, -13f);
                tPos.y = terrain.SampleHeight(tPos) + terrain.transform.position.y + 1.5f;
                towerGO.transform.SetPositionAndRotation(tPos, Quaternion.identity);
                towerGO.transform.localScale = Vector3.one * 1.3f;
                tower.BuildMesh();
                tower.PushMaterialParams();
                var tmr = towerGO.GetComponent<MeshRenderer>();
                if (tmr != null) tmr.sharedMaterial = towerMat;
                log.Append("Tower study placed. ");
            }

            var grainMat = LoadOrCreateGrainMaterial();
            if (grainMat != null)
            {
                var grainGO = FindOrCreateChild(root.transform, "GZ_FactFX_GrainPour");
                // The pour spills from the REAL tower's rim (it stands at the POI anchor).
                Vector3 gPos = ct.anchor;
                gPos.y = terrain.SampleHeight(gPos) + terrain.transform.position.y + 10.3f;
                grainGO.transform.position = gPos;
                var ps = ConfigureGrainSystem(grainGO, grainMat);
                var driver = GetOrAdd<GZGrainPourDriver>(grainGO);
                driver.fx = fx;
                driver.grain = ps;
                log.Append("Grain pour ready. ");
            }
        }

        // ---------------- Phase 4: Cattle (golden herd + route glow + behavior) ----------------
        var herd = UnityEngine.Object.FindFirstObjectByType<GZHerdRunner>();
        if (herd != null)
        {
            log.Append(SwapHerdMaterialToGold());

            var routeMat = LoadOrCreateRouteMaterial();
            if (routeMat != null && herd.waypoints != null && herd.waypoints.Length >= 2)
            {
                var ribbonGO = FindOrCreateChild(root.transform, "GZ_FactFX_RouteRibbon");
                ribbonGO.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var ribbon = GetOrAdd<GZRouteRibbon>(ribbonGO);
                ribbon.herd = herd;
                ribbon.ribbonMaterial = routeMat;
                ribbon.BuildMesh();
                var rmr = ribbonGO.GetComponent<MeshRenderer>();
                if (rmr != null) rmr.sharedMaterial = routeMat;
                log.Append("Route ribbon built. ");
            }

            var herdFX = GetOrAdd<GZHerdBehaviorFX>(fxGO);
            herdFX.fx = fx;
            herdFX.herd = herd;
            log.Append("Herd behavior beat wired. ");
        }
        else log.Append("WARN: no GZHerdRunner (run GZAmbientLifeSetup) — cattle FX skipped. ");

        EditorUtility.SetDirty(root);
        EditorUtility.SetDirty(director);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets(); // persist the ring material's canonical tuning
        log.Append("Scene saved.");
        return log.ToString();
    }

    public static string RemoveFactFX()
    {
        var scene = SceneManager.GetActiveScene();
        var log = new System.Text.StringBuilder();

        var root = GameObject.Find(RootName);
        if (root != null) { UnityEngine.Object.DestroyImmediate(root); log.Append("Removed " + RootName + ". "); }
        else log.Append("No " + RootName + " root found. ");

        var director = UnityEngine.Object.FindFirstObjectByType<GZTourDirector>();
        if (director != null && director.fxDirector != null)
        {
            director.fxDirector = null;
            EditorUtility.SetDirty(director);
            log.Append("Unwired tour director. ");
        }

        // Give the herd its plain URP/Lit look back (properties carry over).
        var herdMat = AssetDatabase.LoadAssetAtPath<Material>(HerdMatPath);
        if (herdMat != null && herdMat.shader != null && herdMat.shader.name == "GreatZimbabwe/HerdGold")
        {
            herdMat.shader = Shader.Find("Universal Render Pipeline/Lit");
            EditorUtility.SetDirty(herdMat);
            log.Append("Restored herd material to URP/Lit. ");
        }

        foreach (var path in new[] { RingMatPath, WallMatPath, TowerMatPath, RouteMatPath, GrainMatPath, GrainTexPath })
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) != null)
            {
                AssetDatabase.DeleteAsset(path);
                log.Append("Deleted " + path + ". ");
            }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        log.Append("Scene saved.");
        return log.ToString();
    }

    // ---------- CLI entry points ----------

    public static void SetupFromCli()
    {
        OpenSceneIfNeeded();
        string result = SetupFactFX();
        Debug.Log("[GZFactFXSetup] " + result);
        if (result.StartsWith("ABORT")) throw new Exception(result);
    }

    /// <summary>
    /// Visual smoke test: freezes a mid-cue ghost pulse at the Great Enclosure and renders
    /// a still through the same preview pipeline the tour setup uses.
    /// </summary>
    public static void PreviewPulseFromCli()
    {
        OpenSceneIfNeeded();
        string setup = SetupFactFX();
        Debug.Log("[GZFactFXSetup] " + setup);
        if (setup.StartsWith("ABORT")) throw new Exception(setup);

        var director = UnityEngine.Object.FindFirstObjectByType<GZTourDirector>();
        var poi = director.FindPoi("great_enclosure");
        if (poi == null) throw new Exception("POI great_enclosure missing — re-run GZTourSetup.SetupTour().");

        Shader.SetGlobalFloat("_GZFX_Pulse", 1f);
        Shader.SetGlobalFloat("_GZFX_PulseT", 0.55f);
        Shader.SetGlobalColor("_GZFX_Color", GZFactFXDirector.ThemeColor(GZFXTheme.Ghost));

        var ring = UnityEngine.Object.FindFirstObjectByType<GZFactFXRing>();
        ring.BeginPulse(poi);

        string path = Environment.GetEnvironmentVariable("GZ_FX_PREVIEW_PATH");
        if (string.IsNullOrEmpty(path)) path = "Temp/gz_fx_preview.png";
        string result = GZTourSetup.RenderPoiPreview("great_enclosure", 40f, path);
        Debug.Log("[GZFactFXSetup] preview: " + result);

        Shader.SetGlobalFloat("_GZFX_Pulse", 0f);
        Shader.SetGlobalFloat("_GZFX_PulseT", 0f);
        ring.EndPulse();
    }

    /// <summary>
    /// Renders the wall assembly frozen mid-build and fully built with the chevron live,
    /// from the Great Enclosure orbit. Stills go to %GZ_FX_PREVIEW_DIR% (or Temp/).
    /// </summary>
    public static void PreviewWallFromCli()
    {
        OpenSceneIfNeeded();
        string setup = SetupFactFX();
        Debug.Log("[GZFactFXSetup] " + setup);
        if (setup.StartsWith("ABORT")) throw new Exception(setup);

        var director = UnityEngine.Object.FindFirstObjectByType<GZTourDirector>();
        var cam = director.tourCamera;
        var poi = director.FindPoi("great_enclosure");
        if (poi == null) throw new Exception("POI great_enclosure missing — re-run GZTourSetup.SetupTour().");

        Vector3 focus = cam.FocusOf(poi, out float groundY);
        Shader.SetGlobalVector("_GZFX_Focus",
            new Vector4(focus.x, groundY, focus.z, GZFactFXRing.RadiusFor(poi)));
        Shader.SetGlobalColor("_GZFX_Color", GZFactFXDirector.ThemeColor(GZFXTheme.Ghost));

        string dir = Environment.GetEnvironmentVariable("GZ_FX_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Temp";

        Shader.SetGlobalFloat("_GZFX_WallAssembly", 0.45f);
        Debug.Log("[GZFactFXSetup] wall mid: "
                  + GZTourSetup.RenderPoiPreview("great_enclosure", 250f, dir + "/gz_wall_mid.png"));

        Shader.SetGlobalFloat("_GZFX_WallAssembly", 1f);
        Shader.SetGlobalFloat("_GZFX_Chevron", 1f);
        Debug.Log("[GZFactFXSetup] wall full: "
                  + GZTourSetup.RenderPoiPreview("great_enclosure", 250f, dir + "/gz_wall_full.png"));
        Debug.Log("[GZFactFXSetup] wall east: "
                  + GZTourSetup.RenderPoiPreview("great_enclosure", 100f, dir + "/gz_wall_full_east.png"));

        // The East Ruins two-scale pair, gated to its own POI.
        var erPoi = director.FindPoi("east_ruins");
        if (erPoi != null)
        {
            Vector3 erFocus = cam.FocusOf(erPoi, out float erGround);
            Shader.SetGlobalVector("_GZFX_Focus",
                new Vector4(erFocus.x, erGround, erFocus.z, GZFactFXRing.RadiusFor(erPoi)));
            Shader.SetGlobalColor("_GZFX_Color", GZFactFXDirector.ThemeColor(GZFXTheme.Ochre));
            Debug.Log("[GZFactFXSetup] ER pair: "
                      + GZTourSetup.RenderPoiPreview("east_ruins", 210f, dir + "/gz_wall_er_pair.png"));
        }

        Shader.SetGlobalFloat("_GZFX_WallAssembly", 0f);
        Shader.SetGlobalFloat("_GZFX_Chevron", 0f);
        Shader.SetGlobalVector("_GZFX_Focus", Vector4.zero);
    }

    /// <summary>
    /// Renders the Phase 3 + 4 hero moments frozen mid-cue: tower scan sweeping, granary
    /// pouring (particles pre-simulated), and the gilded herd on its glowing route.
    /// Stills go to %GZ_FX_PREVIEW_DIR% (or Temp/).
    /// </summary>
    public static void PreviewHeroesFromCli()
    {
        OpenSceneIfNeeded();
        string setup = SetupFactFX();
        Debug.Log("[GZFactFXSetup] " + setup);
        if (setup.StartsWith("ABORT")) throw new Exception(setup);

        var director = UnityEngine.Object.FindFirstObjectByType<GZTourDirector>();
        var cam = director.tourCamera;
        string dir = Environment.GetEnvironmentVariable("GZ_FX_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Temp";

        // --- Conical Tower: scan mid-sweep ---
        var ct = director.FindPoi("conical_tower");
        Vector3 ctFocus = cam.FocusOf(ct, out float ctGround);
        Shader.SetGlobalVector("_GZFX_Focus",
            new Vector4(ctFocus.x, ctGround, ctFocus.z, GZFactFXRing.RadiusFor(ct)));
        Shader.SetGlobalColor("_GZFX_Color", GZFactFXDirector.ThemeColor(GZFXTheme.Ghost));
        Shader.SetGlobalFloat("_GZFX_TowerScan", 0.55f);
        Debug.Log("[GZFactFXSetup] tower scan: "
                  + GZTourSetup.RenderPoiPreview("conical_tower", 150f, dir + "/gz_tower_scan.png"));
        Shader.SetGlobalFloat("_GZFX_TowerScan", 0f);

        // --- Conical Tower: granary pour (simulate a burst, then render) ---
        Shader.SetGlobalColor("_GZFX_Color", GZFactFXDirector.ThemeColor(GZFXTheme.Gold));
        var grain = UnityEngine.Object.FindFirstObjectByType<GZGrainPourDriver>();
        if (grain != null && grain.grain != null)
        {
            grain.grain.Simulate(2.6f, true, true);
            Debug.Log("[GZFactFXSetup] grain pour: "
                      + GZTourSetup.RenderPoiPreview("conical_tower", 150f, dir + "/gz_tower_grain.png"));
            grain.grain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        // --- Cattle: golden rim + route glow ---
        var ca = director.FindPoi("cattle");
        Vector3 caFocus = cam.FocusOf(ca, out float caGround);
        Shader.SetGlobalVector("_GZFX_Focus",
            new Vector4(caFocus.x, caGround, caFocus.z, GZFactFXRing.RadiusFor(ca)));
        Shader.SetGlobalFloat("_GZFX_HerdGold", 1f);
        Shader.SetGlobalFloat("_GZFX_RouteGlow", 1f);
        Debug.Log("[GZFactFXSetup] cattle gold: "
                  + GZTourSetup.RenderPoiPreview("cattle", 200f, dir + "/gz_cattle_gold.png"));

        Shader.SetGlobalFloat("_GZFX_HerdGold", 0f);
        Shader.SetGlobalFloat("_GZFX_RouteGlow", 0f);
        Shader.SetGlobalVector("_GZFX_Focus", Vector4.zero);
    }

    static void OpenSceneIfNeeded()
    {
        if (SceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath);
    }

    /// <summary>Idempotent Shuriken config for the granary pour: a gentle golden fountain
    /// from the rim, arcing outward under gravity. Rebuilt from code on every setup run.</summary>
    static ParticleSystem ConfigureGrainSystem(GameObject go, Material mat)
    {
        var ps = go.GetComponent<ParticleSystem>();
        if (ps == null) ps = go.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = true;
        main.duration = 10f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.0f, 3.0f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2.2f, 3.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.3f);
        main.gravityModifier = 0.85f;
        main.maxParticles = 900;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startColor = GZFactFXDirector.ThemeColor(GZFXTheme.Gold);

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 150f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 55f;
        shape.radius = 1.9f;          // the tower's rim
        shape.radiusThickness = 0.2f; // spill from the edge, not the middle

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        var psr = go.GetComponent<ParticleSystemRenderer>();
        psr.sharedMaterial = mat;
        psr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        psr.receiveShadows = false;

        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        return ps;
    }

    static string SwapHerdMaterialToGold()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(HerdMatPath);
        if (mat == null) return "WARN: herd material missing (run GZAmbientLifeSetup) — gold rim skipped. ";
        var shader = Shader.Find("GreatZimbabwe/HerdGold");
        if (shader == null) return "WARN: HerdGold shader missing. ";
        if (mat.shader == shader) return "Herd gold rim already active. ";
        mat.shader = shader; // _BaseColor carries over from URP/Lit
        mat.SetFloat("_RimPower", 2f);
        EditorUtility.SetDirty(mat);
        return "Herd material swapped to HerdGold (rest look unchanged). ";
    }

    static void PlaceWall(Transform root, string name, Material mat, Terrain terrain,
                          Vector3 anchor, float baseHeight, float yawDegrees, float scale,
                          bool chevron, int seed)
    {
        var go = FindOrCreateChild(root, name);
        var wall = GetOrAdd<GZWallAssembly>(go);
        wall.wallMaterial = mat;
        wall.chevronCrest = chevron;
        wall.seed = seed;

        Vector3 pos = anchor;
        pos.y = terrain.SampleHeight(pos) + terrain.transform.position.y + baseHeight;
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yawDegrees, 0f));
        go.transform.localScale = Vector3.one * scale;

        wall.BuildMesh();
        wall.PushMaterialParams();
        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null) mr.sharedMaterial = mat;
    }

    // ---------- helpers ----------

    static GameObject FindOrCreateChild(Transform parent, string name)
    {
        var t = parent.Find(name);
        if (t != null) return t.gameObject;
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go;
    }

    static T GetOrAdd<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }

    static Material LoadOrCreateRingMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(RingMatPath);
        if (mat == null)
        {
            var shader = Shader.Find("GreatZimbabwe/FactFXRing");
            if (shader == null) { Debug.LogError("FactFXRing shader missing"); return null; }
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, RingMatPath);
        }
        // Canonical tuning lives here so re-running setup always restores the intended look.
        mat.SetFloat("_Intensity", 0.85f);
        mat.SetFloat("_BandWidth", 0.035f);
        mat.SetFloat("_ShimmerScale", 0.14f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Material LoadOrCreateTowerMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(TowerMatPath);
        if (mat == null)
        {
            var shader = Shader.Find("GreatZimbabwe/TowerScan");
            if (shader == null) { Debug.LogError("TowerScan shader missing"); return null; }
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, TowerMatPath);
        }
        mat.SetFloat("_Intensity", 0.5f); // dims come from GZTowerFX.PushMaterialParams
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Material LoadOrCreateRouteMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(RouteMatPath);
        if (mat == null)
        {
            var shader = Shader.Find("GreatZimbabwe/RouteGlow");
            if (shader == null) { Debug.LogError("RouteGlow shader missing"); return null; }
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, RouteMatPath);
        }
        mat.SetFloat("_Intensity", 0.8f);
        mat.SetFloat("_DashCount", 26f);
        mat.SetFloat("_FlowSpeed", 1.2f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Material LoadOrCreateGrainMaterial()
    {
        var tex = LoadOrCreateGrainTexture();
        var mat = AssetDatabase.LoadAssetAtPath<Material>(GrainMatPath);
        if (mat == null)
        {
            var shader = Shader.Find("GreatZimbabwe/FXParticleAdd");
            if (shader == null) { Debug.LogError("FXParticleAdd shader missing"); return null; }
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, GrainMatPath);
        }
        if (tex != null) mat.SetTexture("_BaseMap", tex);
        mat.SetFloat("_Intensity", 1.1f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>Tiny radial-falloff dot for the grain billboards — generated, no image assets.</summary>
    static Texture2D LoadOrCreateGrainTexture()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(GrainTexPath);
        if (existing != null) return existing;

        const int size = 32;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size - 0.5f, dy = (y + 0.5f) / size - 0.5f;
                float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(Mathf.SmoothStep(1f, 0.1f, r)) * 255f);
                px[y * size + x] = new Color32(255, 255, 255, a);
            }
        tex.SetPixels32(px);
        File.WriteAllBytes(GrainTexPath, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(GrainTexPath);

        var imp = (TextureImporter)AssetImporter.GetAtPath(GrainTexPath);
        imp.alphaIsTransparency = true;
        imp.mipmapEnabled = true;
        imp.wrapMode = TextureWrapMode.Clamp;
        imp.maxTextureSize = 32;
        imp.textureCompression = TextureImporterCompression.Uncompressed;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(GrainTexPath);
    }

    static Material LoadOrCreateWallMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(WallMatPath);
        if (mat == null)
        {
            var shader = Shader.Find("GreatZimbabwe/WallAssembly");
            if (shader == null) { Debug.LogError("WallAssembly shader missing"); return null; }
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, WallMatPath);
        }
        // Canonical tuning lives here so re-running setup always restores the intended look.
        mat.SetFloat("_Intensity", 0.45f);
        mat.SetFloat("_AssembleWindow", 0.33f);
        EditorUtility.SetDirty(mat);
        return mat;
    }
}
