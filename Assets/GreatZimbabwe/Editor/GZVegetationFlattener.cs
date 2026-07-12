using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Removes photogrammetry vegetation artifacts ("tree columns") from the Great Zimbabwe
/// terrain. The survey heightmap is a surface model, so trees appear as sheer-walled
/// columns. Detection is purely geometric (color/texture cannot separate dry canopy from
/// weathered granite in this ortho — measured): a cell is vegetation if it rises sharply
/// above a morphologically-opened ground estimate AND has high-frequency relief. Masonry
/// is protected structurally — thin linear ridges (walls) are removed from the mask by
/// morphological opening and only compact tree-sized blobs are rescued back — plus
/// explicit no-touch buffers over the Great Enclosure, Conical Tower, Hill Complex and
/// the reconstructed village. Flattened cells are re-filled by diffusion interpolation.
/// </summary>
public static class GZVegetationFlattener
{
    const string OrthoPath = "Assets/GreatZimbabwe/GZ_Ortho_8192.jpg";
    const string TerrainDataPath = "Assets/GreatZimbabwe/GZ_TerrainData.asset";
    const int MaskRes = 2048;      // ortho analysis resolution
    const int GridRes = 1025;      // working grid (heightmap / 4)
    const int CoarseRes = 257;     // ground-estimate grid (heightmap / 16)
    const float DefaultAnomalyMeters = 1.8f;

    [MenuItem("Tools/Great Zimbabwe/Flatten Vegetation")]
    static void FlattenMenu()
    {
        string backupDir = Path.Combine(Directory.GetCurrentDirectory(), "Backups_GZ");
        string report = Flatten(backupDir, DefaultAnomalyMeters);
        UnityEngine.Debug.Log(report);
        EditorUtility.DisplayDialog("GZ Vegetation Flattener", report, "OK");
    }

    public static string Preview(string outDir, float anomalyMeters)
    {
        return Run(false, outDir, null, anomalyMeters);
    }

    public static string Flatten(string backupDir, float anomalyMeters)
    {
        return Run(true, null, backupDir, anomalyMeters);
    }

    static Terrain FindTerrain()
    {
        var t = UnityEngine.Object.FindFirstObjectByType<Terrain>();
        if (t == null) throw new Exception("No Terrain found in the open scene");
        return t;
    }

    // Field areas where elongated raised features are hedgerows, not masonry — the
    // wall-preservation rule is bypassed here (monument buffers still win).
    static readonly float[,] AggressiveRects =
    {
        // u0, v0, u1, v1
        { 0.54f, 0.12f, 0.82f, 0.32f },  // fields east / south-east of the Great Enclosure
        { 0.34f, 0.06f, 0.48f, 0.125f }, // fields south of the Great Enclosure
    };

    static bool InAggressive(float u, float v)
    {
        for (int i = 0; i < AggressiveRects.GetLength(0); i++)
            if (u >= AggressiveRects[i, 0] && v >= AggressiveRects[i, 1] &&
                u <= AggressiveRects[i, 2] && v <= AggressiveRects[i, 3]) return true;
        return false;
    }

    // Monument no-touch buffers, in ortho UV space (u right, v up).
    static bool InEllipse(float u, float v, float cu, float cv, float ru, float rv)
    {
        float du = (u - cu) / ru, dv = (v - cv) / rv;
        return du * du + dv * dv < 1f;
    }

    static bool IsProtected(float u, float v)
    {
        // Great Enclosure: protect the wall ring (annulus), flatten trees inside/outside it.
        // Center measured from a calibrated top-down render: world (511.5, 244.5).
        float du = u - 0.4514f, dv = v - 0.2032f;
        float d = Mathf.Sqrt(du * du + dv * dv);
        if (d > 0.0280f && d < 0.0385f) return true;
        // Conical Tower area (south-central interior), generous buffer
        du = u - 0.4620f; dv = v - 0.1880f;
        if (du * du + dv * dv < 0.0132f * 0.0132f) return true;
        // Hill Complex band (cliff walls + summit enclosures)
        if (InEllipse(u, v, 0.490f, 0.725f, 0.210f, 0.058f)) return true;
        // Reconstructed Karanga village (huts are real standing structures)
        if (InEllipse(u, v, 0.223f, 0.128f, 0.035f, 0.032f)) return true;
        // Valley Ruins complex
        if (InEllipse(u, v, 0.513f, 0.360f, 0.065f, 0.075f)) return true;
        // East Ruins
        if (InEllipse(u, v, 0.760f, 0.442f, 0.040f, 0.032f)) return true;
        // Camp Ruins just south of the Great Enclosure
        if (InEllipse(u, v, 0.462f, 0.148f, 0.042f, 0.022f)) return true;
        // Posselt/Philips ruins between GE and Valley Ruins
        if (InEllipse(u, v, 0.425f, 0.278f, 0.025f, 0.035f)) return true;
        return false;
    }

    // ---------- ortho ----------

    static Color32[] LoadOrthoPixels()
    {
        byte[] bytes = File.ReadAllBytes(OrthoPath);
        var full = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!ImageConversion.LoadImage(full, bytes)) throw new Exception("Failed to decode ortho jpg");
        var rt = RenderTexture.GetTemporary(MaskRes, MaskRes, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(full, rt);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var small = new Texture2D(MaskRes, MaskRes, TextureFormat.RGBA32, false);
        small.ReadPixels(new Rect(0, 0, MaskRes, MaskRes), 0, 0);
        small.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        UnityEngine.Object.DestroyImmediate(full);
        var px = small.GetPixels32();
        UnityEngine.Object.DestroyImmediate(small);
        return px;
    }

