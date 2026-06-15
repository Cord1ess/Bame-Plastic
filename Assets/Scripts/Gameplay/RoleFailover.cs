using UnityEngine;
using BamePlastic.Net;

/// Listens for the server's mid-shift driver-failover (INetworkService.RoleReassigned) and, if THIS client is
/// the one promoted to driver, takes the wheel: promotes the bus from proxy → authoritative (GameNet) and
/// switches the local RoleController to the Driver role. Other clients just re-target their avatars (the old
/// driver's avatar vanishes; the promoted player's avatar becomes the driver). Conductor drops need no action —
/// the driver carries on. Play-only, auto-spawned in the game scene; no-op in solo.
public class RoleFailover : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != SceneFlow.Game) return;
        var ctx = SessionContext.Instance;
        if (ctx == null || !ctx.IsMultiplayer) return;          // solo → no failover
        if (FindAnyObjectByType<RoleFailover>() != null) return;
        new GameObject("RoleFailover").AddComponent<RoleFailover>();
    }

    INetworkService _net;
    Role _myOriginalRole;

    void Start()
    {
        var ctx = SessionContext.Instance;
        if (ctx == null) { enabled = false; return; }
        _net = ctx.Net;
        _myOriginalRole = ctx.LocalRole;
        if (_net != null) _net.RoleReassigned += OnReassign;
    }

    void OnDestroy() { if (_net != null) _net.RoleReassigned -= OnReassign; }

    void OnReassign(RoleReassign info)
    {
        // promotedFromRole < 0 → a conductor left, driver carries on. Nothing to do.
        if (info.promotedFromRole < 0) return;

        // Am I the promoted one? My current role equals the role they were promoted FROM.
        // (After this, my role becomes Driver; if the chain ever fires twice, my role would already be Driver and
        // never match again — safe.)
        bool iAmPromoted = (int)CurrentLocalRole() == info.promotedFromRole;
        if (!iAmPromoted) return;

        var gn = GameNet.Instance;
        if (gn != null) gn.PromoteToDriver();

        var rc = FindAnyObjectByType<RoleController>();
        if (rc != null) rc.BecomeDriver();

        Debug.Log("[Failover] Promoted to DRIVER mid-shift (was " + CurrentLocalRole() + ").");
        _myOriginalRole = Role.Driver;   // remember we drive now
    }

    // our live role: GameNet tracks it after a promotion; before any promotion it's our original assignment.
    Role CurrentLocalRole()
    {
        var gn = GameNet.Instance;
        if (gn != null && gn.Active) return gn.LocalRole;
        return _myOriginalRole;
    }
}
