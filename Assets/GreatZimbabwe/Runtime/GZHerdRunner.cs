using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Moves a small herd of agents along a waypoint path, snapped to the terrain,
/// facing their direction of travel. Agents are either instances of
/// <see cref="agentPrefab"/> (drop in ANY animated animal prefab — if it has an
/// Animator its default state, e.g. a run cycle, plays automatically in play
/// mode) or simple dark placeholder blobs when no prefab is assigned.
/// Motion runs in play mode; in edit mode agents are laid out statically along
/// the path so the scene reads correctly in stills.
/// </summary>
[ExecuteAlways]
public class GZHerdRunner : MonoBehaviour
{
    [Tooltip("World-space waypoints. Y is re-snapped to the terrain every frame.")]
    public Vector3[] waypoints;

    [Tooltip("Optional animated prefab (e.g. an antelope with a run animation). Leave empty for placeholder blobs.")]
    public GameObject agentPrefab;

    [Range(1, 200)] public int count = 9;
    public float minSpeed = 3.5f;
    public float maxSpeed = 5.5f;
    [Tooltip("Max sideways offset from the path centreline, in metres.")]
    public float lateralJitter = 6f;
    [Tooltip("Live speed multiplier on the whole herd (driven by Fact-FX behavior beats).")]
    public float speedMultiplier = 1f;
    public Material placeholderMaterial;
    public int seed = 4242;

    static readonly int CowSeedId = Shader.PropertyToID("_CowSeed");

    readonly List<Transform> _agents = new List<Transform>();
    float[] _progress;   // metres along the path per agent
    float[] _lane;       // signed lateral offset per agent
    float[] _speed;
    float _pathLength;

    void OnEnable()
    {
        CollectAgents();
        if (Application.isPlaying && _agents.Count != count) RebuildAgents();
        ApplyCoatSeeds();
        InitAgentState();
        LayoutAgents(0f);
    }

    void Update()
    {
        if (!Application.isPlaying) return;
        if (_agents.Count == 0 || _pathLength <= 0f) return;
        LayoutAgents(Time.deltaTime);
    }

    void CollectAgents()
    {
        _agents.Clear();
        foreach (Transform child in transform) _agents.Add(child);
    }

    [ContextMenu("Rebuild Agents")]
    public void RebuildAgents()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
        }
        _agents.Clear();

        for (int i = 0; i < count; i++)
        {
            GameObject go;
            if (agentPrefab != null)
            {
                go = Instantiate(agentPrefab, transform);
                go.name = agentPrefab.name + "_" + i;
            }
            else
            {
                go = CreatePlaceholder("HerdAgent_" + i);
                go.transform.SetParent(transform, false);
            }
            _agents.Add(go.transform);
        }
        ApplyCoatSeeds();
        InitAgentState();
        LayoutAgents(0f);
    }

    /// <summary>
    /// Gives every agent a distinct _CowSeed so the HerdGold coat shader can vary
    /// hide patches and coat shade per animal. Set via MaterialPropertyBlock on all
    /// child renderers (body + head share one seed so their pattern family matches);
    /// harmless no-op for prefab agents whose shaders ignore the property.
    /// </summary>
    void ApplyCoatSeeds()
    {
        var mpb = new MaterialPropertyBlock();
        for (int i = 0; i < _agents.Count; i++)
        {
            if (_agents[i] == null) continue;
            mpb.SetFloat(CowSeedId, seed % 89 + i * 1.618f);
            foreach (var r in _agents[i].GetComponentsInChildren<MeshRenderer>())
                r.SetPropertyBlock(mpb);
        }
    }

    GameObject CreatePlaceholder(string name)
    {
        // Deliberately abstract stand-in: a low dark body + head blob that reads as
        // a distant animal. Swap for a real animated prefab via agentPrefab.
        var root = new GameObject(name);

        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        DestroyImmediateSafe(body.GetComponent<Collider>());
        body.name = "Body";
        body.transform.SetParent(root.transform, false);
        body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        body.transform.localScale = new Vector3(0.55f, 0.75f, 0.7f);
        body.transform.localPosition = new Vector3(0f, 0.75f, 0f);

        var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        DestroyImmediateSafe(head.GetComponent<Collider>());
        head.name = "Head";
        head.transform.SetParent(root.transform, false);
        head.transform.localScale = new Vector3(0.32f, 0.38f, 0.42f);
        head.transform.localPosition = new Vector3(0f, 1.15f, 0.85f);

        foreach (var r in new[] { body.GetComponent<MeshRenderer>(), head.GetComponent<MeshRenderer>() })
        {
            if (placeholderMaterial != null) r.sharedMaterial = placeholderMaterial;
            // At herd scale (100+ animals = 200+ renderers) per-renderer probe
            // blending is measurable CPU on mobile; the coat shader only needs the
            // ambient probe, which URP still supplies with probes off.
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }
        return root;
    }

    static void DestroyImmediateSafe(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
    }

    void InitAgentState()
    {
        _pathLength = 0f;
        if (waypoints == null || waypoints.Length < 2) return;
        for (int i = 1; i < waypoints.Length; i++)
        {
            Vector3 a = waypoints[i - 1], b = waypoints[i];
            a.y = 0f; b.y = 0f;
            _pathLength += Vector3.Distance(a, b);
        }

        var rng = new System.Random(seed);
        int n = _agents.Count;
        _progress = new float[n];
        _lane = new float[n];
        _speed = new float[n];
        for (int i = 0; i < n; i++)
        {
            // Stagger the herd over the first 40% of the route with per-agent jitter.
            float t = n <= 1 ? 0f : (float)i / (n - 1);
            _progress[i] = _pathLength * (0.05f + 0.35f * t) + (float)rng.NextDouble() * 8f;
            _lane[i] = Mathf.Lerp(-lateralJitter, lateralJitter, (float)rng.NextDouble());
            _speed[i] = Mathf.Lerp(minSpeed, maxSpeed, (float)rng.NextDouble());
        }
    }

    void LayoutAgents(float dt)
    {
        if (_progress == null || _progress.Length != _agents.Count) InitAgentState();
        if (_pathLength <= 0f || _agents.Count == 0) return;

        var terrain = Terrain.activeTerrain;
        for (int i = 0; i < _agents.Count; i++)
        {
            if (_agents[i] == null) continue;
            _progress[i] = Mathf.Repeat(_progress[i] + _speed[i] * speedMultiplier * dt, _pathLength);
            Vector3 pos, dir;
            SamplePath(_progress[i], out pos, out dir);
            Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
            pos += side * _lane[i];
            if (terrain != null)
                pos.y = terrain.SampleHeight(pos) + terrain.transform.position.y;
            _agents[i].position = pos;
            if (dir.sqrMagnitude > 0.0001f)
                _agents[i].rotation = Quaternion.LookRotation(dir, Vector3.up);
        }
    }

    void SamplePath(float meters, out Vector3 pos, out Vector3 dir)
    {
        pos = waypoints[0]; dir = Vector3.forward;
        float remaining = meters;
        for (int i = 1; i < waypoints.Length; i++)
        {
            Vector3 a = waypoints[i - 1], b = waypoints[i];
            Vector3 flatA = new Vector3(a.x, 0f, a.z), flatB = new Vector3(b.x, 0f, b.z);
            float seg = Vector3.Distance(flatA, flatB);
            if (remaining <= seg || i == waypoints.Length - 1)
            {
                float t = seg <= 0f ? 0f : Mathf.Clamp01(remaining / seg);
                pos = Vector3.Lerp(a, b, t);
                dir = (flatB - flatA).normalized;
                return;
            }
            remaining -= seg;
        }
    }
}
