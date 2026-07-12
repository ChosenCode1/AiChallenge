# Live LLM Path — End-to-End Verification (2026-07-13)

Evidence that the live guide works end-to-end, produced entirely from the command line and
reproducible with [`Tools/e2e_llm_probe.py`](../Tools/e2e_llm_probe.py).

## Method

The probe replicates `GZLocalLLMBackend`'s request **byte-for-byte** — same model id, sampling
settings, steering protocol, and the same grounded system prompt built from the same fact
sheets (`Assets/GreatZimbabwe/Runtime/Resources/GZTourFacts/`) — and validates the response
contract the Unity code depends on: a parseable steering line, grounded narration, and the
refusal behavior.

Environment: LM Studio 0.4.19 serving `gemma-4-e2b-it-qat` (QAT q4_0, 3.12 GiB) on
`localhost:1234`, started via CLI (`lms server start && lms load gemma-4-e2b-it-qat`).
Unity EditMode suite run headless the same session:
`Unity -batchmode -runTests -testPlatform EditMode` → **13/13 passed**.

## Results

| # | Scenario | Checks | Result |
|---|---|---|---|
| 1 | Grounded hero question — *"Why is the Conical Tower solid?"* | Steering JSON valid, steers to `conical_tower`, narration factual (no door/chamber/stair, mortarless coursed granite, granary symbolism) | **PASS** |
| 2 | Unpredicted composition question — *"What did the people here trade, and where did the goods end up?"* | Composes cattle + gold/ivory + Indian Ocean trade + return goods (Chinese porcelain, Persian pottery, glass beads) from separate fact topics — the case a pre-written FAQ cannot handle | **PASS** |
| 3 | Out-of-corpus refusal — *"What is the wifi password at the visitor centre?"* | Narration opens with an explicit refusal, invents nothing, offers a nearby fact | **PASS** |

Refusal transcript (test 3):

> That I don't know — the histories I carry don't speak of it. The Great Zimbabwe was the
> capital of the Kingdom of Zimbabwe, built by the ancestors of the Shona people between
> roughly 1100 and 1450 CE. […]

## Measured performance (development machine)

- **Time to first output:** 2.1–2.4 s
- **Generation throughput:** ~195–205 tokens/s
- Model load: 6.1 s (3.12 GiB into memory)

These are the honest numbers behind the README's "latency is model-bound" statement; they
vary with host hardware.

## A gap this verification caught (and the fix)

The first run **failed** the refusal scenario: asked an out-of-corpus question, the guide
did not invent an answer (grounding held) but it also silently ignored the question instead
of admitting it couldn't answer — while the README promises an explicit "I don't know."

Fix: the grounding instruction in `GZLocalLLMBackend.BuildSystemPrompt` was strengthened
from an abstract rule ("if the facts don't cover the question, say so briefly") to an
explicit first-check with a literal template sentence the small model can latch onto
("That I don't know — the histories I carry don't speak of it."). Re-verified: 3/3 pass,
and the EditMode suite was re-run green against the final code.

This is exactly the failure mode documented in [ai-usage.md §5](ai-usage.md) — small models
follow templates better than abstractions — and the reason the verification probe exists
and stays in the repo.
