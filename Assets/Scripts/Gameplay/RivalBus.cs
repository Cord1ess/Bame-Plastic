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

    [Tooltip("Average seconds between this rival collecting a fare (it earns in 10/20/30/50 TIER jumps, never " +
             "tiny fractions — matches the player's fare model).")]
    public float fareInterval = 4f;

    // Runtime
    [HideInInspector] public int earnings;
    float _nextFareIn = -1f;
    [Tooltip("When true, a physical rival bus (RivalBrain) drives this rival's earnings (real fares stolen at " +
             "stops) and the simulated Tick is skipped. Set by the brain when it links to this standings entry.")]
    [HideInInspector] public bool drivenByAgent;

    public void ResetEarnings() { earnings = 0; _nextFareIn = -1f; }

    /// A linked RivalBrain calls this when its bus grabs passengers at a stop — real, earned taka.
    public void AddEarnings(int taka) { earnings += Mathf.Max(0, taka); }

    public void Tick(float dt)
    {
        if (drivenByAgent) return;     // a real agent drives this rival's earnings; no simulation
        if (_nextFareIn < 0f) _nextFareIn = Random.Range(fareInterval * 0.5f, fareInterval * 1.5f);
        _nextFareIn -= dt;
        if (_nextFareIn <= 0f)
        {
            // collect ONE fare = a random tier (10/20/30/50), so the leaderboard jumps cleanly like the player's.
            earnings += Passenger.FareTiers[Random.Range(0, Passenger.FareTiers.Length)];
            _nextFareIn = Random.Range(fareInterval * 0.5f, fareInterval * 1.5f);
        }
    }

    public int Taka => earnings;
}
