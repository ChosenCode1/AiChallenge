# Method — Building the Great Zimbabwe Unity Terrain from Survey Data

How the photogrammetry DEM and orthophoto documented in
[data-statement.md](data-statement.md) were converted into the explorable Unity scene
`Assets/Scenes/GreatZimbabwe.unity`. Every step is scripted and reproducible; no terrain
geometry was authored by hand.

Executed and verified: 2026-07-05.

**Result:** a Unity terrain of **1133.16 m (E-W) × 1203.34 m (N-S)**, elevation range
**983.64–1180.61 m ASL** (196.97 m of relief; terrain local Y=0 corresponds to
983.64 m ASL), draped with the survey orthophoto.

## Toolchain

| Tool | Version | Role |
|---|---|---|
| Python (`py` launcher) | 3.14.4 | preprocessing |
| Pillow | 12.2.0 | all raster operations (numpy is unavailable on the build machine) |
| Unity | 6000.3.14f1 (URP) | terrain construction, scene, renders |

## Stage 1 — Preprocessing (`Tools/process_dem.py`)

Script: [`Tools/process_dem.py`](../Tools/process_dem.py) (a working copy also sits next
to the raw data at `greatZimData/process_dem.py`). Run with `py process_dem.py`.
Total runtime ≈ 12 s.

1. **Crop the DEM to the orthophoto footprint.** Both world files (`.tfw`/`.jgw`) are
   parsed; the ortho's corner coordinates are converted to DEM pixel space. Crop box
   `(1631, 1949) – (7510, 8193)` → 5879 × 6244 px. This makes terrain and texture cover
   the identical ground area, so the texture drapes 1:1.
2. **Metric footprint.** Degrees are converted to metres with the standard series
   expansion for metres-per-degree at the site's mean latitude (-20.2696°):
   111,132.92 − 559.82·cos 2φ + 1.175·cos 4φ (lat) and 111,412.84·cos φ − 93.5·cos 3φ
   (lon), giving 1133.16 × 1203.34 m. A planar approximation is appropriate at this
   extent (< 0.01 % distortion over 1.2 km); no UTM reprojection was performed.
3. **Hole filling (disclosed data modification).** 4,820,428 px = **13.13 %** of the crop
   are nodata (sentinel −32767). Filling: nodata set to 0 with a validity mask; both are
   box-downsampled 64×, giving per-block mean of valid elevations; the ratio image is
   clamped to the valid range [983.64, 1180.61] m, bilinearly upsampled, and composited
   into nodata areas only. Valid survey pixels are untouched. **Filled areas are
   interpolated, not surveyed** — they sit almost entirely along the survey edges (see
   `screenshots/diagnostic_hm_vs_ortho.png`, where fill regions are visible at the
   borders).
4. **Resample to the Unity heightmap grid.** Box-filter resize to 4097 × 4097 (Unity
   heightmap resolutions must be 2ⁿ+1) ≈ 28 cm/sample against the DEM's native 19 cm.
   Flipped vertically so RAW row 0 is the **southern** edge, matching Unity's
   `SetHeights` row order (row = +Z = north).
5. **Quantize to 16-bit.** Heights normalized to [0, 65535] over the elevation range —
   a vertical step of 3.0 mm (no visible terracing). Written little-endian:
   `processed/GZ_heightmap_4097.r16`.
6. **Terrain texture.** Orthophoto decoded at half scale (JPEG draft mode), Lanczos-resampled
   to 8192 × 8192 (from 23514 × 24970 — the slight aspect change is undone when the
   texture is stretched over the non-square terrain), saved as JPEG quality 92:
   `processed/GZ_Ortho_8192.jpg`. Effective ground resolution ≈ 14 cm/px.
7. **Metadata handoff.** `processed/GZ_terrain_meta.json` carries sizes, elevation range,
   and file names to the Unity importer, so no numbers are duplicated in code.

**Stage 1 outputs (MD5, 2026-07-05):**

| File | Size (bytes) | MD5 |
|---|---|---|
| `GZ_heightmap_4097.r16` | 33,570,818 | `eb02fca22c404a41f8b7d666747757cc` |
| `GZ_Ortho_8192.jpg` | 28,273,195 | `6f905c9b1898eb49b2b034f01e1d7590` |
| `GZ_terrain_meta.json` | 307 | `a4c1e1cdbf22f513c2da8cf8b47c2175` |
| `diagnostic_hm_vs_ortho.png` (QA image) | 2,491,184 | `12c68ebc6463bc1604df7a1baf19e754` |

## Stage 2 — Unity terrain construction (`Assets/Editor/GZTerrainBuilder.cs`)

Editor script [`GZTerrainBuilder.cs`](../Assets/Editor/GZTerrainBuilder.cs). Run from the
menu **Nhaka → Build Great Zimbabwe Terrain**, or headless:

```
Unity.exe -batchmode -quit -projectPath "<repo>/My project" ^
  -executeMethod GZTerrainBuilder.BuildFromCli -logFile build.log
```

Steps performed:
1. Reads `GZ_terrain_meta.json` and the RAW heightmap from `../greatZimData/processed/`.
2. Creates a `TerrainData`: heightmap resolution 4097, size
   `(1133.16, 196.97, 1203.34)` m, heights set from the RAW (row 0 = south).
   Saved to `Assets/GreatZimbabwe/GZ_TerrainData.asset`.
3. Imports the orthophoto (`maxTextureSize` 8192, clamp wrap, mipmaps) and wraps it in a
   `TerrainLayer` whose `tileSize` equals the terrain size — the ortho drapes exactly
   once, geo-aligned with the heightmap by construction (identical footprint).
4. Builds the scene `Assets/Scenes/GreatZimbabwe.unity` (terrain + default light rotated
   to a NW sun) and renders three verification screenshots to the project root.

`Assets/GreatZimbabwe/` and the scene are **generated artifacts** — reproducible from the
raw data via the two scripts, which is why the large binaries are not committed.

## Verification evidence (`docs/screenshots/`)

- `diagnostic_hm_vs_ortho.png` — preprocessed heightmap (north-up, grayscale) beside the
  downsampled orthophoto: landform features (Hill Complex ridge, valley slope, Great
  Enclosure oval) align between the two rasters.
- `Preview_GZ_overview.png` — full-terrain oblique from the south-west.
- `Preview_GZ_great_enclosure.png` — the Great Enclosure with its outer wall and interior
  structures clearly readable.
- `Preview_GZ_hill_view.png` — from the Hill Complex granite slope looking south over the
  valley.

Checked: feature alignment between heightmap and ortho, plausible elevation range against
the published site elevation (~1,100 m ASL), no spikes/pits from hole filling, wall and
path features in expected positions.

## Known limitations (honest disclosure)

- **DSM, not DTM:** the source elevation model includes vegetation and standing
  architecture. On a heightfield terrain, trees and the Great Enclosure walls appear as
  extruded columns with smeared side textures (the orthophoto has no side-view imagery).
  Acceptable at overview distances; poor at player scale. Planned remediation: ground
  filtering (DSM→DTM) plus placement of real scanned 3D assets, and/or Zamani Project
  scan data for the monument.
- **13.13 % of the used DEM area is interpolated** (hole filling, §Stage 1.3), almost all
  at the survey edges.
- Heightmap resampled 19 cm → 28 cm; texture 4.8 cm → ~14 cm and JPEG-recompressed.
- Planar metre conversion instead of a projected CRS (negligible at this extent).
- Real-world georeferencing (WGS84 anchor of the terrain origin) is preserved in the
  world files and `GZ_terrain_meta.json`, not in the Unity scene itself.
