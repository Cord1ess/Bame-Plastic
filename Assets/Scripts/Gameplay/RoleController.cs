using UnityEngine;

/// Cycles control + camera across the three crew roles with the toggle key (default C):
///   Driver (the bus) -> Conductor 1 (door) -> Conductor 2 (inside) -> Driver ...
/// While conducting, the bus ignores input and coasts. For Conductor 2 the camera goes top-down and a
/// CLIP PLANE slices the top of the bus off (works on a single-mesh bus — no separate roof needed) so
/// you can see into the cabin. Add this to a manager object: it finds the bus + camera and auto-creates
/// both conductors.
public class RoleController : MonoBehaviour
{
    public BusController bus;
    public Conductor conductor1;
    public InsideConductor conductor2;
    public BusCameraFollow cam;

    // --- exposed for GameNet (avatar pose broadcast + C1 speed gate) ---
    public Transform Conductor1Transform => conductor1 != null ? conductor1.transform : null;
    public Transform Conductor2Transform => conductor2 != null ? conductor2.transform : null;
    public bool Conductor1Aboard => conductor1 == null || conductor1.OnBus;

    // --- which crew the local player is controlling RIGHT NOW (for the mic/combo HUD) ---
    public bool ControllingConductor1 => _role == Role.Conductor1;
    public bool ControllingConductor2 => _role == Role.Conductor2;
    public InsideConductor InsideConductor => conductor2;

    [Header("Camera framing (distance / height / pitch)")]
    public Vector3 driverCam = new Vector3(8f, 5f, 30f);
    public Vector3 conductor1Cam = new Vector3(4.5f, 3f, 30f);
    public Vector3 c2Cam = new Vector3(4f, 2.5f, 30f);   // close chase at the driver's angle, focused on conductor 2

    [Header("Conductor 2 roof cutaway — WHERE the bus is sliced")]
    [Tooltip("Cut height as a FRACTION of bus height from the top (0 = slice at the very roof, almost nothing " +
             "removed; 0.3 = slice 30% down from the roof; 0.8 = slice low, most of the upper bus gone). The clip " +
             "plane is at: busTopY − busHeight·roofCut. Raise to cut LOWER (see more interior).")]
    [Range(0f, 0.9f)] public float roofCut = 0.16f;
    [Tooltip("Extra absolute offset (m) added to the cut height — fine nudge up(+)/down(−) on top of roofCut.")]
    public float roofCutOffset = 0f;
    [HideInInspector] public bool flipCut = false;   // unused since the per-material clip replaced the oblique cut

    enum Role { Driver, Conductor1, Conductor2 }
    Role _role = Role.Driver;
    Camera _camComponent;
    Renderer[] _busRenderers;
    bool _cutawayActive;
    // per-material clip: each bus renderer's ORIGINAL shared materials, and CLIP-shader instances that mirror them
    // (base map/colour) but discard above _ClipY. We swap renderers to the clip set during C2, restore otherwise —
    // so ONLY the bus is cut (the old oblique camera clip cut the whole scene: buildings, sky, everything).
    Material[][] _busOrigMats;
    Material[][] _busClipMats;

    void Start()
    {
        if (bus == null) bus = FindAnyObjectByType<BusController>();
        if (cam == null) cam = FindAnyObjectByType<BusCameraFollow>();
        if (cam != null) _camComponent = cam.GetComponent<Camera>();
        if (bus != null)
        {
            _busRenderers = (bus.busModel != null ? bus.busModel : bus.transform).GetComponentsInChildren<Renderer>();
            BuildClipMaterials();
        }
        if (conductor1 == null) conductor1 = CreateConductor1();
        if (conductor2 == null) conductor2 = CreateConductor2();

        // MULTIPLAYER: take the role assigned in the lobby. SOLO: start as Driver but allow the toggle to
        // preview the other roles (handy for testing).
        var ctx = SessionContext.Instance;
        _multiplayer = ctx != null && ctx.IsMultiplayer;
        _role = (ctx != null) ? MapRole(ctx.LocalRole) : Role.Driver;
        Apply(_role);
    }

    bool _multiplayer;

