using UnityEngine;
using UnityEngine.UI;
using BamePlastic.Net;

/// Top-right currency widget: the player's name + Bhara balance with a "+" button that opens the purchase
/// page. Lives on the menu canvas, visible on the Title screen. Subscribes to PlayerAccount.Changed so the
/// balance updates instantly after a purchase/award. Hidden for guests (no account → no wallet).
public class WalletBar
{
    readonly GameObject _root;
    readonly Text _name, _amount;

    public WalletBar(Transform parent, System.Action onPlus)
    {
        _root = new GameObject("WalletBar", typeof(RectTransform));
        _root.transform.SetParent(parent, false);
        var rt = (RectTransform)_root.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(1, 1);
        rt.anchoredPosition = new Vector2(-28, -24); rt.sizeDelta = new Vector2(360, 56);

        var panel = PixelUI.Panel(_root.transform, "Panel", new Vector2(1, 1), new Vector2(0, 0), new Vector2(360, 56));
        Transform pan = panel.transform;

        // "+" button on the right
        PixelUIWidgets.Button(pan, "Plus", "+", new Vector2(1, 0.5f), new Vector2(-10, 0), new Vector2(40, 40),
                              onPlus, PixelUI.Gold);

        // Bhara amount (gold) left of the +
        _amount = PixelUI.Label(pan, "Amount", "0", 28, TextAnchor.MiddleRight, PixelUI.Gold);
        var art = _amount.rectTransform;
        art.anchorMin = new Vector2(1, 0.5f); art.anchorMax = new Vector2(1, 0.5f); art.pivot = new Vector2(1, 0.5f);
        art.anchoredPosition = new Vector2(-58, -2); art.sizeDelta = new Vector2(150, 36);

        var coin = PixelUI.Label(pan, "Coin", "BHARA", 14, TextAnchor.MiddleRight, PixelUI.InkDim);
        var crt = coin.rectTransform;
        crt.anchorMin = new Vector2(1, 0.5f); crt.anchorMax = new Vector2(1, 0.5f); crt.pivot = new Vector2(1, 0.5f);
        crt.anchoredPosition = new Vector2(-58, 16); crt.sizeDelta = new Vector2(150, 16);

        // player name on the left
        _name = PixelUI.Label(pan, "Name", "", 18, TextAnchor.MiddleLeft, PixelUI.Ink);
        var nrt = _name.rectTransform;
        nrt.anchorMin = new Vector2(0, 0.5f); nrt.anchorMax = new Vector2(0, 0.5f); nrt.pivot = new Vector2(0, 0.5f);
        nrt.anchoredPosition = new Vector2(16, 0); nrt.sizeDelta = new Vector2(150, 40);

        // a little toast under the wallet for the daily bonus
        _toast = PixelUI.Label(_root.transform, "BonusToast", "", 22, TextAnchor.MiddleRight, PixelUI.Green);
        var trt = _toast.rectTransform;
        trt.anchorMin = trt.anchorMax = new Vector2(1, 1); trt.pivot = new Vector2(1, 1);
        trt.anchoredPosition = new Vector2(-28, -84); trt.sizeDelta = new Vector2(360, 30);
        _toast.gameObject.SetActive(false);

        PlayerAccount.Changed += Refresh;
        PlayerAccount.DailyBonusGranted += ShowBonus;
        Refresh();
    }

    Text _toast;

    void ShowBonus(int bhara)
    {
        if (_toast == null) return;
        _toast.text = "+" + bhara + " BHARA  daily bonus!";
        _toast.gameObject.SetActive(true);
        // self-hide after a few seconds via a tiny ticker on the toast object (WalletBar isn't a MonoBehaviour)
        var hide = _toast.gameObject.GetComponent<TimedHide>() ?? _toast.gameObject.AddComponent<TimedHide>();
        hide.Show(4f);
    }

    /// Tiny self-hiding helper for transient UI (no owning MonoBehaviour needed).
    class TimedHide : MonoBehaviour
    {
        float _until;
        public void Show(float seconds) { _until = Time.unscaledTime + seconds; gameObject.SetActive(true); }
        void Update() { if (Time.unscaledTime > _until) gameObject.SetActive(false); }
    }

    public void Refresh()
    {
        if (_root == null) return;
        bool show = PlayerAccount.LoggedIn;
        _root.SetActive(show);
        if (!show) return;
        _name.text = PlayerAccount.Username;
        _amount.text = PlayerAccount.Bhara.ToString();
    }

    public void SetVisible(bool on) => _root.SetActive(on && PlayerAccount.LoggedIn);
}
