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

## Results (final run: 5/5)

| # | Scenario | Checks | Result |
|---|---|---|---|
| 1 | Grounded hero question — *"Why is the Conical Tower solid?"* | Steering JSON valid; narration factual (no door/chamber, mortarless coursed granite, granary symbolism) | **PASS** |
| 2 | Unpredicted composition question — *"What did the people here trade, and where did the goods end up?"* | Composes cattle + gold/ivory + Indian Ocean trade + return goods from separate fact topics — the case a pre-written FAQ cannot handle | **PASS** |
| 3 | Out-of-corpus refusal — *"What is the wifi password at the visitor centre?"* | Explicit "That I don't know…" admission, invents nothing, offers a nearby fact | **PASS** |
| 4 | Vague pointing question at a POI — *"what is on that rock"* (camera at Hill Complex) | Answers from the current place's facts (boulder-threaded walls, Zimbabwe Birds) — must NOT refuse | **PASS** |
| 5 | Deictic place question — *"tell me about this place"* (camera at Great Enclosure) | Answers from the current place's facts — must NOT refuse | **PASS** |

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

Small-model instruction-following cuts both ways; this is exactly the failure family
documented in [ai-usage.md §5](ai-usage.md), and the reason this probe lives in the repo and
runs before every release.
