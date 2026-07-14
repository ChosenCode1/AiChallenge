# Data Statement — Great Zimbabwe Terrain Data

Records the provenance, rights, and verification of every piece of geospatial data used
to build the Great Zimbabwe 3D scene. Companion document: [method-terrain-from-dem.md](method-terrain-from-dem.md)
(how the data was turned into a Unity terrain).

> **The other half of the data story** — the curated factual corpus that grounds the AI
> guide's answers — lives in [`Dataset/`](../Dataset/README.md): per-source provenance
> files, a license ledger (`Dataset/LICENSES.md`), and an automated integrity audit
> (`py Dataset/audit_provenance.py`).

Last updated: 2026-07-10 (terrain content unchanged since 2026-07-05; added the
knowledge-base cross-reference above).

## 1. Primary source

| Field | Value |
|---|---|
| Title | *Great Zimbabwe, DEM and orthophoto* |
| Repository | Zenodo, record [19093686](https://zenodo.org/records/19093686) (version 2, current) |
| DOI | [10.5281/zenodo.19093686](https://doi.org/10.5281/zenodo.19093686) |
| Creators | Daniel Löwenborg, Ezekia Mtetwa — Uppsala University, Sweden |
| License | **Creative Commons Attribution 4.0 International (CC BY 4.0)** |
| Method (per record) | Drone photogrammetry survey of the Great Zimbabwe site |
| Dates | Record publication-date field: 2016 (survey era); deposited on Zenodo 2025 (v1: record 15592736, 2025-06-04; v2 adds the oblique drone photo) |
| Downloaded / verified | 2026-07-05 |

## 2. File inventory and integrity verification

All five files of the record were downloaded to `greatZimData/` (kept outside this Unity
project; large raw data is not committed). On 2026-07-05 every file's MD5 was computed
locally (`Get-FileHash -Algorithm MD5`) and compared against the checksums published by
the Zenodo API (`https://zenodo.org/api/records/19093686`). **All five matched.**

| File | Size (bytes) | MD5 (local = Zenodo) |
|---|---|---|
| `GZ_valley_dem_wgs84.tif` | 127,283,571 | `e5e0df722c5e9c25cda758e4361b8d0a` |
| `GZ_valley_dem_wgs84.tfw` | 78 | `368f94da55a237d9d65fd893a210047b` |
| `GZ_Valley_Orthophoto_wgs84.jpg` | 183,634,673 | `7b4d2b2c69f8443feae7d586c641d712` |
| `GZ_Valley_Orthophoto_wgs84.jgw` | 77 | `ef85a4f69a969e53934324032f75f716` |
| `Great Zimbabwe - drone photo.JPG` | 5,018,109 | `3755c669ec57656c3d09728c27830e05` |

## 3. Dataset characteristics (measured locally)

**DEM — `GZ_valley_dem_wgs84.tif` (+ `.tfw` world file)**
- 9529 × 10192 pixels, 32-bit float elevation (metres ASL), LZW-compressed, 256-px tiles
- Nodata sentinel: `-32767` (photogrammetry gaps)
- CRS: WGS84 geographic (longitude/latitude degrees), per filename and world file
- World file (`.tfw`): pixel size `1.84527e-006°` lon × `-1.74126e-006°` lat;
  top-left pixel at `30.92641913541372°E, -20.26088215125127°`
- Ground sample distance ≈ **19.3 cm/px** at the site latitude
- Elevation range within the area we use: **983.64 – 1180.61 m ASL**

**Orthophoto — `GZ_Valley_Orthophoto_wgs84.jpg` (+ `.jgw` world file)**
- 23514 × 24970 pixels, RGB JPEG
- World file (`.jgw`): pixel size `4.61301e-007°` lon × `-4.35304e-007°` lat;
  top-left pixel at `30.9294293069206°E, -20.26427757454482°`
- Ground sample distance ≈ **4.8 cm/px**
- The orthophoto footprint lies entirely inside the DEM footprint

**Oblique photo — `Great Zimbabwe - drone photo.JPG`**: reference imagery only; not used
as model input.

## 4. Coverage and known gaps

- Coverage is the **Valley Complex and Great Enclosure area** (~1.13 × 1.20 km). The Hill
  Complex is captured only along its southern face at the northern edge of the footprint.
- Within the footprint we use, **13.13 % of DEM pixels are nodata** (survey-edge gaps and
  failed reconstruction patches). How these are filled — and disclosed as interpolated,
  non-survey elevations — is documented in the method document, §"Hole filling".
- The orthophoto has black (no-imagery) corners where survey coverage was irregular.
- The DEM is a **digital surface model (DSM)**: vegetation and standing walls are part of
  the elevation surface, not separated from the ground.

## 5. Rights and required attribution

CC BY 4.0 permits copying, redistribution, and adaptation (including commercial use)
provided attribution is given. The following credit line appears in the repository README
(§Attribution), which is the project's attribution record:

> Terrain and aerial imagery: "Great Zimbabwe, DEM and orthophoto" by Daniel Löwenborg
> and Ezekia Mtetwa (Uppsala University), Zenodo,
> [doi:10.5281/zenodo.19093686](https://doi.org/10.5281/zenodo.19093686), licensed
> [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). The data was cropped,
> hole-filled, resampled, and converted to a game-engine terrain; modifications are
> documented in the project repository.

(CC BY 4.0 requires indicating that modifications were made — the sentence above plus the
method document satisfies this.)

## 6. Related sources considered but not used as data

- **Sketchfab models** by the same authors ([sketchfab.com/lowenborg](https://sketchfab.com/lowenborg)):
  "Great Zimbabwe Valley", "Great Zimbabwe: Hill Complex", "Great Zimbabwe: The Great
  Enclosure". Marked **not downloadable** — used as visual reference only; no geometry or
  textures extracted.
- **Zamani Project** (University of Cape Town) laser-scan documentation of Great
  Zimbabwe: data requested by email on 2026-07-05; pending. If granted, its use will be
  recorded here with its own terms.

No other elevation, imagery, or model data has been used in the scene as of this
statement's date.
