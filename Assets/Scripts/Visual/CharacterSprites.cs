using System.Collections.Generic;
using UnityEngine;

/// Loads + slices all character art from Resources/Characters (built once, cached). Provides:
///   • 32 PEDESTRIAN characters, each a 9-frame WALK cycle — sliced from the 4 CharacterSheetN (each 9 pages,
///     a 4×2 = 8-character grid per page; the SAME cell across pages 1-9 is one character's walk cycle).
///   • CONDUCTOR 1 / 2 + DRIVER frame sets (WalkRight, RunRight, PassangerGrab, WalkFront/Back) + static poses.
/// Sprites use a bottom-centre pivot (feet on the ground). Slicing reads the readable sheet textures
/// (CharacterSpriteSetup makes them readable). Front-facing art → the billboard flips X by travel direction.
public static class CharacterSprites
{
    const int Cols = 4, Rows = 2, PerPage = Cols * Rows;   // 8 characters per sheet page
    const int Pages = 9;                                   // pages "1 (1)".."1 (9)" → 9 walk frames
    const int Sheets = 4;

    static bool _built;
    static readonly List<Sprite[]> _pedestrians = new List<Sprite[]>();   // [character][frame] front walk cycles
    static readonly List<Sprite> _backs = new List<Sprite>();             // [character] static BACK sprite (seated)

    // conductor/driver sets (loaded from their folders by name)
    public static Sprite[] C1Walk, C1Run, C1Grab;
    public static Sprite C1OnDoor, C1Front, C1Pose;
    public static Sprite[] C2WalkFront, C2WalkBack;
    public static Sprite C2Collect, C2Pose;
    public static Sprite DriverFront, DriverBack, DriverPose;

    // Traffic police (checkpoint hazard). CharacterSheet2 top row: cell 0 = male, cell 1 = female.
    public static Sprite PoliceMale, PoliceFemale;

    public static int PedestrianCount { get { Build(); return _pedestrians.Count; } }

    /// A pedestrian's 9-frame walk cycle by index (wraps). Null if none loaded (caller falls back to placeholder).
    public static Sprite[] Pedestrian(int index)
    {
        Build();
        if (_pedestrians.Count == 0) return null;
        index = ((index % _pedestrians.Count) + _pedestrians.Count) % _pedestrians.Count;
        return _pedestrians[index];
    }

    /// A pedestrian's BACK sprite by the SAME index (for when they sit on the bus facing away). Null if none.
    public static Sprite PedestrianBack(int index)
    {
        Build();
        if (_backs.Count == 0) return null;
        index = ((index % _backs.Count) + _backs.Count) % _backs.Count;
        return _backs[index];
    }

    public static void Build()
    {
        if (_built) return;
        _built = true;
        try { BuildInternal(); }
        catch (System.Exception e) { Debug.LogWarning("[CharacterSprites] build issue (using whatever loaded): " + e.Message); }
    }

