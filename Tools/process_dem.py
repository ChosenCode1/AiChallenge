"""Convert GZ valley DEM + orthophoto into Unity terrain inputs.

Outputs to greatZimData/processed/:
  GZ_heightmap_4097.r16  - 16-bit LE RAW, 4097x4097, row 0 = SOUTH edge
  GZ_Ortho_8192.jpg      - orthophoto downsampled to 8192x8192
  GZ_terrain_meta.json   - world size / elevation range for the Unity importer

No numpy (blocked on this machine) - Pillow C ops + array module only.
"""
import json
import math
import os
import time
from array import array

from PIL import Image, ImageMath

Image.MAX_IMAGE_PIXELS = None
DATA = r"C:\Users\User\Desktop\AiChallenge\greatZimData"
OUT = os.path.join(DATA, "processed")
os.makedirs(OUT, exist_ok=True)

HEIGHT_RES = 4097          # Unity heightmap resolution (2^n + 1)
ORTHO_RES = 8192
NODATA_THRESH = -1000.0    # anything below this is a photogrammetry hole

t0 = time.time()


def read_worldfile(path):
    with open(path) as f:
        v = [float(line.strip()) for line in f if line.strip()]
    return {"px_x": v[0], "px_y": v[3], "lon0": v[4], "lat0": v[5]}


dem_w = read_worldfile(os.path.join(DATA, "GZ_valley_dem_wgs84.tfw"))
ort_w = read_worldfile(os.path.join(DATA, "GZ_Valley_Orthophoto_wgs84.jgw"))

ortho = Image.open(os.path.join(DATA, "GZ_Valley_Orthophoto_wgs84.jpg"))
OW, OH = ortho.size

# ---- georeferencing: crop DEM to the ortho footprint ----
o_lon0, o_lat0 = ort_w["lon0"], ort_w["lat0"]
o_lon1 = o_lon0 + OW * ort_w["px_x"]
o_lat1 = o_lat0 + OH * ort_w["px_y"]

box = (
    int((o_lon0 - dem_w["lon0"]) / dem_w["px_x"]),
    int((o_lat0 - dem_w["lat0"]) / dem_w["px_y"]),
    int(math.ceil((o_lon1 - dem_w["lon0"]) / dem_w["px_x"])),
    int(math.ceil((o_lat1 - dem_w["lat0"]) / dem_w["px_y"])),
)

lat_mid = math.radians((o_lat0 + o_lat1) / 2)
m_per_deg_lat = 111132.92 - 559.82 * math.cos(2 * lat_mid) + 1.175 * math.cos(4 * lat_mid)
m_per_deg_lon = 111412.84 * math.cos(lat_mid) - 93.5 * math.cos(3 * lat_mid)
size_x = (o_lon1 - o_lon0) * m_per_deg_lon   # E-W metres
size_z = (o_lat0 - o_lat1) * m_per_deg_lat   # N-S metres

dem = Image.open(os.path.join(DATA, "GZ_valley_dem_wgs84.tif"))
crop = dem.crop(box)
W, H = crop.size
print(f"[{time.time()-t0:5.1f}s] cropped DEM {W}x{H}, footprint {size_x:.1f} x {size_z:.1f} m")

# ---- scan: valid min/max, nodata mask, zero out holes ----
buf = array("f", crop.tobytes())
mask = bytearray(W * H)
vmin, vmax, holes = 1e30, -1e30, 0
for i, v in enumerate(buf):
    if v <= NODATA_THRESH:
        buf[i] = 0.0
        holes += 1
    else:
        mask[i] = 255
        if v < vmin:
            vmin = v
        if v > vmax:
            vmax = v
print(f"[{time.time()-t0:5.1f}s] elev {vmin:.2f}..{vmax:.2f} m, holes: {holes} px ({100*holes/(W*H):.2f}%)")

vals = Image.frombuffer("F", (W, H), buf.tobytes(), "raw", "F", 0, 1)
mask_img = Image.frombuffer("L", (W, H), bytes(mask), "raw", "L", 0, 1)

# ---- fill holes: block-average inpaint (values/weights pyramid, all C ops) ----
if holes:
    sw, sh = max(1, W // 64), max(1, H // 64)
    small_v = vals.resize((sw, sh), Image.BOX)
    small_w = mask_img.convert("F").resize((sw, sh), Image.BOX)  # 0..255
    lo, hi = vmin, vmax
    fill_small = ImageMath.lambda_eval(
        lambda d: d["min"](d["max"](d["v"] * 255.0 / d["max"](d["w"], 0.5), lo), hi),
        v=small_v, w=small_w,
    )
    fill_up = fill_small.resize((W, H), Image.BILINEAR)
    vals = Image.composite(vals, fill_up, mask_img)
    print(f"[{time.time()-t0:5.1f}s] holes filled")

# ---- resample to Unity heightmap, flip so row 0 = south ----
hm = vals.resize((HEIGHT_RES, HEIGHT_RES), Image.BOX)
hm = hm.transpose(Image.FLIP_TOP_BOTTOM)
scale = 65535.0 / (vmax - vmin)
out16 = array("H", bytes(2 * HEIGHT_RES * HEIGHT_RES))
for i, v in enumerate(array("f", hm.tobytes())):
    n = int((v - vmin) * scale + 0.5)
    out16[i] = 0 if n < 0 else (65535 if n > 65535 else n)
raw_path = os.path.join(OUT, "GZ_heightmap_4097.r16")
with open(raw_path, "wb") as f:
    f.write(out16.tobytes())  # little-endian on x86
print(f"[{time.time()-t0:5.1f}s] wrote {raw_path} ({os.path.getsize(raw_path)/1e6:.1f} MB)")

# ---- orthophoto -> 8192 terrain texture (draft decodes JPEG at 1/2 scale) ----
ortho.draft("RGB", (ORTHO_RES, ORTHO_RES))
tex = ortho.convert("RGB").resize((ORTHO_RES, ORTHO_RES), Image.LANCZOS)
tex_path = os.path.join(OUT, "GZ_Ortho_8192.jpg")
tex.save(tex_path, quality=92)
print(f"[{time.time()-t0:5.1f}s] wrote {tex_path} ({os.path.getsize(tex_path)/1e6:.1f} MB)")

# ---- metadata for the Unity importer ----
meta = {
    "sizeX": round(size_x, 2),
    "sizeZ": round(size_z, 2),
    "minElev": round(vmin, 2),
    "maxElev": round(vmax, 2),
    "heightRes": HEIGHT_RES,
    "rawFile": "GZ_heightmap_4097.r16",
    "orthoFile": "GZ_Ortho_8192.jpg",
    "holesFilled": holes,
    "source": "Zenodo record 19093686 (CC-BY 4.0) - Lowenborg & Mtetwa, Uppsala University",
}
with open(os.path.join(OUT, "GZ_terrain_meta.json"), "w") as f:
    json.dump(meta, f, indent=2)
print(f"[{time.time()-t0:5.1f}s] done. meta: {meta}")
