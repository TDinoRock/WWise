using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Arena
{
    /// <summary>
    /// Full Inspector control over a URP <see cref="Light2D"/>, for both standalone level lights and
    /// lights parented to a player. Every Light2D property is exposed here, including the two the
    /// public API only exposes as getters (normal map distance and quality), plus extras Light2D has
    /// no concept of: radius measured in grid tiles, flicker and pulse.
    ///
    /// Values are pushed onto the Light2D every time they change, in edit mode and at runtime, so the
    /// Scene view updates as you drag a slider.
    /// </summary>
    [RequireComponent(typeof(Light2D))]
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class ArenaLight : MonoBehaviour
    {
        public enum RadiusUnit
        {
            /// <summary>Radii are in grid tiles and scale with GridService's cell size.</summary>
            Tiles = 0,
            /// <summary>Radii are Unity world units, exactly as Light2D stores them.</summary>
            WorldUnits = 1,
        }

        public enum FlickerShape { Perlin = 0, Sine = 1, Square = 2 }

        // ------------------------------------------------------------------ shape

        [Header("Shape")]
        [Tooltip("Point is the usual character/torch light. Global floods everything and ignores radius.")]
        [SerializeField] private Light2D.LightType lightType = Light2D.LightType.Point;

        [Tooltip("Tiles scales the radii by GridService's cell size; World Units passes them through untouched.")]
        [SerializeField] private RadiusUnit radiusUnit = RadiusUnit.Tiles;

        [Tooltip("Cell size assumed when no GridService exists (edit mode, prefabs).")]
        [Min(0.01f)]
        [SerializeField] private float fallbackTileSize = 1f;

        [Tooltip("Outer radius: where the light reaches zero.")]
        [Min(0f)]
        [SerializeField] private float outerRadius = 4.5f;

        [Tooltip("Inner radius: the fully lit core, as a fraction of the outer radius.")]
        [Range(0f, 1f)]
        [SerializeField] private float innerRadiusFraction = 0.25f;

        [Tooltip("Spot cone: fully lit angle. 360 is a full circle.")]
        [Range(0f, 360f)]
        [SerializeField] private float innerAngle = 360f;

        [Tooltip("Spot cone: outer angle where the light falls off. Must be >= inner angle.")]
        [Range(0f, 360f)]
        [SerializeField] private float outerAngle = 360f;

        // ------------------------------------------------------------------ appearance

        [Header("Appearance")]
        [SerializeField] private Color color = Color.white;

        [Min(0f)]
        [SerializeField] private float intensity = 1f;

        [Tooltip("How sharply the light fades toward the outer radius. 0 = linear, 1 = tight.")]
        [Range(0f, 1f)]
        [SerializeField] private float falloffIntensity = 0.5f;

        [Tooltip("Index into the 2D Renderer's Light Blend Styles list.")]
        [Min(0)]
        [SerializeField] private int blendStyleIndex = 0;

        [Tooltip("How this light combines with others: Additive adds, Alpha Blend replaces by alpha.")]
        [SerializeField] private Light2D.OverlapOperation overlapOperation = Light2D.OverlapOperation.Additive;

        [Tooltip("Draw order among lights sharing a blend style.")]
        [SerializeField] private int lightOrder = 0;

        [Tooltip("Sprite used as a light cookie. Only meaningful for the Sprite light type.")]
        [SerializeField] private Sprite lightCookieSprite;

        // ------------------------------------------------------------------ shadows

        [Header("Shadows")]
        [Tooltip("Objects carrying a ShadowCaster2D block this light.")]
        [SerializeField] private bool shadowsEnabled = true;

        [Range(0f, 1f)]
        [SerializeField] private float shadowStrength = 1f;

        [Tooltip("Blurs the shadow edge.")]
        [Range(0f, 1f)]
        [SerializeField] private float shadowSoftness = 0f;

        [Tooltip("Falloff curve of the soft shadow edge.")]
        [Range(0f, 1f)]
        [SerializeField] private float shadowSoftnessFalloff = 0.5f;

        // ------------------------------------------------------------------ volumetrics

        [Header("Volumetric Light")]
        [SerializeField] private bool volumetricEnabled = false;

        [Range(0f, 1f)]
        [SerializeField] private float volumetricIntensity = 0f;

        [SerializeField] private bool volumetricShadowsEnabled = false;

        [Range(0f, 1f)]
        [SerializeField] private float volumetricShadowIntensity = 0f;

        // ------------------------------------------------------------------ normal map

        [Header("Normal Map")]
        [Tooltip("Disabled skips normal mapping. Fast suits small on-screen shapes, Accurate large ones.")]
        [SerializeField] private Light2D.NormalMapQuality normalMapQuality = Light2D.NormalMapQuality.Disabled;

        [Tooltip("Simulated distance of the light above the surface, for normal-mapped shading.")]
        [Min(0f)]
        [SerializeField] private float normalMapDistance = 3f;

        // ------------------------------------------------------------------ target layers

        [Header("Target Sorting Layers")]
        [Tooltip("ON: light every sorting layer. OFF: only the layer IDs listed below.")]
        [SerializeField] private bool lightAllSortingLayers = true;

        [Tooltip("Sorting layer IDs this light affects. Use the context menu to fill from names.")]
        [SerializeField] private int[] targetSortingLayers = System.Array.Empty<int>();

        // ------------------------------------------------------------------ animation

        [Header("Flicker")]
        [SerializeField] private bool flicker = false;
        [SerializeField] private FlickerShape flickerShape = FlickerShape.Perlin;

        [Tooltip("Peak intensity deviation as a fraction of Intensity. 0.1 = plus or minus 10%.")]
        [Range(0f, 1f)]
        [SerializeField] private float flickerIntensityAmount = 0.1f;

        [Tooltip("Peak radius deviation, in whatever unit Radius Unit is set to.")]
        [Min(0f)]
        [SerializeField] private float flickerRadiusAmount = 0.15f;

        [Min(0f)]
        [SerializeField] private float flickerSpeed = 8f;

        [Tooltip("Randomises the flicker phase so identical lights don't flicker in lockstep. Set -1 to seed from this object's name.")]
        [SerializeField] private int flickerSeed = -1;

        [Header("Pulse")]
        [Tooltip("Smooth sine breathing, layered on top of flicker.")]
        [SerializeField] private bool pulse = false;

        [Range(0f, 1f)]
        [SerializeField] private float pulseAmount = 0.2f;

        [Min(0f)]
        [SerializeField] private float pulseSpeed = 1f;

        // ------------------------------------------------------------------ runtime readout

        [Header("Runtime (read-only)")]
        [InspectorReadOnly, SerializeField] private float currentTileSize = 1f;
        [InspectorReadOnly, SerializeField] private float appliedOuterRadius;
        [InspectorReadOnly, SerializeField] private float appliedInnerRadius;
        [InspectorReadOnly, SerializeField] private float appliedIntensity;

        // ------------------------------------------------------------------ public API

        public Light2D Light { get; private set; }

        public float OuterRadius { get => outerRadius; set { outerRadius = Mathf.Max(0f, value); Apply(); } }
        public float Intensity { get => intensity; set { intensity = Mathf.Max(0f, value); Apply(); } }
        public Color LightColor { get => color; set { color = value; Apply(); } }
        public bool Flicker { get => flicker; set => flicker = value; }
        public bool Pulse { get => pulse; set => pulse = value; }

        // Light2D exposes these two only as getters, so they are written through their serialized fields.
        private static readonly FieldInfo NormalMapQualityField =
            typeof(Light2D).GetField("m_NormalMapQuality", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo NormalMapDistanceField =
            typeof(Light2D).GetField("m_NormalMapDistance", BindingFlags.NonPublic | BindingFlags.Instance);

        private float noisePhase;

        // ------------------------------------------------------------------ lifecycle

        private void Awake()
        {
            Light = GetComponent<Light2D>();
            noisePhase = flickerSeed >= 0 ? flickerSeed : Mathf.Abs(name.GetHashCode() % 1000);
        }

        private void OnEnable() => Apply();

        private void OnValidate()
        {
            if (Light == null) Light = GetComponent<Light2D>();
            if (outerAngle < innerAngle) outerAngle = innerAngle;
            Apply();
        }

        private void Update()
        {
            if (!Application.isPlaying)
            {
                Apply();            // keep the Scene view live while editing
                return;
            }
            if (flicker || pulse) Apply();
        }

        /// <summary>Copies the Light2D's current values into this component, so an existing light can be adopted.</summary>
        [ContextMenu("Pull Values From Light2D")]
        public void PullFromLight2D()
        {
            if (Light == null) Light = GetComponent<Light2D>();
            if (Light == null) return;

            lightType = Light.lightType;
            color = Light.color;
            intensity = Light.intensity;
            falloffIntensity = Light.falloffIntensity;
            blendStyleIndex = Light.blendStyleIndex;
            overlapOperation = Light.overlapOperation;
            lightOrder = Light.lightOrder;
            lightCookieSprite = Light.lightCookieSprite;
            shadowsEnabled = Light.shadowsEnabled;
            shadowStrength = Light.shadowIntensity;
            shadowSoftness = Light.shadowSoftness;
            shadowSoftnessFalloff = Light.shadowSoftnessFalloffIntensity;
            volumetricEnabled = Light.volumetricEnabled;
            volumetricIntensity = Light.volumeIntensity;
            volumetricShadowsEnabled = Light.volumetricShadowsEnabled;
            volumetricShadowIntensity = Light.shadowVolumeIntensity;
            normalMapQuality = Light.normalMapQuality;
            normalMapDistance = Light.normalMapDistance;
            innerAngle = Light.pointLightInnerAngle;
            outerAngle = Light.pointLightOuterAngle;

            float unit = radiusUnit == RadiusUnit.Tiles ? TileSize() : 1f;
            outerRadius = Light.pointLightOuterRadius / Mathf.Max(0.0001f, unit);
            innerRadiusFraction = Light.pointLightOuterRadius > 0f
                ? Mathf.Clamp01(Light.pointLightInnerRadius / Light.pointLightOuterRadius)
                : 0f;
        }

        [ContextMenu("Target All Sorting Layers")]
        public void TargetAllSortingLayers()
        {
            SortingLayer[] layers = SortingLayer.layers;
            targetSortingLayers = new int[layers.Length];
            for (int i = 0; i < layers.Length; i++) targetSortingLayers[i] = layers[i].id;
            lightAllSortingLayers = true;
            Apply();
        }

        private float TileSize()
        {
            GridService grid = Application.isPlaying ? GridService.Instance : null;
            return grid != null ? grid.CellSize : fallbackTileSize;
        }

        // ------------------------------------------------------------------ apply

        /// <summary>Writes every exposed value onto the Light2D, including the animated offsets.</summary>
        public void Apply()
        {
            if (Light == null) Light = GetComponent<Light2D>();
            if (Light == null) return;

            float intensityScale = 1f;
            float radiusOffset = 0f;

            if (Application.isPlaying)
            {
                if (flicker)
                {
                    float n = Wave(flickerShape, Time.time * flickerSpeed + noisePhase);
                    intensityScale += n * flickerIntensityAmount;
                    radiusOffset += n * flickerRadiusAmount;
                }
                if (pulse)
                    intensityScale += Mathf.Sin(Time.time * pulseSpeed * Mathf.PI * 2f) * pulseAmount;
            }

            currentTileSize = radiusUnit == RadiusUnit.Tiles ? TileSize() : 1f;
            float outer = Mathf.Max(0f, (outerRadius + radiusOffset) * currentTileSize);

            appliedOuterRadius = outer;
            appliedInnerRadius = outer * innerRadiusFraction;
            appliedIntensity = Mathf.Max(0f, intensity * intensityScale);

            Light.lightType = lightType;
            Light.pointLightOuterRadius = appliedOuterRadius;
            Light.pointLightInnerRadius = appliedInnerRadius;
            Light.pointLightInnerAngle = innerAngle;
            Light.pointLightOuterAngle = Mathf.Max(innerAngle, outerAngle);

            Light.color = color;
            Light.intensity = appliedIntensity;
            Light.falloffIntensity = falloffIntensity;
            Light.blendStyleIndex = blendStyleIndex;
            Light.overlapOperation = overlapOperation;
            Light.lightOrder = lightOrder;
            Light.lightCookieSprite = lightCookieSprite;

            Light.shadowsEnabled = shadowsEnabled;
            Light.shadowIntensity = shadowStrength;
            Light.shadowSoftness = shadowSoftness;
            Light.shadowSoftnessFalloffIntensity = shadowSoftnessFalloff;

            Light.volumetricEnabled = volumetricEnabled;
            Light.volumeIntensity = volumetricIntensity;
            Light.volumetricShadowsEnabled = volumetricShadowsEnabled;
            Light.shadowVolumeIntensity = volumetricShadowIntensity;

            NormalMapQualityField?.SetValue(Light, normalMapQuality);
            NormalMapDistanceField?.SetValue(Light, normalMapDistance);

            if (!lightAllSortingLayers && targetSortingLayers is { Length: > 0 })
                Light.targetSortingLayers = targetSortingLayers;
        }

        private static float Wave(FlickerShape shape, float t) => shape switch
        {
            FlickerShape.Sine => Mathf.Sin(t),
            FlickerShape.Square => Mathf.Sign(Mathf.Sin(t)),
            _ => Mathf.PerlinNoise(t, 0f) * 2f - 1f,
        };

        // ------------------------------------------------------------------ gizmos

        private void OnDrawGizmosSelected()
        {
            if (Light == null || lightType == Light2D.LightType.Global) return;

            Gizmos.color = new Color(color.r, color.g, color.b, 0.55f);
            DrawCircle(appliedOuterRadius > 0f ? appliedOuterRadius : outerRadius * TileSize());
            Gizmos.color = new Color(color.r, color.g, color.b, 0.25f);
            DrawCircle(appliedInnerRadius);
        }

        private void DrawCircle(float radius)
        {
            if (radius <= 0f) return;
            const int segments = 48;
            Vector3 centre = transform.position;
            Vector3 previous = centre + new Vector3(radius, 0f, 0f);

            for (int i = 1; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                Vector3 next = centre + new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f);
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }
    }
}
