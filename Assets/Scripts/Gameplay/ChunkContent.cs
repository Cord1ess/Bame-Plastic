using System.Collections.Generic;
using UnityEngine;

/// Hosts a road-chunk's world content so it RIDES THE TREADMILL POOL. Content is built once as
/// CHILDREN of the chunk (at load), then only repositioned/reset when the chunk is reused — never
/// Instantiated mid-game. Because it's parented under the chunk root it also travels correctly when
/// the chunk is recycled and (later) when FloatingOrigin shifts the world.
///
/// LevelLayoutGenerator calls OnActivated() each time the chunk goes live (first spawn or reuse) —
/// the same "I'm live now, reset yourself" signal as TriggerExit.ReArm(). It's auto-added to pooled
/// chunks (toggle on the generator); add it to a chunk prefab yourself to tune per-chunk.
///
/// For now it spawns a placeholder roadside CROWD; the same pattern will host bus stops + waiting
/// passengers next.
public class ChunkContent : MonoBehaviour
{
    [Header("Roadside crowd (placeholder)")]
    [Tooltip("People per chunk. 0 = none.")]
    public int crowdCount = 6;
    public float characterHeight = 1.8f;
    [Tooltip("Scatter radius around the road anchor.")]
    public float crowdRadius = 8f;
    [Tooltip("Offset to the side of the road anchor (approximate — precise roadside comes with authored stop points later).")]
    public float sideOffset = 7f;

    static readonly Color[] Palette =
    {
        new Color(0.85f,0.25f,0.25f), new Color(0.25f,0.5f,0.85f), new Color(0.95f,0.8f,0.25f),
        new Color(0.3f,0.75f,0.4f),   new Color(0.7f,0.4f,0.8f),   new Color(0.95f,0.55f,0.2f),
        new Color(0.9f,0.9f,0.9f),    new Color(0.4f,0.7f,0.75f),
    };

    readonly List<BillboardCharacter> _crowd = new List<BillboardCharacter>();
    bool _built;

    void Start() { EnsureBuilt(); }

    // Called by LevelLayoutGenerator when this chunk is placed into play (first spawn or reuse).
    public void OnActivated()
    {
        EnsureBuilt();
        // Reuse just resets cheap state (no Instantiate) — re-randomise colours so reused chunks differ.
        for (int i = 0; i < _crowd.Count; i++)
            if (_crowd[i] != null) _crowd[i].SetColor(RandomColor());
    }

    void EnsureBuilt()
    {
        if (_built) return;
        _built = true;
        if (crowdCount <= 0) return;

        // Anchor near the chunk's exit trigger — a known on-road point in every chunk.
        Vector3 anchorWorld = FindAnchor().position;

        for (int i = 0; i < crowdCount; i++)
        {
            BillboardCharacter bc = BillboardCharacter.Create("Crowd_" + i, RandomColor(), characterHeight, Vector3.zero, transform);
            float ang = i * 137.5f * Mathf.Deg2Rad;                         // golden-angle scatter
            float rad = crowdRadius * (0.35f + 0.65f * Mathf.Repeat(i * 0.37f, 1f));
            // Offsets are in METRES (world space), so spacing is independent of the chunk's scale.
            Vector3 worldOffset = new Vector3(Mathf.Cos(ang) * rad + sideOffset, 0f, Mathf.Sin(ang) * rad);
            bc.transform.position = anchorWorld + worldOffset;              // child of chunk -> rides it forever
            _crowd.Add(bc);
        }
    }

    Transform FindAnchor()
    {
        TriggerExit te = GetComponentInChildren<TriggerExit>(true);
        return te != null ? te.transform : transform;
    }

    static Color RandomColor() => Palette[Random.Range(0, Palette.Length)];
}
