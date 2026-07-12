using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Scrolls a cloud-noise light cookie across the directional light so soft cloud
/// shadows sweep over the terrain. Pure shader-side animation: zero draw calls,
/// zero geometry. Remove the component (or run GZAmbientLifeSetup.RemoveAll) to undo.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Light))]
public class GZCloudShadows : MonoBehaviour
{
    [Tooltip("Wind heading in degrees (0 = +X, 90 = +Z).")]
    public float windDegrees = 25f;

    [Tooltip("Cloud drift speed in metres per second.")]
    public float windSpeed = 9f;

    [Tooltip("World size of one cookie tile in metres. Keep in sync with the light's cookie size.")]
    public float cookieSizeMeters = 1400f;

    Light _light;
    UniversalAdditionalLightData _data;

    void OnEnable()
    {
        _light = GetComponent<Light>();
        _data = GetComponent<UniversalAdditionalLightData>();
    }

    void Update()
    {
        if (_light == null || _light.cookie == null) return;
        if (_data == null)
        {
            _data = GetComponent<UniversalAdditionalLightData>();
            if (_data == null) return;
        }

        float t = Application.isPlaying ? Time.time : Time.realtimeSinceStartup;
        float rad = windDegrees * Mathf.Deg2Rad;
        Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        Vector2 off = dir * (windSpeed * t);
        // Wrap so the offset never grows unbounded.
        off.x = Mathf.Repeat(off.x, cookieSizeMeters);
        off.y = Mathf.Repeat(off.y, cookieSizeMeters);
        _data.lightCookieOffset = off;
    }
}
