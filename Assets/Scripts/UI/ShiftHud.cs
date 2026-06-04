using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// The competitive-shift HUD, built entirely in code so there's no manual Canvas setup:
///   • top-left  : your taka + a bus-health bar
///   • top-centre: the shift timer (MM:SS)
///   • top-right : the live rival STANDINGS board (you marked with *)
///   • full-screen end-of-shift SUMMARY panel (your placement + final board + restart hint)
/// It just reads ShiftManager each frame. ShiftManager spawns this automatically, but you can also
/// drop it on its own GameObject if you want to pre-place it.
public class ShiftHud : MonoBehaviour
{
    const float HealthBarWidth = 320f;
    const float HealthInnerWidth = HealthBarWidth - 6f;

    Font _font;
    Text _takaText;
    Text _timerText;
    Text _standingsText;
    Text _healthText;
    RawImage _healthFill;
    RectTransform _healthFillRT;

    GameObject _summaryPanel;
    Text _summaryText;

    readonly StringBuilder _sb = new StringBuilder(256);

    void Start()
    {
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (_font == null) _font = Font.CreateDynamicFontFromOSFont("Arial", 16);
        BuildUI();
    }

    void Update()
    {
        ShiftManager sm = ShiftManager.Instance;
        if (sm == null) return;

        _takaText.text = "Tk " + sm.Earnings.ToString("N0");

        int t = Mathf.CeilToInt(sm.TimeRemaining);
        _timerText.text = string.Format("{0:0}:{1:00}", t / 60, t % 60);

        float hp01 = sm.maxHealth > 0f ? Mathf.Clamp01(sm.Health / sm.maxHealth) : 0f;
        _healthFillRT.sizeDelta = new Vector2(HealthInnerWidth * hp01, -6f);
        _healthFill.color = Color.Lerp(new Color(0.85f, 0.2f, 0.15f), new Color(0.25f, 0.8f, 0.35f), hp01);
        _healthText.text = "BUS " + Mathf.RoundToInt(sm.Health) + "%";

        _standingsText.text = BuildStandings(sm, "-- STANDINGS --");

        if (sm.IsOver && !_summaryPanel.activeSelf) ShowSummary(sm);
    }

    string BuildStandings(ShiftManager sm, string header)
    {
        _sb.Length = 0;
        _sb.AppendLine(header);
        var standings = sm.GetStandings();
        for (int i = 0; i < standings.Count; i++)
        {
            var s = standings[i];
            _sb.Append(i + 1).Append(". ").Append(s.name).Append("   Tk ").Append(s.taka.ToString("N0"));
            if (s.isPlayer) _sb.Append("  *");
            _sb.AppendLine();
        }
        return _sb.ToString();
    }

    void ShowSummary(ShiftManager sm)
    {
        _summaryPanel.SetActive(true);
        int rank = sm.PlayerRank();
        int count = sm.GetStandings().Count;
        string place = rank == 1 ? "1st" : rank == 2 ? "2nd" : rank == 3 ? "3rd" : rank + "th";

        _sb.Length = 0;
        _sb.AppendLine("SHIFT OVER");
        _sb.AppendLine();
        _sb.Append("You placed ").Append(place).Append(" of ").Append(count).AppendLine();
        _sb.Append("Earnings:  Tk ").Append(sm.Earnings.ToString("N0")).AppendLine();
        _sb.AppendLine();
        _sb.Append(BuildStandings(sm, "FINAL STANDINGS"));
        _sb.AppendLine();
        _sb.Append("Press R to drive another shift");
        _summaryText.text = _sb.ToString();
    }

    // ---------- UI construction ----------
    void BuildUI()
    {
        GameObject canvasGO = new GameObject("HUDCanvas");
        canvasGO.transform.SetParent(transform, false);
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();
        Transform root = canvas.transform;

        // Taka (top-left, big)
        _takaText = MakeText(root, "Taka", "Tk 0", new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -24), 54, TextAnchor.UpperLeft);
        _takaText.color = new Color(1f, 0.85f, 0.2f);
        _takaText.rectTransform.sizeDelta = new Vector2(560, 70);

        // Health bar (under taka)
        MakeHealthBar(root);

        // Timer (top-centre)
        _timerText = MakeText(root, "Timer", "10:00", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -24), 48, TextAnchor.UpperCenter);
        _timerText.rectTransform.sizeDelta = new Vector2(260, 64);

        // Standings (top-right)
        _standingsText = MakeText(root, "Standings", "", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-30, -24), 28, TextAnchor.UpperRight);
        _standingsText.rectTransform.sizeDelta = new Vector2(460, 340);

        // Summary panel (built last so it draws on top; hidden until the shift ends)
        BuildSummaryPanel(root);
    }

    void MakeHealthBar(Transform parent)
    {
        RawImage bg = MakeSolid(parent, "HealthBarBG", new Color(0f, 0f, 0f, 0.55f));
        RectTransform bgrt = bg.rectTransform;
        bgrt.anchorMin = new Vector2(0, 1); bgrt.anchorMax = new Vector2(0, 1); bgrt.pivot = new Vector2(0, 1);
        bgrt.anchoredPosition = new Vector2(30, -104);
        bgrt.sizeDelta = new Vector2(HealthBarWidth, 30);

        _healthFill = MakeSolid(bg.transform, "HealthBarFill", new Color(0.25f, 0.8f, 0.35f));
        _healthFillRT = _healthFill.rectTransform;
        _healthFillRT.anchorMin = new Vector2(0, 0); _healthFillRT.anchorMax = new Vector2(0, 1); _healthFillRT.pivot = new Vector2(0, 0.5f);
        _healthFillRT.anchoredPosition = new Vector2(3, 0);
        _healthFillRT.sizeDelta = new Vector2(HealthInnerWidth, -6f);

        _healthText = MakeText(parent, "HealthLabel", "BUS 100%", new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, -106), 18, TextAnchor.MiddleLeft);
        _healthText.rectTransform.sizeDelta = new Vector2(300, 24);
    }

    void BuildSummaryPanel(Transform parent)
    {
        RawImage panel = MakeSolid(parent, "SummaryPanel", new Color(0f, 0f, 0f, 0.85f));
        _summaryPanel = panel.gameObject;
        RectTransform rt = panel.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

        _summaryText = MakeText(panel.transform, "SummaryText", "", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, 34, TextAnchor.MiddleCenter);
        _summaryText.rectTransform.sizeDelta = new Vector2(940, 780);
        _summaryPanel.SetActive(false);
    }

    Text MakeText(Transform parent, string name, string content, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, int fontSize, TextAnchor align)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Text txt = go.AddComponent<Text>();
        txt.font = _font;
        txt.text = content;
        txt.fontSize = fontSize;
        txt.alignment = align;
        txt.color = Color.white;
        txt.horizontalOverflow = HorizontalWrapMode.Overflow;
        txt.verticalOverflow = VerticalWrapMode.Overflow;

        Shadow shadow = go.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.7f);
        shadow.effectDistance = new Vector2(2f, -2f);

        RectTransform rt = txt.rectTransform;
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = new Vector2(anchorMin.x, anchorMax.y);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = new Vector2(400, 60);
        return txt;
    }

    RawImage MakeSolid(Transform parent, string name, Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RawImage img = go.AddComponent<RawImage>();
        img.texture = Texture2D.whiteTexture;
        img.color = color;
        return img;
    }
}
