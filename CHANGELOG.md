# Changelog

How Mabwe evolved, day by day, from an empty Unity project to a working offline AI
heritage guide. Entries are grouped by date (this is a competition MVP, not a versioned
release train); each bullet corresponds to real commits in the history.

## 2026-07-14

### Added
- `docs/deployment.md` — offline/kiosk deployment plan, hardware reality, and operating
  costs.
- `docs/demo.md` — demo walkthrough script with a reproduce-it-yourself guide for every
  beat, and the demo video link.
- This changelog.

## 2026-07-13

### Added
- **AI heritage guide** (`feat(guide)`): abstract `GZNpcBackend` with two swappable
  implementations — `GZLocalLLMBackend` (grounded prompts over per-POI fact sheets,
  streaming answers from any OpenAI-compatible local server) and `GZScriptedNpcBackend`
  (fully offline, zero-AI fallback).
- **Explorable world** (`feat(world)`): Great Zimbabwe scene, aerial tour system
  (director / camera / POIs / UI), fact-synced FX layer, `GreatZimbabwe_Mobile` scene
  variant, and editor tooling.
- **Conversation memory**: the guide remembers the last exchanges, so "what else?" adds
  new facts instead of repeating; history turns double as output-format examples.
- **Visible source citations**: every completed answer renders a "Sources:" footer from
  the answer POI's fact-sheet attribution.
- **Full tour-stop steering coverage** and the decline-of-the-city story content.
- **Explicit "I don't know" refusal**, verified live end-to-end from the CLI
  (`Tools/e2e_llm_probe.py`; transcript in `docs/llm-e2e-verification.md`).
- **EditMode tests** for fact-sheet loading and FX narration-cue matching.
- Procedural skybox and a visual polish pass.
- README, MIT license, AI-usage justification (`docs/ai-usage.md`), architecture
  diagram, project settings, screenshots, and `.gitignore`.

### Fixed
- Refusal over-triggering on ordinary sightseeing questions ("what is on that rock?")
  — grounding is now two-tier: vague/pointing questions answer from the current camera
  location; the refusal template is reserved for out-of-corpus questions.
- Steering-JSON echo, glued-header answer loss, and multi-command answers — prompt rules
  plus a defensive streaming parser; the player never sees raw JSON.

### Changed
- All authored content standardized on AD/BC era notation (sources stay verbatim).

## 2026-07-12

### Added
- Processed terrain assets shipped into the project, plus the vegetation flattener
  (`feat(terrain)`).

## 2026-07-10

### Added
- **Curated knowledge base** (`Dataset/`): 43 fact chunks across 10 topics with
  per-source provenance frontmatter, license ledger (`Dataset/LICENSES.md`), eval Q&A
  set, and the automated provenance audit (`audit_provenance.py`).

## 2026-07-05

### Added
- Great Zimbabwe terrain reconstructed in Unity from open, MD5-verified drone survey
  data (Zenodo, CC BY 4.0), with the full data statement and method documentation.

### Fixed
- `URP.png` stored as a proper Git LFS pointer.

## 2026-07-04

### Added
- Initial Unity 6000.3.14f1 project check-in.
