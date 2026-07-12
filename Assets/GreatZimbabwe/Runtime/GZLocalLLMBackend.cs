using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Live guide: streams answers from a local LLM server speaking the
/// OpenAI-compatible chat completions protocol (Ollama, LM Studio, llama.cpp
/// server, vLLM all qualify — just point <see cref="endpoint"/> at it).
/// The model is instructed to open with one JSON steering command line
/// ({"poi":...,"view":...,"orbit":...}) followed by spoken narration; the
/// command is parsed out and never shown. Malformed commands fall back to
/// keyword resolution, and transport errors surface via onComplete(false,...)
/// so the director can seamlessly re-ask the scripted backend.
/// </summary>
public class GZLocalLLMBackend : GZNpcBackend
{
    [Header("Server (OpenAI-compatible chat completions)")]
    [Tooltip("LM Studio default shown. Ollama: http://localhost:11434/v1/chat/completions")]
    public string endpoint = "http://localhost:1234/v1/chat/completions";
    public string model = "gemma-4-e2b-it-qat";
    [Tooltip("Optional bearer token; most local servers ignore auth.")]
    public string apiKey = "";
    [Range(0f, 1.5f)] public float temperature = 0.6f;
    public int maxTokens = 420;
    [Tooltip("Hybrid thinking models (Gemma 4 etc.) burn the whole token budget deliberating before the steering line. \"none\" makes the answer start immediately; servers that don't recognise the field ignore it.")]
    public string reasoningEffort = "none";

    [Header("Timeouts")]
    [Tooltip("Abort if the server hasn't produced any output after this many seconds.")]
    public float firstOutputTimeout = 25f;
    public int totalTimeout = 180;

    [Header("Persona")]
    [TextArea(3, 6)]
    public string personaNote =
        "You are Nyika, a warm and knowledgeable Shona guide narrating an aerial visit " +
        "over a faithful Unity recreation of Great Zimbabwe.";

    UnityWebRequest _request;
    Coroutine _run;

    public override string BackendLabel => "local LLM";

    public override void Ask(GZNpcRequest request, GZNpcCallbacks callbacks)
    {
        Abort();
        _run = StartCoroutine(Run(request, callbacks));
    }

    public override void Abort()
    {
        if (_run != null) { StopCoroutine(_run); _run = null; }
        if (_request != null) { _request.Abort(); _request.Dispose(); _request = null; }
    }

    // ---------- request ----------

    [Serializable] class Msg { public string role; public string content; }
    [Serializable] class ChatRequest
    {
        public string model;
        public bool stream;
        public float temperature;
        public int max_tokens;
        public string reasoning_effort;
        public Msg[] messages;
    }
    [Serializable] class ChunkDelta { public string content; }
    [Serializable] class ChunkChoice { public ChunkDelta delta; public string finish_reason; }
    [Serializable] class Chunk { public ChunkChoice[] choices; }

    IEnumerator Run(GZNpcRequest req, GZNpcCallbacks cb)
    {
        string body = JsonUtility.ToJson(new ChatRequest
        {
            model = model,
            stream = true,
            temperature = temperature,
            max_tokens = maxTokens,
            reasoning_effort = reasoningEffort,
            messages = new[]
            {
                new Msg { role = "system", content = BuildSystemPrompt(req) },
                new Msg { role = "user", content = BuildUserPrompt(req) },
            },
        });

        var parser = new AnswerParser(req, cb);
        var handler = new SseHandler(line => OnSseLine(line, parser));

        _request = new UnityWebRequest(endpoint, "POST")
        {
            uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
            downloadHandler = handler,
            timeout = totalTimeout,
        };
        _request.SetRequestHeader("Content-Type", "application/json");
        if (!string.IsNullOrEmpty(apiKey))
            _request.SetRequestHeader("Authorization", "Bearer " + apiKey);

        var op = _request.SendWebRequest();
        float start = Time.realtimeSinceStartup;
        while (!op.isDone)
        {
            if (!parser.SawAnyOutput && Time.realtimeSinceStartup - start > firstOutputTimeout)
            {
                _request.Abort();
                break;
            }
            yield return null;
        }

        bool transportOk = _request.result == UnityWebRequest.Result.Success;
        string error = transportOk ? null : (_request.error ?? "request failed");
        // Some servers close the stream without a [DONE]; treat received text as the answer.
        bool ok = transportOk || (parser.SawAnyOutput && parser.NarrationEmitted);
        _request.Dispose();
        _request = null;
        _run = null;

        if (ok) parser.Finish();
        cb.onComplete?.Invoke(ok, ok ? null : error);
    }

