using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Offline guide: resolves the question by keywords, steers the camera, and
/// streams the POI's fact sheet as narration at reading speed. This is both
/// the no-LLM demo mode and the automatic fallback when the local LLM server
/// is unreachable, so the whole experience works with nothing else running.
/// </summary>
public class GZScriptedNpcBackend : GZNpcBackend
{
    [Tooltip("Narration streaming rate. ~30 chars/s reads like unhurried speech.")]
    public float charsPerSecond = 32f;
    [Tooltip("Pause before the guide 'decides' where to fly, seconds.")]
    public float thinkDelay = 0.7f;

    Coroutine _run;

    // Reading position per POI, so repeat questions ("what else?") continue
    // through the fact sheet instead of re-reading its opening.
    readonly Dictionary<string, int> _cursor = new Dictionary<string, int>();

    public override string BackendLabel => "offline guide";

    public override void Ask(GZNpcRequest request, GZNpcCallbacks callbacks)
    {
        Abort();
        _run = StartCoroutine(Run(request, callbacks));
    }

    public override void Abort()
    {
        if (_run != null) { StopCoroutine(_run); _run = null; }
    }

    IEnumerator Run(GZNpcRequest req, GZNpcCallbacks cb)
    {
        yield return new WaitForSeconds(thinkDelay);

        var poi = req.keywordGuess ?? req.currentPoi;
        var cmd = new GZTourCommand { poi = poi != null ? poi.id : "stay" };
        cb.onCommand?.Invoke(cmd);

        string narration = BuildNarration(poi, req.keywordGuess == null);
        float perChar = 1f / Mathf.Max(5f, charsPerSecond);
        int i = 0;
        while (i < narration.Length)
        {
            int step = Mathf.Min(3, narration.Length - i);   // 3-char chunks: smooth but cheap
            cb.onToken?.Invoke(narration.Substring(i, step));
            i += step;
            yield return new WaitForSeconds(perChar * step);
        }

        _run = null;
        cb.onComplete?.Invoke(true, null);
    }

    string BuildNarration(GZTourPOI poi, bool wasGuess)
    {
        if (poi == null)
            return "I know this place well — ask me about the Hill Complex, the Great Enclosure, " +
                   "the Conical Tower, the Valley Ruins, the village, or the cattle, and I will fly you there.";

        string facts = GZTourFacts.Load(poi.id);
        if (string.IsNullOrEmpty(facts))
            return (wasGuess ? "While we are here at " : "Let me take you to ") + poi.displayName +
                   " — the curated facts for this place have not been written yet, but enjoy the view while we circle it.";

        string body = facts.Replace("\n", " ").Trim();

        // Continue from where the last answer for this POI stopped.
        int start = 0;
        if (_cursor.TryGetValue(poi.id, out int saved) && saved > 0 && saved < body.Length - 100)
            start = saved;

        // Keep the lock window comfortable: cap at ~600 chars, cut at a sentence end.
        string window = body.Substring(start);
        if (window.Length > 600)
        {
            int cut = window.LastIndexOf(". ", 600);
            window = cut > 200 ? window.Substring(0, cut + 1) : window.Substring(0, 600);
        }
        int next = start + window.Length;
        _cursor[poi.id] = next >= body.Length - 100 ? 0 : next;   // wrap near the end

        string intro = start > 0
            ? "There is more to tell here. "
            : wasGuess
                ? "While we are here at " + poi.displayName + " — "
                : "Let me take you to " + poi.displayName + ". ";
        return intro + window.TrimStart();
    }
}
