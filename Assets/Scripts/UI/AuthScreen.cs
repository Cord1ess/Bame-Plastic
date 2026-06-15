using UnityEngine;
using UnityEngine.UI;
using BamePlastic.Net;

/// Login / Sign-up screen — the gate before the main menu. One pixel panel with a LOGIN ⇄ SIGN UP toggle at
/// the top; login asks username/email + password, signup adds an email field. Submits to PlayerAccount (the
/// backend), shows inline errors, and on success calls MenuController.OnAuthSuccess() to enter the title.
/// Built in code like every other screen; toggled by MenuController via SetVisible.
public class AuthScreen
{
    readonly GameObject _root;
    readonly MenuController _menu;

    bool _signupMode;
    InputField _userField, _emailField, _passField;
    Text _status, _heading, _swapHint;
    GameObject _emailRow;
    PixelButton _submit, _swap;

    public AuthScreen(Transform parent, MenuController menu)
    {
        _menu = menu;
        _root = new GameObject("AuthScreen", typeof(RectTransform));
        _root.transform.SetParent(parent, false);
        var rt = (RectTransform)_root.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        _root.SetActive(false);

        const float MX = 90f, PW = 560f, PH = 720f;
        var panel = PixelUI.Panel(_root.transform, "Panel", new Vector2(0, 0.5f), new Vector2(MX, 0f), new Vector2(PW, PH));
        Transform pan = panel.transform;
        float pad = 30f, innerW = PW - pad * 2f;

        _heading = PixelUI.Label(pan, "Heading", "LOG IN", 44, TextAnchor.UpperLeft, PixelUI.Ink);
        Top(_heading.rectTransform, pad, -24, innerW, 52);

        var blurb = PixelUI.Label(pan, "Blurb", "Sign in to carry your Bhara, buses and upgrades.", 18,
                                  TextAnchor.UpperLeft, PixelUI.InkDim);
        Top(blurb.rectTransform, pad, -78, innerW, 30);

        // SERVER address — the FIRST thing to set on a LAN demo (login/store hit this server). Pre-filled with the
        // saved host; type the host laptop's IP (e.g. 192.168.0.42) here, then log in. "localhost" = same machine.
        BuildServerField(pan, pad, innerW);

        // fields
        float y = -210f;
        Field(pan, "Username", "USERNAME OR EMAIL", pad, ref y, innerW, out _userField, false);
        _emailRow = FieldRow(pan, "Email", "EMAIL", pad, ref y, innerW, out _emailField, false);
        Field(pan, "Password", "PASSWORD", pad, ref y, innerW, out _passField, true);

        // status line (errors / progress)
        _status = PixelUI.Label(pan, "Status", "", 18, TextAnchor.UpperLeft, PixelUI.Red);
        Top(_status.rectTransform, pad, y - 6, innerW, 48);
        var st = _status.GetComponent<Text>(); if (st != null) st.horizontalOverflow = HorizontalWrapMode.Wrap;

        // submit
        _submit = PixelUIWidgets.Button(pan, "Submit", "LOG IN", new Vector2(0, 0), new Vector2(pad, 150),
                                        new Vector2(innerW, 60), Submit, PixelUI.Green);

        // toggle login/signup
        _swapHint = PixelUI.Label(pan, "SwapHint", "Don't have an account?", 18, TextAnchor.MiddleLeft, PixelUI.InkDim);
        Top(_swapHint.rectTransform, pad, -PH + 120f, innerW * 0.55f, 30);
        _swap = PixelUIWidgets.Button(pan, "Swap", "SIGN UP", new Vector2(0, 0), new Vector2(pad + innerW * 0.56f, 78),
                                      new Vector2(innerW * 0.44f, 50), ToggleMode, PixelUI.Cyan);

        // offline / skip — play without an account (no currency persistence)
        PixelUIWidgets.Button(pan, "Skip", "PLAY AS GUEST", new Vector2(0, 0), new Vector2(pad, 22),
                              new Vector2(innerW, 44), Guest, PixelUI.InkDim);

        ApplyMode();
    }

    InputField _serverField;

