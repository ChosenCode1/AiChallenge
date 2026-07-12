# AI Usage — Why AI, How It's Grounded, and How We Keep It Honest

This document justifies the AI in Mabwe: what role it plays, why a simpler non-AI approach
cannot do the job, what model we chose and the trade-offs behind it, and the guardrails that
keep a generative model safe to put in front of national heritage.

## 1. The role AI plays

Mabwe's guide answers **open-ended, natural-language questions** about Great Zimbabwe, asked
by visitors in their own words, and composes each answer from a curated, cited knowledge base.
The AI's job is *language* — understanding what a visitor meant and composing a coherent,
cited answer from verified facts. The AI's job is explicitly **not** *knowledge*: the model's
own memory of Great Zimbabwe is never trusted, and every fact it may state comes from the
corpus injected into its prompt.

## 2. Why not a database, rules, or search? (the baseline argument)

The rubric asks teams to prove a baseline algorithm is insufficient. Ours is easy to state:

> Visitors ask unpredictable, open-ended, multi-turn questions in their own phrasing.
> **You cannot pre-write that FAQ.** A database or rule engine can only return answers
> someone already authored, keyed on phrasings someone already predicted.

Concretely, a lookup baseline fails in three ways that matter on-site:

1. **Unbounded question space.** "Why is the tower solid?", "Was this built by outsiders?",
   "What would a child my age have done here?" — the set of reasonable visitor questions is
   open-ended. Keyword search over an FAQ retrieves *documents*; it cannot *answer*.
2. **Phrasing variance.** The same question arrives as "who built this", "which people made
   these walls", or a follow-up like "and when?". Rules and full-text search are brittle to
   exactly this; language models are built for it.
3. **Composition.** Real answers often combine facts from several topics (trade + cattle +
   chronology). A retrieval-only system returns fragments; the model synthesizes them into
   one coherent, cited answer.

**We ship the baseline ourselves.** `GZScriptedNpcBackend` is a fully scripted, zero-AI guide
that answers from pre-written responses with keyword matching. It exists as an honest offline
fallback — and it demonstrates the gap: the scripted guide can only recite what we predicted;
the LLM guide handles the questions nobody predicted. Running both behind one abstract
interface (`GZNpcBackend`) is a live A/B comparison built into the product.

What we did **not** do is force AI where rules are better. Camera steering, tour sequencing,
FX cueing, and citation rendering are all plain deterministic code. The model is used for the
one task rules cannot do.

## 3. Grounding design: retrieval-free RAG

Every request to the model carries a system prompt built from **per-POI fact sheets**
(`Assets/GreatZimbabwe/Runtime/Resources/GZTourFacts/`), which are distilled from the curated
knowledge base in [`Dataset/`](../Dataset/) — 43 fact chunks traceable to named, licensed
institutional sources (see [data-statement.md](data-statement.md) and
[Dataset/LICENSES.md](../Dataset/LICENSES.md)).

The whole curated corpus is ~8 KB, so it fits into every prompt. That makes our RAG
**retrieval-free**: there is no embedding model, vector database, or similarity-search stage
to build, tune, test, or explain — at this corpus size, retrieval would add moving parts, not
accuracy. The grounding contract in the prompt is:

- Answer **only** from the supplied facts.
- **Cite** the source of what you state.
- If the facts don't cover the question, **say "I don't know"** — never guess.

This is deliberate anti-hallucination design, and for this subject it is also a cultural
duty: the origins of Great Zimbabwe were denied for decades by colonial authorities, so an
invented answer is not a cosmetic bug — it is misinformation about national heritage. The
knowledge base therefore states the Shona origin of the city as plain fact, excludes
colonial-era historiography debates, and uses institutional publishers only.

**When this design changes:** grounding-by-injection scales until the corpus outgrows the
context budget. Adding Victoria Falls and further sites is the trigger to introduce a
retrieval stage (vector store over the same provenance-audited chunks). The chunk format was
designed for that migration from day one.

## 4. Model choice and trade-offs

**Model:** Gemma 4 E2B instruct, QAT-quantized q4_0 GGUF (~3 GB).
**Serving:** any OpenAI-compatible local server on localhost — LM Studio in development;
llama.cpp server or Ollama drop in unchanged. The backend has no vendor coupling.

