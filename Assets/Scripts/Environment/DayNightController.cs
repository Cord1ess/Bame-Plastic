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

    [Header("Sun brightness")]
    [Tooltip("Directional sun intensity at full DAY (noon). Higher = sunnier. The day curve scales this " +
             "0 (night) → this value (noon).")]
    public float dayIntensity = 2.4f;

    [Header("Sun geometry (rises in FRONT of the bus)")]
    [Tooltip("Light azimuth (deg). The light shines ALONG its +Z. ~180 makes it shine back toward the bus " +
             "from AHEAD — so the sun disc sits in FRONT of the bus (you drive toward the sunrise). A small " +
             "offset off 180 gives a nicer raking angle than dead-ahead.")]
    public float sunAzimuth = 188f;
    [Tooltip("Sun elevation at sunrise/sunset (deg above horizon, ~0) and at noon (deg). The sun arcs " +
             "between these so dawn is low-and-in-front and midday is high.")]
    public float sunNoonElevation = 62f;

    [Header("Shift schedule (phase fractions of the 0..1 shift)")]
    [Tooltip("Fraction of the shift spent ramping through SUNRISE at the start — keep small for a quick change.")]
    [Range(0f, 0.5f)] public float sunriseFraction = 0.04f;
    [Tooltip("Fraction spent in DUSK (noon→sunset) before night — small = quick.")]
    [Range(0f, 0.5f)] public float duskFraction = 0.07f;
    [Tooltip("Fraction that is NIGHT at the very end — small = quick. The big remainder is the NOON hold.")]
    [Range(0f, 0.5f)] public float nightFraction = 0.06f;

    // When true, something else (ShiftManager) sets currentTimeOfDay each frame so the day->night
    // cycle tracks the shift clock (dusk == shift end). Leave false for the standalone auto-cycle.
    [HideInInspector]
    public bool externalTimeControl = false;

    [Header("Day/Night Gradients")]
    public Gradient sunColor;        // Tint of the sun (e.g., orange sunset, yellow noon)
    public Gradient ambientColor;    // Ambient environment color (essential for dark night shadow shading!)
    public Gradient fogColors;       // Color of the atmospheric fog

    [Header("Smog (exponential² depth haze that hides distant objects — cheap, WebGL-safe)")]
    public bool controlFog = true;
    [Tooltip("The distance (m) at which the smog FULLY obscures (≈99% opaque). Set just past the building wall " +
             "(BuildingSpawner.spawnAhead, ~200-250) so the wall's far edge + any gaps dissolve into haze while " +
             "the near street stays crisp. Drives the exp² density: denser fog = shorter reach.")]
    public float smogFullDistance = 220f;
    [Tooltip("How much the fog colour matches the SKY (0 = pure smog tint, 1 = the time-of-day sky gradient). " +
             "Higher = the horizon void dissolves into the sky (no seam). ~0.7 kills the 'nothingness' at the " +
             "end of the street while keeping a warm haze.")]
    [Range(0f, 1f)] public float fogSkyMatch = 0.7f;
    [Tooltip("Smog is a touch thicker at dawn/dusk/night: how much the reach pulls IN away from noon " +
             "(0 = constant all day, 0.35 = noticeably hazier at the edges).")]
    [Range(0f, 0.8f)] public float smogTimeVariation = 0.35f;
    [Tooltip("Base smog tint (warm grey-brown Dhaka haze). The time-of-day fogColors gradient tints on top.")]
    public Color smogTint = new Color(0.62f, 0.6f, 0.55f, 1f);
    // legacy linear-fog fields kept for scene back-compat (no longer used directly; see smogFullDistance).
    [HideInInspector] public float smogStart = 40f;
    [HideInInspector] public float smogEnd = 240f;

    [Header("Skybox blending (optional)")]
    [Tooltip("Optional — auto-found. Crossfades cubemap skyboxes across the day in sync with this cycle.")]
    public SkyboxBlender skybox;

    void Start()
    {
        // Flat ambient so our per-frame ambientLight tint actually applies (Skybox/Gradient modes ignore it).
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;

        // EXPONENTIAL² fog = depth haze. Linear fog ramps uniformly → reads as a flat grey CURTAIN at the far
        // plane. exp² thickens NON-linearly with depth: the near street stays crisp, the distance dissolves
        // smoothly into haze (atmospheric, not a wall). Cheap (built-in per-vertex/fragment fog, no extra pass)
        // and fully WebGL-safe — no raymarched volumetrics. Density is driven per-frame in Update().
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

        // 2. Dynamic Ambient Environment Color (This makes the night pitch dark!)
        if (ambientColor != null)
        {
            RenderSettings.ambientLight = ambientColor.Evaluate(currentTimeOfDay);
        }

        // 1+3. SMOG: exponential² depth haze. Density set so it's ~fully opaque at smogFullDistance — pulled IN a
        //       bit toward dawn/dusk/night for a hazier edge. Colour = warm Dhaka tint, nudged toward the
        //       time-of-day gradient (sunset warmth) and dimmed at night, so the distance MELTS into the sky
        //       rather than ending at a hard fog wall.
        if (controlFog)
        {
            float fromNoon = Mathf.Clamp01(Mathf.Abs(currentTimeOfDay - 0.5f) / 0.5f);   // 0 noon .. 1 night
            float reach = Mathf.Max(20f, smogFullDistance * (1f - smogTimeVariation * fromNoon));
            // exp² fog factor = exp(-(density·dist)²); ≈99% obscured when density·dist ≈ 2.146. So to be fully
            // hazed at `reach`, density = 2.146 / reach.
            RenderSettings.fogDensity = 2.146f / reach;

            // Blend the warm smog tint TOWARD the time-of-day sky gradient. Higher fogSkyMatch = fog colour
            // closer to the sky → the ground/road/horizon dissolve into the SAME tone as the skybox, so the
            // 'nothingness' void at the end of the street disappears (no seam between fogged ground and sky).
            Color c = smogTint;
            if (fogColors != null) c = Color.Lerp(c, fogColors.Evaluate(currentTimeOfDay), fogSkyMatch);
            float nightDim = Mathf.Lerp(1f, 0.3f, fromNoon * fromNoon);   // bright by day, dim at night
            RenderSettings.fogColor = new Color(c.r * nightDim, c.g * nightDim, c.b * nightDim, 1f);
        }

        // 4. Skybox crossfade, in sync with the cycle (rotated so its baked sun ~aligns with our directional sun).
        if (skybox == null) skybox = FindAnyObjectByType<SkyboxBlender>();
        if (skybox != null) skybox.Apply(currentTimeOfDay, sunAzimuth);
    }

    // Called by ShiftManager when it owns the clock (externalTimeControl = true).
    public void SetTimeOfDay(float t)
    {
        currentTimeOfDay = Mathf.Clamp01(t);
    }

    /// True when it's dark enough for headlights: NIGHT and the EARLY MORNING before the sun rises (and dusk on).
    /// Matches the sun-intensity ramp (lights on while the sun is at/near zero). Bus lights read this.
    public bool LightsOn => currentTimeOfDay <= 0.26f || currentTimeOfDay >= 0.74f;

    /// 0 (full daylight) .. 1 (full dark) — for fading light intensity smoothly at the edges.
    public float Darkness
    {
        get
        {
            float dawn = Mathf.InverseLerp(0.30f, 0.20f, currentTimeOfDay);   // 1 before sunrise → 0 by full day
            float dusk = Mathf.InverseLerp(0.70f, 0.84f, currentTimeOfDay);   // 0 at day → 1 into night
            return Mathf.Clamp01(Mathf.Max(dawn, dusk));
        }
    }

    /// Drive the cycle from SHIFT PROGRESS (0 = start .. 1 = end). Tuned so transitions are QUICK and NOON
    /// is held the LONGEST: a fast sunrise ramps straight to noon, the day phase PINS time-of-day at noon
    /// (0.5) for its whole (large) duration, then a fast sunset→dusk→night at the very end.
    public void SetShiftProgress(float p)
    {
        p = Mathf.Clamp01(p);
        float rise = Mathf.Clamp01(sunriseFraction);
        float night = Mathf.Clamp01(nightFraction);
        float dusk = Mathf.Clamp01(duskFraction);
        float duskStart = Mathf.Max(rise, 1f - night - dusk);   // DAY = [rise .. duskStart] (the long noon hold)
        float nightStart = 1f - night;

        float tod;
        if (p < rise)                       // SUNRISE → noon, quick: 0.22 → 0.50
            tod = Mathf.Lerp(0.22f, 0.50f, Mathf.SmoothStep(0f, 1f, p / Mathf.Max(0.0001f, rise)));
        else if (p < duskStart)             // DAY: HOLD at noon (0.5) the entire time — the longest phase
            tod = 0.5f;
        else if (p < nightStart)            // DUSK: noon → sunset, quick: 0.50 → 0.78
            tod = Mathf.Lerp(0.50f, 0.78f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(duskStart, nightStart, p)));
        else                                // NIGHT: 0.78 → 0.97, quick
            tod = Mathf.Lerp(0.78f, 0.97f, Mathf.InverseLerp(nightStart, 1f, p));

        currentTimeOfDay = tod;
    }

    void UpdateSun()
    {
        if (sun == null) return;

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

        sun.intensity = dayIntensity * intensityMultiplier;

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