using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// EditMode tests for the Fact-FX trigger spine (FX_PLAN.md Phase 1) and the fact-sheet
/// loader. The matcher and envelope are pure C# with an injected clock — no play mode,
/// no scene, fully deterministic.
/// </summary>
public class GZFactFXTests
{
    static GZFactCue Cue(string id, string poi, params string[] kws)
    {
        return new GZFactCue { id = id, poiId = poi, keywords = kws };
    }

    // ---------- cue matcher ----------

    [Test]
    public void Matcher_FiresWhenKeywordSpansStreamedChunks()
    {
        var m = new GZFactCueMatcher(new[] { Cue("c1", "great_enclosure", "mortar") }, () => 0f);
        m.BeginAnswer("great_enclosure");
        Assert.IsNull(m.Feed("built with no mor"), "half a keyword must not fire");
        var fired = m.Feed("tar at all");
        Assert.IsNotNull(fired, "keyword completed across chunks must fire");
        Assert.AreEqual("c1", fired[0].id);
    }

    [Test]
    public void Matcher_ScopesCuesToTheAnswerPoi()
    {
        var m = new GZFactCueMatcher(new[] { Cue("c1", "conical_tower", "grain") }, () => 0f);
        m.BeginAnswer("cattle");
        Assert.IsNull(m.Feed("grain stores fed the court"),
            "a tower cue must not fire while the answer is about the cattle");
    }

    [Test]
    public void Matcher_GlobalCueFiresAtAnyPoi()
    {
        var m = new GZFactCueMatcher(new[] { Cue("c1", "", "sacred") }, () => 0f);
        m.BeginAnswer("cattle");
        Assert.IsNotNull(m.Feed("it remains a sacred place"));
    }

    [Test]
    public void Matcher_FiresEachCueOncePerAnswer()
    {
        var m = new GZFactCueMatcher(new[] { Cue("c1", "", "mortar") }, () => 0f);
        m.BeginAnswer("x");
        Assert.IsNotNull(m.Feed("no mortar here"));
        Assert.IsNull(m.Feed(" and again no mortar there"), "same cue must not refire in one answer");
    }

    [Test]
    public void Matcher_CooldownBlocksRefireAcrossAnswersThenExpires()
    {
        float now = 0f;
        var cue = Cue("c1", "", "mortar");
        cue.cooldownSeconds = 40f;
        var m = new GZFactCueMatcher(new[] { cue }, () => now);

        m.BeginAnswer("x");
        Assert.IsNotNull(m.Feed("mortar"), "first answer fires");

        now = 10f;
        m.BeginAnswer("x");
        Assert.IsNull(m.Feed("mortar"), "10s later the 40s cooldown still blocks");

        now = 50f;
        m.BeginAnswer("x");
        Assert.IsNotNull(m.Feed("mortar"), "after the cooldown it fires again");
    }

    [Test]
    public void Matcher_CapsFiresPerAnswer()
    {
        var m = new GZFactCueMatcher(
            new[] { Cue("a", "", "alpha"), Cue("b", "", "beta"), Cue("c", "", "gamma") },
            () => 0f)
        { maxFiresPerAnswer = 2 };
        m.BeginAnswer("x");
        var fired = m.Feed("alpha beta gamma");
        Assert.IsNotNull(fired);
        Assert.AreEqual(2, fired.Count, "an answer never fires more than the cap");
    }

    [Test]
    public void Matcher_PoiResolvedAfterNarrationStillMatches()
    {
        // The LLM protocol can emit a few narration words before its steering command
        // parses — the matcher must rescan once the POI is known.
        var m = new GZFactCueMatcher(new[] { Cue("c1", "great_enclosure", "mortar") }, () => 0f);
        m.BeginAnswer("");
        Assert.IsNull(m.Feed("no mortar anywhere"), "POI unknown: scoped cue must wait");
        var fired = m.SetPoi("great_enclosure");
        Assert.IsNotNull(fired, "late-resolved POI must rescan the buffered narration");
        Assert.AreEqual("c1", fired[0].id);
    }

