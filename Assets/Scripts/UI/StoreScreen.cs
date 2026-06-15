using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using BamePlastic.Net;

/// The shop: two tabs — GET BHARA (4 taka→Bhara currency packs, industry-standard layout) and CUSTOMIZE
/// (bus colours, conductors, upgrades bought with Bhara, then equipped). Purchases go through PlayerAccount →
/// the backend (server-authoritative price + wallet), and the returned profile refreshes the UI. The catalogue
/// shown here MIRRORS StoreCatalog.java (kept in sync); the server still validates every buy.
public class StoreScreen
{
    readonly GameObject _root;
    readonly MenuController _menu;
    GameObject[] _pages;
    Text _status;
    readonly List<System.Action> _refreshers = new List<System.Action>();

    // ---- client mirror of StoreCatalog.java (display only; server is authoritative on price/ownership) ----
    struct Pack { public string id; public int taka, bhara; public string label; public bool best; }
    struct Item { public string id, kind, label; public int price; public Color swatch; }

    static readonly Pack[] Packs =
    {
        new Pack{ id="pack_500",  taka=500,  bhara=55,  label="Starter", best=false },
        new Pack{ id="pack_1500", taka=1500, bhara=175, label="Handful", best=false },
        new Pack{ id="pack_3000", taka=3000, bhara=375, label="Stack",   best=true  },
        new Pack{ id="pack_5000", taka=5000, bhara=675, label="Tycoon",  best=false },
    };

    static readonly Item[] Items =
    {
        new Item{ id="bus_default", kind="bus_color", price=0,   label="Classic Green", swatch=new Color(0.28f,0.55f,0.30f) },
        new Item{ id="bus_red",     kind="bus_color", price=120, label="Tomato Red",    swatch=new Color(0.85f,0.27f,0.22f) },
        new Item{ id="bus_blue",    kind="bus_color", price=120, label="Sky Blue",      swatch=new Color(0.30f,0.55f,0.85f) },
        new Item{ id="bus_gold",    kind="bus_color", price=300, label="Gold Rush",     swatch=new Color(0.92f,0.74f,0.25f) },
        new Item{ id="bus_purple",  kind="bus_color", price=220, label="Royal Purple",  swatch=new Color(0.45f,0.30f,0.65f) },
        new Item{ id="bus_black",   kind="bus_color", price=400, label="Midnight",      swatch=new Color(0.12f,0.12f,0.16f) },
        new Item{ id="upg_engine",  kind="upgrade",   price=350, label="Engine Tune",   swatch=PixelUI.Cyan },
        new Item{ id="upg_brakes",  kind="upgrade",   price=280, label="Better Brakes", swatch=PixelUI.Cyan },
        new Item{ id="upg_horn",    kind="upgrade",   price=90,  label="Loud Horn",     swatch=PixelUI.Cyan },
        new Item{ id="upg_capacity",kind="upgrade",   price=450, label="Extra Seats",   swatch=PixelUI.Cyan },
    };

    public StoreScreen(Transform parent, MenuController menu)
    {
        _menu = menu;
        _root = new GameObject("StoreScreen", typeof(RectTransform));
        _root.transform.SetParent(parent, false);
        var rt = (RectTransform)_root.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        _root.SetActive(false);

        // wider centred panel (grid of cards)
        const float PW = 1100f, PH = 760f;
        var panel = PixelUI.Panel(_root.transform, "Panel", new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(PW, PH));
        Transform pan = panel.transform;
        float pad = 34f, innerW = PW - pad * 2f;

        var heading = PixelUI.Label(pan, "Heading", "SHOP", 40, TextAnchor.UpperLeft, PixelUI.Ink);
        var hrt = heading.rectTransform;
        hrt.anchorMin = hrt.anchorMax = new Vector2(0, 1); hrt.pivot = new Vector2(0, 1);
        hrt.anchoredPosition = new Vector2(pad, -22); hrt.sizeDelta = new Vector2(360, 48);

        // live wallet readout in the header
        var bal = PixelUI.Label(pan, "Bal", "", 26, TextAnchor.UpperRight, PixelUI.Gold);
        var brt = bal.rectTransform;
        brt.anchorMin = brt.anchorMax = new Vector2(1, 1); brt.pivot = new Vector2(1, 1);
        brt.anchoredPosition = new Vector2(-pad, -26); brt.sizeDelta = new Vector2(360, 36);
        _refreshers.Add(() => bal.text = PlayerAccount.Bhara + "  BHARA");

        string[] tabs = { "GET BHARA", "CUSTOMIZE" };
        _pages = new GameObject[tabs.Length];
        for (int i = 0; i < tabs.Length; i++)
        {
            var page = new GameObject("Page" + i, typeof(RectTransform));
            page.transform.SetParent(pan, false);
            var prt = (RectTransform)page.transform;
            prt.anchorMin = new Vector2(0, 1); prt.anchorMax = new Vector2(0, 1); prt.pivot = new Vector2(0, 1);
            // leave a clear band at the BOTTOM for the status line + Back button (so content never sits over them)
            prt.anchoredPosition = new Vector2(pad, -150); prt.sizeDelta = new Vector2(innerW, PH - 310f);
            _pages[i] = page;
        }

        BuildPacks(_pages[0].transform, innerW);
        BuildCustomize(_pages[1].transform, innerW);

        PixelUIWidgets.Tabs(pan, "Tabs", tabs, new Vector2(0, 1), new Vector2(pad, -88), new Vector2(innerW, 50), SelectPage);

        _status = PixelUI.Label(pan, "Status", "", 18, TextAnchor.MiddleCenter, PixelUI.Green);
        var srt = _status.rectTransform;
        srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 0); srt.pivot = new Vector2(0.5f, 0);
        srt.anchoredPosition = new Vector2(0, 84); srt.sizeDelta = new Vector2(innerW, 30);