    static void BuildInternal()
    {
        // ---- slice the pedestrian sheets ----
        for (int sheet = 1; sheet <= Sheets; sheet++)
        {
            // load the 9 pages of this sheet
            var pages = new Texture2D[Pages];
            bool any = false;
            for (int p = 0; p < Pages; p++)
            {
                // files are named "1 (1)".."1 (9)" — load the SPRITE then grab its texture (readable)
                var spr = Resources.Load<Sprite>($"Characters/CharacterSheet{sheet}/1 ({p + 1})");
                pages[p] = spr != null ? spr.texture : null;
                if (pages[p] != null) any = true;
            }
            if (!any) continue;

            var basis = System.Array.Find(pages, t => t != null);
            int cellW = basis.width / Cols, cellH = basis.height / Rows;

            // for each of the 8 cells, build a 9-frame walk by taking that cell from each page
            for (int cell = 0; cell < PerPage; cell++)
            {
                int col = cell % Cols;
                int row = cell / Cols;                       // 0 = TOP row, 1 = bottom row
                // texture Y is bottom-up; top row sits at the HIGH y. cell row 0 (top) → y = (Rows-1-0)*cellH
                int x = col * cellW;
                int y = (Rows - 1 - row) * cellH;

                var frames = new List<Sprite>(Pages);
                for (int p = 0; p < Pages; p++)
                {
                    var tex = pages[p]; if (tex == null) continue;
                    var rect = new Rect(x, y, cellW, cellH);
                    var s = Sprite.Create(tex, rect, new Vector2(0.5f, 0f), 100f, 0, SpriteMeshType.FullRect);
                    s.name = $"ped_s{sheet}_c{cell}_f{p}";
                    frames.Add(s);
                }
                if (frames.Count > 0) _pedestrians.Add(frames.ToArray());
            }

            // BACK sprites: "Set N Back" is the same 4×2 grid of 8 characters' backs (same order). Slice into
            // 8 static backs, appended in the SAME index order as the fronts (so back[i] matches walk[i]).
            var backSpr = Resources.Load<Sprite>($"Characters/CharacterSheet{sheet}/Set {sheet} Back");
            var backTex = backSpr != null ? backSpr.texture : null;
            if (backTex != null)
            {
                int bw = backTex.width / Cols, bh = backTex.height / Rows;
                for (int cell = 0; cell < PerPage; cell++)
                {
                    int col = cell % Cols, row = cell / Cols;
                    var rect = new Rect(col * bw, (Rows - 1 - row) * bh, bw, bh);
                    var s = Sprite.Create(backTex, rect, new Vector2(0.5f, 0f), 100f, 0, SpriteMeshType.FullRect);
                    s.name = $"pedback_s{sheet}_c{cell}";
                    _backs.Add(s);
                }
            }
        }

        // ---- conductor / driver sets ----
        C1Walk = LoadFrames("Characters/Conductor 1/WalkRight", 8);
        C1Run  = LoadFrames("Characters/Conductor 1/RunRight", 9);
        C1Grab = LoadFrames("Characters/Conductor 1/PassangerGrab", 8);
        C1OnDoor = Load("Characters/Conductor 1/OnDoor Standing");
        C1Front  = Load("Characters/Conductor 1/Conductor1Front");
        C1Pose   = Load("Characters/Conductor 1/Conductor1Pose");

        C2WalkFront = LoadFrames("Characters/Conductor 2/WalkFrontview", 8);
        C2WalkBack  = LoadFrames("Characters/Conductor 2/WalkBackview", 8);
        C2Collect   = Load("Characters/Conductor 2/CollectingFare Right");
        C2Pose      = Load("Characters/Conductor 2/Conductor2Pose");

        DriverFront = Load("Characters/Driver/DriverFront");
        DriverBack  = Load("Characters/Driver/DriverBack");
        DriverPose  = Load("Characters/Driver/DriverPose");

        // ---- traffic police: top row (cells 0 & 1) of CharacterSheet2's first page ----
        SlicePolice();
    }

    // The two police figures are the TOP-LEFT two cells of CharacterSheet2 (a 4×2 grid). A single standing
    // frame is enough for the static checkpoint figure (no walk cycle needed).
    static void SlicePolice()
    {
        var pageSpr = Resources.Load<Sprite>("Characters/CharacterSheet2/1 (1)");
        var tex = pageSpr != null ? pageSpr.texture : null;
        if (tex == null) return;
        int cellW = tex.width / Cols, cellH = tex.height / Rows;
        int yTop = (Rows - 1) * cellH;                         // top row sits at the HIGH y (texture is bottom-up)
        PoliceMale   = Sprite.Create(tex, new Rect(0 * cellW, yTop, cellW, cellH), new Vector2(0.5f, 0f), 100f, 0, SpriteMeshType.FullRect);
        PoliceFemale = Sprite.Create(tex, new Rect(1 * cellW, yTop, cellW, cellH), new Vector2(0.5f, 0f), 100f, 0, SpriteMeshType.FullRect);
        if (PoliceMale != null) PoliceMale.name = "police_male";
        if (PoliceFemale != null) PoliceFemale.name = "police_female";
    }

    static Sprite Load(string path) => Resources.Load<Sprite>(path);

    // load numbered frames "01".."NN" (also tolerate a missing "01", e.g. RunRight starts at 02)
    static Sprite[] LoadFrames(string folder, int max)
    {
        var list = new List<Sprite>(max);
        for (int i = 1; i <= max; i++)
        {
            var s = Resources.Load<Sprite>($"{folder}/{i:00}");
            if (s != null) list.Add(s);
        }
        return list.Count > 0 ? list.ToArray() : null;
    }
}
