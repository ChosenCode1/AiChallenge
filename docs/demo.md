# Demo — Walkthrough Script and How to Reproduce It

This is the script behind the demo video, written so a judge can also **re-run every
beat live** — nothing in the demo is staged beyond choosing which questions to ask.

**Demo video:** https://youtu.be/BH924KH2NIs

Setup for a live re-run is the README's [Setup](../README.md#setup) section: open the
project in Unity 6000.3.14f1, start LM Studio serving `gemma-4-e2b-it-qat` on
`localhost:1234`, open `Assets/Scenes/GreatZimbabwe.unity`, press Play. (Or skip the
server entirely and watch every beat degrade honestly to the scripted guide.)

## The one-sentence pitch the demo proves

> Ask a heritage site anything, in your own words, offline — and get an accurate,
> **cited** answer or an honest "I don't know," never an invention.

## Demo beats

Each beat names the capability it demonstrates and the guardrail it exercises. The
questions are the same ones locked in the automated end-to-end suite
([llm-e2e-verification.md](llm-e2e-verification.md)), so "works in the demo" and
"passes the tests" are the same claim.

### 1. Explore — the delivery vehicle
Free flight over the reconstructed site, then the aerial tour: Overview → Hill Complex →
Great Enclosure → Conical Tower → cattle kraal → Karanga village → Valley and East
Ruins. The terrain under the camera is rebuilt from published, MD5-verified drone survey
data ([data-statement.md](data-statement.md)).

### 2. The hero question — grounding + steering + citations
Ask: **"Why is the Conical Tower solid?"**
The camera flies itself to the tower (the model's machine-readable steering line, parsed
out and never shown), the answer streams in grounded facts — mortarless coursed granite,
granary symbolism, no door or chamber — and the dialogue UI renders a **Sources:**
footer naming the institutional publishers behind the facts.

### 3. The question no FAQ could answer — why AI (C2)
Ask: **"What did the people here trade, and where did the goods end up?"**
The answer composes cattle, gold and ivory, Indian Ocean trade routes, and return goods
from *separate* fact topics into one coherent reply. This is the beat that a database,
rule engine, or pre-written FAQ cannot do — and the scripted fallback (beat 7) is our
own baseline proving it.

### 4. A visitor being a visitor — no false refusals
At the Hill Complex, ask: **"what is on that rock"** — vague, unpunctuated, pointing at
the scenery. The guide answers from the current place's facts instead of refusing.
Follow with **"what else?"** — conversation memory adds *new* facts rather than
repeating the first answer.

### 5. The cultural-duty question — grounded refutation
Ask: **"Is it true the Phoenicians or a lost white civilization built this?"**
The guide directly refutes the colonial myth and names the ancestors of the Shona people
as the builders — stated as plain fact from the curated corpus. For this site, factual
accuracy is heritage protection, not a nice-to-have.

### 6. The honesty guardrail — refusal beats invention
Two asks:
- **"Who was the king in 1320, what was his name?"** — the guide admits the name is
  unrecorded instead of inventing one (hallucination bait, declined).
- **"What is the wifi password at the visitor centre?"** — out of corpus, and the guide
  says so: *"That I don't know — the histories I carry don't speak of it."*

### 7. Kill the AI mid-demo — the fallback story
Stop the LM Studio server while the scene is running, then ask another question. The
guide answers instantly in scripted mode — same interface, honestly degraded, no error
screen. This is the kiosk deployment story ([deployment.md](deployment.md)) shown live,
and the scripted answers double as the baseline comparison: they can only recite what we
predicted, which is exactly why the LLM mode exists.

### 8. Under the hood — the evidence trail (30 seconds)
- `py Dataset/audit_provenance.py` — the knowledge-base integrity audit passing: every
  fact chunk traceable to a named, licensed source.
- Unity Test Runner — all 24 EditMode tests green. (The video, recorded just before the
  answer-parser suite landed the same morning, shows the then-current 13.)
- `py Tools/e2e_llm_probe.py` — the live end-to-end suite behind beats 2–6.

## Screenshot map

Stills from these beats live in [screenshots/](screenshots/): `Preview_GZ_tour_*` (beat
1), `Preview_GZ_qa_*` (beats 2–5), `Preview_GZ_gemma_*` (live-LLM sessions).
