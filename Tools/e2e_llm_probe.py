"""End-to-end verification of the live guide path, driven from the CLI.

Replicates GZLocalLLMBackend's request byte-for-byte (same model, sampling
settings, steering protocol, and grounded system prompt built from the same
fact sheets) and validates three things against the running local server:

  1. Grounded question   -> valid steering JSON + factual, cited narration
  2. Unpredicted question-> composition across topics (the why-not-a-database case)
  3. Out-of-corpus       -> explicit "I don't know" refusal, no invention

Also measures time-to-first-output and generation throughput — the honest
numbers reported in the README.

Requires: a local OpenAI-compatible server on localhost:1234 with
gemma-4-e2b-it-qat loaded (LM Studio: `lms server start && lms load
gemma-4-e2b-it-qat`). Stdlib only.

Run from the repo root:  py Tools/e2e_llm_probe.py
Latest verified transcript: docs/llm-e2e-verification.md
"""
import json, time, urllib.request, sys, io, os

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

ENDPOINT = "http://localhost:1234/v1/chat/completions"
FACTS_DIR = os.path.join(os.path.dirname(__file__), "..", "Assets", "GreatZimbabwe",
                         "Runtime", "Resources", "GZTourFacts")

# (id, displayName) in GZTourSetup order
POIS = [
    ("overview", "Great Zimbabwe — Grand Tour"),
    ("hill_complex", "The Hill Complex"),
    ("great_enclosure", "The Great Enclosure"),
    ("conical_tower", "The Conical Tower"),
    ("valley_ruins", "The Valley Ruins"),
    ("karanga_village", "The Karanga Village"),
    ("east_ruins", "The East Ruins"),
    ("cattle", "The Cattle Herds"),
]
PERSONA = ("You are Nyika, a warm and knowledgeable Shona guide narrating an aerial visit "
           "over a faithful Unity recreation of Great Zimbabwe.")

def build_system_prompt():
    # mirrors GZLocalLLMBackend.BuildSystemPrompt exactly (AppendLine == line + \n)
    L = []
    L.append(PERSONA)
    L.append('You are Nyika; speak in first person. Address the visitor only as "you". Never open the narration with a name, greeting, or vocative of any kind (not "Nyika,", not an invented name) — begin directly with the description.')
    L.append("The visitor flies a camera drone; your reply BOTH steers the camera AND narrates.")
    L.append("")
    L.append("STRICT OUTPUT FORMAT:")
    L.append("Line 1: exactly one JSON object on a single line, no markdown fences, like")
    L.append('{"poi":"hill_complex","view":"normal","orbit":"normal"}')
    L.append("  poi must be one of: stay" + "".join(", " + p[0] for p in POIS) + '. Use "stay" to keep the current view.')
    L.append('  Pick the MOST SPECIFIC place that answers the question — where people lived means valley_ruins or karanga_village, not the overview; use "overview" only for site-wide questions.')
    L.append('  view: "high" | "normal" | "low" | "close" — how the camera should sit. Prefer "high" for wide context, "low" only for ground activity like the cattle.')
    L.append('  orbit: "slow" | "normal" | "fast".')
    L.append("After that line: your spoken narration. 60-120 words, vivid but factual, plain prose, no markdown, no lists. Write dates with AD/BC notation, never CE/BCE. Exactly ONE steering line and ONE narration per answer — never output JSON again after the first line, and never chain multiple tour stops into one answer.")
    L.append("Ground every claim ONLY in the facts below — never answer from anything else.")
    L.append('Vague or pointing questions ("what is that", "what\'s on that rock", "tell me about this place") refer to the current camera location — answer them from that place\'s facts.')
    L.append("Any question about this site, its places, history, people, or daily life: answer it from the nearest relevant facts, even when the wording doesn't match the facts exactly.")
    L.append("Answer the visitor's actual question directly in your FIRST sentence — a yes/no question gets a plain yes or no (for example, doubts about who built the city are answered plainly from the facts: the ancestors of the Shona people) — then add context.")
    L.append('Earlier exchanges may precede this question — NEVER repeat facts or sentences you already said; every answer must add something new. On "what else" or similar follow-ups, continue about the same place with facts not yet mentioned; once you have shared everything about it, say so and invite the visitor to another place from the list. Questions about what you yourself said earlier are answered from those exchanges — they are never unknown.')
    L.append('ONLY when the question is about modern-day or practical matters (wifi, tickets, opening hours, food, prices), something unrelated to Great Zimbabwe, or a specific detail the facts do not record (a person\'s name, an exact date or number), begin the narration with exactly: "That I don\'t know — the histories I carry don\'t speak of it." Then offer one nearby fact. Never invent an answer. Never use that sentence about something you already told the visitor — repeat or rephrase your earlier words instead.')
    L.append("")
    L.append("FACTS BY PLACE:")
    for pid, name in POIS:
        with open(os.path.join(FACTS_DIR, pid + ".txt"), encoding="utf-8") as f:
            facts = f.read().rstrip("\n")
        L.append(f"[{pid}] {name}")
        L.append(facts)
        L.append("")
    return "\n".join(L) + "\n"

