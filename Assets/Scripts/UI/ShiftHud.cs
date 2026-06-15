using System.Text;
using UnityEngine;
using UnityEngine.UI;
using BamePlastic.Net;

/// The competitive-shift HUD, rebuilt in the PIXEL theme (PixelUI toolkit) — bordered panels, the pixel
/// font, chunky bars, accent colours. All code-built (no manual Canvas):
///   • top-left  : TAKA panel + bus-health bar
///   • top-centre: shift TIMER panel (MM:SS)
///   • top-right : live rival STANDINGS board (you highlighted)
///   • full-screen end-of-shift RESULTS panel
/// Reads ShiftManager each frame. ShiftManager spawns it automatically.
public class ShiftHud : MonoBehaviour
{
    Text _takaText, _timerText, _standingsText, _healthText;
    Image _healthFill;
    RectTransform _healthFillRT;
    float _healthInner;

    GameObject _summaryPanel;
    Text _summaryText;

    Text _toastText;            // transient centre-screen alert (fare lost, police fine)
    float _toastUntil;

    readonly StringBuilder _sb = new StringBuilder(256);

    void Start() { BuildUI(); }

    void OnEnable()
    {
        ShiftManager.FareLost += OnFareLost;
        PoliceHazard.Fined += OnFined;
        PlayerAccount.AchievementUnlocked += OnAchievement;
    }
    void OnDisable()
    {
        ShiftManager.FareLost -= OnFareLost;
        PoliceHazard.Fined -= OnFined;
        PlayerAccount.AchievementUnlocked -= OnAchievement;
    }

    void OnFareLost(int amount) => Toast("<color=#E06A5A>FARE LOST  −" + amount + " B</color>", 1.6f);
    void OnFined(int amount)    => Toast("<color=#FF4D4D>POLICE FINE  −" + amount.ToString("N0") + " B</color>", 2.4f);
    void OnAchievement(string name, int bhara) => Toast("<color=#FFC44D>★ ACHIEVEMENT  " + name + "  +" + bhara + " Bhara</color>", 3.5f);

    /// Show a brief centre-screen alert (richtext allowed). Newer toasts replace older ones.
    void Toast(string msg, float seconds)
    {
        if (_toastText == null) return;
        _toastText.text = msg;
        _toastText.gameObject.SetActive(true);
        _toastUntil = Time.unscaledTime + seconds;
    }

    int _lastEarnings = int.MinValue, _lastTimer = int.MinValue, _lastHealth = int.MinValue;
    float _standingsTimer;

    void Update()
    {
        ShiftManager sm = ShiftManager.Instance;
        if (sm == null) return;

        // rebuild each text only when its DISPLAYED value changes (no per-frame string alloc → no WebGL GC churn)
        if (sm.Earnings != _lastEarnings) { _takaText.text = sm.Earnings.ToString("N0") + " B"; _lastEarnings = sm.Earnings; }

        int t = Mathf.CeilToInt(sm.TimeRemaining);
        if (t != _lastTimer)
        {
            _timerText.text = (t / 60) + ":" + (t % 60).ToString("00");
            _timerText.color = t <= 60 ? PixelUI.Red : PixelUI.Text;     // last minute → red
            _lastTimer = t;
        }

        float hp01 = sm.maxHealth > 0f ? Mathf.Clamp01(sm.Health / sm.maxHealth) : 0f;
        _healthFillRT.sizeDelta = new Vector2(_healthInner * hp01, _healthFillRT.sizeDelta.y);
        _healthFill.color = Color.Lerp(PixelUI.Red, PixelUI.Green, hp01);
        int hp = Mathf.RoundToInt(sm.Health);
        if (hp != _lastHealth) { _healthText.text = "BUS " + hp + "%"; _lastHealth = hp; }

        // standings: a sort + string build — throttle to ~4Hz (it barely changes within a frame)
        // rebuild standings ~4Hz; the "rival leads" warning is now WOVEN INTO the board (the leading rival's row
        // blinks + carries a tag) rather than a separate line below it.
        _standingsTimer -= Time.deltaTime;
        if (_standingsTimer <= 0f) { _standingsText.text = BuildStandings(sm); _standingsTimer = 0.06f; }

        if (_toastText != null && _toastText.gameObject.activeSelf && Time.unscaledTime >= _toastUntil)
            _toastText.gameObject.SetActive(false);

        if (sm.IsOver && !_summaryPanel.activeSelf) ShowSummary(sm);
    }

