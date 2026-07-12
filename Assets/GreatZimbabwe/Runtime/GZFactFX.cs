using System;
using System.Collections.Generic;

/// <summary>
/// Visual theme of a fact cue. Themes map to fixed colors (see
/// <see cref="GZFactFXDirector.ThemeColor"/>) so meaning stays consistent everywhere:
/// Ghost = interpretation / architecture study, Gold = wealth / trade,
/// Ochre = daily life / daga, Ritual = the sacred and the birds.
/// </summary>
public enum GZFXTheme { Ghost, Gold, Ochre, Ritual }

/// <summary>
/// One narration-triggered scene moment: when the guide's streamed answer contains any of
/// the keywords while the tour is flying the matching POI, the FX director plays an
/// attack/hold/release envelope on the global shader channels (FX_PLAN.md §3).
/// This file is deliberately free of UnityEngine so the matcher, envelope, and catalogue
/// stay unit-testable as plain C# (see GZFactFXTests).
/// </summary>
[Serializable]
public class GZFactCue
{
    public string id;
    /// <summary>POI this cue belongs to; "" fires at any POI.</summary>
    public string poiId = "";
    /// <summary>Lowercase substrings searched in the streamed narration.</summary>
    public string[] keywords;
    public GZFXTheme theme = GZFXTheme.Ghost;
    /// <summary>Optional extra shader global (FX_PLAN.md channel registry); "" = ring pulse only.</summary>
    public string channel = "";
    public float attack = 1.0f;
    public float hold = 5.0f;
    public float release = 3.0f;
    public float cooldownSeconds = 40f;
    public float Duration => attack + hold + release;
}

/// <summary>Attack/hold/release envelope math for a playing cue.</summary>
public static class GZFXEnvelope
{
    /// <summary>Intensity 0→1→0 over the cue's lifetime; what _GZFX_Pulse carries.</summary>
    public static float Intensity(GZFactCue cue, float elapsed)
    {
        if (cue == null || elapsed <= 0f) return 0f;
        if (elapsed >= cue.Duration) return 0f;
        if (elapsed < cue.attack) return Smooth01(elapsed / Max(cue.attack, 1e-4f));
        if (elapsed < cue.attack + cue.hold) return 1f;
        return 1f - Smooth01((elapsed - cue.attack - cue.hold) / Max(cue.release, 1e-4f));
    }

    /// <summary>Eased 0→1 over the cue's lifetime; what _GZFX_PulseT carries (expansions, sweeps).</summary>
    public static float ExpansionT(GZFactCue cue, float elapsed)
    {
        if (cue == null) return 0f;
        float t = Clamp01(elapsed / Max(cue.Duration, 1e-4f));
        float inv = 1f - t;
        return 1f - inv * inv * inv; // ease-out cubic: energy dissipating outward
    }

    static float Smooth01(float x) { x = Clamp01(x); return x * x * (3f - 2f * x); }
    static float Clamp01(float x) => x < 0f ? 0f : (x > 1f ? 1f : x);
    static float Max(float a, float b) => a > b ? a : b;
}

/// <summary>
/// Keyword-spots the guide's streamed narration and decides which cues fire. Pure logic:
/// the clock is injected so cooldowns are testable without play mode. Semantics: a cue
/// fires at most once per answer, respects its cooldown across answers, only matches when
/// the answer's POI matches (or the cue is global), and the whole answer fires at most
/// <see cref="maxFiresPerAnswer"/> cues so a 60–120 word reply never becomes a light show.
/// </summary>
public class GZFactCueMatcher
{
    readonly List<GZFactCue> _cues = new List<GZFactCue>();
    readonly Func<float> _now;
    readonly Dictionary<string, float> _lastFireTime = new Dictionary<string, float>();
    readonly HashSet<string> _firedThisAnswer = new HashSet<string>();
    readonly int _maxKeywordLength = 1;

    string _poiId = "";
    string _buffer = "";
    int _scanned;
    int _firesThisAnswer;

    public int maxFiresPerAnswer = 3;

    public GZFactCueMatcher(IEnumerable<GZFactCue> cues, Func<float> now)
    {
        _now = now ?? (() => 0f);
        if (cues == null) return;
        foreach (var cue in cues)
        {
            if (cue == null || cue.keywords == null || cue.keywords.Length == 0) continue;
            _cues.Add(cue);
            foreach (var kw in cue.keywords)
                if (kw != null && kw.Length > _maxKeywordLength) _maxKeywordLength = kw.Length;
        }
    }

