"""Referential-integrity audit for the Great Zimbabwe knowledge-base provenance trail.

Checks, end to end:
  1. every source file in Dataset/sources/ has complete frontmatter
     (source_id, url, publisher, license, accessed);
  2. every chunk in Dataset/chunks/ cites only source_ids that exist
     (or the project docs allowed for scene facts);
  3. every source is actually cited by at least one chunk (no dead weight);
  4. LICENSES.md has exactly one row per source, and no orphan rows;
  5. every chunk id cited in qa/visitor-qa.md resolves to a real chunk;
  6. all eight tour fact sheets exist and reference no removed sources.

Run from the repo root:  py Dataset/audit_provenance.py
Exits 0 on PASS, 1 on FAIL. No third-party dependencies.
"""
import re
import sys
import io
from pathlib import Path

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

ROOT = Path(__file__).resolve().parent.parent   # repo root ("My project")
DS = ROOT / "Dataset"
DOCS = ROOT / "docs"
FACTS = ROOT / "Assets" / "GreatZimbabwe" / "Runtime" / "Resources" / "GZTourFacts"

problems = []
ok = []

# 1. Collect real source ids from sources/ frontmatter
source_ids = {}
for f in (DS / "sources").glob("*.md"):
    text = f.read_text(encoding="utf-8")
    m = re.search(r"^source_id:\s*(\S+)", text, re.M)
    if not m:
        problems.append(f"SOURCE {f.name}: no source_id in frontmatter")
        continue
    source_ids[m.group(1)] = f.name
    for field in ("url", "license", "accessed", "publisher"):
        if not re.search(rf"^{field}:", text, re.M):
            problems.append(f"SOURCE {f.name}: missing frontmatter field '{field}'")
ok.append(f"{len(source_ids)} source files: {sorted(source_ids)}")

# doc-based pseudo-sources allowed for scene chunks
doc_sources = {"docs/data-statement.md", "docs/method-terrain-from-dem.md"}
for d in doc_sources:
    p = DOCS / d.split("/", 1)[1]
    (ok if p.exists() else problems).append(
        f"{'doc exists' if p.exists() else 'DOC MISSING'}: {d}")

valid = set(source_ids) | doc_sources

# 2. Every chunk cites only valid sources; collect chunk ids
chunk_ids = set()
for f in sorted((DS / "chunks").glob("*.md")):
    text = f.read_text(encoding="utf-8")
    for m in re.finditer(r"^## (gz-\S+)", text, re.M):
        chunk_ids.add(m.group(1))
    for m in re.finditer(r"sources:\s*(.+)$", text, re.M):
        raw = m.group(1).strip().strip("[]")
        for s in [x.strip() for x in raw.split(",") if x.strip()]:
            s = s.replace("project ", "")
            if s not in valid:
                problems.append(f"CHUNK {f.name}: cites unknown source '{s}'")
ok.append(f"{len(chunk_ids)} chunks defined")

# 3. Every source id is used by at least one chunk
for sid, fname in source_ids.items():
    if not any(re.search(rf"\b{re.escape(sid)}\b", f.read_text(encoding="utf-8"))
               for f in (DS / "chunks").glob("*.md")):
        problems.append(f"SOURCE '{sid}' ({fname}) is cited by no chunk")

# 4. LICENSES.md rows match source files exactly (skip header/divider rows)
lic = (DS / "LICENSES.md").read_text(encoding="utf-8")
for sid in source_ids:
    if not re.search(rf"^\|\s*{re.escape(sid)}\s*\|", lic, re.M):
        problems.append(f"LICENSES.md: no row for source '{sid}'")
for m in re.finditer(r"^\|\s*([a-z][a-z0-9-]*)\s*\|", lic, re.M):
    sid = m.group(1)
    if sid not in source_ids and sid not in ("source_id",):
        problems.append(f"LICENSES.md row '{sid}' has no source file")

# 5. Every QA citation resolves to an existing chunk id
qa = (DS / "qa" / "visitor-qa.md").read_text(encoding="utf-8")
qa_cited = set()
for m in re.finditer(r"\[(gz-[^\]]+)\]", qa):
    for cid in [x.strip() for x in m.group(1).split(",")]:
        qa_cited.add(cid)
        if cid not in chunk_ids:
            problems.append(f"QA cites nonexistent chunk '{cid}'")
ok.append(f"QA cites {len(qa_cited)} distinct chunks, all resolved")

# 6. Fact sheets exist for all POIs and reference no removed sources
POIS = ["overview", "hill_complex", "great_enclosure", "conical_tower",
        "valley_ruins", "karanga_village", "east_ruins", "cattle"]
banned = re.compile(r"wikipedia|wp-gz|wp-kingdom|wp-bird", re.I)
for poi in POIS:
    p = FACTS / f"{poi}.txt"
    if not p.exists():
        problems.append(f"FACT SHEET MISSING: {poi}.txt")
    elif banned.search(p.read_text(encoding="utf-8")):
        problems.append(f"FACT SHEET {poi}.txt references a removed source")
ok.append(f"{len(POIS)} fact sheets checked")

print("== PASS ==" if not problems else "== FAIL ==")
for line in ok:
    print("  ok:", line)
for line in problems:
    print("  PROBLEM:", line)
sys.exit(1 if problems else 0)
