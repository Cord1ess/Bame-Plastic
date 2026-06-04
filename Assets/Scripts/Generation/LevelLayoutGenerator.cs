using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LevelLayoutGenerator : MonoBehaviour
{
    public LevelChunkData[] levelChunkData;
    public LevelChunkData firstChunk;

    private LevelChunkData previousChunk;

    public Vector3 spawnOrigin;

    private Vector3 spawnPosition;
    public int chunksToSpawn = 10;

    [Tooltip("Spare instances built per chunk variant at load. Higher = more memory but guarantees nothing is built mid-game. Raise it if you still see an occasional early-game hitch.")]
    public int prewarmPerVariant = 4;

    [Tooltip("Auto-add ChunkContent (roadside crowd placeholder) to pooled chunks, so world content rides the treadmill with zero setup. Turn off to place ChunkContent yourself or to disable crowd.")]
    public bool autoPopulateChunks = true;

    // Treadmill pool: every chunk instance is built ONCE, then reused by repositioning it.
    // Nothing is Instantiated/Destroyed or toggled active/inactive during play — that re-activation
    // (and the ProBuilder mesh rebuild it triggers) was the remaining stutter. Idle chunks are
    // parked far away but left active, so reusing one is just a transform move.
    private Dictionary<GameObject, Queue<GameObject>> pool = new Dictionary<GameObject, Queue<GameObject>>();
    private static readonly Vector3 Garage = new Vector3(0f, -100000f, 0f);

    void OnEnable()
    {
        TriggerExit.OnChunkExited += PickAndSpawnChunk;
        TriggerExit.OnChunkRecycle += RecycleChunk;
    }

    private void OnDisable()
    {
        TriggerExit.OnChunkExited -= PickAndSpawnChunk;
        TriggerExit.OnChunkRecycle -= RecycleChunk;
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.T))
        {
            PickAndSpawnChunk();
        }
    }

    void Start()
    {
        Prewarm();                 // build all instances up front (during load)

        previousChunk = firstChunk;

        for (int i = 0; i < chunksToSpawn; i++)
        {
            PickAndSpawnChunk();   // reuses the prewarmed instances
        }
    }

    // Build every variant's spare instances now, while the scene is loading, so no chunk is ever
    // Instantiated during gameplay (the one-time build/mesh cost happens here, not mid-drive).
    void Prewarm()
    {
        for (int t = 0; t < levelChunkData.Length; t++)
        {
            if (levelChunkData[t] == null) continue;
            GameObject[] variants = levelChunkData[t].levelChunks;
            for (int v = 0; v < variants.Length; v++)
            {
                if (variants[v] == null) continue;
                for (int k = 0; k < prewarmPerVariant; k++)
                {
                    CreateInstance(variants[v]);  // built active, parked, queued
                }
            }
        }
    }

    LevelChunkData PickNextChunk()
    {
        List<LevelChunkData> allowedChunkList = new List<LevelChunkData>();
        LevelChunkData nextChunk = null;

        LevelChunkData.Direction nextRequiredDirection = LevelChunkData.Direction.North;

        switch (previousChunk.exitDirection)
        {
            case LevelChunkData.Direction.North:
                nextRequiredDirection = LevelChunkData.Direction.South;
                spawnPosition = spawnPosition + new Vector3(0f, 0, previousChunk.chunkSize.y);

                break;
            case LevelChunkData.Direction.East:
                nextRequiredDirection = LevelChunkData.Direction.West;
                spawnPosition = spawnPosition + new Vector3(previousChunk.chunkSize.x, 0, 0);
                break;
            case LevelChunkData.Direction.South:
                nextRequiredDirection = LevelChunkData.Direction.North;
                spawnPosition = spawnPosition + new Vector3(0, 0, -previousChunk.chunkSize.y);
                break;
            case LevelChunkData.Direction.West:
                nextRequiredDirection = LevelChunkData.Direction.East;
                spawnPosition = spawnPosition + new Vector3(-previousChunk.chunkSize.x, 0, 0);

                break;
            default:
                break;
        }

        for (int i = 0; i < levelChunkData.Length; i++)
        {
            if (levelChunkData[i] != null && levelChunkData[i].entryDirection == nextRequiredDirection)
            {
                allowedChunkList.Add(levelChunkData[i]);
            }
        }

        if (allowedChunkList.Count == 0)
        {
            Debug.LogError("⚠️ LevelLayoutGenerator: No matching chunks found! Please make sure you have assigned the five Level Chunk Data assets (East North, South East, etc.) inside the 'Level Chunk Data' array on the LevelGenerator inspector.");
            return null;
        }

        nextChunk = allowedChunkList[Random.Range(0, allowedChunkList.Count)];

        return nextChunk;

    }

    void PickAndSpawnChunk()
    {
        LevelChunkData chunkToSpawn = PickNextChunk();
        if (chunkToSpawn == null) return;

        GameObject objectFromChunk = chunkToSpawn.levelChunks[Random.Range(0, chunkToSpawn.levelChunks.Length)];
        previousChunk = chunkToSpawn;
        GetFromPool(objectFromChunk, spawnPosition + spawnOrigin);
    }

    // Reuse an idle (parked) instance of 'prefab' by moving it into place and re-arming its
    // trigger(s). No SetActive, no rebuild. Only Instantiates if the pool was somehow exhausted.
    GameObject GetFromPool(GameObject prefab, Vector3 position)
    {
        Queue<GameObject> q;
        if (pool.TryGetValue(prefab, out q) && q.Count > 0)
        {
            GameObject reused = q.Dequeue();
            reused.transform.SetPositionAndRotation(position, Quaternion.identity);
            ActivateChunk(reused);
            return reused;
        }

        // Fallback (rare): pool ran dry for this variant — raise prewarmPerVariant if you see this hitch.
        GameObject created = CreateInstance(prefab, position);
        ActivateChunk(created);
        return created;
    }

    // Re-arm a chunk's triggers and (re)activate its world content (crowd, and later bus stops) when
    // it's placed into play. Pooled chunks are never toggled active, so this is the explicit "I'm live
    // now" signal — the content rides the chunk and only resets here, never re-Instantiates mid-game.
    void ActivateChunk(GameObject chunk)
    {
        TriggerExit[] triggers = chunk.GetComponentsInChildren<TriggerExit>(true);
        for (int i = 0; i < triggers.Length; i++) triggers[i].ReArm();

        ChunkContent[] contents = chunk.GetComponentsInChildren<ChunkContent>(true);
        for (int i = 0; i < contents.Length; i++) contents[i].OnActivated();
    }

    // Make a brand-new instance (the only place we Instantiate). Built active.
    GameObject CreateInstance(GameObject prefab)
    {
        return CreateInstance(prefab, Garage, true);
    }

    GameObject CreateInstance(GameObject prefab, Vector3 position, bool park = false)
    {
        GameObject created = Instantiate(prefab, position, Quaternion.identity);
        PooledChunk marker = created.AddComponent<PooledChunk>();
        marker.sourcePrefab = prefab;

        // Ensure the chunk can host pooled world content (crowd now, bus stops later). Auto-added so the
        // endless chunks get it with zero editor setup; add ChunkContent to a prefab yourself to tune it.
        if (autoPopulateChunks && created.GetComponentInChildren<ChunkContent>(true) == null)
            created.AddComponent<ChunkContent>();

        if (park) Enqueue(prefab, created);
        return created;
    }

    // Return a chunk to its pool (called by TriggerExit once the bus has passed it + the delay).
    void RecycleChunk(GameObject chunk)
    {
        if (chunk == null) return;

        PooledChunk marker = chunk.GetComponent<PooledChunk>();
        if (marker == null || marker.sourcePrefab == null)
        {
            chunk.SetActive(false);  // pre-placed first chunk — fine to just hide
            return;
        }

        // Park it far away but KEEP IT ACTIVE — never toggling active is what makes reuse free.
        chunk.transform.position = Garage;
        Enqueue(marker.sourcePrefab, chunk);
    }

    void Enqueue(GameObject prefab, GameObject instance)
    {
        Queue<GameObject> q;
        if (!pool.TryGetValue(prefab, out q))
        {
            q = new Queue<GameObject>();
            pool[prefab] = q;
        }
        q.Enqueue(instance);
    }

    public void UpdateSpawnOrigin(Vector3 originDelta)
    {
        spawnOrigin = spawnOrigin + originDelta;
    }

}
