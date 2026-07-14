using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// EditMode tests for the streaming answer parser — the seam between the model's
/// raw output and everything the player sees. Locks in the protocol contract and
/// the regressions found live (steering-JSON echo, glued-header narration, header
/// safety valve): docs/llm-e2e-verification.md gaps 4 and 5.
/// </summary>
public class GZAnswerParserTests
{
    class Capture
    {
        public readonly List<GZTourCommand> commands = new List<GZTourCommand>();
        public string narration = "";
        public readonly GZNpcCallbacks callbacks;

        public Capture()
        {
            callbacks = new GZNpcCallbacks
            {
                onCommand = c => commands.Add(c),
                onToken = t => narration += t,
            };
        }
    }

    static GZNpcRequest Req(string keywordGuessId = null)
    {
        return new GZNpcRequest
        {
            question = "test question",
            keywordGuess = keywordGuessId == null ? null : new GZTourPOI { id = keywordGuessId },
        };
    }

    // ---------- the happy path ----------

    [Test]
    public void Parser_SplitsCommandHeaderFromNarration()
    {
        var cap = new Capture();
        var p = new GZLocalLLMBackend.AnswerParser(Req(), cap.callbacks);
        p.Feed("{\"poi\":\"conical_tower\",\"view\":\"close\",\"orbit\":\"slow\"}\n");
        p.Feed("The tower is solid granite.");
        p.Finish();

        Assert.AreEqual(1, cap.commands.Count, "exactly one steering command");
        Assert.AreEqual("conical_tower", cap.commands[0].poi);
        Assert.AreEqual("close", cap.commands[0].view);
        Assert.AreEqual("The tower is solid granite.", cap.narration);
    }

    [Test]
    public void Parser_HandlesTokenByTokenStreaming()
    {
        var cap = new Capture();
        var p = new GZLocalLLMBackend.AnswerParser(Req(), cap.callbacks);
        const string answer = "{\"poi\":\"hill_complex\"}\nWalls thread between boulders.";
        foreach (char c in answer) p.Feed(c.ToString());
        p.Finish();

        Assert.AreEqual(1, cap.commands.Count, "command must fire once despite 1-char tokens");
        Assert.AreEqual("hill_complex", cap.commands[0].poi);
        Assert.AreEqual("Walls thread between boulders.", cap.narration);
    }

    // ---------- documented live gaps (llm-e2e-verification.md) ----------

    [Test]
    public void Parser_KeepsNarrationGluedToTheCommandLine()
    {
        // Gap 5: model emits narration on the SAME line as the JSON, no newline.
        var cap = new Capture();
        var p = new GZLocalLLMBackend.AnswerParser(Req(), cap.callbacks);
        p.Feed("{\"poi\":\"great_enclosure\"} The wall curves for 250 metres.\n");
        p.Feed("It was built without mortar.");
        p.Finish();

        Assert.AreEqual("great_enclosure", cap.commands[0].poi);
        StringAssert.StartsWith("The wall curves", cap.narration, "glued speech must not be discarded");
        StringAssert.Contains("without mortar", cap.narration);
    }

    [Test]
    public void Parser_GluedHeaderWithNoNewlineAtAllStillYieldsBoth()
    {
        // Gap 5 variant: short greeting answer, stream ends before any newline.
        var cap = new Capture();
        var p = new GZLocalLLMBackend.AnswerParser(Req(), cap.callbacks);
        p.Feed("{\"poi\":\"overview\"} Welcome, traveller.");
        p.Finish();

        Assert.AreEqual(1, cap.commands.Count);
        Assert.AreEqual("overview", cap.commands[0].poi);
        Assert.AreEqual("Welcome, traveller.", cap.narration.Trim());
    }