    // A compact SERVER row at the top: type the host laptop's IP (LAN demo) before logging in. Persists to
    // ServerConfig and rebuilds the network service so login/store/online all dial the right machine.
    void BuildServerField(Transform pan, float pad, float w)
    {
        // On a WebGL build served BY the backend, REST + WebSocket are SAME-ORIGIN (derived from the page URL), so
        // there's no server to choose — skip the field entirely. (Desktop/LAN builds keep it.)
        if (ServerConfig.SameOriginWebGL) return;

        var lbl = PixelUI.Label(pan, "SrvLbl", "SERVER  (host IP for LAN, or 'localhost')", 16, TextAnchor.UpperLeft, PixelUI.Gold);
        Top(lbl.rectTransform, pad, -118, w, 22);

        string current = ServerConfig.Offline ? "localhost" : ServerConfig.Host;
        _serverField = PixelUIWidgets.Input(pan, "ServerField", current, "e.g. 192.168.0.42", new Vector2(0, 1),
                                            new Vector2(pad, -142), new Vector2(w, 46), OnServerChanged, 64);
    }

    void OnServerChanged(string value)
    {
        value = (value ?? "").Trim();
        if (value.Length == 0) return;
        ServerConfig.Offline = false;               // typing a real host implies online play
        ServerConfig.Host = value;
        if (SessionContext.Instance != null) SessionContext.Instance.RebuildService();
    }

    // ---- layout helpers ----
    static void Top(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(x, y); rt.sizeDelta = new Vector2(w, h);
    }

    void Field(Transform pan, string name, string label, float pad, ref float y, float w, out InputField field, bool password)
    {
        FieldRow(pan, name, label, pad, ref y, w, out field, password);
    }

    GameObject FieldRow(Transform pan, string name, string label, float pad, ref float y, float w, out InputField field, bool password)
    {
        var row = new GameObject(name + "Row", typeof(RectTransform));
        row.transform.SetParent(pan, false);
        Top((RectTransform)row.transform, pad, y, w, 78);
        Transform r = row.transform;

        var lbl = PixelUI.Label(r, "Lbl", label, 18, TextAnchor.UpperLeft, PixelUI.InkDim);
        Top(lbl.rectTransform, 0, 0, w, 24);
        field = PixelUIWidgets.Input(r, "Field", "", password ? "••••••" : "", new Vector2(0, 1), new Vector2(0, -28),
                                     new Vector2(w, 46), null, 40);
        if (password && field.textComponent != null) field.contentType = InputField.ContentType.Password;
        y -= 88f;
        return row;
    }

    void ToggleMode() { _signupMode = !_signupMode; _status.text = ""; ApplyMode(); }

    void ApplyMode()
    {
        _emailRow.SetActive(_signupMode);
        _heading.text = _signupMode ? "SIGN UP" : "LOG IN";
        _submit.SetLabelText(_signupMode ? "CREATE ACCOUNT" : "LOG IN");
        _swapHint.text = _signupMode ? "Already registered?" : "Don't have an account?";
        _swap.SetLabelText(_signupMode ? "LOG IN" : "SIGN UP");
    }

    void Submit()
    {
        string user = _userField.text.Trim();
        string pass = _passField.text;
        if (string.IsNullOrEmpty(user)) { Fail("Enter a username" + (_signupMode ? "" : " or email")); return; }
        if (string.IsNullOrEmpty(pass)) { Fail("Enter a password"); return; }

        Busy("Connecting…");
        if (_signupMode)
        {
            string email = _emailField.text.Trim();
            if (string.IsNullOrEmpty(email)) { Fail("Enter an email"); return; }
            PlayerAccount.Signup(user, email, pass, OnOk, Fail);
        }
        else
        {
            PlayerAccount.Login(user, pass, OnOk, Fail);
        }
    }

    void OnOk() { _status.text = ""; _menu.OnAuthSuccess(); }

    void Guest()
    {
        PlayerAccount.Logout();           // ensure no stale id; guest has no persistence
        _menu.OnAuthSuccess();
    }

    void Busy(string msg) { _status.color = PixelUI.InkDim; _status.text = msg; }
    void Fail(string msg) { _status.color = PixelUI.Red; _status.text = msg; }

    public void SetVisible(bool on) => _root.SetActive(on);
}