    /// MP FAILOVER: this client was promoted to driver mid-shift (the previous driver dropped). Take the wheel:
    /// switch the local role to Driver and re-apply control/camera. GameNet.PromoteToDriver() has already flipped
    /// the bus out of proxy mode + made this client the authority.
    public void BecomeDriver()
    {
        _role = Role.Driver;
        Apply(_role);
    }

    public bool IsLocalDriver => _role == Role.Driver;

    static Role MapRole(BamePlastic.Net.Role r) => r switch
    {
        BamePlastic.Net.Role.Conductor1 => Role.Conductor1,
        BamePlastic.Net.Role.Conductor2 => Role.Conductor2,
        _ => Role.Driver,
    };

    void Update()
    {
        // In MULTIPLAYER your role is fixed (you can't just become the driver). Only allow free role-cycling
        // in SOLO play (for testing / single-player swapping between the unmanned crew).
        if (_multiplayer) return;
        if (GameInput.Instance.toggleRole.WasPressedThisFrame())
        {
            _role = (Role)(((int)_role + 1) % 3);
            Apply(_role);
        }
    }

    // The clip plane has to track the (moving) bus every frame, so it lives in LateUpdate.
    void LateUpdate()
    {
        // C1 speed gate: keep the bus's conductor1Aboard flag current.
        //   - MULTIPLAYER: the DRIVER owns the authoritative flag (GameNet sets it from C1's synced pose). The
        //     bus reads it. Non-driver clients run a proxy bus (cap is moot there).
        //   - SOLO: it's just whether the locally-controlled C1 is on the bus (true when not running around).
        if (bus != null)
        {
            var gn = BamePlastic.Net.GameNet.Instance;
            if (gn != null && gn.Active) bus.conductor1Aboard = gn.C1Aboard;
            else bus.conductor1Aboard = conductor1 == null || conductor1.OnBus;
        }

        // C2 ROOF CUTAWAY — per-material clip on the BUS ONLY (drives _ClipY to the live roofline each frame).
        if (_role == Role.Conductor2)
        {
            if (!_cutawayActive) SetCutaway(true);
            float clipY = BusRoofClipY();
            if (_busClipMats != null)
                for (int i = 0; i < _busClipMats.Length; i++)
                    if (_busClipMats[i] != null)
                        foreach (var m in _busClipMats[i]) if (m != null) m.SetFloat("_ClipY", clipY);
        }
        else if (_cutawayActive) SetCutaway(false);
    }

    // build a CLIP-shader instance per bus material, mirroring its base map + colour, so the cut bus looks the
    // same below the slice. Done once at Start.
    void BuildClipMaterials()
    {
        Shader clip = Shader.Find("BamePlastic/BusRoofClip");
        if (clip == null || _busRenderers == null) return;
        _busOrigMats = new Material[_busRenderers.Length][];
        _busClipMats = new Material[_busRenderers.Length][];
        for (int i = 0; i < _busRenderers.Length; i++)
        {
            var r = _busRenderers[i];
            if (r == null) continue;
            var orig = r.sharedMaterials;
            _busOrigMats[i] = orig;
            var clips = new Material[orig.Length];
            for (int j = 0; j < orig.Length; j++)
            {
                var src = orig[j];
                var cm = new Material(clip);
                if (src != null)
                {
                    Texture baseTex = src.HasProperty("_BaseMap") ? src.GetTexture("_BaseMap")
                                    : src.HasProperty("_MainTex") ? src.GetTexture("_MainTex") : null;
                    if (baseTex != null) cm.SetTexture("_BaseMap", baseTex);
                    Color c = src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor")
                            : src.HasProperty("_Color") ? src.GetColor("_Color") : Color.white;
                    cm.SetColor("_BaseColor", c);
                    if (src.HasProperty("_Metallic"))   cm.SetFloat("_Metallic", src.GetFloat("_Metallic"));
                    if (src.HasProperty("_Smoothness")) cm.SetFloat("_Smoothness", src.GetFloat("_Smoothness"));
                }
                cm.SetFloat("_ClipY", 99999f);
                clips[j] = cm;
            }
            _busClipMats[i] = clips;
        }
    }