    public void BeginAnswer(string poiId)
    {
        _poiId = poiId ?? "";
        _buffer = "";
        _scanned = 0;
        _firedThisAnswer.Clear();
        _firesThisAnswer = 0;
    }

    /// <summary>
    /// The answer's POI can resolve after narration has started (the LLM protocol may emit a
    /// few words before its steering command parses), so this rescans what already streamed.
    /// Returns newly fired cues, or null.
    /// </summary>
    public List<GZFactCue> SetPoi(string poiId)
    {
        poiId = poiId ?? "";
        if (poiId == _poiId) return null;
        _poiId = poiId;
        _scanned = 0;
        return Scan();
    }

    /// <summary>Feed a streamed narration fragment. Returns newly fired cues, or null.</summary>
    public List<GZFactCue> Feed(string text)
    {
        if (string.IsNullOrEmpty(text) || _buffer.Length > 6000) return null;
        _buffer += text.ToLowerInvariant();
        return Scan();
    }

    List<GZFactCue> Scan()
    {
        if (_buffer.Length == 0) return null;
        // Overlap the already-scanned region so a keyword split across two streamed
        // chunks ("no mor" + "tar") is still seen exactly once.
        int from = _scanned - (_maxKeywordLength - 1);
        if (from < 0) from = 0;

        List<GZFactCue> fired = null;
        foreach (var cue in _cues)
        {
            if (_firesThisAnswer >= maxFiresPerAnswer) break;
            if (cue.poiId.Length > 0 && cue.poiId != _poiId) continue;
            if (_firedThisAnswer.Contains(cue.id)) continue;
            if (_lastFireTime.TryGetValue(cue.id, out float last) &&
                _now() - last < cue.cooldownSeconds) continue;

            foreach (var kw in cue.keywords)
            {
                if (string.IsNullOrEmpty(kw)) continue;
                if (_buffer.IndexOf(kw, from, StringComparison.Ordinal) < 0) continue;
                _firedThisAnswer.Add(cue.id);
                _lastFireTime[cue.id] = _now();
                _firesThisAnswer++;
                if (fired == null) fired = new List<GZFactCue>(2);
                fired.Add(cue);
                break;
            }
        }
        _scanned = _buffer.Length;
        return fired;
    }
}