    static Color32 OrthoAt(Color32[] px, float u, float v)
    {
        int x = Mathf.Clamp((int)(u * (MaskRes - 1)), 0, MaskRes - 1);
        int y = Mathf.Clamp((int)(v * (MaskRes - 1)), 0, MaskRes - 1);
        return px[y * MaskRes + x];
    }

    static bool IsNoData(Color32 c)
    {
        return c.r + c.g + c.b < 40; // black border outside the survey footprint
    }

    // ---------- grid filters (square float grids, index [y * res + x]) ----------

    static float[] Erode3(float[] src, int res)
    {
        var dst = new float[src.Length];
        for (int y = 0; y < res; y++)
        {
            int y0 = Math.Max(0, y - 1), y1 = Math.Min(res - 1, y + 1);
            for (int x = 0; x < res; x++)
            {
                int x0 = Math.Max(0, x - 1), x1 = Math.Min(res - 1, x + 1);
                float v = float.MaxValue;
                for (int yy = y0; yy <= y1; yy++)
                    for (int xx = x0; xx <= x1; xx++)
                        if (src[yy * res + xx] < v) v = src[yy * res + xx];
                dst[y * res + x] = v;
            }
        }
        return dst;
    }

    static float[] Dilate3(float[] src, int res)
    {
        var dst = new float[src.Length];
        for (int y = 0; y < res; y++)
        {
            int y0 = Math.Max(0, y - 1), y1 = Math.Min(res - 1, y + 1);
            for (int x = 0; x < res; x++)
            {
                int x0 = Math.Max(0, x - 1), x1 = Math.Min(res - 1, x + 1);
                float v = float.MinValue;
                for (int yy = y0; yy <= y1; yy++)
                    for (int xx = x0; xx <= x1; xx++)
                        if (src[yy * res + xx] > v) v = src[yy * res + xx];
                dst[y * res + x] = v;
            }
        }
        return dst;
    }

    static float[] Blur3(float[] src, int res)
    {
        var dst = new float[src.Length];
        for (int y = 0; y < res; y++)
        {
            int y0 = Math.Max(0, y - 1), y1 = Math.Min(res - 1, y + 1);
            for (int x = 0; x < res; x++)
            {
                int x0 = Math.Max(0, x - 1), x1 = Math.Min(res - 1, x + 1);
                float sum = 0f; int n = 0;
                for (int yy = y0; yy <= y1; yy++)
                    for (int xx = x0; xx <= x1; xx++) { sum += src[yy * res + xx]; n++; }
                dst[y * res + x] = sum / n;
            }
        }
        return dst;
    }

    static float SampleBilinear(float[] a, int res, float u, float v)
    {
        float fx = Mathf.Clamp01(u) * (res - 1);
        float fy = Mathf.Clamp01(v) * (res - 1);
        int x0 = (int)fx, y0 = (int)fy;
        int x1 = Math.Min(res - 1, x0 + 1), y1 = Math.Min(res - 1, y0 + 1);
        float tx = fx - x0, ty = fy - y0;
        float a00 = a[y0 * res + x0], a10 = a[y0 * res + x1];
        float a01 = a[y1 * res + x0], a11 = a[y1 * res + x1];
        return Mathf.Lerp(Mathf.Lerp(a00, a10, tx), Mathf.Lerp(a01, a11, tx), ty);
    }

