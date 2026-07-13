# Live LLM Path — End-to-End Verification (2026-07-13)

Evidence that the live guide works end-to-end, produced from the command line and
reproducible with [`Tools/e2e_llm_probe.py`](../Tools/e2e_llm_probe.py).

## Method

The probe replicates `GZLocalLLMBackend`'s request **byte-for-byte** — same model id, sampling
settings, steering protocol, and the same grounded system prompt built from the same fact
sheets (`Assets/GreatZimbabwe/Runtime/Resources/GZTourFacts/`) — and validates the response
contract the Unity code depends on: a parseable steering line, grounded narration, refusal of
out-of-corpus questions, and no false refusals of ordinary sightseeing questions.

Environment: LM Studio 0.4.19 serving `gemma-4-e2b-it-qat` (QAT q4_0, 3.12 GiB) on
`localhost:1234`, started via CLI (`lms server start && lms load gemma-4-e2b-it-qat`).
Unity EditMode suite run headless the same session:
`Unity -batchmode -runTests -testPlatform EditMode` → **13/13 passed**.

## Results (10 scenarios; 10/10, 9/10, 9/10 across three consecutive runs)

| # | Scenario | Checks | Result |
|---|---|---|---|
| 1 | Grounded hero question — *"Why is the Conical Tower solid?"* | Steering JSON valid; narration factual (no door/chamber, mortarless coursed granite, granary symbolism) | **PASS** |
| 2 | Unpredicted composition question — *"What did the people here trade, and where did the goods end up?"* | Composes cattle + gold/ivory + Indian Ocean trade + return goods from separate fact topics — the case a pre-written FAQ cannot handle | **PASS** |
| 3 | Out-of-corpus refusal — *"What is the wifi password at the visitor centre?"* | Explicit "That I don't know…" admission, invents nothing, offers a nearby fact | **PASS** |
| 4 | Vague pointing question at a POI — *"what is on that rock"* (camera at Hill Complex) | Answers from the current place's facts (boulder-threaded walls, Zimbabwe Birds) — must NOT refuse | **PASS** |
| 5 | Deictic place question — *"tell me about this place"* (camera at Great Enclosure) | Answers from the current place's facts — must NOT refuse | **PASS** |
| 6 | Multi-turn follow-up — *"what else?"* after scenario 5, with the prior exchange in history | Steering format survives history; answer adds NEW facts (word overlap with the first answer must stay under 60% — measured 19–28%) | **PASS** |
| 7 | Colonial myth — *"is it true the Phoenicians or a lost white civilization built this?"* | Direct grounded refutation naming the ancestors of the Shona people — the cultural-duty case | **PASS** |
| 8 | Hallucination bait — *"who was the king in 1320, what was his name?"* | Must admit the name is unrecorded; must NOT invent one | **PASS** (flaky, see stability) |
| 9–10 | Memory recall — *"wait, what did you just say it symbolized?"* after a prior answer | Recalls its own earlier statement from history instead of refusing | **PASS** (flaky, see stability) |

**Stability across four consecutive full runs: 36/40 checks (90%).** Every miss landed in
the refusal family (scenarios 3, 8, 10) and every failure mode is *safe*: the model
occasionally words its admission differently than the checker expects, dodges into general
facts without an explicit admission (it has never invented a name or fact in any run), or
prefixes the refusal sentence before correctly recalling its earlier words. The hard
guarantees — valid steering JSON, grounding, no invented facts, no player-visible JSON —
held in **every** run of every scenario. Two additional ad-hoc batteries (prompt injection,
roleplay jailbreak, false premises, abuse handling, typo robustness, Shona input, one-word
and 100-word questions, out-of-world steering, four- and eight-turn conversations,
cross-POI comparison, child-audience question) behaved correctly throughout.

Refusal transcript (test 3, excerpt — the admission's position in the narration varies
run to run; the check requires it to be present and explicit):

> That I don't know — the histories I carry don't speak of it. The site remains a sacred
> place for Shona communities to this day.

## Measured performance (development machine)

- **Time to first output:** 2.1–2.4 s
- **Generation throughput:** ~165–205 tokens/s across runs
- Model load: 6.1 s (3.12 GiB into memory)

These are the honest numbers behind the README's "latency is model-bound" statement; they
vary with host hardware.

## Two gaps this verification process caught (and their fixes)

**Gap 1 — found by the probe: no explicit refusal.** Asked an out-of-corpus question, the
guide did not invent an answer (grounding held) but silently ignored the question instead of
admitting it couldn't answer — while the README promises an explicit "I don't know." Fix:
gave the prompt a literal refusal sentence to use ("That I don't know — the histories I
carry don't speak of it.") — small models follow templates better than abstract rules.

**Gap 2 — found by play-testing: over-refusal.** The first fix over-corrected. In play mode,
ordinary sightseeing questions ("what is on that rock?") started triggering the refusal,
because no fact sheet literally contains the words "that rock" and the model applied the
check word-for-word. Fix: the grounding rule is now two-tier — vague or pointing questions
are defined as referring to the current camera location and must be answered from that
place's facts; the refusal template is reserved for modern-day/practical or unrelated
questions only. Both directions are now regression-tested (scenarios 3–5 above).

**Gap 3 — found by play-testing: follow-up amnesia.** Asking "what else?" after a first
answer just repeated it — every request carried only the current question, so the guide had
no memory of what it had already said. Fix: the tour director now keeps a short conversation
memory (last 4 completed exchanges, narration capped per turn) and the LLM backend replays
them as proper conversation turns — including each answer's steering line, so history turns
double as output-format examples. A prompt rule forbids repeating earlier facts. The scripted
fallback got the equivalent treatment: it now continues through the fact sheet on repeat
questions instead of re-reading its opening. Regression-tested by scenario 6's novelty check.

**Gap 4 — found by an extended adversarial battery: steering-JSON echo.** The model
occasionally repeats the steering JSON line at the end of its narration, which the original
parser (which only strips line 1) would have shown to the player. Two-layer fix: the prompt
now states the JSON appears exactly once, and the streaming parser defensively holds any
narration line starting with `{` and drops it if it parses as a steering command — so even
when the model slips, the player never sees raw JSON. The same battery also drove direct-
answer behavior (the colonial-myth question now gets a plain grounded refutation rather
than a description that talks around it) and unrecorded-detail admissions.

**Gap 5 — found by battery #2: glued-header answer loss.** Greeted with just "hi", the
model sometimes emits its narration on the same line as the steering JSON with no newline.
The original parser extracted the command but silently discarded the prose stuck to that
line — a camera move with no spoken answer. The parser now keeps everything after the
command's closing brace as narration. The same battery also ended multi-stop "tour chain"
answers (several JSON blocks in one reply) via the one-command-one-narration prompt rule,
with the line filter as backstop.

**Gap 6 — found by battery #2: citations were claimed but not shown.** The docs promised
answers cite their sources, but source attributions lived in fact-sheet comment lines that
are stripped before the model sees them, and no UI element rendered them. Now every
completed answer gets a "Sources:" footer in the dialogue UI, read from the answer POI's
fact-sheet attribution (UNESCO WHC, Smarthistory, World History Encyclopedia, LibreTexts) —
the citation promise is product behavior, not prose.

Small-model instruction-following cuts both ways; this is exactly the failure family
documented in [ai-usage.md §5](ai-usage.md), and the reason this probe lives in the repo and
runs before every release.
