using UnityEngine;
using System.Collections;

[ExecuteAlways]
public class DayNightController : MonoBehaviour
{
    public Light sun;
    public float secondsInFullDay = 120f;
    [Range(0, 1)]
    public float currentTimeOfDay = 0;
    [HideInInspector]
    public float timeMultiplier = 1f;

    [Header("Sun geometry (rises in FRONT of the bus)")]
    [Tooltip("Light azimuth (deg). The light shines ALONG its +Z. ~180 makes it shine back toward the bus " +
             "from AHEAD — so the sun disc sits in FRONT of the bus (you drive toward the sunrise). A small " +
             "offset off 180 gives a nicer raking angle than dead-ahead.")]
    public float sunAzimuth = 188f;
    [Tooltip("Sun elevation at sunrise/sunset (deg above horizon, ~0) and at noon (deg). The sun arcs " +
             "between these so dawn is low-and-in-front and midday is high.")]
    public float sunNoonElevation = 62f;

    [Header("Shift schedule (phase fractions of the 0..1 shift)")]
    [Tooltip("Fraction of the shift spent on SUNRISE at the very start (0.05 = first 5% ≈ 30s of a 10-min shift).")]
    [Range(0f, 0.5f)] public float sunriseFraction = 0.05f;
    [Tooltip("Fraction of the shift spent at DUSK before night.")]
    [Range(0f, 0.5f)] public float duskFraction = 0.15f;
    [Tooltip("Fraction of the shift that is NIGHT at the very end (0.15 = last 15% ≈ 1.5min).")]
    [Range(0f, 0.5f)] public float nightFraction = 0.15f;

    // When true, something else (ShiftManager) sets currentTimeOfDay each frame so the day->night
    // cycle tracks the shift clock (dusk == shift end). Leave false for the standalone auto-cycle.
    [HideInInspector]
    public bool externalTimeControl = false;

    [Header("Day/Night Gradients")]
    public Gradient sunColor;        // Tint of the sun (e.g., orange sunset, yellow noon)
    public Gradient ambientColor;    // Ambient environment color (essential for dark night shadow shading!)
    public Gradient fogColors;       // Color of the atmospheric fog

    [Header("Fog Settings")]
    public bool controlFog = true;
    public float maxFogDensity = 0.015f; // Deep atmospheric thickness at night/dawn

    float sunInitialIntensity;

