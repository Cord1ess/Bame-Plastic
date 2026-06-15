using UnityEngine;

/// One competing company bus on the shift's money leaderboard — an ADAPTIVE "rubber-band" earner that always
/// keeps the pressure on. Each rival targets a multiple of the PLAYER's current earnings (its `aggression`):
/// when it's behind that target it SURGES (earns fast, can shoot ahead of you and taunt), and when it's ahead it
/// EASES OFF (gives you a window to overtake). So the board breathes — a leader pulls away, you claw back, it
/// surges again. It earns in REAL FARE-TIER JUMPS (10/20/30/50), not a linear trickle, so the numbers look real.
/// The HUD reads `Taka` (shown as Bhara on the board).
[System.Serializable]
public class RivalBus
{
    public string name = "Rival";

    [Tooltip("How hard this rival pushes: its earnings TARGET = aggression × the player's current earnings. " +
             ">1 tends to lead the player, <1 trails. Each rival gets a slightly different value so the pack spreads.")]
    public float aggression = 1.0f;

    [Tooltip("Base taka/sec this rival earns even before the rubber-band kicks in (so it still climbs early when " +
             "the player has near-zero earnings and there's no target to chase yet).")]
    public float baseEarnPerSec = 6f;

    [Tooltip("How fast it closes the gap to its target (taka/sec per taka behind). Higher = snappier catch-up.")]
    public float catchUpRate = 0.35f;

    // Runtime
    [HideInInspector] public int earnings;
    float _nextFareIn = -1f;  // seconds until this rival collects its next FARE (a 10/20/30/50 tier jump)
    float _surgeTimer;        // burst-of-effort timer so surges come in believable waves, not a smooth ramp
    float _surgeMul = 1f;

    [Tooltip("Legacy: when true a physical RivalBrain bus drove this entry. The adaptive model no longer needs " +
             "it (physical rivals are road presence only), but kept so old links don't break.")]
    [HideInInspector] public bool drivenByAgent;

    public void ResetEarnings() { earnings = 0; _nextFareIn = -1f; _surgeTimer = 0f; _surgeMul = 1f; }

    /// A linked RivalBrain calls this when its bus grabs passengers at a stop — real, earned taka ON TOP of the
    /// adaptive sim (a physical rival that actually camps a stop earns a bonus, but the sim guarantees pressure).
    public void AddEarnings(int taka) { earnings += Mathf.Max(0, taka); }

    /// Adaptive tick. `playerEarnings` is the live player taka the rubber-band targets.
    public void Tick(float dt, int playerEarnings)
    {
        // occasional surge waves: every few seconds re-roll a short multiplier so earning comes in bursts.
        _surgeTimer -= dt;
        if (_surgeTimer <= 0f)
        {
            _surgeTimer = Random.Range(2.5f, 5f);
            _surgeMul = Random.Range(0.5f, 1.8f);     // sometimes loafs, sometimes floors it
        }

        float target = Mathf.Max(0, playerEarnings) * Mathf.Max(0.1f, aggression);
        float gap = target - earnings;                // + = behind its target (push), - = ahead (ease off)

        // The adaptive rubber-band controls HOW OFTEN this rival collects a fare, not a continuous trickle. It
        // earns in REAL TIER JUMPS (10/20/30/50) like the player, so the board climbs in believable chunks.
        // effortRate ≈ taka/sec it "wants" to earn; the interval between fares = avgFare / effortRate.
        float effortRate = (baseEarnPerSec + gap * catchUpRate) * _surgeMul;
        effortRate = Mathf.Clamp(effortRate, 0.5f, baseEarnPerSec * 6f + 200f);   // never stalls fully / never insane

        if (_nextFareIn < 0f) _nextFareIn = NextInterval(effortRate);
        _nextFareIn -= dt;
        if (_nextFareIn <= 0f)
        {
            // collect ONE fare = a random tier, so earnings jump 10/20/30/50 (never odd linear amounts).
            earnings += Passenger.FareTiers[Random.Range(0, Passenger.FareTiers.Length)];
            _nextFareIn = NextInterval(effortRate);
        }
    }

    // seconds until the next fare so the long-run rate ≈ effortRate (avg tier ≈ 27.5 taka), with a little jitter.
    static float NextInterval(float effortRate)
    {
        const float avgFare = 27.5f;                  // mean of {10,20,30,50}
        float interval = avgFare / Mathf.Max(0.5f, effortRate);
        return interval * Random.Range(0.6f, 1.4f);
    }

    /// Back-compat overload (no player context) — falls back to a gentle base climb.
    public void Tick(float dt) => Tick(dt, earnings);

    public int Taka => earnings;
}