    [Test]
    public void Parser_DropsEchoedSteeringJsonFromNarration()
    {
        // Gap 4: model repeats the steering JSON inside the narration.
        var cap = new Capture();
        var p = new GZLocalLLMBackend.AnswerParser(Req(), cap.callbacks);
        p.Feed("{\"poi\":\"cattle\"}\n");
        p.Feed("Cattle were wealth here.\n");
        p.Feed("{\"poi\":\"cattle\",\"view\":\"low\"}\n");
        p.Feed("Herds numbered in the thousands.");
        p.Finish();

        Assert.AreEqual(1, cap.commands.Count, "echoed JSON must not fire a second command");
        StringAssert.DoesNotContain("poi", cap.narration, "raw JSON must never reach the player");
        StringAssert.Contains("Cattle were wealth", cap.narration);
        StringAssert.Contains("Herds numbered", cap.narration);
    }

    [Test]
    public void Parser_DropsEchoedJsonEvenWhenStreamEndsWithoutNewline()
    {
        var cap = new Capture();
        var p = new GZLocalLLMBackend.AnswerParser(Req(), cap.callbacks);
        p.Feed("{\"poi\":\"east_ruins\"}\n");
        p.Feed("Quiet walls here.\n");
        p.Feed("{\"poi\":\"east_ruins\"}");   // echo, then the stream just stops
        p.Finish();

        StringAssert.DoesNotContain("{", cap.narration, "held echo line must be dropped at Finish");
        StringAssert.Contains("Quiet walls", cap.narration);
    }

    [Test]
    public void Parser_KeepsBraceLinesThatAreNotCommands()
    {
        // The filter must only eat real steering commands, not prose in braces.
        var cap = new Capture();
        var p = new GZLocalLLMBackend.AnswerParser(Req(), cap.callbacks);
        p.Feed("{\"poi\":\"overview\"}\n");
        p.Feed("{a curious aside} and the story continues.\n");
        p.Finish();

        StringAssert.Contains("{a curious aside}", cap.narration,
            "non-command brace text is legitimate narration");
    }

    // ---------- protocol violations fall back safely ----------

    [Test]
    public void Parser_MalformedHeaderFallsBackToKeywordGuessAndKeepsSpeech()
    {
        var cap = new Capture();
        var p = new GZLocalLLMBackend.AnswerParser(Req("karanga_village"), cap.callbacks);
        p.Feed("The village bustled with daily life.\n");
        p.Feed("Grain, clay, and cattle everywhere.");
        p.Finish();

        Assert.AreEqual(1, cap.commands.Count, "fallback command must still fire");
        Assert.AreEqual("karanga_village", cap.commands[0].poi, "fallback steers to the keyword guess");
        StringAssert.Contains("village bustled", cap.narration, "the header line was speech — keep it");
    }

    [Test]
    public void Parser_NoKeywordGuessFallsBackToStay()
    {
        var cap = new Capture();
        var p = new GZLocalLLMBackend.AnswerParser(Req(), cap.callbacks);
        p.Feed("No JSON anywhere in this answer.\n");
        p.Finish();

        Assert.AreEqual("stay", cap.commands[0].poi, "no guess means the camera stays put");
    }

    [Test]
    public void Parser_HeaderSafetyValveTreatsLongJsonlessStreamAsSpeech()
    {
        // A model that ignores the protocol never sends a newline — after 350
        // chars the parser must give up on a header instead of buffering forever.
        var cap = new Capture();
        var p = new GZLocalLLMBackend.AnswerParser(Req("overview"), cap.callbacks);
        string prose = new string('x', 400);
        p.Feed(prose);
        p.Finish();

        Assert.AreEqual(1, cap.commands.Count, "safety valve must produce a fallback command");
        Assert.AreEqual("overview", cap.commands[0].poi);
        Assert.IsTrue(cap.narration.Contains("xxx"), "the buffered prose must be shown, not swallowed");
    }

    [Test]
    public void Parser_EmptyStreamProducesNoNarration()
    {
        var cap = new Capture();
        var p = new GZLocalLLMBackend.AnswerParser(Req(), cap.callbacks);
        p.Finish();

        Assert.IsFalse(p.NarrationEmitted, "nothing streamed, nothing shown");
        Assert.AreEqual("", cap.narration);
    }
}