    [Test]
    public void Matcher_IsCaseInsensitiveToNarration()
    {
        var m = new GZFactCueMatcher(new[] { Cue("c1", "", "mortar") }, () => 0f);
        m.BeginAnswer("x");
        Assert.IsNotNull(m.Feed("No MORTAR at all"));
    }

    // ---------- envelope ----------

    [Test]
    public void Envelope_RisesHoldsAndReleases()
    {
        var cue = new GZFactCue { attack = 1f, hold = 2f, release = 1f };
        Assert.AreEqual(0f, GZFXEnvelope.Intensity(cue, 0f), 1e-4f);
        Assert.Greater(GZFXEnvelope.Intensity(cue, 0.5f), 0.4f);
        Assert.AreEqual(1f, GZFXEnvelope.Intensity(cue, 1.5f), 1e-4f, "hold phase is full intensity");
        Assert.Less(GZFXEnvelope.Intensity(cue, 3.5f), 0.6f);
        Assert.AreEqual(0f, GZFXEnvelope.Intensity(cue, 4f), 1e-4f);
        Assert.AreEqual(0f, GZFXEnvelope.Intensity(cue, 99f), 1e-4f);
    }

    [Test]
    public void Envelope_ExpansionIsMonotonicAndCompletes()
    {
        var cue = new GZFactCue { attack = 1f, hold = 2f, release = 1f };
        float prev = -1f;
        for (int i = 0; i <= 20; i++)
        {
            float v = GZFXEnvelope.ExpansionT(cue, cue.Duration * i / 20f);
            Assert.GreaterOrEqual(v, prev, "expansion must never move backwards");
            prev = v;
        }
        Assert.AreEqual(1f, prev, 1e-3f, "expansion must reach the full radius");
    }

    // ---------- catalogue sanity ----------

    [Test]
    public void Catalog_IsWellFormed()
    {
        var knownPois = new HashSet<string>
        {
            "", "overview", "hill_complex", "great_enclosure", "conical_tower",
            "valley_ruins", "karanga_village", "east_ruins", "cattle"
        };
        var cues = GZFactFXCatalog.BuildDefault();
        Assert.Greater(cues.Count, 20, "catalogue should cover all eight POIs");

        var ids = new HashSet<string>();
        foreach (var cue in cues)
        {
            Assert.IsFalse(string.IsNullOrEmpty(cue.id), "cue without id");
            Assert.IsTrue(ids.Add(cue.id), "duplicate cue id: " + cue.id);
            Assert.IsTrue(knownPois.Contains(cue.poiId), cue.id + " references unknown POI '" + cue.poiId + "'");
            Assert.IsNotNull(cue.keywords, cue.id + " has null keywords");
            Assert.Greater(cue.keywords.Length, 0, cue.id + " has no keywords");
            foreach (var kw in cue.keywords)
                Assert.AreEqual(kw.ToLowerInvariant(), kw, cue.id + " keyword must be lowercase: '" + kw + "'");
            if (!string.IsNullOrEmpty(cue.channel))
                StringAssert.StartsWith("_GZFX_", cue.channel, cue.id + " channel must use the _GZFX_ prefix");
            Assert.Greater(cue.Duration, 1f, cue.id + " envelope too short to see");
        }
    }

    // ---------- fact-sheet loader (handoff checklist item 3) ----------

    static readonly string[] AllPoiIds =
    {
        "overview", "hill_complex", "great_enclosure", "conical_tower",
        "valley_ruins", "karanga_village", "east_ruins", "cattle"
    };

    [Test]
    public void TourFacts_LoadsEverySheetAndStripsEditorialLines()
    {
        foreach (var id in AllPoiIds)
        {
            string facts = GZTourFacts.Load(id);
            Assert.IsNotEmpty(facts, "fact sheet missing or empty: " + id);
            foreach (var line in facts.Split('\n'))
                Assert.IsFalse(line.TrimStart().StartsWith("#"),
                    id + " leaked an editorial # line: " + line);
        }
    }

    [Test]
    public void TourFacts_UnknownPoiReturnsEmpty()
    {
        Assert.AreEqual("", GZTourFacts.Load("nonexistent_poi"));
        Assert.AreEqual("", GZTourFacts.Load(null));
        Assert.AreEqual("", GZTourFacts.Load(""));
    }
}