| Decision | What we traded | Why it's right for this product |
|---|---|---|
| Small model (E2B) over a larger one | Some fluency and reasoning depth | Runs on a consumer laptop/kiosk — the deployment reality. Grounding does the knowledge work, so model size buys less here than it would for a raw chatbot. |
| Quantized (q4_0 QAT) | Marginal quality loss (QAT minimizes it) | 3 GB on disk, laptop-class RAM; quantization-aware training is exactly the "optimized for the use case" fit. |
| Open weights | No frontier-model quality | Sovereignty and licensing: the whole stack is inspectable, license-clean, and runs with zero foreign cloud dependency. |
| Local inference over cloud API | Latency depends on host hardware; no SLA | Offline-first is the product requirement (heritage sites and classrooms with no connectivity), and no visitor query ever leaves the device. |
| Streaming with capped length (420 tokens) | Long-form answers | Guide-style answers, responsive feel, bounded worst-case wait. |
| `reasoning_effort: "none"` | The model's hybrid "thinking" mode | Thinking tokens burned the whole budget before the answer began; disabling it makes answers start immediately. Servers that don't recognize the field ignore it — the setting degrades gracefully. |

One implementation detail worth explaining: each answer opens with a single machine-readable
JSON steering line (`{"poi":..., "view":..., "orbit":...}`) that the backend parses out —
never shown to the player — to fly the camera to the place under discussion. Malformed
steering falls back to keyword resolution; the answer still renders. This is how the AI and
the 3D world stay in sync without giving the model any direct control over game code.

## 5. Guardrails, failure modes, and honest limits

**Layered guardrails:**

1. **Grounded prompt** — the model answers only from supplied, verified facts.
2. **Citations** — answers name their sources; the UI renders them.
3. **"I don't know" fallback** — instructed refusal beats confident invention.
4. **Bounded output** — capped token budget; temperature 0.6.
5. **Structural isolation** — the model emits text and one parsed steering hint; it cannot
   invoke game systems, and a malformed response degrades to keyword steering.
6. **Backend fallback** — any transport/model failure silently re-asks the scripted guide;
   the experience never dies.
7. **No secrets, no personal data** — the endpoint is localhost, configured in the Unity
   inspector; nothing is collected from users, so there is no consent or privacy surface.

**Known failure modes, stated honestly:** a small model can still occasionally misphrase a
grounded fact or imperfectly follow the citation format; latency is host-hardware-bound
(answers stream, but tokens/sec varies by machine); and the guide is evaluated in English
only — Shona/Ndebele are roadmap items, not claims.

## 6. Validation and oversight

- **Knowledge-base integrity (automated):** `py Dataset/audit_provenance.py` verifies the
  full provenance trail — every chunk cites a real source with license/URL/access metadata
  and a ledger row, and every fact sheet resolves. Run after any dataset change.
- **Eval question set:** [`Dataset/qa/visitor-qa.md`](../Dataset/qa/visitor-qa.md) holds
  anticipated visitor questions with reference answers grounded in the chunks, used to
  spot-check the guide's answers against ground truth.
- **Unit tests (automated):** Unity EditMode tests cover the fact-sheet loader and the
  narration cue matcher (`Assets/GreatZimbabwe/Tests/`).
- **Steering-protocol check:** the JSON steering line and its keyword fallback were tested
  against the live model server during development.
- **Human oversight by construction:** the corpus is human-curated and human-audited; the
  model cannot introduce new "facts" into the product, only phrase existing ones. Content
  decisions (institutional sources only, no colonial-era historiography, Shona origin stated
  as fact) were made by the team, not the model.

## 7. The design we rejected

The original architecture was a separate Python service doing embedding-based vector
retrieval against the model server. We dropped it (2026-07-09) for reasons worth recording:

- At an ~8 KB corpus, retrieval adds infrastructure and failure modes without adding
  accuracy — the classic "sledgehammer" over-engineering the rubric penalizes, just in
  RAG form.
- A single Unity process is the stronger offline-kiosk story: one app plus one local model
  server, no sidecar service to install, monitor, or restart.

The rejected design returns, deliberately, as the scaling path in §3 — when the corpus grows
past what fits in a prompt, not before.
