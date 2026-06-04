using UnityEngine;

/// Placeholder traffic/obstacle that penalises the bus on contact.
/// Setup: give the prefab a Collider with "Is Trigger" ON. Detection mirrors TriggerExit
/// (it looks for a BusController on whatever enters), so no special layer/tag is required.
public class Obstacle : MonoBehaviour
{
    [Tooltip("Fraction of speed kept after a hit (0.5 = lose half your speed, 0 = dead stop).")]
    [Range(0f, 1f)] public float speedAfterHit = 0.5f;

    [Tooltip("Destroy this obstacle once hit (e.g. a knocked-over cone). Leave off for persistent props.")]
    public bool destroyOnHit = false;

    void OnTriggerEnter(Collider other)
    {
        BusController bus = other.GetComponentInParent<BusController>();
        if (bus == null) return;

        bus.ApplyImpact(speedAfterHit);
        if (destroyOnHit) Destroy(gameObject);
    }
}
