using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Builds the Great Zimbabwe terrain from the photogrammetry DEM/orthophoto
/// preprocessed into greatZimData/processed/ (see process_dem.py).
/// Data: Zenodo 19093686, CC-BY 4.0, Lowenborg & Mtetwa (Uppsala University).
/// CLI: Unity.exe -batchmode -quit -projectPath "My project"
///      -executeMethod GZTerrainBuilder.BuildFromCli -logFile build.log
/// </summary>
public static class GZTerrainBuilder
{
    [Serializable]
    private class Meta
    {
        public float sizeX;
        public float sizeZ;
        public float minElev;
        public float maxElev;
        public int heightRes;
        public string rawFile;
        public string orthoFile;
        public int holesFilled;
        public string source;
    }

    private const string AssetDir = "Assets/GreatZimbabwe";
    private const string ScenePath = "Assets/Scenes/GreatZimbabwe.unity";

    public static void BuildFromCli()
    {
        try
        {
            Build();
        }
        catch (Exception e)
        {
            Debug.LogError("GZTerrainBuilder failed: " + e);
            EditorApplication.Exit(1);
        }
    }

    [MenuItem("Nhaka/Build Great Zimbabwe Terrain")]
    public static void Build()
    {
        string dataDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "greatZimData", "processed"));
        Meta meta = JsonUtility.FromJson<Meta>(File.ReadAllText(Path.Combine(dataDir, "GZ_terrain_meta.json")));
        Debug.Log($"GZ terrain: {meta.sizeX}x{meta.sizeZ} m, elev {meta.minElev}..{meta.maxElev} m, res {meta.heightRes}");

        Directory.CreateDirectory(Path.Combine(Application.dataPath, "GreatZimbabwe"));
        AssetDatabase.Refresh();

        Texture2D ortho = ImportOrtho(Path.Combine(dataDir, meta.orthoFile), meta.orthoFile);
        TerrainData terrainData = BuildTerrainData(Path.Combine(dataDir, meta.rawFile), meta);
        AttachOrthoLayer(terrainData, ortho, meta);
        BuildScene(terrainData, meta);
        AssetDatabase.SaveAssets();
        Debug.Log("GZTerrainBuilder: done.");
    }

    private static Texture2D ImportOrtho(string srcPath, string fileName)
    {
        string destAssetPath = AssetDir + "/" + fileName;
        File.Copy(srcPath, Path.Combine(Application.dataPath, "GreatZimbabwe", fileName), true);
        AssetDatabase.ImportAsset(destAssetPath);

        var importer = (TextureImporter)AssetImporter.GetAtPath(destAssetPath);
        importer.maxTextureSize = 8192;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.mipmapEnabled = true;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(destAssetPath);
    }

    private static TerrainData BuildTerrainData(string rawPath, Meta meta)
    {
        int res = meta.heightRes;
        byte[] raw = File.ReadAllBytes(rawPath);
        if (raw.Length != res * res * 2)
            throw new InvalidDataException($"RAW size {raw.Length} != {res}x{res}x2");

        // RAW is little-endian ushort, row 0 = SOUTH edge, columns west->east,
        // which matches SetHeights' [z, x] ordering directly.
        var heights = new float[res, res];
        int i = 0;
        for (int z = 0; z < res; z++)
        {
            for (int x = 0; x < res; x++, i += 2)
            {
                heights[z, x] = (raw[i] | (raw[i + 1] << 8)) / 65535f;
            }
        }

        var td = new TerrainData { heightmapResolution = res };
        td.size = new Vector3(meta.sizeX, meta.maxElev - meta.minElev, meta.sizeZ);
        td.baseMapResolution = 2048;
        td.SetHeights(0, 0, heights);
        AssetDatabase.CreateAsset(td, AssetDir + "/GZ_TerrainData.asset");
        return td;
    }

    private static void AttachOrthoLayer(TerrainData td, Texture2D ortho, Meta meta)
    {
        var layer = new TerrainLayer
        {
            diffuseTexture = ortho,
            tileSize = new Vector2(meta.sizeX, meta.sizeZ), // drape exactly once
            tileOffset = Vector2.zero,
            metallic = 0f,
            smoothness = 0f,
        };
        AssetDatabase.CreateAsset(layer, AssetDir + "/GZ_OrthoLayer.terrainlayer");
        td.terrainLayers = new[] { layer };
    }

    private static void BuildScene(TerrainData td, Meta meta)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        GameObject terrainGo = Terrain.CreateTerrainGameObject(td);
        terrainGo.name = "GZ_Terrain";
        var terrain = terrainGo.GetComponent<Terrain>();
        terrain.heightmapPixelError = 3f;
        terrain.basemapDistance = 3000f;

        var light = UnityEngine.Object.FindFirstObjectByType<Light>();
        if (light != null)
            light.transform.rotation = Quaternion.Euler(50f, 210f, 0f);

        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log("Saved scene " + ScenePath);

        string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        // (x, z) guesses from the north-up ortho: Great Enclosure ~ (487, 337), Hill Complex ~ (737, 938)
        Snap(new Vector3(400f, 450f, -350f), new Vector3(566f, 80f, 600f), Path.Combine(root, "Preview_GZ_overview.png"));
        Snap(new Vector3(487f, 140f, 80f), new Vector3(487f, 30f, 337f), Path.Combine(root, "Preview_GZ_great_enclosure.png"));
        Snap(new Vector3(737f, 230f, 950f), new Vector3(487f, 30f, 337f), Path.Combine(root, "Preview_GZ_hill_view.png"));
    }

    private static void Snap(Vector3 pos, Vector3 lookAt, string path)
    {
        var go = new GameObject("PreviewCam");
        var cam = go.AddComponent<Camera>();
        go.transform.position = pos;
        go.transform.LookAt(lookAt);
        cam.fieldOfView = 55f;
        cam.farClipPlane = 6000f;

        var rt = new RenderTexture(1920, 1080, 24);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());

        cam.targetTexture = null;
        RenderTexture.active = null;
        UnityEngine.Object.DestroyImmediate(rt);
        UnityEngine.Object.DestroyImmediate(tex);
        UnityEngine.Object.DestroyImmediate(go);
        Debug.Log("Rendered " + path);
    }
}
