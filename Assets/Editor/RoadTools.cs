using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.ProBuilder;

/// Road shaping for ProBuilder roads. Auto-detects the road's axes (length = longest local extent,
/// width = next, up = shortest) and auto-caps the slice count to the vertex density, so it behaves on
/// low-poly meshes too. Width ops are global (bulletproof, scale-aware); path/height ops are slice-based
/// and smoothed. Click Diagnose first if unsure; everything is undoable (Ctrl+Z) and stacks.
///
/// Menu: Bame ▸ Road Tools
public class RoadTools : EditorWindow
{
    int slices = 32;
    float blend = 0.5f;
    int smoothIterations = 4;
    float multiplier = 1.5f;
    float targetWidth = 19.5f;   // WORLD metres

    enum Op { Widen, SetWidth, NormalizeWidth, SmoothCorners, Straighten, RecenterWidth, Flatten, SmoothHeight }

    [MenuItem("Bame/Road Tools")]
    static void Open() => GetWindow<RoadTools>("Road Tools");

    void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Select the road ProBuilder object(s), then click an op. Axes auto-detected; slices auto-capped " +
            "to the vert count. Start with low Blend. Diagnose prints what it sees.",
            MessageType.Info);
        if (GUILayout.Button("Diagnose selected")) DiagnoseSelection();

        slices = EditorGUILayout.IntSlider("Slices (max)", slices, 4, 120);
        blend = EditorGUILayout.Slider("Blend (strength)", blend, 0f, 1f);
        smoothIterations = EditorGUILayout.IntSlider("Smooth passes", smoothIterations, 1, 40);

        Section("Width  (bulletproof / scale-aware)");
        multiplier = EditorGUILayout.Slider("Widen multiplier", multiplier, 0.5f, 4f);
        if (GUILayout.Button("Widen × multiplier")) Apply(Op.Widen);
        targetWidth = EditorGUILayout.FloatField("Target width (world m)", targetWidth);
        if (GUILayout.Button("Set width to target")) Apply(Op.SetWidth);
        if (GUILayout.Button("Normalize width (best-effort)")) Apply(Op.NormalizeWidth);

        Section("Path / corners");
        if (GUILayout.Button("Smooth corners")) Apply(Op.SmoothCorners);
        if (GUILayout.Button("Straighten")) Apply(Op.Straighten);
        if (GUILayout.Button("Recentre width")) Apply(Op.RecenterWidth);

        Section("Height");
        if (GUILayout.Button("Flatten height")) Apply(Op.Flatten);
        if (GUILayout.Button("Smooth height")) Apply(Op.SmoothHeight);
    }

    static void Section(string t) { EditorGUILayout.Space(); EditorGUILayout.LabelField(t, EditorStyles.boldLabel); }

    static float Comp(Vector3 v, int i) => i == 0 ? v.x : i == 1 ? v.y : v.z;
    static Vector3 SetComp(Vector3 v, int i, float val) { if (i == 0) v.x = val; else if (i == 1) v.y = val; else v.z = val; return v; }

    static void DetectAxes(IList<Vector3> p, out int len, out int wid, out int up, out Vector3 size)
    {
        Vector3 mn = p[0], mx = p[0];
        for (int i = 1; i < p.Count; i++) { mn = Vector3.Min(mn, p[i]); mx = Vector3.Max(mx, p[i]); }
        size = mx - mn;
        len = size.x >= size.y && size.x >= size.z ? 0 : (size.y >= size.z ? 1 : 2);
        int a = (len + 1) % 3, b = (len + 2) % 3;
        wid = Comp(size, a) >= Comp(size, b) ? a : b;
        up = Comp(size, a) >= Comp(size, b) ? b : a;
    }

    void DiagnoseSelection()
    {
        string[] ax = { "X", "Y", "Z" };
        foreach (GameObject go in Selection.gameObjects)
        {
            ProBuilderMesh pb = go.GetComponent<ProBuilderMesh>();
            if (pb == null) { Debug.Log($"[RoadTools] '{go.name}' has no ProBuilderMesh."); continue; }
            IList<Vector3> p = pb.positions;
            if (p.Count == 0) continue;
            DetectAxes(p, out int len, out int wid, out int up, out Vector3 size);
            Vector3 ls = go.transform.lossyScale;
            int eff = Mathf.Clamp(slices, 4, Mathf.Max(4, p.Count / 4));
            Debug.Log($"[RoadTools] '{go.name}': verts {p.Count} → eff. slices {eff}. " +
                      $"length={ax[len]} ({Comp(size, len) * Comp(ls, len):0.0}m world), " +
                      $"width={ax[wid]} ({Comp(size, wid) * Comp(ls, wid):0.0}m world), up={ax[up]}. scale {ls}.");
        }
    }

    void Apply(Op op)
    {
        int done = 0;
        foreach (GameObject go in Selection.gameObjects)
        {
            ProBuilderMesh pb = go.GetComponent<ProBuilderMesh>();
            if (pb == null) continue;
            ApplyTo(pb, op);
            done++;
        }
        if (done == 0) EditorUtility.DisplayDialog("Road Tools", "Select a ProBuilder road object first.", "OK");
    }

    void ApplyTo(ProBuilderMesh pb, Op op)
    {
        IList<Vector3> p = pb.positions;
        int n = p.Count;
        if (n < 3) return;

        DetectAxes(p, out int LEN, out int WID, out int UP, out Vector3 _);
        Undo.RecordObject(pb, "Road " + op);

        float lossyW = Mathf.Abs(Comp(pb.transform.lossyScale, WID));
        if (lossyW < 0.0001f) lossyW = 1f;

        // ---- WIDTH ops: global scale about the overall width-centre (no slicing → can't spike) ----
        if (op == Op.Widen || op == Op.SetWidth)
        {
            float minW = float.MaxValue, maxW = float.MinValue, sumW = 0f;
            for (int i = 0; i < n; i++) { float w = Comp(p[i], WID); minW = Mathf.Min(minW, w); maxW = Mathf.Max(maxW, w); sumW += w; }
            float centerW = sumW / n;
            float extent = Mathf.Max(0.0001f, maxW - minW);
            float scale = (op == Op.Widen) ? multiplier : (targetWidth / lossyW) / extent;
            scale = Mathf.LerpUnclamped(1f, scale, blend);

            Vector3[] dst = new Vector3[n];
            for (int i = 0; i < n; i++) { Vector3 v = p[i]; v = SetComp(v, WID, centerW + (Comp(v, WID) - centerW) * scale); dst[i] = v; }
            Commit(pb, dst);
            Debug.Log($"[RoadTools] {op} '{pb.name}' ×{scale:0.00} → ~{extent * scale * lossyW:0.0}m world width.");
            return;
        }

        // ---- slice model (auto-capped) for path / normalize / height ----
        int S = Mathf.Clamp(slices, 4, Mathf.Max(4, n / 4));
        float minL = float.MaxValue, maxL = float.MinValue;
        for (int i = 0; i < n; i++) { float l = Comp(p[i], LEN); minL = Mathf.Min(minL, l); maxL = Mathf.Max(maxL, l); }
        float span = Mathf.Max(0.0001f, maxL - minL);

        float[] mn = new float[S], mx = new float[S], uSum = new float[S], wSum = new float[S];
        int[] cnt = new int[S];
        for (int b = 0; b < S; b++) { mn[b] = float.MaxValue; mx[b] = float.MinValue; }
        for (int i = 0; i < n; i++)
        {
            int b = Mathf.Clamp((int)((Comp(p[i], LEN) - minL) / span * (S - 1)), 0, S - 1);
            float w = Comp(p[i], WID);
            mn[b] = Mathf.Min(mn[b], w); mx[b] = Mathf.Max(mx[b], w); wSum[b] += w; uSum[b] += Comp(p[i], UP); cnt[b]++;
        }
        float[] cw = new float[S], hw = new float[S], cu = new float[S];
        for (int b = 0; b < S; b++)
        {
            if (cnt[b] > 0) { cw[b] = wSum[b] / cnt[b]; hw[b] = (mx[b] - mn[b]) * 0.5f; cu[b] = uSum[b] / cnt[b]; }
            else { cw[b] = hw[b] = cu[b] = float.NaN; }
        }
        FillGaps(cw); FillGaps(hw); FillGaps(cu);
        ClampToMedian(hw, 0.3f, 3f);   // kill outlier slices

        float[] cw2 = (float[])cw.Clone(), hw2 = (float[])hw.Clone(), cu2 = (float[])cu.Clone();
        bool flatten = false; float meanU = Mean(cu);

        switch (op)
        {
            case Op.NormalizeWidth: float aw = Mean(hw); for (int b = 0; b < S; b++) hw2[b] = Mathf.Lerp(hw[b], aw, blend); break;
            case Op.SmoothCorners: float[] cs = MovingAverage(cw, smoothIterations); for (int b = 0; b < S; b++) cw2[b] = Mathf.Lerp(cw[b], cs[b], blend); break;
            case Op.Straighten: for (int b = 0; b < S; b++) cw2[b] = Mathf.Lerp(cw[b], Mathf.Lerp(cw[0], cw[S - 1], b / (float)(S - 1)), blend); break;
            case Op.RecenterWidth: float m = Mean(cw); for (int b = 0; b < S; b++) cw2[b] = cw[b] - m * blend; break;
            case Op.Flatten: flatten = true; break;
            case Op.SmoothHeight: float[] us = MovingAverage(cu, smoothIterations); for (int b = 0; b < S; b++) cu2[b] = Mathf.Lerp(cu[b], us[b], blend); break;
        }

        Vector3[] outv = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            Vector3 v = p[i];
            float t = (Comp(v, LEN) - minL) / span * (S - 1);
            float sCw = Sample(cw, t), sHw = Sample(hw, t), dCw = Sample(cw2, t), dHw = Sample(hw2, t);
            float norm = sHw > 0.0001f ? (Comp(v, WID) - sCw) / sHw : 0f;
            v = SetComp(v, WID, dCw + norm * dHw);
            float u = Comp(v, UP);
            v = SetComp(v, UP, flatten ? Mathf.Lerp(u, meanU, blend) : u + Sample(cu2, t) - Sample(cu, t));
            outv[i] = v;
        }
        Commit(pb, outv);
        Debug.Log($"[RoadTools] {op} '{pb.name}' (slices {S}, blend {blend:0.00}).");
    }

    static void Commit(ProBuilderMesh pb, Vector3[] dst)
    {
        pb.positions = dst;
        pb.ToMesh();
        pb.Refresh();
        MeshCollider mc = pb.GetComponent<MeshCollider>();
        MeshFilter mf = pb.GetComponent<MeshFilter>();
        if (mc != null && mf != null) { mc.sharedMesh = null; mc.sharedMesh = mf.sharedMesh; }
        EditorUtility.SetDirty(pb);
    }

    static void ClampToMedian(float[] a, float lo, float hi)
    {
        List<float> vals = new List<float>();
        for (int i = 0; i < a.Length; i++) if (!float.IsNaN(a[i])) vals.Add(a[i]);
        if (vals.Count == 0) return;
        vals.Sort();
        float med = vals[vals.Count / 2];
        if (med <= 0.0001f) return;
        for (int i = 0; i < a.Length; i++) a[i] = Mathf.Clamp(a[i], med * lo, med * hi);
    }

    static void FillGaps(float[] a)
    {
        int last = -1;
        for (int i = 0; i < a.Length; i++)
        {
            if (float.IsNaN(a[i])) continue;
            if (last >= 0) for (int j = last + 1; j < i; j++) a[j] = Mathf.Lerp(a[last], a[i], (j - last) / (float)(i - last));
            last = i;
        }
        float v = 0f;
        for (int i = 0; i < a.Length; i++) { if (!float.IsNaN(a[i])) v = a[i]; else a[i] = v; }
    }

    static float Sample(float[] a, float t)
    {
        int b0 = Mathf.Clamp(Mathf.FloorToInt(t), 0, a.Length - 1);
        int b1 = Mathf.Clamp(b0 + 1, 0, a.Length - 1);
        return Mathf.Lerp(a[b0], a[b1], t - b0);
    }

    static float[] MovingAverage(float[] a, int iterations)
    {
        float[] cur = (float[])a.Clone(), next = new float[a.Length];
        for (int it = 0; it < iterations; it++)
        {
            for (int i = 0; i < a.Length; i++)
            {
                float sum = cur[i]; int c = 1;
                if (i > 0) { sum += cur[i - 1]; c++; }
                if (i < a.Length - 1) { sum += cur[i + 1]; c++; }
                next[i] = sum / c;
            }
            (cur, next) = (next, cur);
        }
        return cur;
    }

    static float Mean(float[] a) { float s = 0f; for (int i = 0; i < a.Length; i++) s += a[i]; return a.Length > 0 ? s / a.Length : 0f; }
}