/// <summary>
/// The cue catalogue: which spoken facts trigger which scene moments. Keywords are tuned to
/// the vocabulary of Resources/GZTourFacts/*.txt — the LLM grounds its answers there, so
/// these words reliably appear in its narration (and always appear in the offline guide's,
/// which streams the sheets verbatim). Channels are reserved in FX_PLAN.md §3; a cue with
/// an unimplemented channel still pulses the universal ring, so every answer reacts today.
/// </summary>
public static class GZFactFXCatalog
{
    public static List<GZFactCue> BuildDefault()
    {
        var cues = new List<GZFactCue>
        {
            // ---- overview ----
            Cue("ov_trade", "overview", GZFXTheme.Gold, "_GZFX_TradeThread",
                "gold", "ivory", "indian ocean", "porcelain", "trade"),
            Cue("ov_population", "overview", GZFXTheme.Ochre, "_GZFX_Hearths",
                "ten thousand", "daga"),
            Cue("ov_tribute", "overview", GZFXTheme.Gold, "",
                "tribute", "150", "minor zimbabwe", "settlements"),
            Cue("ov_stone", "overview", GZFXTheme.Ghost, "",
                "mortar", "dry-stone", "dry stone", "granite"),
            Cue("ov_heritage", "overview", GZFXTheme.Ritual, "",
                "unesco", "world heritage", "sacred", "independence"),

            // ---- hill complex ----
            Cue("hc_birds", "hill_complex", GZFXTheme.Ritual, "_GZFX_SoapstoneBirds",
                "soapstone", "zimbabwe bird", "birds", "steatite"),
            Cue("hc_boulders", "hill_complex", GZFXTheme.Ghost, "",
                "boulder", "worked with the hill", "passageway"),
            Cue("hc_walls", "hill_complex", GZFXTheme.Ghost, "",
                "nine meters", "turret", "monolith"),
            Cue("hc_cave", "hill_complex", GZFXTheme.Ritual, "",
                "cave", "sacred"),

            // ---- great enclosure ----
            Cue("ge_no_mortar", "great_enclosure", GZFXTheme.Ghost, "_GZFX_WallAssembly",
                "mortar", "dry-laid", "dry stone", "granite blocks", "courses"),
            Cue("ge_chevron", "great_enclosure", GZFXTheme.Ghost, "_GZFX_Chevron",
                "chevron"),
            Cue("ge_engineering", "great_enclosure", GZFXTheme.Ghost, "",
                "leans", "drainage", "drain"),
            Cue("ge_daga", "great_enclosure", GZFXTheme.Ochre, "",
                "daga", "family", "kitchen"),
            Cue("ge_scale", "great_enclosure", GZFXTheme.Ghost, "",
                "250 meters", "largest single", "ten meters high"),

            // ---- conical tower ----
            Cue("ct_solid", "conical_tower", GZFXTheme.Ghost, "_GZFX_TowerScan",
                "completely solid", "no door", "solid drum", "no chamber", "stair"),
            Cue("ct_granary", "conical_tower", GZFXTheme.Gold, "_GZFX_GrainPour",
                "grain", "granary", "generosity", "distribut"),
            Cue("ct_approach", "conical_tower", GZFXTheme.Ghost, "",
                "narrow passage", "reveals itself", "approach"),

            // ---- valley ruins ----
            Cue("vr_finds", "valley_ruins", GZFXTheme.Gold, "_GZFX_TradeThread",
                "porcelain", "beads", "copper", "ingot"),
            Cue("vr_daga", "valley_ruins", GZFXTheme.Ochre, "_GZFX_GhostVillage",
                "daga", "clay", "thatched"),
            Cue("vr_population", "valley_ruins", GZFXTheme.Ochre, "_GZFX_Hearths",
                "ten thousand", "eighteen", "twenty thousand", "thousand"),

            // ---- karanga village ----
            Cue("kv_build", "karanga_village", GZFXTheme.Ochre, "_GZFX_HutAssembly",
                "clay", "wooden frames", "granitic", "pole"),
            Cue("kv_hearth", "karanga_village", GZFXTheme.Ochre, "_GZFX_Hearths",
                "hearth", "pot-stand", "bench"),
            Cue("kv_compound", "karanga_village", GZFXTheme.Ochre, "",
                "kitchen", "compound", "court"),
            Cue("kv_honesty", "karanga_village", GZFXTheme.Ghost, "",
                "modern", "recreat", "model"),

            // ---- east ruins ----
            Cue("er_sprawl", "east_ruins", GZFXTheme.Ghost, "_GZFX_BoundarySweep",
                "hectare", "square kilometer", "730", "sprawl"),
            Cue("er_household", "east_ruins", GZFXTheme.Ochre, "_GZFX_WallAssembly",
                "household", "family", "compound"),
            Cue("er_vanished", "east_ruins", GZFXTheme.Ochre, "",
                "little remains", "above ground"),

            // ---- cattle ----
            Cue("ca_wealth", "cattle", GZFXTheme.Gold, "_GZFX_HerdGold",
                "wealth", "status", "dowry", "walking"),
            Cue("ca_seasonal", "cattle", GZFXTheme.Ochre, "_GZFX_RouteGlow",
                "seasonal", "pasture", "moved"),
            Cue("ca_economy", "cattle", GZFXTheme.Ochre, "",
                "barter", "marketplace", "farming", "everyday"),
            Cue("ca_power", "cattle", GZFXTheme.Gold, "",
                "before gold", "elites", "power"),
        };

        // Hero-cue timing overrides: block-assembly effects need a long attack so the
        // courses land readably instead of snapping together in one second.
        foreach (var cue in cues)
        {
            switch (cue.id)
            {
                case "ge_no_mortar": cue.attack = 3.5f; cue.hold = 4.5f; cue.release = 2.5f; break;
                case "ge_chevron": cue.attack = 2.2f; cue.hold = 4f; cue.release = 2f; break;
                case "er_household": cue.attack = 3f; cue.hold = 4f; cue.release = 2.5f; break;
                case "ct_solid": cue.attack = 4f; cue.hold = 3.5f; cue.release = 2.5f; break;      // slow scan sweep
                case "ct_granary": cue.attack = 1.5f; cue.hold = 6f; cue.release = 2.5f; break;    // long pour
                case "ca_seasonal": cue.attack = 1.5f; cue.hold = 6f; cue.release = 2.5f; break;   // herd quickens
            }
        }
        return cues;
    }

    static GZFactCue Cue(string id, string poiId, GZFXTheme theme, string channel,
                         params string[] keywords)
    {
        return new GZFactCue
        {
            id = id,
            poiId = poiId,
            theme = theme,
            channel = channel,
            keywords = keywords,
        };
    }
}
