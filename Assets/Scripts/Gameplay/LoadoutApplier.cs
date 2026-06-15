using UnityEngine;
using BamePlastic.Net;

/// Applies the logged-in account's purchased loadout to the live game: the equipped BUS COLOUR (tints the bus
/// body), the equipped CONDUCTOR skin, and owned UPGRADES (engine = top speed, capacity = standing room).
/// Play-only, auto-spawned in the game scene; no-ops for a guest. Re-applies the colour whenever the account
/// changes (so equipping a different colour in the shop updates the bus immediately, even though the shop runs
/// in the same scene as the menu) and whenever the bus (re)appears.
public class LoadoutApplier : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != SceneFlow.Game) return;
        if (FindAnyObjectByType<LoadoutApplier>() != null) return;
        var go = new GameObject("LoadoutApplier");
        go.AddComponent<LoadoutApplier>();
    }

    // bus colour ids → tint (mirrors StoreCatalog bus_color items)
    public static Color BusColor(string id) => id switch
    {
        "bus_red"    => new Color(0.85f, 0.27f, 0.22f),
        "bus_blue"   => new Color(0.30f, 0.55f, 0.85f),
        "bus_gold"   => new Color(0.92f, 0.74f, 0.25f),
        "bus_purple" => new Color(0.45f, 0.30f, 0.65f),
        "bus_black"  => new Color(0.14f, 0.14f, 0.18f),
        _            => new Color(0.28f, 0.55f, 0.30f),   // classic green (default)
    };

    bool _upgradesApplied;
    string _appliedColor;            // last bus-colour id we tinted (re-tint only on change)
    BusController _bus;

    void OnEnable()  { PlayerAccount.Changed += OnAccountChanged; }
    void OnDisable() { PlayerAccount.Changed -= OnAccountChanged; }

    void OnAccountChanged() { _appliedColor = null; }   // force a re-tint on the next Update

    void Update()
    {
        if (!PlayerAccount.LoggedIn) return;             // guest → leave defaults (re-checks if they log in later)

        if (_bus == null) _bus = BusController.Instance;
        if (_bus == null) return;                        // wait for the bus

        // (re)apply the bus colour whenever the equipped id differs from what's on the bus now
        if (_appliedColor != PlayerAccount.EquippedBus)
        {
            ApplyBusColor(_bus);
            _appliedColor = PlayerAccount.EquippedBus;
        }

        // upgrades are one-shot stat bumps (don't stack them every change)
        if (!_upgradesApplied) { ApplyUpgrades(_bus); _upgradesApplied = true; }
    }

    void ApplyBusColor(BusController bus)
    {
        Transform model = bus.busModel != null ? bus.busModel : bus.transform;
        Color tint = BusColor(PlayerAccount.EquippedBus);
        var rends = model.GetComponentsInChildren<MeshRenderer>();
        if (rends == null || rends.Length == 0) return;

        // tint the LARGEST renderer (the body shell) + any of comparable size; skip small parts (wheels/trim/glass).
        float maxVol = 0f;
        foreach (var r in rends) { float v = Vol(r.bounds.size); if (v > maxVol) maxVol = v; }
        int tinted = 0;
        foreach (var r in rends)
        {
            if (Vol(r.bounds.size) < maxVol * 0.35f) continue;
            foreach (var mat in r.materials)             // tint EVERY submaterial on the body renderer
            {
                if (mat == null) continue;
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
                if (mat.HasProperty("_Color"))     mat.SetColor("_Color", tint);
                tinted++;
            }
        }
        if (tinted == 0)
            Debug.LogWarning("[Loadout] No bus body material had _BaseColor/_Color to tint.");
    }

    static float Vol(Vector3 s) => s.x * s.y * s.z;

    void ApplyUpgrades(BusController bus)
    {
        // upgrades only apply when EQUIPPED (owned but toggled on) — the player chooses their loadout.
        if (PlayerAccount.IsUpgradeEquipped("upg_engine")) bus.maxSpeed *= 1.15f;       // +15% top speed
        if (PlayerAccount.IsUpgradeEquipped("upg_brakes")) bus.brakeRate *= 1.30f;      // stronger brakes
        if (PlayerAccount.IsUpgradeEquipped("upg_capacity"))
        {
            var bp = BusPassengers.Instance;
            if (bp != null) bp.standCapacity += 6;                          // more standing room
        }
        if (PlayerAccount.IsUpgradeEquipped("upg_horn"))
        {
            var audio = FindAnyObjectByType<BusAudio>();
            if (audio != null) { audio.hornVolume = Mathf.Min(1f, audio.hornVolume * 1.4f); audio.hornNudgeRange *= 1.5f; }
        }
    }
}
