using UnityEngine;

/// Tags a spawned chunk with the prefab it came from, so the pool can return it to the right
/// bucket when it's recycled. Added automatically by LevelLayoutGenerator — you don't add this
/// in the editor.
public class PooledChunk : MonoBehaviour
{
    public GameObject sourcePrefab;
}
