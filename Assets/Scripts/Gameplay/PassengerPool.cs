using System.Collections.Generic;
using UnityEngine;

/// Global pool of placeholder passengers, pre-built at load so nothing is Instantiated mid-game (keeps
/// streaming smooth). Bus stops borrow waiting passengers via Take(); they're Returned when a stop gives
/// up un-boarded ones, or when an aboard passenger leaves the bus. SplineStopSpawner auto-creates one if
/// none exists; you don't place it by hand.
public class PassengerPool : MonoBehaviour
{
    public static PassengerPool Instance { get; private set; }

    public int poolSize = 250;
    public float passengerHeight = 1.8f;

    readonly Queue<Passenger> _free = new Queue<Passenger>();
    Passenger[] _all;     // indexed by PoolIndex — lets multiplayer look up "passenger N" identically on every client

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        _all = new Passenger[poolSize];
        for (int i = 0; i < poolSize; i++) { var p = CreateOne(i); _all[i] = p; _free.Enqueue(p); }
    }

    Passenger CreateOne(int i)
    {
        BillboardCharacter v = BillboardCharacter.Create("Passenger_" + i, Color.grey, passengerHeight, Vector3.zero, transform);
        Passenger p = v.gameObject.AddComponent<Passenger>();
        p.PoolIndex = i;                 // stable id — same passenger N on every client (deterministic pool)
        p.Setup(v);
        p.Hide();
        return p;
    }

    /// Look up a passenger by its pool index (multiplayer mirroring). Null if out of range.
    public Passenger ByIndex(int i) => (_all != null && i >= 0 && i < _all.Length) ? _all[i] : null;

    /// ALL pooled passengers (active + free). Iterate + check `state` instead of FindObjectsByType<Passenger>
    /// every frame (no scene scan, no allocation) — used by the conductor grab/call/AI scans.
    public IReadOnlyList<Passenger> All => _all;

    public Passenger Take()
    {
        return _free.Count > 0 ? _free.Dequeue() : null;
    }

    public void Return(Passenger p)
    {
        if (p == null) return;
        p.Hide();
        p.transform.SetParent(transform, false);
        _free.Enqueue(p);
    }
}
