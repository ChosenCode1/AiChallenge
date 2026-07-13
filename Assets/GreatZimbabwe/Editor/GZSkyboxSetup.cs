using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Setup/teardown for the procedural savanna skybox (GreatZimbabwe/Skybox).
/// Creates GZ_Skybox.mat, assigns it as the environment skybox of both tour scenes
/// (desktop + mobile), wires RenderSettings.sun to the directional light so the
/// shader's sun disc and the actual lighting agree, and refreshes the ambient probe.
/// Reversible via <see cref="RemoveSkybox"/> (restores Unity's Default-Skybox).
/// CLI: `Unity.exe -batchmode -quit -projectPath ... -executeMethod GZSkyboxSetup.Apply`
/// </summary>
public static class GZSkyboxSetup
{
    const string MatPath = "Assets/GreatZimbabwe/GZ_Skybox.mat";
    const string ShaderName = "GreatZimbabwe/Skybox";

    static readonly string[] ScenePaths =
    {
        "Assets/Scenes/GreatZimbabwe.unity",
        "Assets/Scenes/GreatZimbabwe_Mobile.unity",
    };

    [MenuItem("Tools/Great Zimbabwe/Sky/Setup Savanna Skybox (both scenes)")]
    public static void Apply() { Debug.Log(SetupSkybox()); }

    [MenuItem("Tools/Great Zimbabwe/Sky/Remove Savanna Skybox (both scenes)")]
    public static void Remove() { Debug.Log(RemoveSkybox()); }

    public static string SetupSkybox()
    {
        var mat = LoadOrCreateSkyboxMaterial();
        if (mat == null) return "ABORT: shader '" + ShaderName + "' not found (compile errors?).";
        return ForEachScene(scene =>
        {
            ApplyToActiveScene(mat);
            var sun = RenderSettings.sun;
            return "skybox=GZ_Skybox, sun=" + (sun != null ? sun.name : "NONE");
        });
    }

    public static string RemoveSkybox()
    {
        var defaultSky = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
        return ForEachScene(scene =>
        {
            RenderSettings.skybox = defaultSky;
            DynamicGI.UpdateEnvironment();
            return "skybox=Default-Skybox";
        });
    }

    /// <summary>Sets skybox/sun/ambient on whatever scene is currently open. Does not save.</summary>
    static void ApplyToActiveScene(Material mat)
    {
        RenderSettings.skybox = mat;
        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.ambientIntensity = 1f;
        var sun = FindSun();
        if (sun != null) RenderSettings.sun = sun;
        DynamicGI.UpdateEnvironment();
    }

    static Material LoadOrCreateSkyboxMaterial()
    {
        var shader = Shader.Find(ShaderName);
        if (shader == null) return null;
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, MatPath);
        }
        else if (mat.shader != shader)
        {
            mat.shader = shader;
        }
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        return mat;
    }

    /// <summary>Opens each tour scene, runs the action, saves. Restores the originally open scene.</summary>
    static string ForEachScene(System.Func<Scene, string> action)
    {
        // Interactive editor: give the user the chance to keep unsaved work before we switch scenes.
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return "Cancelled: unsaved scene changes.";

        var log = new System.Text.StringBuilder();
        string original = SceneManager.GetActiveScene().path;
        foreach (var path in ScenePaths)
        {
            if (!System.IO.File.Exists(path))
            {
                log.Append(path).Append(": MISSING | ");
                continue;
            }
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            string result = action(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            log.Append(path).Append(": ").Append(result).Append(" | ");
        }
        if (!string.IsNullOrEmpty(original) && original != SceneManager.GetActiveScene().path
            && System.IO.File.Exists(original))
            EditorSceneManager.OpenScene(original, OpenSceneMode.Single);
        AssetDatabase.SaveAssets();
        return log.Append("Done.").ToString();
    }

    static Light FindSun()
    {
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional && l.enabled)
                return l;
        return null;
    }

    // ---------- one-shot bootstrap ----------

    const string AppliedKey = "GZ.SkyboxBootstrap.Applied";

    /// <summary>
    /// Applies the skybox to the *currently open* tour scene the first time these scripts
    /// compile in an already-running editor, so the sky shows up without a menu click.
    /// Touches only the active scene, never switches scenes, never saves — the user keeps
    /// full control of when the change lands on disk. Runs once (EditorPrefs-guarded);
    /// use the menu item to cover the mobile scene and persist both.
    /// </summary>
    [InitializeOnLoadMethod]
    static void BootstrapOnce()
    {
        if (Application.isBatchMode || EditorPrefs.GetBool(AppliedKey, false)) return;
        EditorApplication.delayCall += () =>
        {
            if (!SceneManager.GetActiveScene().path.Contains("GreatZimbabwe")) return;
            var mat = LoadOrCreateSkyboxMaterial();
            if (mat == null) return;
            ApplyToActiveScene(mat);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorPrefs.SetBool(AppliedKey, true);
            Debug.Log("[GZSkyboxSetup] Savanna skybox applied to the open scene (not yet saved). " +
                      "Run Tools > Great Zimbabwe > Sky > Setup Savanna Skybox to save it into both tour scenes.");
        };
    }
}
