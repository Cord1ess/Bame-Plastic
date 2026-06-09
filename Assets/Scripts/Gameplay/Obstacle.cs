using UnityEngine;

/// Placeholder traffic/obstacle that penalises the bus on contact: sheds speed (ApplyImpact) and
/// damages bus health (ShiftManager.Damage). Put a trigger Collider on it. Detection is robust — it
/// matches the bus's physics sphere (by Rigidbody) or anything carrying BusController/BusTag.
public class Obstacle : MonoBehaviour
{
    [Tooltip("Fraction of speed kept after a hit (0.5 = lose half, 0 = dead stop).")]
    [Range(0f, 1f)] public float speedAfterHit = 0.5f;

    [Tooltip("Bus health lost per hit.")]
    public int damageOnHit = 12;

    [Tooltip("Hide this obstacle once hit (e.g. a knocked-over cone). Off = persistent traffic.")]
    public bool hideOnHit = false;

    [Tooltip("Min seconds between hits from THIS obstacle. Stops a drag/scrape registering 50x/sec.")]
    public float hitCooldown = 1f;

    float _lastHitTime = -999f;

    void OnTriggerEnter(Collider other) => TryHit(other);
    void OnTriggerStay(Collider other) => TryHit(other);   // also catch dragging, but the cooldown gates it

    void TryHit(Collider other)
    {
        if (Time.time - _lastHitTime < hitCooldown) return;   // debounce: one hit per cooldown, even while dragging

        BusController bus = BusController.Instance;
        if (bus == null) return;

        bool isBus = (other.attachedRigidbody != null && other.attachedRigidbody == bus.sphere)
                  || other.GetComponentInParent<BusController>() != null
                  || other.GetComponentInParent<BusTag>() != null;
        if (!isBus) return;

        _lastHitTime = Time.time;
        bus.ApplyImpact(speedAfterHit);
        if (ShiftManager.Instance != null) ShiftManager.Instance.Damage(damageOnHit);
        if (hideOnHit) gameObject.SetActive(false);
    }
}
