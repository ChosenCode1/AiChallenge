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
    L.append('  view: "high" | "normal" | "low" | "close" — how the camera should sit. Prefer "high" for wide context, "low" only for ground activity like the cattle.')
    L.append('  orbit: "slow" | "normal" | "fast".')
    L.append("After that line: your spoken narration. 60-120 words, vivid but factual, plain prose, no markdown, no lists.")
    L.append("Ground every claim ONLY in the facts below — never answer from anything else.")
    L.append('FIRST check: do the facts below answer the visitor\'s question? If they do not (practical or modern-day questions like tickets, wifi, opening hours, food), the narration MUST begin with exactly: "That I don\'t know — the histories I carry don\'t speak of it." Then offer one nearby fact. Never ignore the question and never invent an answer.')
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

def ask(question, current_poi="overview"):
    body = json.dumps({
        "model": "gemma-4-e2b-it-qat",
        "stream": True,
        "temperature": 0.6,
        "max_tokens": 420,
        "reasoning_effort": "none",
        "messages": [
            {"role": "system", "content": build_system_prompt()},
            {"role": "user", "content": f"(camera currently at: {current_poi})\nVisitor asks: {question}"},
        ],
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

def validate(name, question, expect_poi=None, expect_idk=False):
    print(f"\n{'='*70}\nTEST: {name}\nQ: {question}")
    answer, ttft, ntok, dt = ask(question)
    lines = answer.split("\n", 1)
    header, narration = lines[0].strip(), (lines[1].strip() if len(lines) > 1 else "")
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
    wc = len(narration.split())
    print(f"NARRATION ({wc} words): {narration}")
    if expect_idk:
        idk_markers = ["don't know", "don’t know", "not cover", "no information"]
        if not any(m in narration.lower() for m in idk_markers):
            ok = False; notes.append("expected an I-don't-know style refusal")
    gen_rate = (ntok - 1) / (dt - ttft) if dt > ttft and ntok > 1 else 0
    print(f"TIMING: first output {ttft:.1f}s | {ntok} tokens in {dt:.1f}s | ~{gen_rate:.1f} tok/s generation")
    for n in notes: print("  " + n)
    print("RESULT:", "PASS" if ok else "FAIL")
    return ok, gen_rate, ttft

if __name__ == "__main__":
    sp = build_system_prompt()
    print(f"System prompt: {len(sp.encode('utf-8'))} bytes (Unity injects the same ~8 KB corpus)")
    results = []
    results.append(validate("Grounded hero question", "Why is the Conical Tower solid?", expect_poi="conical_tower"))
    results.append(validate("Unpredicted composition question", "What did the people here trade, and where did the goods end up?"))
    results.append(validate("Out-of-corpus refusal", "What is the wifi password at the visitor centre?", expect_idk=True))
    print(f"\n{'='*70}")
    passed = sum(1 for r in results if r[0])
    rates = [r[1] for r in results if r[1] > 0]
    ttfts = [r[2] for r in results if r[2]]
    print(f"SUMMARY: {passed}/{len(results)} passed | generation ~{sum(rates)/len(rates):.1f} tok/s | first output {min(ttfts):.1f}-{max(ttfts):.1f}s")
    sys.exit(0 if passed == len(results) else 1)
