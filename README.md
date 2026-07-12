# Mabwe — Offline AI Heritage Guide for Zimbabwe

*Mabwe* (Shona: "stones") is a sovereign, offline-capable AI heritage guide for Great Zimbabwe,
delivered through an explorable Unity experience. As you fly over a faithful recreation of the
monument built from open survey data, you can ask the guide open-ended questions in your own
words — and get accurate, **cited** answers from a local language model running entirely
on-device. No internet. No foreign cloud. No data leaves the machine.

**The AI guide is the product. The explorable world is how we deliver it.**

![Great Zimbabwe overview](docs/screenshots/Preview_GZ_overview.png)

## Problem

Tourists and students exploring Great Zimbabwe have open-ended questions — *"Why is the tower
solid?", "Who actually built this?", "What did they trade?"* — often on-site with poor or no
connectivity. Static signs and pre-written FAQs cannot anticipate those questions, and cloud AI
assistants need internet access and send Zimbabwean heritage queries to foreign servers.
Factual accuracy is also a cultural duty here: the origins of Great Zimbabwe were denied for
decades by colonial authorities, so a guide that invents answers doesn't just have a bug — it
spreads misinformation about national heritage.

## Solution

An explorable Unity recreation of Great Zimbabwe (terrain reconstructed from published survey
data) with an in-scene AI guide, **Nyika**, who answers natural-language questions using *only*
a curated, provenance-audited knowledge base — and cites her sources. Ask about the Conical
Tower and the camera flies you there while she answers. The whole experience runs offline on a
consumer laptop or a tourism/classroom kiosk.

## Why AI (and why not just a database?)

Visitors ask open-ended, unpredictable, multi-turn questions in their own words. You cannot
pre-write that FAQ: a lookup table or SQL query can only return answers someone already
authored, keyed on phrasings someone already predicted. Mabwe instead **synthesizes** answers
over retrieved, verified facts — a language model is used precisely for what rules and search
cannot do (understanding intent and composing an answer), and *not* for what it is bad at
(being the source of truth). The knowledge itself never comes from the model's memory:

- The guide answers **only** from curated fact sheets injected into every prompt.
- Every fact is traceable to a named, licensed institutional source (see [Data](#data)).
- Answers cite their sources; when the facts don't cover a question, the guide says
  **"I don't know"** rather than guessing.
- A fully scripted, zero-AI fallback mode exists — which is exactly the point: the scripted
  mode can only recite its script, while the LLM mode handles the questions nobody predicted.
  Running both side by side is our own baseline comparison.

## Demo

<!-- TODO after demo recording: add video link -->
Demo video: *coming with final submission.*

| Exploring | Asking the guide | Live LLM session |
|---|---|---|
| ![Tour over the Great Enclosure](docs/screenshots/Preview_GZ_tour_enclosure.png) | ![Guide answering at the tower](docs/screenshots/Preview_GZ_qa_tower.png) | ![Gemma-backed live answer](docs/screenshots/Preview_GZ_gemma_tour_test.png) |

More captures in [docs/screenshots/](docs/screenshots/).

## Architecture

One process, fully offline. The AI layer sits behind an abstract backend so it is
independently testable and swappable.

```
┌──────────────────────────────────────────────────────────────┐
│ UNITY (C#) — single offline process                          │
│                                                              │
│  Explorable Great Zimbabwe scene + aerial tour system        │
│  (GZTourDirector / GZTourCamera / GZTourPOI / GZTourUI)      │
│            │ player asks a question                          │
│            ▼                                                 │
│  GZNpcBackend (abstract)  ←— the swap point                  │
│   ├─ GZLocalLLMBackend                                       │
│   │    grounded system prompt from per-POI fact sheets       │
│   │    (whole curated corpus injected into every prompt)     │
│   │    ──HTTP──►  any OpenAI-compatible LOCAL server on      │
│   │    localhost (LM Studio / llama.cpp / Ollama) running    │
│   │    a small quantized open-weights model; streams back    │
│   └─ GZScriptedNpcBackend — fully offline scripted fallback  │
│            │                                                 │
│            ▼                                                 │
│  Dialogue UI renders answer + citations; Fact-FX layer fires │
│  keyword-cued scene moments in sync with the narration       │
└──────────────────────────────────────────────────────────────┘
```

The tour director tries the local LLM first and **silently falls back** to the scripted guide
on any failure — the demo never dies, and a kiosk with no model installed still works honestly
in scripted mode.

**Grounding method — retrieval-free RAG.** The entire curated corpus is small enough (~8 KB)
to fit into every prompt, so there is no embedding/vector-search stage to build, test, or
explain. The guardrails carry the anti-hallucination story: answer only from supplied facts,
cite them, and say so when they don't cover the question. A vector store becomes worthwhile
only as the corpus grows (see [Roadmap](#roadmap)).

## Data

Every fact the guide can utter is traceable. See **[docs/data-statement.md](docs/data-statement.md)**
for the full statement of sources, rights, and limitations.

- **Knowledge base** — [`Dataset/`](Dataset/): 43 curated fact chunks across 10 topic files,
  each fact cross-checked across sources where possible, with per-source provenance
  frontmatter, a [license ledger](Dataset/LICENSES.md) (UNESCO WHC, Smarthistory, LibreTexts,
  World History Encyclopedia — institutional publishers only), and an eval Q&A set.
- **Automated provenance audit** — `py Dataset/audit_provenance.py` verifies the entire trail:
  every chunk cites a real source file, every source has license/URL/access metadata and a
  ledger row, every Q&A answer and fact sheet resolves. It fails loudly on any break.
- **Terrain** — reconstructed from published, MD5-verified drone survey data (CC BY 4.0);
  method in [docs/method-terrain-from-dem.md](docs/method-terrain-from-dem.md).
- No personal data is collected anywhere in the product. No synthetic data is used in the
  knowledge base.

## AI Method

A small quantized open-weights chat model — **Gemma 4 E2B instruct (QAT q4_0 GGUF, ~3 GB)** —
served by any OpenAI-compatible local server (LM Studio, llama.cpp server, Ollama; the backend
has no vendor coupling). The model receives a grounded system prompt containing the curated
fact sheets and is instructed to answer only from them, cite sources, and admit when it
doesn't know. It also emits a single machine-readable steering line per answer (parsed out,
never shown) that flies the camera to the place being discussed. Guardrails: grounding,
citations, "I don't know" fallback, capped response length, and the fully scripted offline
mode. Design rationale and trade-offs: [docs/ai-usage.md](docs/ai-usage.md).

## Setup

Requires **Unity 6000.3.14f1**.

**Option A — zero setup (scripted guide):**
1. Open the project in Unity and open `Assets/Scenes/GreatZimbabwe.unity`.
2. Press Play. Ask questions in the question bar; the scripted guide answers offline.

**Option B — live AI guide (recommended):**
1. Install [LM Studio](https://lmstudio.ai/) (or any OpenAI-compatible local server) and
   download `gemma-4-e2b-it-qat`.
2. Start the local server (LM Studio default: `http://localhost:1234`). Nothing else is sent
   anywhere — the endpoint is localhost.
3. Press Play. The guide now answers live; if the server stops, it falls back to scripted
   mode automatically.

The server endpoint, model id, and generation settings are plain inspector fields on the
`GZLocalLLMBackend` component — no keys, no config files, no secrets.

A `GreatZimbabwe_Mobile` scene variant with pre-baked terrain targets lower-end devices.

## Tests

- **Unity EditMode tests** (`Assets/GreatZimbabwe/Tests/`): fact-sheet loading and the FX
  narration cue matcher. Run via *Window ▸ General ▸ Test Runner*, or headless:
  `Unity -batchmode -projectPath . -runTests -testPlatform EditMode`.
- **Provenance audit**: `py Dataset/audit_provenance.py` — the knowledge-base integrity suite.

Dev tooling note: `com.unity.pipeline` in the package manifest supports our automated
batch-mode build/test workflow during development; `Packages/packages-lock.json` pins all
dependency versions.

## Known Limitations

Honesty over polish — these are the current rough spots:

- **Small offline model.** Grounding and citations dramatically reduce hallucination, but a
  3 GB model can still occasionally misphrase a fact. The knowledge base, not the model, is
  the source of truth, and the guide is instructed to decline rather than guess.
- **English-first.** Shona/Ndebele answers are untested and not claimed (see Roadmap).
- **Laptop-class device required** for the live guide (the model needs ~4 GB of RAM);
  scripted mode runs anywhere the Unity app runs.
- **Two-part kiosk install** in live mode: the Unity app plus a local model server. Scripted
  mode is a single app.
- **Latency is model-bound.** Answers stream token-by-token; speed depends on the host
  machine. We report measured throughput rather than promising response times.
- Terrain fidelity is highest where published survey coverage is densest (Valley Complex);
  see the data statement for coverage details.

## Roadmap

- **Victoria Falls (Mosi-oa-Tunya)** — data pipeline already built; next site after
  submission.
- **Shona and Ndebele** guide evaluation.
- **Vector retrieval** when the corpus outgrows the inject-everything approach
  (multi-site knowledge base).
- Kiosk packaging for tourism offices and classrooms.

## Team

- **James Greeff** — team lead / lead developer (Unity, AI integration)
- **Lovemore Ncube** — product direction & design

## AI-Assisted Development Disclosure

Parts of this codebase were built with AI coding assistance. The team understands, maintains,
and can explain all code in this repository.

## License

Code is released under the [MIT License](LICENSE). The knowledge base carries per-source
licenses recorded in [Dataset/LICENSES.md](Dataset/LICENSES.md); terrain source data is
CC BY 4.0 as documented in [docs/data-statement.md](docs/data-statement.md).
