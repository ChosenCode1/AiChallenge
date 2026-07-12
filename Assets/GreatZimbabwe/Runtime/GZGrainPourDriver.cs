using UnityEngine;

/// <summary>
/// Behavior FX: plays the golden grain-pour ParticleSystem while the granary cue is live
/// — in Shona tradition a ruler shows generosity through his granary, so when the guide
/// speaks that fact, grain spills gently from the real tower's rim. Emission simply
/// starts on CueStarted and stops (letting live particles die out) on CueEnded; the
/// particles' own color-over-lifetime does the fading. Clearly spectral, never literal:
/// the granary reading is the favored interpretation, not a certainty (FX_PLAN rule 2).
/// </summary>
public class GZGrainPourDriver : MonoBehaviour
{
    [Header("Wiring (set by GZFactFXSetup)")]
    public GZFactFXDirector fx;
    public ParticleSystem grain;

    [Tooltip("Cue that opens the granary.")]
    public string cueId = "ct_granary";

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
        if (grain != null) grain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    void OnCueStarted(GZFactCue cue)
    {
        if (cue.id == cueId && grain != null) grain.Play(true);
    }

    void OnCueEnded(GZFactCue cue)
    {
        if (cue.id == cueId && grain != null)
            grain.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }
}
