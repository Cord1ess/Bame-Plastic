using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TriggerExit : MonoBehaviour
{
    public float delay = 5f;

    public delegate void ExitAction();
    public static event ExitAction OnChunkExited;

    // Raised once the bus has passed this chunk + the delay elapsed, so the generator can recycle it.
    public static event System.Action<GameObject> OnChunkRecycle;

    private bool exited = false;

    // Pooled chunks are reused, so re-arm the trigger every time the chunk is (re)activated.
    private void OnEnable()
    {
        exited = false;
    }

    // Called by the generator when a chunk is repositioned for reuse (treadmill pool keeps chunks
    // active and just moves them, so OnEnable doesn't fire — this re-arms the trigger explicitly).
    public void ReArm()
    {
        exited = false;
        StopAllCoroutines();
    }

    private void OnTriggerEnter(Collider other)
    {
        BusTag busTag = other.GetComponent<BusTag>();
        if (busTag == null) busTag = other.GetComponentInParent<BusTag>();
        if (busTag == null) busTag = other.GetComponentInChildren<BusTag>();

        if (busTag != null)
        {
            if (!exited)
            {
                exited = true;
                OnChunkExited?.Invoke();
                StartCoroutine(WaitAndDeactivate());
            }


        }
    }

    IEnumerator WaitAndDeactivate()
    {
        yield return new WaitForSeconds(delay);

        GameObject chunkRoot = transform.root.gameObject;
        if (OnChunkRecycle != null)
            OnChunkRecycle(chunkRoot);   // hand back to the pool
        else
            chunkRoot.SetActive(false);  // fallback if nothing is pooling
    }



}