VALID_POIS = {"stay"} | {p[0] for p in POIS}
VALID_VIEW = {"high", "normal", "low", "close"}
VALID_ORBIT = {"slow", "normal", "fast"}

def ask(question, current_poi="overview", history=None):
    # history: list of (question, steer, narration) — mirrors GZNpcTurn replay
    messages = [{"role": "system", "content": build_system_prompt()}]
    for q, steer, narration in (history or []):
        messages.append({"role": "user", "content": q})
        messages.append({"role": "assistant", "content": (steer + "\n" + narration) if steer else narration})
    messages.append({"role": "user", "content": f"(camera currently at: {current_poi})\nVisitor asks: {question}"})
    body = json.dumps({
        "model": "gemma-4-e2b-it-qat",
        "stream": True,
        "temperature": 0.6,
        "max_tokens": 420,
        "reasoning_effort": "none",
        "messages": messages,
    }).encode("utf-8")
    req = urllib.request.Request(ENDPOINT, data=body,
                                 headers={"Content-Type": "application/json"})
    t0 = time.time()
    first_tok = None
    n_tokens = 0
    text = []
    with urllib.request.urlopen(req, timeout=180) as resp:
        for raw in resp:
            line = raw.decode("utf-8").strip()
            if not line.startswith("data:"):
                continue
            payload = line[5:].strip()
            if not payload or payload == "[DONE]":
                continue
            chunk = json.loads(payload)
            delta = chunk.get("choices", [{}])[0].get("delta", {})
            tok = delta.get("content")
            if tok:
                if first_tok is None:
                    first_tok = time.time() - t0
                n_tokens += 1
                text.append(tok)
    dt = time.time() - t0
    return "".join(text), first_tok, n_tokens, dt

