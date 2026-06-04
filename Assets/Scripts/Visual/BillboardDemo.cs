using UnityEngine;

/// TEST HELPER — scatters a crowd of placeholder billboard characters near this object so you can
/// drive past them and check the billboarding + colours. Drop it on an empty GameObject placed at
/// ground level. Not used by any gameplay system; delete it once the real passenger/stop system is in.
public class BillboardDemo : MonoBehaviour
{
    public int count = 24;
    public float characterHeight = 1.8f;
    [Tooltip("Half-extents (X, Z) of the area around this object to scatter the crowd in.")]
    public Vector2 areaHalfExtents = new Vector2(14f, 22f);
    [Tooltip("Keep this radius around the centre clear (where the bus starts).")]
    public float clearRadius = 4f;

    static readonly Color[] Palette =
    {
        new Color(0.85f, 0.25f, 0.25f), new Color(0.25f, 0.5f, 0.85f), new Color(0.95f, 0.8f, 0.25f),
        new Color(0.3f, 0.75f, 0.4f),   new Color(0.7f, 0.4f, 0.8f),   new Color(0.95f, 0.55f, 0.2f),
        new Color(0.9f, 0.9f, 0.9f),    new Color(0.4f, 0.7f, 0.75f),
    };

    void Start()
    {
        Vector3 c = transform.position;
        int placed = 0, guard = 0;
        while (placed < count && guard < count * 20)
        {
            guard++;
            float x = Random.Range(-areaHalfExtents.x, areaHalfExtents.x);
            float z = Random.Range(-areaHalfExtents.y, areaHalfExtents.y);
            if (x * x + z * z < clearRadius * clearRadius) continue;     // don't spawn on the bus

            Vector3 pos = c + new Vector3(x, 0f, z);
            Color col = Palette[Random.Range(0, Palette.Length)];
            BillboardCharacter.Create("Crowd_" + placed, col, characterHeight, pos, transform);
            placed++;
        }
    }
}
