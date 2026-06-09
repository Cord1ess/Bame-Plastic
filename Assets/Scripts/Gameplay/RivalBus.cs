using UnityEngine;

/// One competing company bus on the shift's money leaderboard.
///
/// For now rivals are *simulated* earners — a believable number that climbs unevenly through the
/// shift. The HUD/leaderboard only reads `Taka`, so later you can replace this with a real AI bus
/// (or a networked player) without touching any UI. Plain serializable data + a per-frame tick.
[System.Serializable]
public class RivalBus
{
    public string name = "Rival";

    [Tooltip("Baseline taka earned per second.")]
    public float earnRate = 8f;

    [Tooltip("How much the per-second earning randomly varies (0 = steady, 1 = very bursty).")]
    [Range(0f, 1f)]
    public float burstiness = 0.5f;

    // Runtime
    [HideInInspector] public float earnings;
    [Tooltip("When true, a physical rival bus (RivalBrain) drives this rival's earnings (real fares stolen at " +
             "stops) and the simulated Tick is skipped. Set by the brain when it links to this standings entry.")]
    [HideInInspector] public bool drivenByAgent;

    public void ResetEarnings() { earnings = 0f; }

    /// A linked RivalBrain calls this when its bus grabs passengers at a stop — real, earned taka.
    public void AddEarnings(int taka) { earnings += Mathf.Max(0, taka); }

    public void Tick(float dt)
    {
        if (drivenByAgent) return;     // a real agent drives this rival's earnings; no simulation
        // Random multiplier around 1.0 so each rival's total climbs unevenly (feels alive).
        float jitter = 1f + Random.Range(-burstiness, burstiness);
        earnings += earnRate * jitter * dt;
        if (earnings < 0f) earnings = 0f;
    }

    public int Taka => Mathf.RoundToInt(earnings);
}
