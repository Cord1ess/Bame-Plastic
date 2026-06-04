using System.Collections.Generic;
using UnityEngine;

/// Spawns placeholder traffic/obstacles ahead of the bus and recycles them once passed.
/// Lightweight stand-in traffic for the free-roam track. Tune spawnAhead / lateralSpread to your road width.
public class TrafficSpawner : MonoBehaviour
{
    [Header("References")]
    public Transform bus;                       // the player bus root
    public GameObject[] obstaclePrefabs;        // e.g. parked car / CNG / rickshaw placeholders

    [Header("Spawning")]
    [Tooltip("Distance ahead of the bus to spawn. Keep modest so turns don't push spawns off the road.")]
    public float spawnAhead = 45f;
    [Tooltip("Random sideways offset from the bus's heading line (tune to road width).")]
    public float lateralSpread = 4f;
    [Tooltip("Seconds between spawn attempts.")]
    public float spawnInterval = 1.2f;
    [Tooltip("Maximum obstacles alive at once.")]
    public int maxActive = 12;

    [Header("Ground Seating")]
    [Tooltip("Layers counted as drivable ground; spawned obstacles are dropped onto it (set to your 'Ground' layer).")]
    public LayerMask groundMask;

    [Header("Recycle")]
    [Tooltip("Destroy an obstacle once it's this far behind the bus.")]
    public float despawnBehind = 30f;

    readonly List<GameObject> _active = new List<GameObject>();
    float _timer;

    void Update()
    {
        if (bus == null || obstaclePrefabs == null || obstaclePrefabs.Length == 0) return;

        // Recycle obstacles the bus has driven past (or that were destroyed on hit).
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            if (_active[i] == null) { _active.RemoveAt(i); continue; }
            Vector3 toObj = _active[i].transform.position - bus.position;
            if (Vector3.Dot(bus.forward, toObj) < -despawnBehind)
            {
                Destroy(_active[i]);
                _active.RemoveAt(i);
            }
        }

        _timer -= Time.deltaTime;
        if (_timer <= 0f && _active.Count < maxActive)
        {
            _timer = spawnInterval;
            SpawnOne();
        }
    }

    void SpawnOne()
    {
        Vector3 right = Vector3.Cross(Vector3.up, bus.forward).normalized;
        Vector3 pos = bus.position
                    + bus.forward * spawnAhead
                    + right * Random.Range(-lateralSpread, lateralSpread);

        // Drop onto the road surface if we can find it under the spawn point.
        if (groundMask.value != 0 &&
            Physics.Raycast(pos + Vector3.up * 10f, Vector3.down, out RaycastHit hit, 50f, groundMask))
        {
            pos.y = hit.point.y;
        }

        GameObject prefab = obstaclePrefabs[Random.Range(0, obstaclePrefabs.Length)];
        _active.Add(Instantiate(prefab, pos, Quaternion.LookRotation(bus.forward, Vector3.up)));
    }
}
