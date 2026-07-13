using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// How a tour point-of-interest finds its focus point each frame.
/// </summary>
public enum GZFocusKind
{
    /// <summary>Fixed world anchor; Y is re-sampled from the terrain.</summary>
    StaticAnchor,
    /// <summary>Centroid of the live <see cref="GZHerdRunner"/> agents (the cattle).</summary>
    HerdCentroid,
}

/// <summary>
/// One flyable destination in the aerial tour. The numbers encode the "good view"
/// for that place — e.g. the Hill Complex uses a high, wide orbit so the
/// photogrammetry terrain is never seen up close, while the cattle POI is a low,
/// tight chase orbit. Question commands can still modify these via view hints.
/// </summary>
[Serializable]
public class GZTourPOI
{
    [Tooltip("Stable id used by NPC commands and facts files, e.g. 'hill_complex'.")]
    public string id;
    public string displayName;

    [Tooltip("Lower-case keywords that map player questions to this POI.")]
    public string[] keywords;

    public GZFocusKind focusKind = GZFocusKind.StaticAnchor;

    [Tooltip("World-space anchor. Y is ignored; ground height is sampled from the terrain.")]
    public Vector3 anchor;

    [Tooltip("Gaze point height above the ground at the anchor (aim at wall tops, not dirt).")]
    public float focusHeightOffset = 4f;

    [Tooltip("Camera height above the focus ground, in metres. High values hide terrain quality.")]
    public float altitude = 110f;

    [Tooltip("Orbit ring radius around the focus, in metres.")]
    public float orbitRadius = 160f;

    [Tooltip("Hard floor: camera never gets closer to the terrain than this, anywhere.")]
    public float minClearance = 45f;

    [Tooltip("Linear cruise speed along the orbit ring, m/s.")]
    public float cruiseSpeed = 15f;
}

/// <summary>
/// Steering command produced by an NPC answer. Field names match the JSON the
/// local LLM is instructed to emit, e.g. {"poi":"hill_complex","view":"high","orbit":"slow"}.
/// "poi" may also be "stay" to keep the current destination and just narrate.
/// </summary>
[Serializable]
public class GZTourCommand
{
    public string poi = "";
    public string view = "";   // high | normal | low | close
    public string orbit = "";  // slow | normal | fast
}

/// <summary>
/// Maps free-text questions to POIs by keyword scoring. Used directly by the
/// scripted backend and as a safety net when the LLM emits a malformed command.
/// </summary>
public static class GZKeywordResolver
{
    public static GZTourPOI Resolve(string question, IList<GZTourPOI> pois)
    {
        if (string.IsNullOrWhiteSpace(question) || pois == null) return null;
        string q = " " + question.ToLowerInvariant() + " ";
        GZTourPOI best = null;
        int bestScore = 0;
        foreach (var poi in pois)
        {
            if (poi == null || poi.keywords == null) continue;
            int score = 0;
            foreach (var kw in poi.keywords)
            {
                if (string.IsNullOrEmpty(kw)) continue;
                if (q.Contains(kw.ToLowerInvariant())) score += kw.Length;
            }
            if (score > bestScore) { bestScore = score; best = poi; }
        }
        return best;
    }
}

/// <summary>
/// Loads per-POI fact sheets from Resources/GZTourFacts/&lt;poiId&gt;.txt.
/// Lines starting with '#' are editorial notes and are stripped.
/// </summary>
public static class GZTourFacts
{
    public static string Load(string poiId)
    {
        if (string.IsNullOrEmpty(poiId)) return "";
        var asset = Resources.Load<TextAsset>("GZTourFacts/" + poiId);
        if (asset == null) return "";
        var sb = new System.Text.StringBuilder();
        foreach (var raw in asset.text.Replace("\r", "").Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith("#")) continue;
            sb.AppendLine(line);
        }
        return sb.ToString().Trim();
    }

    /// <summary>
    /// The source attribution recorded in the sheet's '#' header
    /// ("... Sources: X, Y, Z. ..."), for display under the guide's answers.
    /// Returns "" if the sheet or its attribution is missing.
    /// </summary>
    public static string Sources(string poiId)
    {
        if (string.IsNullOrEmpty(poiId)) return "";
        var asset = Resources.Load<TextAsset>("GZTourFacts/" + poiId);
        if (asset == null) return "";
        foreach (var raw in asset.text.Replace("\r", "").Split('\n'))
        {
            string line = raw.Trim();
            if (!line.StartsWith("#")) continue;
            int at = line.IndexOf("Sources:");
            if (at < 0) continue;
            string tail = line.Substring(at + "Sources:".Length).Trim();
            int stop = tail.IndexOf(". ");
            if (stop > 0) tail = tail.Substring(0, stop);
            return tail.TrimEnd('.');
        }
        return "";
    }
}
