using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// One place to tune a Light2D: size in grid squares, colour, shadows, flicker, pulse and on/off fading.
/// Works on character lights, torches, or any other light. Changes show in the Scene view without
/// pressing Play.
///
/// This script only sets values on Unity's own Light2D component; Light2D does the actual lighting.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Light2D))]
public class GameLight : MonoBehaviour
{
    [Header("On / Off")]
    [Tooltip("Turn the light on or off. It fades over Fade Time instead of snapping.")]
    public bool lightOn = true;
    [Tooltip("Seconds to fade in or out. 0 = instant.")]
    [Min(0f)] public float fadeTime = 0.3f;

    [Header("Size (in grid squares)")]
    [Tooltip("How far the light reaches, in squares.")]
    [Min(0f)] public float radius = 4.5f;
    [Tooltip("Part of the radius that is at full brightness before it starts fading out (0 to 1).")]
    [Range(0f, 1f)] public float innerRadius = 0.2f;
    [Tooltip("Size of one grid square in world units. 1 for the default Grid.")]
    [Min(0.01f)] public float squareSize = 1f;

    [Header("Look")]
    public Color color = Color.white;
    [Min(0f)] public float intensity = 1.1f;
    [Tooltip("How quickly the light fades towards the edge. Higher = sharper edge.")]
    [Range(0f, 1f)] public float falloff = 0.5f;

    [Header("Shadows")]
    [Tooltip("Walls (Shadow Caster 2D) block this light.")]
    public bool castShadows = true;
    [Tooltip("How dark this light's shadows are. 1 = completely dark. Below 1 lets overlapping shadows from several lights blend more naturally.")]
    [Range(0f, 1f)] public float shadowStrength = 0.8f;
    [Tooltip("Blur on the shadow edges. Higher = softer, wider shadow edges.")]
    [Range(0f, 1f)] public float shadowSoftness = 0.4f;
    [Tooltip("How quickly the soft edge fades out. Higher = the blur fades over a shorter distance.")]
    [Range(0f, 1f)] public float shadowSoftnessFalloff = 0.5f;

    [Header("Flicker (like a flame)")]
    public bool flicker = false;
    [Tooltip("How much the brightness wobbles (0 to 1 of the intensity).")]
    [Range(0f, 1f)] public float flickerAmount = 0.15f;
    [Tooltip("How fast it wobbles.")]
    [Min(0f)] public float flickerSpeed = 8f;
    [Tooltip("Also wobble the size a little.")]
    [Range(0f, 0.5f)] public float flickerRadiusAmount = 0.05f;

    [Header("Pulse (slow breathing)")]
    public bool pulse = false;
    [Range(0f, 1f)] public float pulseAmount = 0.2f;
    [Tooltip("Pulses per second.")]
    [Min(0f)] public float pulseSpeed = 0.5f;

    [Header("Current (shown for debugging)")]
    [Range(0f, 1f)] public float brightness = 1f; // 0 = off, 1 = fully on (moves while fading)

    private Light2D light2D;
    private float noiseSeed;

    private void OnEnable()
    {
        light2D = GetComponent<Light2D>();
        // Different random start per light so flickering lights don't move in sync.
        noiseSeed = Random.Range(0f, 100f);
        if (lightOn) brightness = 1f;
        else brightness = 0f;
        Apply();
    }

    private void Update()
    {
        // Fade towards on (1) or off (0). Snap straight there in the editor when not playing.
        float target = 0f;
        if (lightOn) target = 1f;
        if (!Application.isPlaying || fadeTime <= 0f) brightness = target;
        else brightness = Mathf.MoveTowards(brightness, target, Time.deltaTime / fadeTime);

        Apply();
    }

    // Inspector edits take effect immediately.
    private void OnValidate()
    {
        if (light2D == null) light2D = GetComponent<Light2D>();
        Apply();
    }

    /// <summary>Copies all settings onto the Light2D, adding flicker and pulse on top.</summary>
    private void Apply()
    {
        if (light2D == null) return;

        float time = Application.isPlaying ? Time.time : 0f;
        float intensityScale = 1f;
        float radiusScale = 1f;

        if (flicker)
        {
            // Perlin noise gives a smooth random value between 0 and 1; shift it to -1..1.
            float noise = Mathf.PerlinNoise(noiseSeed, time * flickerSpeed) * 2f - 1f;
            intensityScale += noise * flickerAmount;
            radiusScale += noise * flickerRadiusAmount;
        }

        if (pulse)
            intensityScale += Mathf.Sin(time * pulseSpeed * Mathf.PI * 2f) * pulseAmount;

        float outer = radius * squareSize * radiusScale;
        light2D.pointLightOuterRadius = outer;
        light2D.pointLightInnerRadius = outer * innerRadius;
        light2D.color = color;
        light2D.intensity = Mathf.Max(0f, intensity * intensityScale * brightness);
        light2D.falloffIntensity = falloff;

        light2D.shadowsEnabled = castShadows;
        light2D.shadowIntensity = shadowStrength;
        light2D.shadowSoftness = shadowSoftness;
        light2D.shadowSoftnessFalloffIntensity = shadowSoftnessFalloff;
    }

    /// <summary>Turn the light on (e.g. when a torch is lit). It fades in over fadeTime.</summary>
    public void TurnOn() => lightOn = true;

    /// <summary>Turn the light off. It fades out over fadeTime.</summary>
    public void TurnOff() => lightOn = false;
}