    void OnSseLine(string line, AnswerParser parser)
    {
        if (!line.StartsWith("data:")) return;
        string payload = line.Substring(5).Trim();
        if (payload.Length == 0 || payload == "[DONE]") return;
        Chunk chunk;
        try { chunk = JsonUtility.FromJson<Chunk>(payload); }
        catch { return; }
        if (chunk?.choices == null || chunk.choices.Length == 0) return;
        string token = chunk.choices[0].delta != null ? chunk.choices[0].delta.content : null;
        if (!string.IsNullOrEmpty(token)) parser.Feed(token);
    }

    // ---------- prompts ----------

    string BuildSystemPrompt(GZNpcRequest req)
    {
        var sb = new StringBuilder();
        sb.AppendLine(personaNote);
        sb.AppendLine("You are Nyika; speak in first person. Address the visitor only as \"you\". Never open the narration with a name, greeting, or vocative of any kind (not \"Nyika,\", not an invented name) — begin directly with the description.");
        sb.AppendLine("The visitor flies a camera drone; your reply BOTH steers the camera AND narrates.");
        sb.AppendLine();
        sb.AppendLine("STRICT OUTPUT FORMAT:");
        sb.AppendLine("Line 1: exactly one JSON object on a single line, no markdown fences, like");
        sb.AppendLine("{\"poi\":\"hill_complex\",\"view\":\"normal\",\"orbit\":\"normal\"}");
        sb.Append("  poi must be one of: stay");
        if (req.pois != null)
            foreach (var p in req.pois) sb.Append(", ").Append(p.id);
        sb.AppendLine(". Use \"stay\" to keep the current view.");
        sb.AppendLine("  view: \"high\" | \"normal\" | \"low\" | \"close\" — how the camera should sit. Prefer \"high\" for wide context, \"low\" only for ground activity like the cattle.");
        sb.AppendLine("  orbit: \"slow\" | \"normal\" | \"fast\".");
        sb.AppendLine("After that line: your spoken narration. 60-120 words, vivid but factual, plain prose, no markdown, no lists.");
        sb.AppendLine("Ground every claim ONLY in the facts below. If the facts don't cover the question, say so briefly and share the nearest relevant fact.");
        sb.AppendLine();
        sb.AppendLine("FACTS BY PLACE:");
        if (req.pois != null)
        {
            foreach (var p in req.pois)
            {
                string facts = GZTourFacts.Load(p.id);
                sb.Append("[").Append(p.id).Append("] ").AppendLine(p.displayName);
                if (!string.IsNullOrEmpty(facts)) sb.AppendLine(facts);
                sb.AppendLine();
            }
        }
        return sb.ToString();
    }

    static string BuildUserPrompt(GZNpcRequest req)
    {
        string current = req.currentPoi != null ? req.currentPoi.id : "overview";
        return "(camera currently at: " + current + ")\nVisitor asks: " + req.question;
    }

    // ---------- streaming answer parser ----------

    /// <summary>
    /// Splits the streamed answer into the command header (first line) and the
    /// narration. The command fires as soon as its line completes so the camera
    /// starts moving while the model is still talking.
    /// </summary>
    class AnswerParser
    {
        readonly GZNpcRequest _req;
        readonly GZNpcCallbacks _cb;
        readonly StringBuilder _header = new StringBuilder();
        bool _headerDone;

