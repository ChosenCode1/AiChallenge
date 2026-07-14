# Deployment — Running Mabwe Offline, For Real

How Mabwe is deployed on the machines it is actually meant for: a judge's or developer's
laptop today, and a tourism-office / museum / classroom kiosk next. Everything here is
software-first, on-device, and offline — the only network traffic the product ever
generates is HTTP to `localhost`.

Companion docs: [ai-usage.md](ai-usage.md) (model and guardrails),
[llm-e2e-verification.md](llm-e2e-verification.md) (measured performance),
[demo.md](demo.md) (walkthrough).

## 1. The two deployment modes

Mabwe deliberately ships with two modes behind one abstract backend, and that is the
deployment story in miniature:

| | **Live mode** (recommended) | **Scripted mode** (zero setup) |
|---|---|---|
| Parts installed | Unity app **+** a local LLM server with the model file | Unity app only |
| AI | Local LLM, grounded and cited | None — pre-written scripted guide |
| Handles | Open-ended questions nobody predicted | Only what we pre-wrote |
| Internet needed | **None** | **None** |
| Failure behavior | Silently falls back to scripted mode | n/a — it *is* the fallback |

The honest framing: **a live-mode kiosk is a two-part install.** We state that plainly
rather than hiding it, because the fallback design means the second part is never a
single point of failure — if the model server is stopped, crashes, or was never
installed, the guide keeps working in scripted mode and the experience never dies.

## 2. Hardware requirements (honest numbers)

| | Minimum (scripted mode) | Live mode |
|---|---|---|
| Machine class | Anything that runs a Unity desktop build | Consumer laptop / mini-PC / kiosk PC |
| RAM | Unity app footprint only | **~4 GB free for the model** (3.12 GiB weights) on top of the app — 16 GB total is comfortable |
| Disk | App build | App build + ~3 GB model file + server (~0.5 GB) |
| GPU | Integrated is fine | Not required; speeds up inference when present |
| Network | None | None (localhost only) |

Measured on our development machine (see the
[verification transcript](llm-e2e-verification.md)): model load 6.1 s, first words of an
answer in **2.1–2.4 s**, then **~165–205 tokens/s** streaming. These numbers are
host-hardware-bound and will differ on other machines — we report what we measured and
promise nothing faster.

A `GreatZimbabwe_Mobile` scene variant with pre-baked terrain targets lower-end devices
in scripted mode.

## 3. Installing a kiosk (live mode)

Target: a Windows kiosk PC or laptop at a visitor centre, tourism office, lodge or
hotel lobby, or classroom.
One-time setup, done anywhere with internet, after which the machine never needs
connectivity again:

1. **Build or copy the Unity player.** A standalone desktop build produced from this
   project (Unity 6000.3.14f1, standard build pipeline). Copy the build folder onto the
   kiosk.
2. **Install a local model server.** LM Studio is what we develop against
   (0.4.19 verified); llama.cpp server or Ollama drop in unchanged because the backend
   speaks the OpenAI-compatible API with no vendor coupling.
3. **Copy the model file** — `gemma-4-e2b-it-qat` (QAT q4_0 GGUF, ~3 GB). Open weights,
   license-clean, redistributable to the kiosk without any account or key.
4. **Configure autostart.** The server starts headless at boot (LM Studio:
   `lms server start && lms load gemma-4-e2b-it-qat`, or the llama.cpp equivalent as a
   scheduled task / service), then the Unity app launches fullscreen.
5. **Point the app at the server** — the endpoint URL, model id, and generation settings
   are plain inspector fields on the `GZLocalLLMBackend` component, set before building.
   Default is LM Studio's `http://localhost:1234`. **No keys, no config files, no
   secrets** — there is nothing to leak because nothing needs credentials.

Order doesn't matter operationally: if the app comes up before the model server, the
guide answers in scripted mode and switches nothing off; restarting the server restores
live answers on the next question.

**Scripted-only kiosk:** step 1 alone. This is the right configuration for hardware that
can't hold the model, and it is an honest degraded mode, not a broken one.

## 4. Updates and content maintenance

- **Knowledge updates are data, not code.** New or corrected facts land in `Dataset/`
  chunks and the per-POI fact sheets, pass `py Dataset/audit_provenance.py` (the
  provenance audit fails loudly on any break), then ship as a rebuilt app copied to the
  kiosk — by USB stick if need be. No connectivity required at the kiosk, ever.
- **Model swaps are a file copy** plus one inspector field. Anything served over the
  OpenAI-compatible API works; before promoting a new model, `Tools/e2e_llm_probe.py`
  re-runs the grounding, steering, and refusal checks against it.
- **Nothing phones home.** There is no telemetry, no analytics, no account system, and
  no personal data collected — a visitor's questions never leave the machine they were
  typed on. There is no consent or privacy surface to manage, which for a public-sector
  heritage deployment is a feature, not an omission.

## 5. Operating cost reality

The deployment model was chosen so that the marginal cost of a visitor's question is
**zero**:

- **No per-query cost.** Local inference means no API fees, no per-token billing, no
  usage caps. A kiosk that answers 50 questions a day costs the same as one that
  answers 5,000.
- **No connectivity cost.** Heritage sites and rural classrooms with poor or no
  coverage are the design target, not an afterthought to apologize for.
- **One-time hardware.** A consumer-class PC in the 16 GB RAM range covers live mode;
  existing school or office machines cover scripted mode.
- **Ongoing costs** are electricity and ordinary machine upkeep by whoever operates the
  venue (tourism office, museum, lodge or hotel, school) — the same staff who maintain
  any other kiosk or display.

## 6. Security posture

- All inference traffic is `localhost` HTTP inside one machine; nothing listens for
  external connections on the product's behalf.
- The repository and the build contain **no secrets** — the only "configuration" is a
  localhost URL in a Unity inspector field.
- The model cannot invoke game systems: it emits text plus one machine-readable steering
  line that deterministic code parses, validates, and (if malformed) discards. Prompt
  injection through visitor questions was exercised in the adversarial battery
  (see [llm-e2e-verification.md](llm-e2e-verification.md)) — the answer degrades, the
  app does not.
- A kiosk build runs fullscreen as a standard user; nothing requires elevation.

## 7. Known limitations (deployment view)

Honesty over polish — mirrored from the README and kept current:

- **Two-part install in live mode.** Unity app + model server. Mitigated by the silent
  scripted fallback, but it is still two things to set up once.
- **Laptop-class device required for live mode.** The model needs ~4 GB of free RAM.
  Scripted mode runs anywhere the Unity build runs.
- **Latency is model-bound.** Answers stream token-by-token; speed depends on the host
  machine. We publish measured numbers, not promises.
- **English-first.** Shona/Ndebele answers are untested and not claimed (roadmap).
- **Small offline model.** Grounding and citations dramatically reduce hallucination,
  but a 3 GB model can still occasionally misphrase a fact; the guide is instructed to
  decline rather than guess.
- **Single-machine scope today.** Multi-kiosk fleet management (remote monitoring,
  staged rollouts) is future operations work, not something we claim now.
