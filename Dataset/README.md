# Great Zimbabwe — Educational Dataset

Curated factual corpus for the in-scene guide LLM (the aerial tour NPC). Scope: the **history and operation of the city — the feeling of being there**. Historiography/attribution debates and colonial-era sources are deliberately excluded.

## Layout

```
Dataset/
  sources/    Raw fetched source texts, one file per source, frontmatter records
              URL, publisher, license, access date. These are the evidence base —
              never edit their content, only their metadata.
  chunks/     The curated corpus. Facts cross-checked across >= 2 sources where
              possible. This is what feeds the guide (RAG or fact sheets).
  qa/         Anticipated visitor questions answered from the chunks, for eval
              and few-shot prompting.
  LICENSES.md License ledger for every source.
  audit_provenance.py  Automated integrity audit of the whole trail (see below).
```

## Verifying the provenance trail

Run `py Dataset/audit_provenance.py` from the repo root (no dependencies). It fails
loudly if: a chunk cites a source that has no file, a source file lacks license/URL/access
frontmatter, a source has no LICENSES.md row (or vice versa), a Q&A answer cites a
deleted chunk, or a fact sheet is missing or references a removed source. Run it after
any change to `chunks/`, `sources/`, `qa/`, or the fact sheets. Last clean run: 2026-07-10.

## Chunk format

Each `chunks/*.md` file covers one topic. Every chunk is a `##` section:

```
## gz-<topic>-<nn> — Title
> location: <scene tag> · confidence: established|uncertain · sources: <source_ids>

One self-contained factual passage, guide-voiced, 50–150 words.
```

- `location`: `site_wide`, `hill_complex`, `great_enclosure`, `valley_ruins`, or `scene` (facts about this Unity recreation itself). Maps onto the tour POIs.
- `confidence: uncertain` marks genuinely open questions (Great Enclosure's purpose, population figures, causes of decline). The guide should present these as open, briefly — never as debates.
- `sources`: `source_id` values from the frontmatter of files in `sources/`.

## Feeding the tour

The tour NPC reads fact sheets from
`Assets/GreatZimbabwe/Runtime/Resources/GZTourFacts/<poiId>.txt`
(plain lines, `#` comments stripped). Those files are **generated from these chunks** — all eight POIs (overview, hill_complex, great_enclosure, conical_tower, valley_ruins, karanga_village, east_ruins, cattle) were populated from this corpus on 2026-07-09 and re-synced on 2026-07-10 after the Wikipedia removal. When chunks change, update the fact sheets to match; keep each sheet to ~5–8 spoken-style lines (the scripted fallback streams at ~32 chars/s).

## Known factual discrepancies (kept honest, not hidden)

| Fact | Values across sources | Dataset stance |
|---|---|---|
| Peak population | >10k (UNESCO) / ~18k (WHE, LibreTexts) / up to 20k (Smarthistory) | "over ten thousand", note higher estimates |
| Great Enclosure wall height | 9.7 m (WHE) / 32 ft ≈ 9.8 m (Smarthistory) | "nearly 10 m in places" |
| Conical Tower size | 10 m tall × 5 m base (WHE; only source giving dimensions) | "about 10 m tall, 5 m at base" |
| Site area | ~800 ha property (UNESCO) / 1,700 acres ≈ 690 ha (WHE) / 730 ha (LibreTexts) | "roughly 700–800 ha, over 7 km²" |
| Valley Ruins dating | 14th–16th c. (LibreTexts) vs "19th c." (UNESCO brief synthesis, referring to late reoccupation) | 14th–16th c. |
| End of habitation | abandoned ~1450 (UNESCO, LibreTexts) / inhabited early 1500s (de Barros, via LibreTexts) / inhabited to ~1550 (WHE) | capital declined ~1450–1500; some habitation into the early 1500s |

## Rules for extending this dataset

1. Every fact must trace to a file in `sources/` (or the project's own `docs/data-statement.md` for scene facts).
2. New sources get their own file in `sources/` with full frontmatter, and a row in `LICENSES.md`.
3. No historiography/denialism content; no colonial-era excavation reports (user decision, 2026-07-09).
4. The Shona origin of the city is stated as plain fact.
5. Scholarly uncertainty (tower purpose, decline causes) gets one light "still open" mention, not a debate narrative.
6. Institutional and specialist publishers only — no Wikipedia (user decision, 2026-07-10; it was used as early scaffolding and fully removed, with Wikipedia-only facts deleted rather than kept).
