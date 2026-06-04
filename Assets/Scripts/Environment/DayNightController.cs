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

        // 3. Dynamic Fog Density (thicker at dawn/night for moody cyberpunk atmosphere)
        if (controlFog)
        {
            // Thicker fog at night/dawn (using sine wave mapped to positive range)
            float fogFactor = Mathf.Sin(currentTimeOfDay * Mathf.PI * 2);
            float targetDensity = Mathf.Lerp(maxFogDensity * 0.3f, maxFogDensity, (fogFactor + 1f) / 2f);
            RenderSettings.fogDensity = targetDensity;
        }
    }

    // Called by ShiftManager when it owns the clock (externalTimeControl = true).
    public void SetTimeOfDay(float t)
    {
        currentTimeOfDay = Mathf.Clamp01(t);
    }

    void UpdateSun()
    {
        if (sun == null) return;

        // Lazy initialize the sun's base intensity if we are in Edit Mode
        if (sunInitialIntensity == 0)
        {
            sunInitialIntensity = sun.intensity > 0 ? sun.intensity : 1f;
        }

        // Rotate the sun across the sky
        sun.transform.localRotation = Quaternion.Euler((currentTimeOfDay * 360f) - 90, 170, 0);

        // Calculate Sun Intensity
        float intensityMultiplier = 1;
        if (currentTimeOfDay <= 0.23f || currentTimeOfDay >= 0.75f)
        {
            intensityMultiplier = 0;
        }
        else if (currentTimeOfDay <= 0.25f)
        {
            intensityMultiplier = Mathf.Clamp01((currentTimeOfDay - 0.23f) * (1 / 0.02f));
        }
        else if (currentTimeOfDay >= 0.73f)
        {
            intensityMultiplier = Mathf.Clamp01(1 - ((currentTimeOfDay - 0.73f) * (1 / 0.02f)));
        }

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
        sunColors[0] = new GradientColorKey(Color.black, 0.0f);                      // Night
        sunColors[1] = new GradientColorKey(new Color(1f, 0.6f, 0.2f), 0.25f);       // Dawn (orange)
        sunColors[2] = new GradientColorKey(new Color(1f, 1f, 0.9f), 0.5f);          // Noon (bright yellow/white)
        sunColors[3] = new GradientColorKey(new Color(0.9f, 0.3f, 0.1f), 0.75f);     // Sunset (warm red-orange)
        sunColors[4] = new GradientColorKey(Color.black, 1.0f);                      // Night
        
        GradientAlphaKey[] sunAlphas = new GradientAlphaKey[2] {
            new GradientAlphaKey(1.0f, 0.0f),
            new GradientAlphaKey(1.0f, 1.0f)
        };
        sunColor.SetKeys(sunColors, sunAlphas);

        // 2. Initialize Ambient Color Gradient (pitch dark at night)
        ambientColor = new Gradient();
        GradientColorKey[] ambColors = new GradientColorKey[5];
        ambColors[0] = new GradientColorKey(new Color(0.015f, 0.015f, 0.06f), 0.0f);  // Night (deep navy)
        ambColors[1] = new GradientColorKey(new Color(0.64f, 0.44f, 0.31f), 0.25f);   // Dawn (soft peach)
        ambColors[2] = new GradientColorKey(new Color(0.69f, 0.75f, 0.82f), 0.5f);    // Noon (bright grey-blue)
        ambColors[3] = new GradientColorKey(new Color(0.63f, 0.31f, 0.25f), 0.75f);   // Sunset (warm orange/red)
        ambColors[4] = new GradientColorKey(new Color(0.015f, 0.015f, 0.06f), 1.0f);  // Night (deep navy)
        
        GradientAlphaKey[] ambAlphas = new GradientAlphaKey[2] {
            new GradientAlphaKey(1.0f, 0.0f),
            new GradientAlphaKey(1.0f, 1.0f)
        };
        ambientColor.SetKeys(ambColors, ambAlphas);

        // 3. Initialize Fog Colors Gradient
        fogColors = new Gradient();
        GradientColorKey[] fColors = new GradientColorKey[5];
        fColors[0] = new GradientColorKey(new Color(0.015f, 0.015f, 0.05f), 0.0f);    // Night (dark indigo)
        fColors[1] = new GradientColorKey(new Color(0.64f, 0.44f, 0.31f), 0.25f);     // Dawn
        fColors[2] = new GradientColorKey(new Color(0.69f, 0.75f, 0.82f), 0.5f);      // Noon
        fColors[3] = new GradientColorKey(new Color(0.63f, 0.31f, 0.25f), 0.75f);     // Sunset
        fColors[4] = new GradientColorKey(new Color(0.015f, 0.015f, 0.05f), 1.0f);    // Night (dark indigo)
        
        GradientAlphaKey[] fogAlphas = new GradientAlphaKey[2] {
            new GradientAlphaKey(1.0f, 0.0f),
            new GradientAlphaKey(1.0f, 1.0f)
        };
        fogColors.SetKeys(fColors, fogAlphas);
    }
}