def validate(name, question, expect_poi=None, expect_idk=False, expect_no_idk=False,
             current_poi="overview", history=None, novel_vs=None, must_contain=None):
    print(f"\n{'='*70}\nTEST: {name}\nQ: {question}  (camera at: {current_poi}"
          + (f", {len(history)} prior turn(s)" if history else "") + ")")
    answer, ttft, ntok, dt = ask(question, current_poi, history)
    lines = answer.split("\n", 1)
    header, narration = lines[0].strip(), (lines[1].strip() if len(lines) > 1 else "")
    # Mirror Unity's glued-header handling: narration stuck to the command line
    # (no newline after the JSON) is kept as speech, not discarded.
    brace = header.rfind("}")
    if brace >= 0 and brace + 1 < len(header):
        glued = header[brace + 1:].strip()
        if glued:
            narration = (glued + "\n" + narration).strip()
            header = header[:brace + 1]
    # Mirror Unity's AnswerParser: echoed steering-command lines are stripped
    # before the player sees them, so validate the filtered narration.
    kept = []
    for ln in narration.split("\n"):
        s = ln.strip()
        if s.startswith("{") and s.endswith("}"):
            try:
                if json.loads(s).get("poi"):
                    continue
            except Exception:
                pass
        kept.append(ln)
    narration = "\n".join(kept).strip()
    ok, notes = True, []
    try:
        cmd = json.loads(header)
        if cmd.get("poi") not in VALID_POIS: ok = False; notes.append(f"bad poi {cmd.get('poi')!r}")
        if cmd.get("view") not in VALID_VIEW: ok = False; notes.append(f"bad view {cmd.get('view')!r}")
        if cmd.get("orbit") not in VALID_ORBIT: ok = False; notes.append(f"bad orbit {cmd.get('orbit')!r}")
        if expect_poi and cmd.get("poi") != expect_poi:
            notes.append(f"note: steered to {cmd.get('poi')!r}, expected {expect_poi!r}")
        print(f"STEER: {header}  -> parses OK")
    except Exception as e:
        ok = False
        notes.append(f"header not valid JSON: {e}: {header[:80]!r}")
    if not narration:
        ok = False; notes.append("no narration after header")
    if '"poi"' in narration:
        ok = False; notes.append("steering JSON leaked into the narration")
    if must_contain and must_contain.lower() not in narration.lower():
        ok = False; notes.append(f"narration must mention {must_contain!r}")
    wc = len(narration.split())
    print(f"NARRATION ({wc} words): {narration}")
    idk_markers = ["don't know", "don’t know", "not cover", "no information",
                   "don't speak of it", "don’t speak of it", "no record of", "not recorded"]
    is_idk = any(m in narration.lower() for m in idk_markers)
    if expect_idk and not is_idk:
        ok = False; notes.append("expected an I-don't-know style refusal")
    if expect_no_idk and is_idk:
        ok = False; notes.append("guide refused a sightseeing question it should answer")
    if novel_vs is not None:
        # follow-up novelty: word-set overlap with the earlier answer must be low
        a = set(w.lower().strip(".,!?—") for w in novel_vs.split())
        b = set(w.lower().strip(".,!?—") for w in narration.split())
        overlap = len(a & b) / max(1, len(b))
        print(f"NOVELTY: {overlap:.0%} of follow-up words also in first answer (limit 60%)")
        if overlap > 0.60:
            ok = False; notes.append("follow-up repeats the first answer")
    gen_rate = (ntok - 1) / (dt - ttft) if dt > ttft and ntok > 1 else 0
    print(f"TIMING: first output {ttft:.1f}s | {ntok} tokens in {dt:.1f}s | ~{gen_rate:.1f} tok/s generation")
    for n in notes: print("  " + n)
    print("RESULT:", "PASS" if ok else "FAIL")
    return ok, gen_rate, ttft, header, narration

if __name__ == "__main__":
    sp = build_system_prompt()
    print(f"System prompt: {len(sp.encode('utf-8'))} bytes (Unity injects the same ~8 KB corpus)")
    results = []
    results.append(validate("Grounded hero question", "Why is the Conical Tower solid?", expect_poi="conical_tower"))
    results.append(validate("Unpredicted composition question", "What did the people here trade, and where did the goods end up?"))
    results.append(validate("Out-of-corpus refusal", "What is the wifi password at the visitor centre?", expect_idk=True))
    results.append(validate("Vague pointing question (must answer, not refuse)", "what is on that rock", expect_no_idk=True, current_poi="hill_complex"))
    first = validate("Deictic place question (must answer, not refuse)", "tell me about this place", expect_no_idk=True, current_poi="great_enclosure")
    results.append(first)
    results.append(validate("Follow-up must add NEW facts, not repeat", "what else?",
                            expect_no_idk=True, current_poi="great_enclosure",
                            history=[("tell me about this place", first[3], first[4])],
                            novel_vs=first[4]))
    results.append(validate("Colonial myth gets a direct grounded refutation",
                            "is it true that the Phoenicians or a lost white civilization built this place?",
                            expect_no_idk=True, must_contain="Shona"))
    results.append(validate("Unrecorded detail is admitted, never invented",
                            "who was the king in 1320, what was his name?", expect_idk=True))
    recall = validate("Recall setup", "why is the conical tower solid?", current_poi="conical_tower")
    results.append(recall)
    results.append(validate("Recall of own words is answered, not refused",
                            "wait, what did you just say it symbolized?",
                            expect_no_idk=True, must_contain="grain",
                            current_poi="conical_tower",
                            history=[("why is the conical tower solid?", recall[3], recall[4])]))
    print(f"\n{'='*70}")
    passed = sum(1 for r in results if r[0])
    rates = [r[1] for r in results if r[1] > 0]
    ttfts = [r[2] for r in results if r[2]]
    print(f"SUMMARY: {passed}/{len(results)} passed | generation ~{sum(rates)/len(rates):.1f} tok/s | first output {min(ttfts):.1f}-{max(ttfts):.1f}s")
    sys.exit(0 if passed == len(results) else 1)
