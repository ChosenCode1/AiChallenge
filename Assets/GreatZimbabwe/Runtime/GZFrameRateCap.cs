using UnityEngine;

/// <summary>
/// Caps the play-mode/build frame rate so the GPU isn't saturated rendering
/// hundreds of uncapped frames per second — leaving compute headroom for other
/// GPU tenants on the machine (e.g. a local LLM doing inference).
/// </summary>
public class GZFrameRateCap : MonoBehaviour
{
    [Tooltip("Target frame rate for play mode and builds. 60 leaves most of the GPU idle in this scene.")]
    public int targetFps = 60;

    void OnEnable()
    {
        QualitySettings.vSyncCount = 0; // vsync would override targetFrameRate
        Application.targetFrameRate = targetFps;
    }

    void OnDisable()
    {
        Application.targetFrameRate = -1;
    }
}