    string BuildStandings(ShiftManager sm)
    {
        _sb.Length = 0;
        var standings = sm.GetStandings();
        bool rivalAhead = sm.RivalAhead && sm.IsRunning;
        // blink phase (0..1) for the warning highlight on the leading rival's row
        bool blink = Mathf.Sin(Time.unscaledTime * 6f) > 0f;

        for (int i = 0; i < standings.Count; i++)
        {
            var s = standings[i];
            // a rival who is currently OUT-EARNING the player (and is the leader) gets the warning treatment.
            bool isLeadingRival = !s.isPlayer && rivalAhead && s.name == sm.LeaderName;
            string row = (i + 1) + ". " + s.name + "  " + s.taka.ToString("N0") + " B";

            if (s.isPlayer)
            {
                // player gold; if being out-earned, append a small warning marker on the player's own row
                _sb.Append("<color=#FFC44D>").Append(row).Append(" ◀");
                if (rivalAhead) _sb.Append("  <color=#FF5555>BEAT ").Append(sm.LeaderName).Append("!</color>");
                _sb.Append("</color>");
            }
            else if (isLeadingRival)
            {
                // the threatening leader: blink red↔bright + a ▲ tag so the warning lives IN the board
                string hex = blink ? "#FF4D4D" : "#FFB0B0";
                _sb.Append("<color=").Append(hex).Append('>').Append("▲ ").Append(row).Append("  LEADING</color>");
            }
            else
            {
                _sb.Append("<color=#F2E9D2>").Append(row).Append("</color>");
            }
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
        bool won = rank == 1;

        _sb.Length = 0;
        _sb.Append(won ? "<color=#FFC44D>SHIFT WON</color>" : "<color=#F2E9D2>SHIFT OVER</color>").AppendLine();
        _sb.AppendLine();
        _sb.Append("You placed <color=#FFC44D>").Append(place).Append("</color> of ").Append(count).AppendLine();
        _sb.Append("Earnings  <color=#FFC44D>").Append(sm.Earnings.ToString("N0")).Append(" B</color>").AppendLine();
        // account reward (Bhara is granted at shift end via PlayerAccount.AwardEarnings; 100 tk = 10 Bhara)
        if (PlayerAccount.LoggedIn)
        {
            int bharaEarned = (Mathf.Max(0, sm.Earnings) / 100) * 10;
            _sb.Append("Bhara  <color=#6CD57E>+").Append(bharaEarned).Append("</color>   (wallet ")
               .Append(PlayerAccount.Bhara).Append(")").AppendLine();
            _sb.Append("<color=#8A839C>Career  Tk ").Append(PlayerAccount.TotalEarnings.ToString("N0")).Append("</color>").AppendLine();
        }
        _sb.AppendLine();
        _sb.Append("<color=#8A839C>-- FINAL STANDINGS --</color>").AppendLine();
        _sb.Append(BuildStandings(sm));
        _sb.AppendLine();
        _sb.Append("<color=#9A93AB>Press R to drive another shift</color>");
        _summaryText.text = _sb.ToString();
    }

    // ---------- UI construction (white theme, broken-square panels) ----------
    void BuildUI()
    {
        Canvas canvas = PixelUI.Canvas(transform, "HUDCanvas", 10);
        Transform root = canvas.transform;

        // TOP-CENTRE: "SHIFT ENDS IN" + timer — NO background, dark outlined text so the top stays clean.
        var shiftLabel = PixelUI.Label(root, "ShiftLabel", "SHIFT ENDS IN", 22, TextAnchor.UpperCenter, PixelUI.InkDim, outline: true);
        AnchorTopCentre(shiftLabel.rectTransform, new Vector2(0, -20), new Vector2(520, 28));
        _timerText = PixelUI.Label(root, "Timer", "10:00", 60, TextAnchor.UpperCenter, PixelUI.Ink, outline: true);
        AnchorTopCentre(_timerText.rectTransform, new Vector2(0, -50), new Vector2(420, 70));

        // TOP-RIGHT: STANDINGS — NO background, dark outlined text so the top stays clear.
        var boardTitle = PixelUI.Label(root, "BoardTitle", "STANDINGS", 24, TextAnchor.UpperRight, PixelUI.InkDim, outline: true);
        AnchorCorner(boardTitle.rectTransform, new Vector2(1, 1), new Vector2(-30, -20), new Vector2(440, 30));
        _standingsText = PixelUI.Label(root, "Standings", "", 26, TextAnchor.UpperRight, PixelUI.Ink, outline: true);
        AnchorCorner(_standingsText.rectTransform, new Vector2(1, 1), new Vector2(-30, -56), new Vector2(560, 320));

        // BOTTOM-LEFT: money + health, in white panels (balances the bottom-right speedometer).
        BuildMoneyHealth(root);

        // CENTRE alert toast (fare lost / police fine) — hidden until something fires it.
        _toastText = PixelUI.Label(root, "Toast", "", 40, TextAnchor.MiddleCenter, PixelUI.Text, outline: true);
        var tr = _toastText.rectTransform;
        tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 0.5f); tr.pivot = new Vector2(0.5f, 0.5f);
        tr.anchoredPosition = new Vector2(0, 160); tr.sizeDelta = new Vector2(900, 70);
        _toastText.gameObject.SetActive(false);

        BuildSummaryPanel(root);
    }

