using System.Collections.Generic;
using UnityEngine;

/// Sets up the marked RIVAL company buses (L4). Rivals are NOT a separate system — each is a normal
/// TrafficSystem vehicle (a bus) with a RivalBrain attached, so it drives, curves, avoids and collides
/// exactly like ambient traffic and can't drift "all over the place". This component just tells the
/// TrafficSystem which rivals to deploy (names + colours) once it's ready, then gets out of the way;
/// TrafficSystem keeps them alive ahead of the player for the whole shift.
[RequireComponent(typeof(TrafficSystem))]
public class RivalManager : MonoBehaviour
{
    [System.Serializable]
    public struct RivalDef { public string name; public Color color; }

    [Header("Rivals")]
    // Names MUST match entries in ShiftManager.GenerateDefaultRivals so a physical rival bus LINKS to an existing
    // standings entry (and earns a real-fare bonus on top of its adaptive sim) instead of creating a 6th board row.
    // We put physical buses on the strongest leaders so the bus you SEE on the road is one you're actually racing.
    public List<RivalDef> rivals = new List<RivalDef>
    {
        new RivalDef { name = "Balaka",         color = new Color(0.85f, 0.2f, 0.25f) },
        new RivalDef { name = "Victor Classic", color = new Color(0.2f, 0.45f, 0.9f) },
        new RivalDef { name = "Raida",          color = new Color(0.95f, 0.7f, 0.15f) },
    };

    TrafficSystem _traffic;
    int _deployed;          // how many rivals have been spawned so far

    void Awake() { _traffic = GetComponent<TrafficSystem>(); }

    void Update()
    {
        if (!Application.isPlaying || _deployed >= rivals.Count) return;
        // deploy the next rival; SpawnRival returns null until the traffic system is ready, so just retry
        // next frame. Each rival is spawned exactly once (indexed by _deployed).
        if (_traffic.SpawnRival(rivals[_deployed].name, rivals[_deployed].color) != null) _deployed++;
    }
}
