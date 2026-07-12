using UnityEngine;

/// <summary>
/// Behavior FX: the live herd visibly quickens while the guide describes the seasonal
/// movement between pastures, then settles back. Eases GZHerdRunner.speedMultiplier
/// toward a boost while the cue plays — words directly driving the animals that are
/// already on screen. Always restores fully (CueEnded and OnDisable both reset).
/// </summary>
public class GZHerdBehaviorFX : MonoBehaviour
{
    [Header("Wiring (set by GZFactFXSetup)")]
    public GZFactFXDirector fx;
    public GZHerdRunner herd;

    [Tooltip("Cue that stirs the herd.")]
    public string cueId = "ca_seasonal";
    public float boost = 1.6f;
    [Tooltip("How fast the multiplier eases toward its target, per second.")]
    public float easeRate = 0.6f;

    float _target = 1f;

    void OnEnable()
    {
        if (fx == null) return;
        fx.CueStarted += OnCueStarted;
        fx.CueEnded += OnCueEnded;
    }

    void OnDisable()
    {
        if (fx != null)
        {
            fx.CueStarted -= OnCueStarted;
            fx.CueEnded -= OnCueEnded;
        }
        _target = 1f;
        if (herd != null) herd.speedMultiplier = 1f;
    }

    void OnCueStarted(GZFactCue cue) { if (cue.id == cueId) _target = boost; }
    void OnCueEnded(GZFactCue cue) { if (cue.id == cueId) _target = 1f; }

    void Update()
    {
        if (herd == null || !Application.isPlaying) return;
        herd.speedMultiplier =
            Mathf.MoveTowards(herd.speedMultiplier, _target, easeRate * Time.deltaTime);
    }
}
