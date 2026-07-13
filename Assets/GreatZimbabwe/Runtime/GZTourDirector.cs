using System.Collections.Generic;
using UnityEngine;

public enum GZTourState
{
    /// <summary>Cruising; the visitor may ask a question.</summary>
    Idle,
    /// <summary>A question is being answered — input is locked until the guide finishes.</summary>
    Answering,
}

/// <summary>
/// The tour's state machine and single owner of the ask/answer contract:
/// one question at a time, input locked from the moment a question is accepted
/// until the guide's narration completes, then unlocked again. Prefers the
/// local LLM backend and silently falls back to the scripted guide if the LLM
/// server is unreachable. After a configurable idle period on a POI the camera
/// drifts back to the overview grand tour.
/// </summary>
public class GZTourDirector : MonoBehaviour
{
    [Header("Wiring (set by GZTourSetup)")]
    public GZTourCamera tourCamera;
    public GZTourUI ui;
    public GZScriptedNpcBackend scriptedBackend;
    public GZLocalLLMBackend llmBackend;
    [Tooltip("Optional narration-synced scene FX (set by GZFactFXSetup).")]
    public GZFactFXDirector fxDirector;

    [Header("Behaviour")]
    [Tooltip("Try the local LLM first; on failure the scripted guide answers instead.")]
    public bool preferLocalLLM = true;
    [Tooltip("Seconds of silence on a POI before the camera resumes the grand tour. 0 = never.")]
    public float idleResumeSeconds = 45f;
    public string overviewPoiId = "overview";

    [Header("Points of interest")]
    public List<GZTourPOI> pois = new List<GZTourPOI>();

    public GZTourState State { get; private set; } = GZTourState.Idle;

    GZNpcBackend _activeBackend;
    bool _fellBack;
    string _question;
    float _lastAnswerEnd = -1f;
    bool _awaitingArrival;

    // Short conversation memory so follow-ups ("what else?") have context.
    // Kept small: enough for a follow-up chain, bounded prompt cost.
    const int MaxHistoryTurns = 4;
    const int MaxTurnChars = 700;
    readonly List<GZNpcTurn> _history = new List<GZNpcTurn>();
    readonly System.Text.StringBuilder _narration = new System.Text.StringBuilder();
    string _lastSteer;
    GZTourPOI _answerPoi;

    GZTourPOI Overview => FindPoi(overviewPoiId);

    void Start()
    {
        if (tourCamera != null)
            tourCamera.Configure(pois, Overview, FindFirstObjectByType<GZHerdRunner>());
        if (ui != null)
        {
            ui.SetLocked(false);
            ui.SetStatus("Ask the guide — your questions steer the flight", GZTourUI.StatusTone.Ready);
        }
    }

    void OnDisable()
    {
        if (_activeBackend != null) _activeBackend.Abort();
        if (fxDirector != null) fxDirector.EndAnswer();
    }

    void Update()
    {
        if (_awaitingArrival && tourCamera != null && tourCamera.OnStation)
        {
            _awaitingArrival = false;
            if (ui != null && tourCamera.CurrentPoi != null)
                ui.SetDestination("Orbiting: " + tourCamera.CurrentPoi.displayName);
        }

        if (State != GZTourState.Idle || idleResumeSeconds <= 0f || _lastAnswerEnd < 0f) return;
        bool onOverview = tourCamera == null || tourCamera.CurrentPoi == null ||
                          tourCamera.CurrentPoi.id == overviewPoiId;
        if (!onOverview && Time.time - _lastAnswerEnd > idleResumeSeconds)
        {
            _lastAnswerEnd = -1f;
            tourCamera.ResumeGrandTour();
            _awaitingArrival = true;
            if (ui != null) ui.SetDestination("Resuming the grand tour");
        }
    }

    /// <summary>
    /// Entry point for the UI. Returns false (and does nothing) while a previous
    /// answer is still playing — that refusal IS the question lock.
    /// </summary>
    public bool AskQuestion(string question)
    {
        if (State != GZTourState.Idle || string.IsNullOrWhiteSpace(question)) return false;

        State = GZTourState.Answering;
        _question = question.Trim();
        _fellBack = false;

        if (ui != null)
        {
            ui.SetLocked(true);
            ui.ShowQuestion(_question);
        }

        _activeBackend = (preferLocalLLM && llmBackend != null) ? (GZNpcBackend)llmBackend : scriptedBackend;
        if (_activeBackend == null) _activeBackend = scriptedBackend;
        DispatchTo(_activeBackend);
        return true;
    }

