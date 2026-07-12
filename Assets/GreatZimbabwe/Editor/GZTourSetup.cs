using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// One-shot setup/teardown for the Great Zimbabwe guided aerial tour:
/// tour camera + director + question UI + NPC backends (local LLM with
/// scripted offline fallback), and the POI catalogue. POI anchors reuse the
/// monument zone centers calibrated in <see cref="GZVegetationFlattener"/>
/// (ortho UV × terrain size), so they survive any terrain size tweaks.
/// Everything lives under one root and is reversible via <see cref="RemoveTour"/>.
/// Drive from the CLI: eval "return GZTourSetup.SetupTour();"
/// </summary>
public static class GZTourSetup
{
    const string RootName = "GZ_Tour";
    const string DisabledCameraName = "Main Camera";

    public static string Ping() => "GZTour v1 compiled";

    [MenuItem("Tools/Great Zimbabwe/Tour/Setup Aerial Tour")]
    static void SetupMenu() { Debug.Log(SetupTour()); }

    [MenuItem("Tools/Great Zimbabwe/Tour/Remove Aerial Tour")]
    static void RemoveMenu() { Debug.Log(RemoveTour()); }

    public static string SetupTour()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.path.Contains("GreatZimbabwe"))
            return "ABORT: active scene is " + scene.path + ", not GreatZimbabwe.";
        var terrain = Terrain.activeTerrain;
        if (terrain == null)
            return "ABORT: no Terrain in the open scene — open the PC scene (GreatZimbabwe.unity), not the mobile bake.";

        Vector3 size = terrain.terrainData.size;
        var log = new System.Text.StringBuilder();

        var root = GameObject.Find(RootName);
        if (root == null) root = new GameObject(RootName);

        // ---------------- tour camera ----------------
        var camGO = FindOrCreateChild(root.transform, "GZ_TourCamera");
        var cam = camGO.GetComponent<Camera>();
        if (cam == null) cam = camGO.AddComponent<Camera>();
        cam.fieldOfView = 58f;
        cam.nearClipPlane = 0.5f;
        cam.farClipPlane = 2600f;
        camGO.tag = "MainCamera";
        if (camGO.GetComponent<UniversalAdditionalCameraData>() == null)
            camGO.AddComponent<UniversalAdditionalCameraData>();
        if (camGO.GetComponent<AudioListener>() == null)
            camGO.AddComponent<AudioListener>();
        var tourCam = camGO.GetComponent<GZTourCamera>();
        if (tourCam == null) tourCam = camGO.AddComponent<GZTourCamera>();

        // The original scene camera would fight ours for display + audio.
        var oldCam = GameObject.Find(DisabledCameraName);
        if (oldCam != null && oldCam != camGO)
        {
            oldCam.SetActive(false);
            log.Append("Disabled '").Append(DisabledCameraName).Append("' (restored by RemoveTour). | ");
        }

        // ---------------- director + backends + UI ----------------
        var dirGO = FindOrCreateChild(root.transform, "GZ_TourDirector");
        var director = GetOrAdd<GZTourDirector>(dirGO);
        var scripted = GetOrAdd<GZScriptedNpcBackend>(dirGO);
        var llm = GetOrAdd<GZLocalLLMBackend>(dirGO);

        var uiGO = FindOrCreateChild(root.transform, "GZ_TourUI");
        var ui = GetOrAdd<GZTourUI>(uiGO);
        ui.director = director;

        director.tourCamera = tourCam;
        director.ui = ui;
        director.scriptedBackend = scripted;
        director.llmBackend = llm;
        director.preferLocalLLM = true;

        // ---------------- POI catalogue ----------------
        // Anchors: ortho-UV zone centers from GZVegetationFlattener × terrain size.
        // Altitudes encode the quality rule: monuments viewed high and wide
        // (photogrammetry never seen up close), cattle viewed low and tight.
        director.pois = new List<GZTourPOI>
        {
            Poi("overview", "Great Zimbabwe — Grand Tour", size, 0.487f, 0.390f,
                alt: 230f, radius: 380f, clearance: 90f, speed: 26f, gaze: 0f,
                kws: new[] { "overview", "everything", "whole", "entire", "site", "city", "tour", "around", "kingdom", "layout", "trade" }),

            Poi("hill_complex", "The Hill Complex", size, 0.490f, 0.725f,
                alt: 150f, radius: 260f, clearance: 60f, speed: 18f, gaze: 8f,
                kws: new[] { "hill", "acropolis", "summit", "boulders", "ritual", "spiritual", "sacred", "bird", "eastern enclosure" }),

            Poi("great_enclosure", "The Great Enclosure", size, 0.4514f, 0.2032f,
                alt: 115f, radius: 170f, clearance: 45f, speed: 15f, gaze: 6f,
                kws: new[] { "great enclosure", "enclosure", "wall", "imba", "largest", "passage" }),

            Poi("conical_tower", "The Conical Tower", size, 0.4620f, 0.1880f,
                alt: 85f, radius: 115f, clearance: 40f, speed: 12f, gaze: 5f,
                kws: new[] { "tower", "conical", "granary", "grain" }),

            Poi("valley_ruins", "The Valley Ruins", size, 0.513f, 0.360f,
                alt: 105f, radius: 155f, clearance: 45f, speed: 15f, gaze: 4f,
                kws: new[] { "valley", "daga", "houses", "homes", "lived", "population", "people", "town" }),

            Poi("karanga_village", "The Karanga Village", size, 0.223f, 0.128f,
                alt: 85f, radius: 125f, clearance: 40f, speed: 13f, gaze: 3f,
                kws: new[] { "village", "karanga", "hut", "thatch", "museum", "reconstruct" }),

            Poi("east_ruins", "The East Ruins", size, 0.760f, 0.442f,
                alt: 95f, radius: 135f, clearance: 40f, speed: 14f, gaze: 3f,
                kws: new[] { "east ruins", "east" }),

            Poi("cattle", "The Cattle Herds", size, 0.322f, 0.345f,
                alt: 32f, radius: 65f, clearance: 16f, speed: 9f, gaze: 1.5f,
                kws: new[] { "cattle", "cow", "cows", "herd", "animal", "animals", "livestock", "beef", "graze", "grazing", "wealth", "oxen" },
                focus: GZFocusKind.HerdCentroid),
        };
        log.Append(director.pois.Count).Append(" POIs (anchors from calibrated zone UVs). | ");

        // Park the camera on the overview ring so play mode starts mid-cruise.
        var overview = director.pois[0];
        Vector3 focus = overview.anchor;
        focus.y = terrain.SampleHeight(focus) + terrain.transform.position.y;
        Vector3 start = focus + Quaternion.Euler(0f, 205f, 0f) * Vector3.forward * overview.orbitRadius;
        start.y = focus.y + overview.altitude;
        start.y = Mathf.Max(start.y, terrain.SampleHeight(start) + terrain.transform.position.y + overview.minClearance);
        camGO.transform.position = start;
        camGO.transform.LookAt(focus);

        EditorUtility.SetDirty(root);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        log.Append("Tour ready: enter play mode and ask a question (local LLM on ")
           .Append(llm.endpoint).Append(", offline fallback active). Scene saved.");
        return log.ToString();
    }

    public static string RemoveTour()
    {
        var scene = SceneManager.GetActiveScene();
        var log = new System.Text.StringBuilder();

        var root = GameObject.Find(RootName);
        if (root != null) { Object.DestroyImmediate(root); log.Append("Removed " + RootName + ". | "); }
        else log.Append("No " + RootName + " root found. | ");

        // GameObject.Find can't see inactive objects — scan scene roots.
        foreach (var go in scene.GetRootGameObjects())
        {
            if (go.name == DisabledCameraName && !go.activeSelf)
            {
                go.SetActive(true);
                log.Append("Re-enabled '" + DisabledCameraName + "'. | ");
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        log.Append("Scene saved.");
        return log.ToString();
    }

    /// <summary>
    /// Renders a still from a POI's orbit ring using the same numbers the runtime
    /// camera flies — for judging framing/altitude without entering play mode.
    /// </summary>
    public static string RenderPoiPreview(string poiId, float azimuthDeg, string path)
    {
        var director = Object.FindFirstObjectByType<GZTourDirector>();
        if (director == null) return "No GZTourDirector in scene — run SetupTour first.";
        var poi = director.FindPoi(poiId);
        if (poi == null) return "Unknown POI '" + poiId + "'.";
        var terrain = Terrain.activeTerrain;
        if (terrain == null) return "No terrain.";

        Vector3 focus;
        float ground;
        var herd = Object.FindFirstObjectByType<GZHerdRunner>();
        if (poi.focusKind == GZFocusKind.HerdCentroid && herd != null && herd.transform.childCount > 0)
        {
            Vector3 sum = Vector3.zero;
            int n = 0;
            foreach (Transform a in herd.transform) { sum += a.position; n++; }
            focus = sum / n;
            ground = focus.y;
            focus += Vector3.up * poi.focusHeightOffset;
        }
        else
        {
            ground = terrain.SampleHeight(poi.anchor) + terrain.transform.position.y;
            focus = new Vector3(poi.anchor.x, ground + poi.focusHeightOffset, poi.anchor.z);
        }

        Vector3 pos = focus + Quaternion.Euler(0f, azimuthDeg, 0f) * Vector3.forward * poi.orbitRadius;
        pos.y = ground + poi.altitude;
        pos.y = Mathf.Max(pos.y, terrain.SampleHeight(pos) + terrain.transform.position.y + poi.minClearance);

        return GZVegetationFlattener.RenderPreview(path, pos.x, pos.y, pos.z, focus.x, focus.y, focus.z, 58f)
               + " cam=" + pos.ToString("F0") + " focus=" + focus.ToString("F0");
    }

    // ---------- helpers ----------

    static GZTourPOI Poi(string id, string name, Vector3 terrainSize, float u, float v,
                         float alt, float radius, float clearance, float speed, float gaze,
                         string[] kws, GZFocusKind focus = GZFocusKind.StaticAnchor)
    {
        return new GZTourPOI
        {
            id = id,
            displayName = name,
            keywords = kws,
            focusKind = focus,
            anchor = new Vector3(u * terrainSize.x, 0f, v * terrainSize.z),
            focusHeightOffset = gaze,
            altitude = alt,
            orbitRadius = radius,
            minClearance = clearance,
            cruiseSpeed = speed,
        };
    }

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
}