    void BuildMoneyHealth(Transform root)
    {
        // BHARA (earnings) panel
        var takaPanel = PixelUI.Panel(root, "TakaPanel", new Vector2(0, 0), new Vector2(32, 122), new Vector2(300, 84));
        PixelUI.Label(takaPanel.transform, "TakaLabel", "BHARA", 18, TextAnchor.UpperLeft, PixelUI.InkDim)
               .rectTransform.anchoredPosition = new Vector2(16, -10);
        _takaText = PixelUI.Label(takaPanel.transform, "Taka", "0 B", 40, TextAnchor.LowerLeft, PixelUI.Gold);
        var trt = _takaText.rectTransform;
        trt.anchorMin = trt.anchorMax = new Vector2(0, 0); trt.pivot = new Vector2(0, 0);
        trt.anchoredPosition = new Vector2(16, 12); trt.sizeDelta = new Vector2(270, 50);

        // HEALTH panel (below taka, bottom-left corner)
        var hpPanel = PixelUI.Panel(root, "HealthPanel", new Vector2(0, 0), new Vector2(32, 32), new Vector2(300, 76));
        _healthText = PixelUI.Label(hpPanel.transform, "HealthLabel", "BUS 100%", 18, TextAnchor.UpperLeft, PixelUI.InkDim);
        _healthText.rectTransform.anchoredPosition = new Vector2(16, -10);
        _healthFillRT = PixelUI.BeveledBar(hpPanel.transform, "HealthBar", new Vector2(0, 0), new Vector2(16, 14),
                                           new Vector2(268, 26), PixelUI.Green, out _healthFill, out _healthInner);
    }

    // helpers: anchor a label rect at top-centre / a corner (no-background HUD text)
    void AnchorTopCentre(RectTransform rt, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
    }
    void AnchorCorner(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor; rt.pivot = anchor;
        rt.anchoredPosition = pos; rt.sizeDelta = size;
    }

    void BuildSummaryPanel(Transform root)
    {
        // dim full-screen backdrop
        var backdrop = PixelUI.Block(root, "SummaryBackdrop", new Color(0.02f, 0.01f, 0.05f, 0.88f));
        _summaryPanel = backdrop.gameObject;
        var brt = backdrop.rectTransform;
        brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one; brt.offsetMin = Vector2.zero; brt.offsetMax = Vector2.zero;

        // centred results panel
        var panel = PixelUI.Panel(backdrop.transform, "ResultsPanel", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(720, 760));
        _summaryText = PixelUI.Label(panel.transform, "SummaryText", "", 30, TextAnchor.UpperCenter, PixelUI.Text);
        var rt = _summaryText.rectTransform;
        rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(0.5f, 1);
        rt.anchoredPosition = new Vector2(0, -40); rt.sizeDelta = new Vector2(-60, 700);
        _summaryPanel.SetActive(false);
    }
}
