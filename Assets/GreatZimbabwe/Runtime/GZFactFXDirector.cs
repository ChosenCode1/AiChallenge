using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plays the scene's reaction to the guide's words. GZTourDirector feeds it the answer
/// lifecycle (BeginAnswer / SetAnswerPoi / FeedNarration / EndAnswer); it keyword-matches
/// the streamed narration against <see cref="GZFactFXCatalog"/> and plays one cue at a
/// time as an attack/hold/release envelope written to global shader floats (FX_PLAN.md §3).
/// Bespoke FX shaders read those globals; behavior FX subscribe to CueStarted/CueEnded.
/// The universal ground ring (<see cref="GZFactFXRing"/>) pulses for every cue, so each
/// answer visibly lands even before its bespoke effect exists.
/// </summary>
public class GZFactFXDirector : MonoBehaviour
{
    [Header("Wiring (set by GZFactFXSetup)")]
    public GZTourCamera tourCamera;
    public GZFactFXRing ring;

    [Header("Behaviour")]
    [Tooltip("Pause between queued cues, seconds.")]
    public float interCueGap = 0.6f;
    [Tooltip("Most cues one answer may fire.")]
    [Range(1, 6)] public int maxFiresPerAnswer = 3;

    /// <summary>Fired when a cue's envelope starts / ends — hook behavior FX here.</summary>
    public event Action<GZFactCue> CueStarted;
    public event Action<GZFactCue> CueEnded;

    public GZFactCue ActiveCue { get; private set; }

    static readonly int PulseId = Shader.PropertyToID("_GZFX_Pulse");
    static readonly int PulseTId = Shader.PropertyToID("_GZFX_PulseT");
    static readonly int ColorId = Shader.PropertyToID("_GZFX_Color");
    static readonly int FocusId = Shader.PropertyToID("_GZFX_Focus");

    GZFactCueMatcher _matcher;
    readonly Queue<GZFactCue> _queue = new Queue<GZFactCue>();
    GZTourPOI _answerPoi;
    float _cueStart;
    float _nextCueAt;
    int _activeChannelId;

    GZFactCueMatcher Matcher
    {
        get
        {
            if (_matcher == null)
                _matcher = new GZFactCueMatcher(GZFactFXCatalog.BuildDefault(), () => Time.time)
                { maxFiresPerAnswer = maxFiresPerAnswer };
            return _matcher;
        }
    }

    void Awake() { ResetGlobals(); }

    void OnDisable()
    {
        _queue.Clear();
        if (ActiveCue != null) Finish(ActiveCue);
        ResetGlobals();
    }

    // ---------- answer lifecycle (called by GZTourDirector) ----------

    public void BeginAnswer()
    {
        _queue.Clear();
        _answerPoi = null;
        Matcher.BeginAnswer("");
    }

    public void SetAnswerPoi(GZTourPOI poi)
    {
        _answerPoi = poi;
        Enqueue(Matcher.SetPoi(poi != null ? poi.id : ""));
    }

    public void FeedNarration(string token) { Enqueue(Matcher.Feed(token)); }

    /// <summary>Answer finished or aborted: unplayed cues drop; the active one finishes naturally.</summary>
    public void EndAnswer() { _queue.Clear(); }

    // ---------- playback ----------

    void Update()
    {
        if (ActiveCue != null)
        {
            float elapsed = Time.time - _cueStart;
            if (elapsed >= ActiveCue.Duration) Finish(ActiveCue);
            else Drive(ActiveCue, elapsed);
        }
        if (ActiveCue == null && _queue.Count > 0 && Time.time >= _nextCueAt)
            Play(_queue.Dequeue());
    }

    void Enqueue(List<GZFactCue> fired)
    {
        if (fired == null) return;
        foreach (var cue in fired) _queue.Enqueue(cue);
    }

    void Play(GZFactCue cue)
    {
        ActiveCue = cue;
        _cueStart = Time.time;
        _activeChannelId = string.IsNullOrEmpty(cue.channel) ? 0 : Shader.PropertyToID(cue.channel);
        Shader.SetGlobalColor(ColorId, ThemeColor(cue.theme));
        PushFocusGlobal();
        if (ring != null) ring.BeginPulse(_answerPoi);
        Drive(cue, 0f);
        CueStarted?.Invoke(cue);
    }

    void Drive(GZFactCue cue, float elapsed)
    {
        float intensity = GZFXEnvelope.Intensity(cue, elapsed);
        Shader.SetGlobalFloat(PulseId, intensity);
        Shader.SetGlobalFloat(PulseTId, GZFXEnvelope.ExpansionT(cue, elapsed));
        if (_activeChannelId != 0) Shader.SetGlobalFloat(_activeChannelId, intensity);
        // The cattle POI focuses the live herd centroid — keep the FX centered on it.
        if (_answerPoi != null && _answerPoi.focusKind == GZFocusKind.HerdCentroid)
            PushFocusGlobal();
        if (ring != null) ring.Tick();
    }

    void Finish(GZFactCue cue)
    {
        ActiveCue = null;
        Shader.SetGlobalFloat(PulseId, 0f);
        Shader.SetGlobalFloat(PulseTId, 0f);
        if (_activeChannelId != 0) Shader.SetGlobalFloat(_activeChannelId, 0f);
        _activeChannelId = 0;
        if (ring != null) ring.EndPulse();
        _nextCueAt = Time.time + interCueGap;
        CueEnded?.Invoke(cue);
    }

    void PushFocusGlobal()
    {
        if (tourCamera == null || _answerPoi == null) return;
        Vector3 focus = tourCamera.FocusOf(_answerPoi, out float groundY);
        Shader.SetGlobalVector(FocusId,
            new Vector4(focus.x, groundY, focus.z, GZFactFXRing.RadiusFor(_answerPoi)));
    }

    void ResetGlobals()
    {
        Shader.SetGlobalFloat(PulseId, 0f);
        Shader.SetGlobalFloat(PulseTId, 0f);
        Shader.SetGlobalColor(ColorId, Color.black);
        Shader.SetGlobalVector(FocusId, Vector4.zero);
    }

    /// <summary>The fixed theme palette (FX_PLAN.md rule 3). Never hardcode FX colors elsewhere.</summary>
    public static Color ThemeColor(GZFXTheme theme)
    {
        switch (theme)
        {
            case GZFXTheme.Gold: return new Color(0.96f, 0.76f, 0.29f);
            case GZFXTheme.Ochre: return new Color(0.88f, 0.54f, 0.24f);
            case GZFXTheme.Ritual: return new Color(0.62f, 0.79f, 0.56f);
            default: return new Color(0.62f, 0.85f, 0.91f); // Ghost
        }
    }
}
