using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One completed exchange, kept so follow-ups ("what else?") have context.</summary>
public class GZNpcTurn
{
    public string question;
    /// <summary>The steering line the answer opened with; replayed so history turns double as format examples.</summary>
    public string steer;
    public string narration;
}

/// <summary>Everything an NPC backend needs to answer one question.</summary>
public class GZNpcRequest
{
    public string question;
    public List<GZTourPOI> pois;
    /// <summary>Best keyword match for the question; may be null.</summary>
    public GZTourPOI keywordGuess;
    /// <summary>Where the camera is currently looking (for "stay" answers).</summary>
    public GZTourPOI currentPoi;
    /// <summary>Recent completed exchanges, oldest first; may be null or empty.</summary>
    public List<GZNpcTurn> history;
}

/// <summary>Streamed answer callbacks. All are invoked on the main thread.</summary>
public class GZNpcCallbacks
{
    /// <summary>Camera steering command — fired once, as early as possible.</summary>
    public Action<GZTourCommand> onCommand;
    /// <summary>Incremental narration text for the subtitle display.</summary>
    public Action<string> onToken;
    /// <summary>(success, errorMessage). Fired exactly once, always last.</summary>
    public Action<bool, string> onComplete;
}

/// <summary>
/// Base class for the tour guide's brain. The director talks only to this
/// interface, so the local LLM and the offline script are interchangeable.
/// </summary>
public abstract class GZNpcBackend : MonoBehaviour
{
    public abstract string BackendLabel { get; }
    public abstract void Ask(GZNpcRequest request, GZNpcCallbacks callbacks);
    public abstract void Abort();
}