        PixelUIWidgets.Button(pan, "Back", "◀ BACK", new Vector2(0, 0), new Vector2(pad, 22), new Vector2(220, 56),
                              () => _menu.BackToTitle(), PixelUI.InkDim);

        PlayerAccount.Changed += RefreshAll;
        SelectPage(0);
    }

    void SelectPage(int i)
    {
        if (_pages == null) return;
        for (int p = 0; p < _pages.Length; p++) if (_pages[p]) _pages[p].SetActive(p == i);
    }

    // ---------- GET BHARA: 4 packs in a row ----------
    void BuildPacks(Transform p, float w)
    {
        var note = PixelUI.Label(p, "Rate", "Exchange rate: 100 taka = 10 Bhara  •  bigger packs include a bonus",
                                 18, TextAnchor.UpperLeft, PixelUI.InkDim);
        var nrt = note.rectTransform; nrt.anchorMin = nrt.anchorMax = new Vector2(0, 1); nrt.pivot = new Vector2(0, 1);
        nrt.anchoredPosition = new Vector2(2, 0); nrt.sizeDelta = new Vector2(w, 28);

        int n = Packs.Length;
        float gap = 22f, cardW = (w - gap * (n - 1)) / n, cardH = 320f;
        for (int i = 0; i < n; i++)
        {
            var pk = Packs[i];
            var card = PixelUI.Panel(p, "Pack" + i, new Vector2(0, 1), new Vector2(i * (cardW + gap), -48),
                                     new Vector2(cardW, cardH), soft: true);
            Transform c = card.transform;

            if (pk.best)
            {
                var ribbon = PixelUI.Label(c, "Best", "BEST VALUE", 16, TextAnchor.MiddleCenter, PixelUI.Ink);
                var rrt = ribbon.rectTransform; rrt.anchorMin = rrt.anchorMax = new Vector2(0.5f, 1); rrt.pivot = new Vector2(0.5f, 1);
                rrt.anchoredPosition = new Vector2(0, -10); rrt.sizeDelta = new Vector2(cardW - 20, 24);
                var rb = PixelUI.Block(c, "RibbonBg", PixelUI.Gold); rb.rectTransform.SetSiblingIndex(0);
                var rbrt = rb.rectTransform; rbrt.anchorMin = new Vector2(0,1); rbrt.anchorMax = new Vector2(1,1); rbrt.pivot=new Vector2(0.5f,1);
                rbrt.anchoredPosition = new Vector2(0,-8); rbrt.sizeDelta = new Vector2(-16, 28);
            }

            PixelUI.Label(c, "Label", pk.label, 24, TextAnchor.MiddleCenter, PixelUI.Ink)
                   .rectTransform.anchoredPosition = new Vector2(0, cardH * 0.30f);

            var amt = PixelUI.Label(c, "Amt", pk.bhara + "", 52, TextAnchor.MiddleCenter, PixelUI.Gold);
            amt.rectTransform.anchoredPosition = new Vector2(0, cardH * 0.06f);
            PixelUI.Label(c, "Bhara", "BHARA", 16, TextAnchor.MiddleCenter, PixelUI.InkDim)
                   .rectTransform.anchoredPosition = new Vector2(0, -cardH * 0.10f);

            string id = pk.id;
            PixelUIWidgets.Button(c, "Buy", pk.taka + " TK", new Vector2(0.5f, 0), new Vector2(0, 18),
                                  new Vector2(cardW - 28, 54), () => BuyPack(id), PixelUI.Green);
        }
    }

    void BuyPack(string id)
    {
        if (!RequireAccount()) return;
        // REAL payment via SSLCommerz (sandbox): open the hosted gateway in the browser, then poll for completion.
        Busy("Opening secure checkout…");
        ShowCheckout("Starting secure checkout…\nA payment page will open in your browser.", false);
        PlayerAccount.InitiatePayment(id, (gatewayUrl, tranId) =>
        {
            Application.OpenURL(gatewayUrl);   // the player completes payment in their browser (sandbox test card)
            ShowCheckout("Complete the payment in your browser.\n\nUse a SANDBOX test card —\nVISA 4111 1111 1111 1111, any future expiry, any CVV.\n\nWaiting for confirmation…", true);
            PlayerAccount.WaitForPayment(tranId, result =>
            {
                if (result == "COMPLETED") { HideCheckout(); Ok("Payment complete — Bhara added!"); }
                else if (result == "FAILED") { HideCheckout(); Fail("Payment failed — no charge made."); }
                else { /* TIMEOUT */ HideCheckout(); Fail("Payment timed out. If you paid, your balance will update shortly."); }
            });
        }, msg => { HideCheckout(); Fail(msg); });
    }

    // ---------- CUSTOMIZE: owned/equip grid by category ----------
    void BuildCustomize(Transform p, float w)
    {
        float y = 0f;
        y = Category(p, "BUS COLOURS", "bus_color", w, y);
        y = Category(p, "UPGRADES",    "upgrade",   w, y);
    }

    float Category(Transform p, string title, string kind, float w, float y)
    {
        var head = PixelUI.Label(p, "Cat_" + kind, title, 22, TextAnchor.MiddleLeft, PixelUI.Gold);
        var hrt = head.rectTransform; hrt.anchorMin = hrt.anchorMax = new Vector2(0, 1); hrt.pivot = new Vector2(0, 1);
        hrt.anchoredPosition = new Vector2(2, y); hrt.sizeDelta = new Vector2(w, 28);
        y -= 36f;

        var list = new List<Item>();
        foreach (var it in Items) if (it.kind == kind) list.Add(it);

        float gap = 16f, perRow = 6f, cardW = (w - gap * (perRow - 1)) / perRow, cardH = 150f;
        for (int i = 0; i < list.Count; i++)
        {
            int col = i % (int)perRow;
            var card = PixelUI.Panel(p, kind + "_" + i, new Vector2(0, 1), new Vector2(col * (cardW + gap), y),
                                     new Vector2(cardW, cardH), soft: true);
            BuildItemCard(card.transform, list[i], cardW, cardH);
            if (col == (int)perRow - 1) y -= cardH + gap;
        }
        if (list.Count % (int)perRow != 0) y -= cardH + gap;
        return y - 12f;
    }

    void BuildItemCard(Transform c, Item it, float w, float h)
    {
        // swatch
        var sw = PixelUI.Block(c, "Swatch", it.swatch);
        var swrt = sw.rectTransform; swrt.anchorMin = new Vector2(0.5f, 1); swrt.anchorMax = new Vector2(0.5f, 1); swrt.pivot = new Vector2(0.5f, 1);
        swrt.anchoredPosition = new Vector2(0, -12); swrt.sizeDelta = new Vector2(w - 28, 46);

        PixelUI.Label(c, "Name", it.label, 16, TextAnchor.MiddleCenter, PixelUI.Ink)
               .rectTransform.anchoredPosition = new Vector2(0, -2);

        // action area — depends on owned/equipped state; rebuilt on every Changed
        var btnGo = new GameObject("Action", typeof(RectTransform));
        btnGo.transform.SetParent(c, false);
        Top((RectTransform)btnGo.transform, w, h);

        string id = it.id; string kind = it.kind; int price = it.price;
        System.Action rebuild = null;
        rebuild = () =>
        {
            foreach (Transform ch in btnGo.transform) Object.Destroy(ch.gameObject);
            bool owned = PlayerAccount.Owns(id);
            bool isUpgrade = kind == "upgrade";
            bool equippable = kind == "bus_color" || kind == "conductor" || isUpgrade;
            bool equipped = (kind == "bus_color" && PlayerAccount.EquippedBus == id)
                         || (kind == "conductor" && PlayerAccount.EquippedConductor == id)
                         || (isUpgrade && PlayerAccount.IsUpgradeEquipped(id));

            if (!owned)
            {
                PixelUIWidgets.Button(btnGo.transform, "Buy", price + " ♦", new Vector2(0.5f, 0), new Vector2(0, 8),
                                      new Vector2(w - 26, 40), () => BuyItem(id), PixelUI.Green);
            }
            else if (equipped && isUpgrade)
            {
                // upgrades TOGGLE — an equipped upgrade shows an UNEQUIP button (cosmetics show a static EQUIPPED tag)
                PixelUIWidgets.Button(btnGo.transform, "Unequip", "✓ ON", new Vector2(0.5f, 0), new Vector2(0, 8),
                                      new Vector2(w - 26, 40), () => EquipItem(id), PixelUI.Green);
            }
            else if (equipped)
            {
                PixelUI.Label(btnGo.transform, "On", "EQUIPPED", 16, TextAnchor.MiddleCenter, PixelUI.Green)
                       .rectTransform.anchoredPosition = new Vector2(0, 24);
            }
            else if (equippable)
            {
                PixelUIWidgets.Button(btnGo.transform, "Equip", isUpgrade ? "EQUIP" : "EQUIP", new Vector2(0.5f, 0), new Vector2(0, 8),
                                      new Vector2(w - 26, 40), () => EquipItem(id), PixelUI.Cyan);
            }
            else
            {
                PixelUI.Label(btnGo.transform, "Owned", "OWNED", 16, TextAnchor.MiddleCenter, PixelUI.InkDim)
                       .rectTransform.anchoredPosition = new Vector2(0, 24);
            }
        };
        _refreshers.Add(rebuild);
        rebuild();
    }

    static void Top(RectTransform rt, float w, float h)
    {
        rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 0); rt.pivot = new Vector2(0.5f, 0);
        rt.anchoredPosition = new Vector2(0, 0); rt.sizeDelta = new Vector2(0, 52);
    }

    void BuyItem(string id)
    {
        if (!RequireAccount()) return;
        Busy("Buying…");
        PlayerAccount.BuyItem(id, () => Ok("Unlocked!"), Fail);
    }

    void EquipItem(string id)
    {
        if (!RequireAccount()) return;
        PlayerAccount.Equip(id, () => Ok("Equipped"), Fail);
    }

    bool RequireAccount()
    {
        if (PlayerAccount.LoggedIn) return true;
        Fail("Log in to buy (you're playing as guest)");
        return false;
    }

    // ---------- checkout overlay (shown during the SSLCommerz browser payment) ----------
    GameObject _checkout;
    Text _checkoutText;

    void ShowCheckout(string msg, bool cancellable)
    {
        if (_checkout == null) BuildCheckout();
        _checkout.SetActive(true);
        _checkoutText.text = msg;
    }
    void HideCheckout() { if (_checkout != null) _checkout.SetActive(false); }

    void BuildCheckout()
    {
        // dim full-screen backdrop + a centred panel, parented to the store root so it draws on top.
        var backdrop = PixelUI.Block(_root.transform, "CheckoutBackdrop", new Color(0.02f, 0.01f, 0.05f, 0.9f));
        _checkout = backdrop.gameObject;
        var brt = backdrop.rectTransform;
        brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one; brt.offsetMin = Vector2.zero; brt.offsetMax = Vector2.zero;

        var panel = PixelUI.Panel(backdrop.transform, "CheckoutPanel", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(720, 460));
        PixelUI.Label(panel.transform, "Title", "SECURE CHECKOUT", 34, TextAnchor.UpperCenter, PixelUI.Gold)
               .rectTransform.anchoredPosition = new Vector2(0, -28);

        _checkoutText = PixelUI.Label(panel.transform, "Body", "", 22, TextAnchor.MiddleCenter, PixelUI.Ink);
        var bt = _checkoutText.rectTransform;
        bt.anchorMin = new Vector2(0, 0.5f); bt.anchorMax = new Vector2(1, 0.5f); bt.pivot = new Vector2(0.5f, 0.5f);
        bt.anchoredPosition = new Vector2(0, 20); bt.sizeDelta = new Vector2(-60, 240);
        _checkoutText.horizontalOverflow = HorizontalWrapMode.Wrap;

        PixelUIWidgets.Button(panel.transform, "CloseCheckout", "CLOSE", new Vector2(0.5f, 0), new Vector2(0, 24),
                              new Vector2(220, 56), HideCheckout, PixelUI.InkDim);
        _checkout.SetActive(false);
    }

    void RefreshAll() { foreach (var r in _refreshers) r?.Invoke(); }

    void Busy(string m) { _status.color = PixelUI.InkDim; _status.text = m; }
    void Ok(string m)   { _status.color = PixelUI.Green;  _status.text = m; }
    void Fail(string m) { _status.color = PixelUI.Red;    _status.text = m; }

    public void SetVisible(bool on)
    {
        _root.SetActive(on);
        if (on) { RefreshAll(); SelectPage(0); }
    }
}