    static void JacobiFill(float[] h, bool[] unknown, int res, int iters)
    {
        var tmp = (float[])h.Clone();
        var src = h; var dst = tmp;
        for (int it = 0; it < iters; it++)
        {
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    int i = y * res + x;
                    if (!unknown[i]) { dst[i] = src[i]; continue; }
                    float sum = 0f; int n = 0;
                    if (x > 0) { sum += src[i - 1]; n++; }
                    if (x < res - 1) { sum += src[i + 1]; n++; }
                    if (y > 0) { sum += src[i - res]; n++; }
                    if (y < res - 1) { sum += src[i + res]; n++; }
                    dst[i] = sum / n;
                }
            }
            var s = src; src = dst; dst = s;
        }
        if (src != h) Array.Copy(src, h, h.Length);
    }

    // ---------- core ----------

    static string Run(bool apply, string outDir, string backupDir, float anomalyMeters)
    {
        var sw = Stopwatch.StartNew();
        var terrain = FindTerrain();
        var td = terrain.terrainData;
        float heightScale = td.size.y;

        var px = LoadOrthoPixels();
        int res = td.heightmapResolution; // 4097
        float[,] h = td.GetHeights(0, 0, res, res); // [y(z), x], normalized

        // working grid 1025
        var h1 = new float[GridRes * GridRes];
        for (int y = 0; y < GridRes; y++)
            for (int x = 0; x < GridRes; x++)
                h1[y * GridRes + x] = h[y * 4, x * 4];

        // coarse grid 257: ground estimate by morphological opening (~65 m radius)
        var h2 = new float[CoarseRes * CoarseRes];
        var noData2 = new float[CoarseRes * CoarseRes];
        for (int y = 0; y < CoarseRes; y++)
        {
            float v = y / (float)(CoarseRes - 1);
            for (int x = 0; x < CoarseRes; x++)
            {
                float u = x / (float)(CoarseRes - 1);
                h2[y * CoarseRes + x] = h1[(y * 4) * GridRes + (x * 4)];
                noData2[y * CoarseRes + x] = IsNoData(OrthoAt(px, u, v)) ? 1f : 0f;
            }
        }
        var opened = h2;
        for (int i = 0; i < 15; i++) opened = Erode3(opened, CoarseRes);
        for (int i = 0; i < 15; i++) opened = Dilate3(opened, CoarseRes);
        var noDataZone = noData2;
        for (int i = 0; i < 5; i++) noDataZone = Dilate3(noDataZone, CoarseRes);

        // high-frequency relief (rejects smooth convex granite domes)
        var hBlur = Blur3(h1, GridRes);
        var resid = new float[GridRes * GridRes];
        for (int i = 0; i < resid.Length; i++) resid[i] = Math.Abs(h1[i] - hBlur[i]);
        var residMax = Dilate3(resid, GridRes);
        float residThr = 0.35f / heightScale;

        // raw vegetation mask
        var mask = new float[GridRes * GridRes];
        long candidates = 0, rejectedSmooth = 0, vetoedEdge = 0;
        for (int y = 0; y < GridRes; y++)
        {
            float v = y / (float)(GridRes - 1);
            for (int x = 0; x < GridRes; x++)
            {
                float u = x / (float)(GridRes - 1);
                int i = y * GridRes + x;
                float ground = SampleBilinear(opened, CoarseRes, u, v);
                float anomalyM = (h1[i] - ground) * heightScale;
                if (anomalyM <= anomalyMeters) continue;
                candidates++;
                if (SampleBilinear(noDataZone, CoarseRes, u, v) > 0.05f) { vetoedEdge++; continue; }
                if (residMax[i] < residThr) { rejectedSmooth++; continue; }
                mask[i] = 1f;
            }
        }

        // close(2): bridge gaps inside canopy blobs
        mask = Dilate3(mask, GridRes); mask = Dilate3(mask, GridRes);
        mask = Erode3(mask, GridRes); mask = Erode3(mask, GridRes);

        // open(2): strip thin linear ridges (walls) out of the mask...
        var openedMask = Erode3(mask, GridRes); openedMask = Erode3(openedMask, GridRes);
        openedMask = Dilate3(openedMask, GridRes); openedMask = Dilate3(openedMask, GridRes);

        // ...then rescue compact tree-sized blobs the opening also removed
        long rescued = 0, wallLike = 0;
        {
            var visited = new bool[GridRes * GridRes];
            var stack = new Stack<int>();
            var cells = new List<int>();
            for (int i = 0; i < mask.Length; i++)
            {
                if (mask[i] < 0.5f || openedMask[i] > 0.5f || visited[i]) continue;
                // BFS over residue component (mask on, openedMask off)
                stack.Clear(); cells.Clear();
                stack.Push(i); visited[i] = true;
                int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
                while (stack.Count > 0)
                {
                    int c = stack.Pop();
                    cells.Add(c);
                    int cx = c % GridRes, cy = c / GridRes;
                    if (cx < minX) minX = cx; if (cx > maxX) maxX = cx;
                    if (cy < minY) minY = cy; if (cy > maxY) maxY = cy;
                    for (int k = 0; k < 4; k++)
                    {
                        int nx = cx + (k == 0 ? 1 : k == 1 ? -1 : 0);
                        int ny = cy + (k == 2 ? 1 : k == 3 ? -1 : 0);
                        if (nx < 0 || ny < 0 || nx >= GridRes || ny >= GridRes) continue;
                        int ni = ny * GridRes + nx;
                        if (visited[ni] || mask[ni] < 0.5f || openedMask[ni] > 0.5f) continue;
                        visited[ni] = true;
                        stack.Push(ni);
                    }
                }
                bool small = (maxX - minX + 1) <= 6 && (maxY - minY + 1) <= 6 && cells.Count <= 30;
                if (small) { foreach (var c in cells) openedMask[c] = 1f; rescued++; }
                else wallLike++;
            }
        }

        // in hedgerow field rects, elongated features are trees — restore the closed mask
        for (int y = 0; y < GridRes; y++)
        {
            float v = y / (float)(GridRes - 1);
            for (int x = 0; x < GridRes; x++)
            {
                int i = y * GridRes + x;
                if (mask[i] > 0.5f && openedMask[i] < 0.5f &&
                    InAggressive(x / (float)(GridRes - 1), v)) openedMask[i] = 1f;
            }
        }
        mask = openedMask;

        // fill enclosed holes (canopy interiors) — flood from border over unmasked cells
        {
            var reach = new bool[GridRes * GridRes];
            var stack = new Stack<int>();
            for (int x = 0; x < GridRes; x++)
            {
                int top = (GridRes - 1) * GridRes + x;
                if (mask[x] < 0.5f && !reach[x]) { reach[x] = true; stack.Push(x); }
                if (mask[top] < 0.5f && !reach[top]) { reach[top] = true; stack.Push(top); }
            }
            for (int y = 0; y < GridRes; y++)
            {
                int l = y * GridRes, r = y * GridRes + GridRes - 1;
                if (mask[l] < 0.5f && !reach[l]) { reach[l] = true; stack.Push(l); }
                if (mask[r] < 0.5f && !reach[r]) { reach[r] = true; stack.Push(r); }
            }
            while (stack.Count > 0)
            {
                int c = stack.Pop();
                int cx = c % GridRes, cy = c / GridRes;
                for (int k = 0; k < 4; k++)
                {
                    int nx = cx + (k == 0 ? 1 : k == 1 ? -1 : 0);
                    int ny = cy + (k == 2 ? 1 : k == 3 ? -1 : 0);
                    if (nx < 0 || ny < 0 || nx >= GridRes || ny >= GridRes) continue;
                    int ni = ny * GridRes + nx;
                    if (reach[ni] || mask[ni] > 0.5f) continue;
                    reach[ni] = true;
                    stack.Push(ni);
                }
            }
            for (int i = 0; i < mask.Length; i++)
                if (mask[i] < 0.5f && !reach[i]) mask[i] = 1f;
        }

        // monument no-touch buffers override everything
        for (int y = 0; y < GridRes; y++)
        {
            float v = y / (float)(GridRes - 1);
            for (int x = 0; x < GridRes; x++)
            {
                float u = x / (float)(GridRes - 1);
                if (mask[y * GridRes + x] > 0.5f && IsProtected(u, v)) mask[y * GridRes + x] = 0f;
            }
        }

        long maskedCount = 0;
        for (int i = 0; i < mask.Length; i++) if (mask[i] > 0.5f) maskedCount++;

        mask = Dilate3(mask, GridRes); // skirt
        var maskF = Blur3(Blur3(mask, GridRes), GridRes);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("candidates=" + candidates + " rejectedSmooth=" + rejectedSmooth +
                      " vetoedEdge=" + vetoedEdge + " rescuedSmallBlobs=" + rescued +
                      " wallLikeProtected=" + wallLike);
        sb.AppendLine("masked=" + maskedCount + " (" + (100.0 * maskedCount / mask.Length).ToString("F1") + "% of cells)");

        if (!apply)
        {
            var opx = new Color32[px.Length];
            for (int y = 0; y < MaskRes; y++)
            {
                float v = y / (float)(MaskRes - 1);
                for (int x = 0; x < MaskRes; x++)
                {
                    float u = x / (float)(MaskRes - 1);
                    int i = y * MaskRes + x;
                    if (SampleBilinear(maskF, GridRes, u, v) > 0.3f)
                        opx[i] = new Color32((byte)Math.Min(255, px[i].r / 2 + 128), (byte)(px[i].g / 3), (byte)(px[i].b / 3), 255);
                    else if (IsProtected(u, v))
                        opx[i] = new Color32((byte)(px[i].r / 3), (byte)(px[i].g / 3), (byte)Math.Min(255, px[i].b / 2 + 128), 255);
                    else opx[i] = px[i];
                }
            }
            var overlay = new Texture2D(MaskRes, MaskRes, TextureFormat.RGBA32, false);
            overlay.SetPixels32(opx);
            overlay.Apply();
            Directory.CreateDirectory(outDir);
            string p = Path.Combine(outDir, "gz_veg_mask_overlay.png");
            File.WriteAllBytes(p, ImageConversion.EncodeToPNG(overlay));
            UnityEngine.Object.DestroyImmediate(overlay);
            sb.AppendLine("overlay=" + p);
            sb.AppendLine("previewMs=" + sw.ElapsedMilliseconds);
            return sb.ToString();
        }

        // ---- apply ----
        Directory.CreateDirectory(backupDir);
        string backup = Path.Combine(backupDir,
            "GZ_TerrainData_backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".asset");
        File.Copy(TerrainDataPath, backup, true);

        var unk1 = new bool[GridRes * GridRes];
        for (int i = 0; i < unk1.Length; i++) unk1[i] = maskF[i] > 0.15f;

        var fill2 = new float[CoarseRes * CoarseRes];
        var unk2 = new bool[CoarseRes * CoarseRes];
        double knownSum = 0; long knownN = 0;
        for (int y = 0; y < CoarseRes; y++)
        {
            for (int x = 0; x < CoarseRes; x++)
            {
                bool anyUnknown = false;
                for (int yy = y * 4; yy <= Math.Min(GridRes - 1, y * 4 + 3) && !anyUnknown; yy++)
                    for (int xx = x * 4; xx <= Math.Min(GridRes - 1, x * 4 + 3); xx++)
                        if (unk1[yy * GridRes + xx]) { anyUnknown = true; break; }
                float val = h1[Math.Min(GridRes - 1, y * 4) * GridRes + Math.Min(GridRes - 1, x * 4)];
                fill2[y * CoarseRes + x] = val;
                unk2[y * CoarseRes + x] = anyUnknown;
                if (!anyUnknown) { knownSum += val; knownN++; }
            }
        }
        float knownMean = knownN > 0 ? (float)(knownSum / knownN) : 0.5f;
        for (int i = 0; i < fill2.Length; i++) if (unk2[i]) fill2[i] = knownMean;
        JacobiFill(fill2, unk2, CoarseRes, 400);

        var fill1 = (float[])h1.Clone();
        for (int y = 0; y < GridRes; y++)
        {
            float v = y / (float)(GridRes - 1);
            for (int x = 0; x < GridRes; x++)
            {
                if (!unk1[y * GridRes + x]) continue;
                float u = x / (float)(GridRes - 1);
                fill1[y * GridRes + x] = SampleBilinear(fill2, CoarseRes, u, v);
            }
        }
        JacobiFill(fill1, unk1, GridRes, 150);
        long fillMs = sw.ElapsedMilliseconds;

        // apply to full-res heightmap
        long changed = 0;
        float maxDrop = 0f;
        for (int y = 0; y < res; y++)
        {
            float v = y / (float)(res - 1);
            for (int x = 0; x < res; x++)
            {
                float u = x / (float)(res - 1);
                float fm = SampleBilinear(maskF, GridRes, u, v);
                if (fm < 0.01f) continue;
                float orig = h[y, x];
                float filled = SampleBilinear(fill1, GridRes, u, v);
                float target = Math.Min(filled, orig); // never raise ground
                float nh = orig + fm * (target - orig);
                if (orig - nh > maxDrop) maxDrop = orig - nh;
                if ((orig - nh) * heightScale > 0.25f) changed++;
                h[y, x] = nh;
            }
        }

        td.SetHeights(0, 0, h);
        EditorUtility.SetDirty(td);
        AssetDatabase.SaveAssets();

        sb.AppendLine("backup=" + backup);
        sb.AppendLine("cellsLowered>0.25m=" + changed);
        sb.AppendLine("maxDrop=" + (maxDrop * heightScale).ToString("F1") + "m");
        sb.AppendLine("fillMs=" + fillMs + " totalMs=" + sw.ElapsedMilliseconds);
        return sb.ToString();
    }

    /// <summary>
    /// Full-resolution cleanup for skinny tree columns (narrower than ~4.5 m) that slip
    /// between the coarse-grid samples of the main pass. Morphological opening with a
    /// ~2 m radius removes only narrow spikes; a "tallness" gate (peak drop > 2.5 m in
    /// the neighbourhood) spares low field walls, and monument buffers plus the survey
    /// edge are skipped entirely.
    /// </summary>
    public static string SpikePass()
    {
        var sw = Stopwatch.StartNew();
        var terrain = FindTerrain();
        var td = terrain.terrainData;
        float heightScale = td.size.y;
        var px = LoadOrthoPixels();
        int res = td.heightmapResolution;
        float[,] h = td.GetHeights(0, 0, res, res);

        // no-data zone on coarse grid, as in Run()
        var noData2 = new float[CoarseRes * CoarseRes];
        for (int y = 0; y < CoarseRes; y++)
        {
            float v = y / (float)(CoarseRes - 1);
            for (int x = 0; x < CoarseRes; x++)
            {
                float u = x / (float)(CoarseRes - 1);
                noData2[y * CoarseRes + x] = IsNoData(OrthoAt(px, u, v)) ? 1f : 0f;
            }
        }
        for (int i = 0; i < 5; i++) noData2 = Dilate3(noData2, CoarseRes);

        const int R = 8; // opening radius in heightmap px (~2.2 m)
        var flat = new float[res * res];
        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
                flat[y * res + x] = h[y, x];

        // separable min filter (erosion), then separable max filter (dilation)
        var tmp = new float[res * res];
        MinFilterH(flat, tmp, res, R); MinFilterV(tmp, flat, res, R);
        MaxFilterH(flat, tmp, res, R); MaxFilterV(tmp, flat, res, R);
        // flat now holds the opened surface

        // drop map and neighbourhood tallness
        var drop = new float[res * res];
        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
                drop[y * res + x] = h[y, x] - flat[y * res + x];
        var tall = new float[res * res];
        MaxFilterH(drop, tmp, res, 5); MaxFilterV(tmp, tall, res, 5);

        float dropThr = 0.6f / heightScale;
        float tallThr = 2.5f / heightScale;
        long changed = 0;
        for (int y = 20; y < res - 20; y++)
        {
            float v = y / (float)(res - 1);
            for (int x = 20; x < res - 20; x++)
            {
                int i = y * res + x;
                if (drop[i] < dropThr || tall[i] < tallThr) continue;
                float u = x / (float)(res - 1);
                if (IsProtected(u, v)) continue;
                if (SampleBilinear(noData2, CoarseRes, u, v) > 0.05f) continue;
                h[y, x] = flat[i];
                changed++;
            }
        }

        td.SetHeights(0, 0, h);
        EditorUtility.SetDirty(td);
        AssetDatabase.SaveAssets();
        return "spikeCellsFlattened=" + changed + " totalMs=" + sw.ElapsedMilliseconds;
    }

    static void MinFilterH(float[] src, float[] dst, int res, int r)
    {
        for (int y = 0; y < res; y++)
        {
            int row = y * res;
            for (int x = 0; x < res; x++)
            {
                int x0 = Math.Max(0, x - r), x1 = Math.Min(res - 1, x + r);
                float m = float.MaxValue;
                for (int xx = x0; xx <= x1; xx++) if (src[row + xx] < m) m = src[row + xx];
                dst[row + x] = m;
            }
        }
    }

    static void MinFilterV(float[] src, float[] dst, int res, int r)
    {
        for (int x = 0; x < res; x++)
        {
            for (int y = 0; y < res; y++)
            {
                int y0 = Math.Max(0, y - r), y1 = Math.Min(res - 1, y + r);
                float m = float.MaxValue;
                for (int yy = y0; yy <= y1; yy++) if (src[yy * res + x] < m) m = src[yy * res + x];
                dst[y * res + x] = m;
            }
        }
    }

    static void MaxFilterH(float[] src, float[] dst, int res, int r)
    {
        for (int y = 0; y < res; y++)
        {
            int row = y * res;
            for (int x = 0; x < res; x++)
            {
                int x0 = Math.Max(0, x - r), x1 = Math.Min(res - 1, x + r);
                float m = float.MinValue;
                for (int xx = x0; xx <= x1; xx++) if (src[row + xx] > m) m = src[row + xx];
                dst[row + x] = m;
            }
        }
    }

    static void MaxFilterV(float[] src, float[] dst, int res, int r)
    {
        for (int x = 0; x < res; x++)
        {
            for (int y = 0; y < res; y++)
            {
                int y0 = Math.Max(0, y - r), y1 = Math.Min(res - 1, y + r);
                float m = float.MinValue;
                for (int yy = y0; yy <= y1; yy++) if (src[yy * res + x] > m) m = src[yy * res + x];
                dst[y * res + x] = m;
            }
        }
    }

    /// <summary>
    /// Surgical cleanup: level a circular area (terrain-local coords, metres) down to the
    /// surrounding ground level. Monument protection zones are still honoured cell-by-cell,
    /// so a circle overlapping a wall buffer only flattens the unprotected part.
    /// </summary>
    public static string FlattenCircle(float worldX, float worldZ, float radius)
    {
        var terrain = FindTerrain();
        var td = terrain.terrainData;
        int res = td.heightmapResolution;
        Vector3 size = td.size;
        float cxf = worldX / size.x * (res - 1);
        float cyf = worldZ / size.z * (res - 1);
        float rC = radius / size.x * (res - 1);
        int pad = (int)(rC + 30);
        int x0 = Mathf.Clamp((int)(cxf - pad), 0, res - 1), x1 = Mathf.Clamp((int)(cxf + pad), 0, res - 1);
        int y0 = Mathf.Clamp((int)(cyf - pad), 0, res - 1), y1 = Mathf.Clamp((int)(cyf + pad), 0, res - 1);
        int w = x1 - x0 + 1, hgt = y1 - y0 + 1;
        float[,] h = td.GetHeights(x0, y0, w, hgt);

        var rim = new List<float>();
        for (int y = 0; y < hgt; y++)
            for (int x = 0; x < w; x++)
            {
                float dx = (x0 + x) - cxf, dy = (y0 + y) - cyf;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d < rC || d > rC + 20) continue;
                float u = (x0 + x) / (float)(res - 1), v = (y0 + y) / (float)(res - 1);
                if (IsProtected(u, v)) continue;
                rim.Add(h[y, x]);
            }
        if (rim.Count == 0) return "no unprotected rim found";
        rim.Sort();
        float ground = rim[rim.Count * 15 / 100];

        long changed = 0;
        for (int y = 0; y < hgt; y++)
            for (int x = 0; x < w; x++)
            {
                float dx = (x0 + x) - cxf, dy = (y0 + y) - cyf;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d > rC) continue;
                float u = (x0 + x) / (float)(res - 1), v = (y0 + y) / (float)(res - 1);
                if (IsProtected(u, v)) continue;
                float fall = 1f - Mathf.Clamp01((d - (rC - 8f)) / 8f);
                fall = fall * fall * (3f - 2f * fall);
                float target = Mathf.Min(h[y, x], ground);
                float nh = h[y, x] + fall * (target - h[y, x]);
                if (h[y, x] - nh > 0.001f) changed++;
                h[y, x] = nh;
            }
        td.SetHeights(x0, y0, h);
        EditorUtility.SetDirty(td);
        AssetDatabase.SaveAssets();
        return "circle(" + worldX.ToString("F0") + "," + worldZ.ToString("F0") + ",r" + radius.ToString("F0") +
               ") ground=" + (ground * size.y).ToString("F1") + "m cellsChanged=" + changed;
    }

    /// <summary>
    /// Scan the CURRENT heightmap for residual column clusters and report their world
    /// positions, sizes and zone membership — for aiming FlattenCircle at leftovers.
    /// </summary>
    public static string LocateColumns(float minAnomalyM)
    {
        var terrain = FindTerrain();
        var td = terrain.terrainData;
        float heightScale = td.size.y;
        var px = LoadOrthoPixels();
        int res = td.heightmapResolution;
        float[,] h = td.GetHeights(0, 0, res, res);

        var h1 = new float[GridRes * GridRes];
        for (int y = 0; y < GridRes; y++)
            for (int x = 0; x < GridRes; x++)
                h1[y * GridRes + x] = h[y * 4, x * 4];

        var h2 = new float[CoarseRes * CoarseRes];
        var noData2 = new float[CoarseRes * CoarseRes];
        for (int y = 0; y < CoarseRes; y++)
        {
            float v = y / (float)(CoarseRes - 1);
            for (int x = 0; x < CoarseRes; x++)
            {
                float u = x / (float)(CoarseRes - 1);
                h2[y * CoarseRes + x] = h1[(y * 4) * GridRes + (x * 4)];
                noData2[y * CoarseRes + x] = IsNoData(OrthoAt(px, u, v)) ? 1f : 0f;
            }
        }
        var opened = h2;
        for (int i = 0; i < 15; i++) opened = Erode3(opened, CoarseRes);
        for (int i = 0; i < 15; i++) opened = Dilate3(opened, CoarseRes);
        var noDataZone = noData2;
        for (int i = 0; i < 5; i++) noDataZone = Dilate3(noDataZone, CoarseRes);

        var mask = new bool[GridRes * GridRes];
        var anom = new float[GridRes * GridRes];
        for (int y = 0; y < GridRes; y++)
        {
            float v = y / (float)(GridRes - 1);
            for (int x = 0; x < GridRes; x++)
            {
                float u = x / (float)(GridRes - 1);
                int i = y * GridRes + x;
                float a = (h1[i] - SampleBilinear(opened, CoarseRes, u, v)) * heightScale;
                anom[i] = a;
                mask[i] = a > minAnomalyM;
            }
        }

        var visited = new bool[GridRes * GridRes];
        var clusters = new List<string>();
        var stack = new Stack<int>();
        var results = new List<(int count, string line)>();
        for (int i = 0; i < mask.Length; i++)
        {
            if (!mask[i] || visited[i]) continue;
            stack.Clear();
            stack.Push(i); visited[i] = true;
            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
            int count = 0; float peak = 0f; long sx = 0, sy = 0;
            while (stack.Count > 0)
            {
                int c = stack.Pop(); count++;
                int cx = c % GridRes, cy = c / GridRes;
                sx += cx; sy += cy;
                if (cx < minX) minX = cx; if (cx > maxX) maxX = cx;
                if (cy < minY) minY = cy; if (cy > maxY) maxY = cy;
                if (anom[c] > peak) peak = anom[c];
                for (int k = 0; k < 4; k++)
                {
                    int nx = cx + (k == 0 ? 1 : k == 1 ? -1 : 0);
                    int ny = cy + (k == 2 ? 1 : k == 3 ? -1 : 0);
                    if (nx < 0 || ny < 0 || nx >= GridRes || ny >= GridRes) continue;
                    int ni = ny * GridRes + nx;
                    if (visited[ni] || !mask[ni]) continue;
                    visited[ni] = true;
                    stack.Push(ni);
                }
            }
            if (count < 6) continue; // ignore specks
            float cu = (sx / (float)count) / (GridRes - 1);
            float cv = (sy / (float)count) / (GridRes - 1);
            float wx = cu * td.size.x, wz = cv * td.size.z;
            float wMeters = (maxX - minX + 1) * td.size.x / (GridRes - 1);
            float hMeters = (maxY - minY + 1) * td.size.z / (GridRes - 1);
            float groundM = SampleBilinear(opened, CoarseRes, cu, cv) * heightScale;
            string line = "world=(" + wx.ToString("F0") + "," + wz.ToString("F0") + ")" +
                          " size=" + wMeters.ToString("F0") + "x" + hMeters.ToString("F0") + "m" +
                          " peak=" + peak.ToString("F1") + "m ground=" + groundM.ToString("F0") + "m" +
                          " cells=" + count +
                          (IsProtected(cu, cv) ? " ZONE" : "") +
                          (SampleBilinear(noDataZone, CoarseRes, cu, cv) > 0.05f ? " EDGE" : "");
            results.Add((count, line));
        }
        results.Sort((a, b) => b.count.CompareTo(a.count));
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("clusters=" + results.Count + " (minAnomaly=" + minAnomalyM.ToString("F1") + "m, minCells=6)");
        for (int i = 0; i < Math.Min(50, results.Count); i++) sb.AppendLine("#" + (i + 1) + " " + results[i].line);
        return sb.ToString();
    }

    /// <summary>
    /// Repeatedly find the tallest remaining peak above local ground within a search
    /// radius and flatten a small circle centered on it. Self-aims from the heightmap,
    /// so it is immune to screen-coordinate estimation error. The height gate keeps
    /// ruin walls (low) from ever being selected; only tall tree columns qualify.
    /// </summary>
    public static string FlattenPeaks(float cx, float cz, float searchR, float circleR,
                                      float minAboveM, int maxPeaks)
    {
        var terrain = FindTerrain();
        var td = terrain.terrainData;
        int res = td.heightmapResolution;
        Vector3 size = td.size;
        var sb = new System.Text.StringBuilder();
        int done = 0;
        for (int iter = 0; iter < maxPeaks; iter++)
        {
            float cxf = cx / size.x * (res - 1);
            float cyf = cz / size.z * (res - 1);
            float rC = searchR / size.x * (res - 1);
            // read a wider region than the search circle so the ground percentile is
            // taken from true surroundings, not the top of a wide flat grove
            float rR = rC + 25f / size.x * (res - 1);
            int x0 = Mathf.Clamp((int)(cxf - rR), 0, res - 1), x1 = Mathf.Clamp((int)(cxf + rR), 0, res - 1);
            int y0 = Mathf.Clamp((int)(cyf - rR), 0, res - 1), y1 = Mathf.Clamp((int)(cyf + rR), 0, res - 1);
            int w = x1 - x0 + 1, hgt = y1 - y0 + 1;
            float[,] h = td.GetHeights(x0, y0, w, hgt);

            var sample = new List<float>();
            for (int y = 0; y < hgt; y += 3)
                for (int x = 0; x < w; x += 3) sample.Add(h[y, x]);
            sample.Sort();
            float ground = sample[sample.Count / 10];

            float best = ground; int bx = -1, by = -1;
            for (int y = 0; y < hgt; y++)
                for (int x = 0; x < w; x++)
                {
                    float dx = (x0 + x) - cxf, dy = (y0 + y) - cyf;
                    if (dx * dx + dy * dy > rC * rC) continue;
                    if (h[y, x] > best) { best = h[y, x]; bx = x0 + x; by = y0 + y; }
                }
            if (bx < 0 || (best - ground) * size.y < minAboveM) break;

            float wx = bx / (float)(res - 1) * size.x;
            float wz = by / (float)(res - 1) * size.z;
            string r = FlattenCircleForce(wx, wz, circleR);
            sb.AppendLine("peak " + ((best - ground) * size.y).ToString("F1") + "m @ (" +
                          wx.ToString("F0") + "," + wz.ToString("F0") + ") -> " + r);
            done++;
        }
        sb.AppendLine("peaksFlattened=" + done);
        return sb.ToString();
    }

    /// <summary>
    /// Like FlattenCircle but IGNORES protection zones — only for visually confirmed
    /// vegetation inside a buffer, aimed via a calibrated top-down render.
    /// </summary>
    public static string FlattenCircleForce(float worldX, float worldZ, float radius)
    {
        var terrain = FindTerrain();
        var td = terrain.terrainData;
        int res = td.heightmapResolution;
        Vector3 size = td.size;
        float cxf = worldX / size.x * (res - 1);
        float cyf = worldZ / size.z * (res - 1);
        float rC = radius / size.x * (res - 1);
        int pad = (int)(rC + 30);
        int x0 = Mathf.Clamp((int)(cxf - pad), 0, res - 1), x1 = Mathf.Clamp((int)(cxf + pad), 0, res - 1);
        int y0 = Mathf.Clamp((int)(cyf - pad), 0, res - 1), y1 = Mathf.Clamp((int)(cyf + pad), 0, res - 1);
        int w = x1 - x0 + 1, hgt = y1 - y0 + 1;
        float[,] h = td.GetHeights(x0, y0, w, hgt);

        var rim = new List<float>();
        for (int y = 0; y < hgt; y++)
            for (int x = 0; x < w; x++)
            {
                float dx = (x0 + x) - cxf, dy = (y0 + y) - cyf;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d >= rC && d <= rC + 20) rim.Add(h[y, x]);
            }
        if (rim.Count == 0) return "no rim found";
        rim.Sort();
        float ground = rim[rim.Count * 15 / 100];

        long changed = 0;
        for (int y = 0; y < hgt; y++)
            for (int x = 0; x < w; x++)
            {
                float dx = (x0 + x) - cxf, dy = (y0 + y) - cyf;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d > rC) continue;
                float fall = 1f - Mathf.Clamp01((d - (rC - 8f)) / 8f);
                fall = fall * fall * (3f - 2f * fall);
                float target = Mathf.Min(h[y, x], ground);
                float nh = h[y, x] + fall * (target - h[y, x]);
                if (h[y, x] - nh > 0.001f) changed++;
                h[y, x] = nh;
            }
        td.SetHeights(x0, y0, h);
        EditorUtility.SetDirty(td);
        AssetDatabase.SaveAssets();
        return "FORCE circle(" + worldX.ToString("F0") + "," + worldZ.ToString("F0") + ",r" + radius.ToString("F0") +
               ") ground=" + (ground * size.y).ToString("F1") + "m cellsChanged=" + changed;
    }

    // ---------- preview rendering ----------

    public static string RenderPreview(string path, float camX, float camY, float camZ,
                                       float lookX, float lookY, float lookZ, float fov)
    {
        var go = new GameObject("GZ_PreviewCam");
        RenderTexture rt = null;
        try
        {
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.5f;
            cam.farClipPlane = 6000f;
            cam.clearFlags = CameraClearFlags.Skybox;
            go.transform.position = new Vector3(camX, camY, camZ);
            go.transform.LookAt(new Vector3(lookX, lookY, lookZ));

            int w = 1600, hgt = 900;
            rt = new RenderTexture(w, hgt, 24);
            cam.targetTexture = rt;
            string mode;
            var req = new UnityEngine.Rendering.RenderPipeline.StandardRequest();
            req.destination = rt;
            if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(cam, req))
            {
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, req);
                mode = "StandardRequest";
            }
            else
            {
                cam.Render();
                mode = "CameraRender";
            }

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, hgt, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, w, hgt), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;

            var p32 = tex.GetPixels32();
            long lum = 0;
            for (int i = 0; i < p32.Length; i += 997) lum += p32[i].r + p32[i].g + p32[i].b;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, ImageConversion.EncodeToPNG(tex));
            UnityEngine.Object.DestroyImmediate(tex);
            return "saved=" + path + " mode=" + mode + " lumSample=" + lum;
        }
        finally
        {
            if (rt != null) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    public static string TerrainStats()
    {
        var terrain = FindTerrain();
        var td = terrain.terrainData;
        int res = td.heightmapResolution;
        float[,] h = td.GetHeights(0, 0, res, res);
        float max = 0f; int mx = 0, my = 0;
        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
                if (h[y, x] > max) { max = h[y, x]; mx = x; my = y; }
        Vector3 world = terrain.transform.position + new Vector3(
            mx / (float)(res - 1) * td.size.x, max * td.size.y, my / (float)(res - 1) * td.size.z);
        return "size=" + td.size.ToString("F1") + " highestPoint=" + world.ToString("F1");
    }
}