    // swap the bus renderers to the clip materials (cut) or back to the originals (whole).
    void SetCutaway(bool on)
    {
        _cutawayActive = on;
        if (_busRenderers == null || _busClipMats == null) return;
        for (int i = 0; i < _busRenderers.Length; i++)
        {
            if (_busRenderers[i] == null) continue;
            _busRenderers[i].sharedMaterials = on ? _busClipMats[i] : _busOrigMats[i];
        }
    }

    void Apply(Role r)
    {
        bool driving = r == Role.Driver;
        bool c1 = r == Role.Conductor1;
        bool c2 = r == Role.Conductor2;

        // Enable exactly one action set: this is what keeps WASD from reaching the bus while on foot.
        if (driving) GameInput.Instance.EnableDriving();
        else GameInput.Instance.EnableOnFoot();

        if (bus != null) bus.controlEnabled = driving;
        if (conductor1 != null) conductor1.SetControlled(c1);
        if (conductor2 != null) conductor2.SetControlled(c2);

        // SOLO: the two crew the human ISN'T controlling run automatically (and can be switched into any time).
        // MULTIPLAYER: other roles are remote avatars / driven by their own clients — never local AI.
        if (!_multiplayer)
        {
            if (conductor1 != null) conductor1.SetAI(!c1);
            if (conductor2 != null) conductor2.SetAI(!c2);
        }

        if (cam == null || bus == null) return;
        if (driving)
        {
            // driver view: SPEED FRAMING (eases idle→fast by bus speed), not a fixed driverCam.
            cam.Retarget(bus.transform, null, cam.idleDistance, cam.idleHeight, cam.idlePitch);
            cam.UseSpeedFraming();
        }
        else if (c1)
        {
            // conductor-1 view: SPEED FRAMING by C1's own run speed (its idle/fast pair lives on the camera).
            cam.Retarget(conductor1.transform, bus.transform, cam.c1IdleCam.x, cam.c1IdleCam.y, cam.c1IdleCam.z);
            cam.UseConductor1Framing(conductor1);
        }
        else cam.Retarget(conductor2.transform, bus.transform, 3.3f, 3f, 33f);   // conductor-2: fixed framing
    }

    // World Y to slice at: top of the bus minus roofCutFraction of its height.
    float BusRoofClipY()
    {
        bool has = false;
        Bounds b = default;
        if (_busRenderers != null)
        {
            foreach (Renderer r in _busRenderers)
            {
                if (r == null || !(r is MeshRenderer)) continue;
                if (!has) { b = r.bounds; has = true; }
                else b.Encapsulate(r.bounds);
            }
        }
        if (!has) return (bus != null ? bus.transform.position.y : 0f) + 3f;
        return b.max.y - b.size.y * roofCut + roofCutOffset;
    }

    Conductor CreateConductor1()
    {
        Transform home = bus != null ? bus.transform : transform;
        if (BusPassengers.Instance != null && BusPassengers.Instance.doorAnchor != null)
            home = BusPassengers.Instance.doorAnchor;
        BillboardCharacter v = BillboardCharacter.Create("Conductor1", new Color(0.12f, 0.12f, 0.16f), 1.9f, home.position);
        Conductor c = v.gameObject.AddComponent<Conductor>();
        c.Setup(v, home);
        return c;
    }

    InsideConductor CreateConductor2()
    {
        Transform cabin = bus != null ? bus.transform : transform;
        Vector3 center = new Vector3(0f, 1.4f, 0f);
        Vector3 size = new Vector3(2f, 0f, 8.5f);
        if (BusPassengers.Instance != null)
        {
            cabin = BusPassengers.Instance.Cabin;
            center = BusPassengers.Instance.cabinLocalCenter;
            size = BusPassengers.Instance.cabinLocalSize;
        }
        BillboardCharacter v = BillboardCharacter.Create("Conductor2", new Color(0.18f, 0.1f, 0.1f), 1.9f, cabin.position);
        InsideConductor c = v.gameObject.AddComponent<InsideConductor>();
        c.Setup(v, cabin, center, size);
        return c;
    }
}