    /// <summary>Public steering hook — also usable directly (debug consoles, future gestures).</summary>
    public void ApplyCommand(GZTourCommand cmd)
    {
        if (cmd == null || tourCamera == null) return;
        GZTourPOI poi = null;
        if (!string.IsNullOrEmpty(cmd.poi) && cmd.poi != "stay") poi = FindPoi(cmd.poi);
        if (poi == null && cmd.poi != "stay")
            poi = GZKeywordResolver.Resolve(_question, pois);
        if (poi == null) poi = tourCamera.CurrentPoi ?? Overview;
        if (poi == null) return;

        tourCamera.FlyTo(poi, cmd.view, cmd.orbit);
        _awaitingArrival = true;
        _answerPoi = poi;
        if (fxDirector != null) fxDirector.SetAnswerPoi(poi);
        if (ui != null) ui.SetDestination("En route: " + poi.displayName);
    }

    public GZTourPOI FindPoi(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var p in pois)
            if (p != null && p.id == id) return p;
        return null;
    }

    // ---------- backend plumbing ----------

    void DispatchTo(GZNpcBackend backend)
    {
        _narration.Length = 0;   // a fallback re-ask starts a fresh answer
        _lastSteer = null;
        if (fxDirector != null) fxDirector.BeginAnswer();
        if (ui != null)
            ui.SetStatus("The guide is thinking (" + backend.BackendLabel + ")…", GZTourUI.StatusTone.Busy);

        backend.Ask(BuildRequest(), new GZNpcCallbacks
        {
            onCommand = HandleCommand,
            onToken = HandleToken,
            onComplete = HandleComplete,
        });
    }

    GZNpcRequest BuildRequest()
    {
        return new GZNpcRequest
        {
            question = _question,
            pois = pois,
            keywordGuess = GZKeywordResolver.Resolve(_question, pois),
            currentPoi = tourCamera != null ? tourCamera.CurrentPoi : null,
            history = new List<GZNpcTurn>(_history),
        };
    }

    void HandleCommand(GZTourCommand cmd)
    {
        ApplyCommand(cmd);
        if (cmd != null)
            _lastSteer = "{\"poi\":\"" + (string.IsNullOrEmpty(cmd.poi) ? "stay" : cmd.poi) +
                         "\",\"view\":\"" + (string.IsNullOrEmpty(cmd.view) ? "normal" : cmd.view) +
                         "\",\"orbit\":\"" + (string.IsNullOrEmpty(cmd.orbit) ? "normal" : cmd.orbit) + "\"}";
        if (ui != null)
            ui.SetStatus("The guide is speaking — questions unlock when it finishes", GZTourUI.StatusTone.Busy);
    }

    void HandleToken(string token)
    {
        _narration.Append(token);
        if (ui != null) ui.AppendAnswer(token);
        if (fxDirector != null) fxDirector.FeedNarration(token);
    }

    void HandleComplete(bool success, string error)
    {
        if (!success && !_fellBack && _activeBackend == llmBackend && scriptedBackend != null)
        {
            _fellBack = true;
            _activeBackend = scriptedBackend;
            if (ui != null)
                ui.SetStatus("Local LLM unreachable (" + (error ?? "no response") + ") — offline guide answering", GZTourUI.StatusTone.Warn);
            DispatchTo(scriptedBackend);
            return;
        }

        if (success && _narration.Length > 0)
        {
            string said = _narration.ToString().Trim();
            if (said.Length > MaxTurnChars) said = said.Substring(0, MaxTurnChars);
            _history.Add(new GZNpcTurn { question = _question, steer = _lastSteer, narration = said });
            if (_history.Count > MaxHistoryTurns) _history.RemoveAt(0);

            // Every fact the guide can state is curated and attributed —
            // show the answer's sources under the narration.
            var citedPoi = _answerPoi ?? (tourCamera != null ? tourCamera.CurrentPoi : null);
            string sources = citedPoi != null ? GZTourFacts.Sources(citedPoi.id) : "";
            if (ui != null && sources.Length > 0)
                ui.AppendAnswer("\n\nSources: " + sources);
        }

        State = GZTourState.Idle;
        _lastAnswerEnd = Time.time;
        if (fxDirector != null) fxDirector.EndAnswer();
        if (ui != null)
        {
            ui.SetLocked(false);
            ui.SetStatus("Ask the guide — your questions steer the flight", GZTourUI.StatusTone.Ready);
        }
    }
}