        public bool SawAnyOutput { get; private set; }
        public bool NarrationEmitted { get; private set; }

        public AnswerParser(GZNpcRequest req, GZNpcCallbacks cb) { _req = req; _cb = cb; }

        public void Feed(string token)
        {
            SawAnyOutput = true;
            if (_headerDone) { EmitNarration(token); return; }

            _header.Append(token);
            string buffered = _header.ToString();
            int nl = buffered.IndexOf('\n');
            // Safety valve: a model that ignores the protocol never sends a newline
            // early — after 350 chars give up on a header and treat it all as speech.
            if (nl < 0 && buffered.Length < 350) return;

            string headerLine = nl >= 0 ? buffered.Substring(0, nl) : buffered;
            string rest = nl >= 0 ? buffered.Substring(nl + 1) : "";
            _headerDone = true;

            var cmd = ExtractCommand(headerLine);
            if (cmd == null)
            {
                cmd = FallbackCommand();
                EmitNarration(headerLine.TrimStart());  // header was actually speech
                if (rest.Length > 0) EmitNarration(rest);
            }
            else if (rest.Length > 0) EmitNarration(rest);
            _cb.onCommand?.Invoke(cmd);
        }

        public void Finish()
        {
            if (_headerDone) return;
            // Stream ended before a newline: try the buffer as a command, else as speech.
            _headerDone = true;
            string buffered = _header.ToString();
            var cmd = ExtractCommand(buffered) ?? FallbackCommand();
            _cb.onCommand?.Invoke(cmd);
            int brace = buffered.IndexOf('}');
            string speech = ExtractCommand(buffered) != null && brace >= 0
                ? buffered.Substring(brace + 1) : buffered;
            if (!string.IsNullOrWhiteSpace(speech)) EmitNarration(speech.TrimStart());
        }

        void EmitNarration(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            NarrationEmitted = true;
            _cb.onToken?.Invoke(text);
        }

        GZTourCommand FallbackCommand()
        {
            return new GZTourCommand
            {
                poi = _req.keywordGuess != null ? _req.keywordGuess.id : "stay",
            };
        }

        static GZTourCommand ExtractCommand(string line)
        {
            int a = line.IndexOf('{');
            int b = line.LastIndexOf('}');
            if (a < 0 || b <= a) return null;
            try
            {
                var cmd = JsonUtility.FromJson<GZTourCommand>(line.Substring(a, b - a + 1));
                return string.IsNullOrEmpty(cmd.poi) ? null : cmd;
            }
            catch { return null; }
        }
    }

    /// <summary>
    /// Byte-accurate SSE line splitter. Buffers raw bytes so multi-byte UTF-8
    /// characters split across network packets never corrupt, and only decodes
    /// complete lines.
    /// </summary>
    class SseHandler : DownloadHandlerScript
    {
        readonly Action<string> _onLine;
        readonly List<byte> _buffer = new List<byte>(4096);

        public SseHandler(Action<string> onLine) : base(new byte[8192]) { _onLine = onLine; }

        protected override bool ReceiveData(byte[] data, int dataLength)
        {
            if (data == null || dataLength <= 0) return false;
            for (int i = 0; i < dataLength; i++)
            {
                byte b = data[i];
                if (b == (byte)'\n')
                {
                    if (_buffer.Count > 0)
                    {
                        string line = Encoding.UTF8.GetString(_buffer.ToArray()).TrimEnd('\r');
                        _buffer.Clear();
                        if (line.Length > 0) _onLine(line);
                    }
                }
                else _buffer.Add(b);
            }
            return true;
        }

        protected override void CompleteContent()
        {
            if (_buffer.Count == 0) return;
            string line = Encoding.UTF8.GetString(_buffer.ToArray()).TrimEnd('\r');
            _buffer.Clear();
            if (line.Length > 0) _onLine(line);
        }
    }
}