    void Start()
    {
        if (sun != null)
        {
            sunInitialIntensity = sun.intensity;
        }

        // Flat ambient so our per-frame ambientLight tint actually applies (Skybox/Gradient modes ignore it).
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;

        // Force enable fog and setup exponential decay for premium graphics
        if (controlFog)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
        }
    }

    void Update()
    {
        UpdateSun();

        // Only advance the time automatically during actual gameplay — unless ShiftManager is
        // driving currentTimeOfDay to keep the cycle locked to the shift clock.
        if (Application.isPlaying && !externalTimeControl)
        {
            currentTimeOfDay += (Time.deltaTime / secondsInFullDay) * timeMultiplier;

            if (currentTimeOfDay >= 1)
            {
                currentTimeOfDay = 0;
            }
        }

        // 1. Dynamic Fog Color
        if (fogColors != null)
        {
            RenderSettings.fogColor = fogColors.Evaluate(currentTimeOfDay);
        }

        // 2. Dynamic Ambient Environment Color (This makes the night pitch dark!)
        if (ambientColor != null)
        {
            RenderSettings.ambientLight = ambientColor.Evaluate(currentTimeOfDay);
        }

        // 3. Dynamic Fog Density: LIGHT at midday (clear sunny day), thicker toward dawn/dusk/night. Distance
        //    from noon (0.5) drives it — 0 at noon → 1 at midnight.
        if (controlFog)
        {
            float fromNoon = Mathf.Clamp01(Mathf.Abs(currentTimeOfDay - 0.5f) / 0.5f);   // 0 noon .. 1 night
            RenderSettings.fogDensity = Mathf.Lerp(maxFogDensity * 0.25f, maxFogDensity, fromNoon * fromNoon);
        }
    }

    // Called by ShiftManager when it owns the clock (externalTimeControl = true).
    public void SetTimeOfDay(float t)
    {
        currentTimeOfDay = Mathf.Clamp01(t);
    }

    /// Drive the cycle from SHIFT PROGRESS (0 = start .. 1 = end) through the phase schedule, so the visual
    /// time-of-day isn't a flat lerp: a quick sunrise, a long sunny day, then dusk into a short night.
    /// Maps progress → canonical timeOfDay (0.25 sunrise, 0.5 noon, 0.75 dusk, ~0.97 deep night).
    public void SetShiftProgress(float p)
    {
        p = Mathf.Clamp01(p);
        float rise = Mathf.Clamp01(sunriseFraction);
        float night = Mathf.Clamp01(nightFraction);
        float dusk = Mathf.Clamp01(duskFraction);
        float duskStart = Mathf.Max(rise, 1f - night - dusk);   // day runs [rise .. duskStart]
        float nightStart = 1f - night;                           // [duskStart .. nightStart] is dusk

        float tod;
        if (p < rise)                       // SUNRISE: 0.18 (pre-dawn) → 0.30 (full morning)
            tod = Mathf.Lerp(0.18f, 0.30f, p / Mathf.Max(0.0001f, rise));
        else if (p < duskStart)             // DAY: 0.30 → 0.62, sun swings through noon (0.5)
            tod = Mathf.Lerp(0.30f, 0.62f, Mathf.InverseLerp(rise, duskStart, p));
        else if (p < nightStart)            // DUSK: 0.62 → 0.80 (sunset)
            tod = Mathf.Lerp(0.62f, 0.80f, Mathf.InverseLerp(duskStart, nightStart, p));
        else                                // NIGHT: 0.80 → 0.97 (deep night)
            tod = Mathf.Lerp(0.80f, 0.97f, Mathf.InverseLerp(nightStart, 1f, p));

        currentTimeOfDay = tod;
    }

    void UpdateSun()
    {
        if (sun == null) return;

        // Lazy initialize the sun's base intensity if we are in Edit Mode
        if (sunInitialIntensity == 0)
        {
            sunInitialIntensity = sun.intensity > 0 ? sun.intensity : 1f;
        }

        // SUN ARC, rising in FRONT of the bus. dayPhase: 0 at sunrise (0.25) → 1 at sunset (0.75); the sun's
        // ELEVATION follows a sine so it's on the horizon at dawn/dusk and highest at noon. The AZIMUTH is
        // ~straight ahead (+Z, sunAzimuth≈0) so it climbs up from in front of the bus. A directional light
        // points ALONG its travel, so the LIGHT direction = -(toward the sun): we build the sun's sky
        // position then look from it toward the origin.
        float dayPhase = Mathf.InverseLerp(0.25f, 0.75f, currentTimeOfDay);   // 0 dawn .. 1 dusk
        float elevation = Mathf.Sin(Mathf.Clamp01(dayPhase) * Mathf.PI) * sunNoonElevation;  // 0→noon→0
        // below the horizon outside [dawn..dusk] (night) so it doesn't light the scene from underground
        if (currentTimeOfDay < 0.25f) elevation = Mathf.Lerp(-12f, 0f, Mathf.InverseLerp(0.18f, 0.25f, currentTimeOfDay));
        else if (currentTimeOfDay > 0.75f) elevation = Mathf.Lerp(0f, -16f, Mathf.InverseLerp(0.75f, 0.97f, currentTimeOfDay));
        // light direction comes FROM the sun: rotate down by `elevation`, around the front azimuth.
        sun.transform.rotation = Quaternion.Euler(elevation, sunAzimuth, 0f);

        // Intensity: ramps up through sunrise, full across the day, fades through dusk to 0 at night.
        float intensityMultiplier;
        if (currentTimeOfDay <= 0.22f) intensityMultiplier = 0f;                                   // night/pre-dawn
        else if (currentTimeOfDay <= 0.30f) intensityMultiplier = Mathf.InverseLerp(0.22f, 0.30f, currentTimeOfDay); // sunrise ramp
        else if (currentTimeOfDay < 0.70f) intensityMultiplier = 1f;                               // full day
        else if (currentTimeOfDay < 0.82f) intensityMultiplier = 1f - Mathf.InverseLerp(0.70f, 0.82f, currentTimeOfDay); // dusk fade
        else intensityMultiplier = 0f;                                                              // night

        sun.intensity = sunInitialIntensity * intensityMultiplier;

        // 4. Dynamic Sun Tinting
        if (sunColor != null)
        {
            sun.color = sunColor.Evaluate(currentTimeOfDay);
        }
    }

    private void Reset()
    {
        // 1. Initialize Sun Color Gradient
        sunColor = new Gradient();
        GradientColorKey[] sunColors = new GradientColorKey[5];
        sunColors[0] = new GradientColorKey(new Color(0.05f, 0.06f, 0.12f), 0.0f);   // Night
        sunColors[1] = new GradientColorKey(new Color(1f, 0.55f, 0.25f), 0.25f);     // Dawn (warm orange)
        sunColors[2] = new GradientColorKey(new Color(1f, 0.97f, 0.88f), 0.5f);      // Noon (bright warm white)
        sunColors[3] = new GradientColorKey(new Color(1f, 0.45f, 0.2f), 0.75f);      // Sunset (warm orange)
        sunColors[4] = new GradientColorKey(new Color(0.05f, 0.06f, 0.12f), 1.0f);   // Night
        
        GradientAlphaKey[] sunAlphas = new GradientAlphaKey[2] {
            new GradientAlphaKey(1.0f, 0.0f),
            new GradientAlphaKey(1.0f, 1.0f)
        };
        sunColor.SetKeys(sunColors, sunAlphas);

        // 2. Initialize Ambient Color Gradient (pitch dark at night)
        ambientColor = new Gradient();
        GradientColorKey[] ambColors = new GradientColorKey[5];
        ambColors[0] = new GradientColorKey(new Color(0.03f, 0.04f, 0.09f), 0.0f);    // Night (deep navy)
        ambColors[1] = new GradientColorKey(new Color(0.4f, 0.34f, 0.32f), 0.25f);    // Dawn (warm)
        ambColors[2] = new GradientColorKey(new Color(0.42f, 0.5f, 0.6f), 0.5f);      // Noon (lower fill → harsher shadows)
        ambColors[3] = new GradientColorKey(new Color(0.36f, 0.27f, 0.26f), 0.75f);   // Sunset (warm)
        ambColors[4] = new GradientColorKey(new Color(0.03f, 0.04f, 0.09f), 1.0f);    // Night (deep navy)
        
        GradientAlphaKey[] ambAlphas = new GradientAlphaKey[2] {
            new GradientAlphaKey(1.0f, 0.0f),
            new GradientAlphaKey(1.0f, 1.0f)
        };
        ambientColor.SetKeys(ambColors, ambAlphas);

        // 3. Initialize Fog Colors Gradient
        fogColors = new Gradient();
        GradientColorKey[] fColors = new GradientColorKey[5];
        fColors[0] = new GradientColorKey(new Color(0.05f, 0.06f, 0.13f), 0.0f);      // Night (dark indigo)
        fColors[1] = new GradientColorKey(new Color(0.85f, 0.65f, 0.5f), 0.25f);      // Dawn (warm haze)
        fColors[2] = new GradientColorKey(new Color(0.74f, 0.83f, 0.92f), 0.5f);      // Noon (light bright haze)
        fColors[3] = new GradientColorKey(new Color(0.8f, 0.5f, 0.4f), 0.75f);        // Sunset (warm haze)
        fColors[4] = new GradientColorKey(new Color(0.05f, 0.06f, 0.13f), 1.0f);      // Night (dark indigo)
        
        GradientAlphaKey[] fogAlphas = new GradientAlphaKey[2] {
            new GradientAlphaKey(1.0f, 0.0f),
            new GradientAlphaKey(1.0f, 1.0f)
        };
        fogColors.SetKeys(fColors, fogAlphas);
    }
}