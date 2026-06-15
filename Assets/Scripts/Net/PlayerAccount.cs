using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace BamePlastic.Net
{
    /// The logged-in player's account + economy, mirrored from the backend (AccountController). "Simple session"
    /// model: signup/login return the profile; we keep the playerId locally (PlayerPrefs) so the session
    /// survives a reload, and re-fetch the profile on menu open. All wallet/unlock mutations go through the
    /// server and return the fresh profile — the DB is the source of truth, this is just the cached view.
    ///
    /// Static singleton so any screen (HUD, shop, customization) reads the same wallet. Raises Changed on every
    /// profile update so the UI can refresh. Network calls run as coroutines on a hidden runner object.
    public static class PlayerAccount
    {
        // ---- cached profile (mirror of the DB row) ----
        public static long Id { get; private set; }
        public static string Username { get; private set; } = "";
        public static string Email { get; private set; } = "";
        public static int Bhara { get; private set; }
        public static long TotalEarnings { get; private set; }
        public static string EquippedBus { get; private set; } = "bus_default";
        public static string EquippedConductor { get; private set; } = "cond_default";
        static readonly HashSet<string> _unlocks = new HashSet<string>();
        static readonly HashSet<string> _equippedUpgrades = new HashSet<string>();

        public static bool LoggedIn => Id > 0;
        public static bool Owns(string itemId) => IsAdmin || string.IsNullOrEmpty(itemId) || itemId.EndsWith("_default") || _unlocks.Contains(itemId);
        /// Is this upgrade currently EQUIPPED (active)? Upgrades are owned via unlocks but only run when equipped.
        public static bool IsUpgradeEquipped(string upgradeId) => _equippedUpgrades.Contains(upgradeId);

        /// Fired whenever the cached profile changes (login, purchase, equip, award). UI subscribes to refresh.
        public static event Action Changed;

        const string KeyId = "acct.playerId";

        // ---------- offline ADMIN bypass (no database) ----------
        // A built-in dev account that logs in WITHOUT touching the backend — for testing the menu/shop/loadout
        // when the server is down. Owns everything, has a huge wallet, and never hits the DB (Refresh/award/buy
        // are no-ops while admin). Use username `admin` + password `bameadmin`.
        const string AdminUser = "admin";
        const string AdminPass = "bameadmin";
        public static bool IsAdmin { get; private set; }

        // every store item id (mirror of StoreCatalog) so the admin owns the full shop
        static readonly string[] AllItems = {
            "bus_default","bus_red","bus_blue","bus_gold","bus_purple","bus_black",
            "cond_default","cond_classic","cond_sharp","cond_legend",
            "upg_engine","upg_brakes","upg_horn","upg_capacity",
        };

        static bool TryAdminLogin(string user, string pass)
        {
            if (!string.Equals(user, AdminUser, StringComparison.OrdinalIgnoreCase) || pass != AdminPass)
                return false;
            IsAdmin = true;
            Id = 999999;                 // sentinel id; never sent to the server
            Username = "ADMIN"; Email = "admin@bame.local";
            Bhara = 999999; TotalEarnings = 0;
            EquippedBus = "bus_default"; EquippedConductor = "cond_default";
            _unlocks.Clear();
            foreach (var it in AllItems) _unlocks.Add(it);
            _equippedUpgrades.Clear();
            foreach (var it in AllItems) if (it.StartsWith("upg_")) _equippedUpgrades.Add(it);   // admin: all upgrades on
            PlayerPrefs.SetString(KeyId, Id.ToString()); PlayerPrefs.SetInt(KeyAdmin, 1); PlayerPrefs.Save();
            Changed?.Invoke();
            return true;
        }

        const string KeyAdmin = "acct.admin";

        // ---------- session ----------

        /// Try to restore a prior session id from PlayerPrefs (call on menu boot). Does NOT fetch — call Refresh.
        public static void RestoreSession()
        {
            // a saved admin session re-grants locally (no DB)
            if (PlayerPrefs.GetInt(KeyAdmin, 0) == 1) { TryAdminLogin(AdminUser, AdminPass); return; }
            long id = 0;
            long.TryParse(PlayerPrefs.GetString(KeyId, "0"), out id);
            Id = id;
        }

        public static void Logout()
        {
            Id = 0; Username = ""; Email = ""; Bhara = 0; TotalEarnings = 0; IsAdmin = false;
            EquippedBus = "bus_default"; EquippedConductor = "cond_default"; _unlocks.Clear();
            PlayerPrefs.DeleteKey(KeyId); PlayerPrefs.DeleteKey(KeyAdmin); PlayerPrefs.Save();
            Changed?.Invoke();
        }

        // ---------- REST calls (callbacks: onOk(), onErr(message)) ----------

        public static void Signup(string username, string email, string password, Action onOk, Action<string> onErr)
            => Post("/api/auth/signup", Json(("username", username), ("email", email), ("password", password)), onOk, onErr);

        public static void Login(string usernameOrEmail, string password, Action onOk, Action<string> onErr)
        {
            // offline admin bypass — succeeds instantly without the backend
            if (TryAdminLogin(usernameOrEmail?.Trim(), password)) { onOk?.Invoke(); return; }
            Post("/api/auth/login", Json(("username", usernameOrEmail), ("password", password)), onOk, onErr);
        }

        /// Re-fetch the latest profile for the stored id (wallet/unlocks may have changed elsewhere).
        public static void Refresh(Action onOk = null, Action<string> onErr = null)
        {
            if (IsAdmin) { onOk?.Invoke(); return; }   // admin is local-only; nothing to fetch
            if (Id <= 0) { onErr?.Invoke("Not logged in"); return; }
            Get($"/api/auth/profile/{Id}", onOk, onErr);
        }

        /// Daily login bonus — grants Bhara once per calendar day. `onResult(grantedBonus)` gets the Bhara added
        /// (0 if already claimed today). Best-effort: silently does nothing for guests/admin/offline.
        public static event Action<int> DailyBonusGranted;   // UI subscribes to show "+N Bhara!"
        public static void ClaimDailyBonus()
        {
            if (!LoggedIn || IsAdmin || ServerConfig.Offline) return;
            Runner.Run(CoDailyBonus());
        }

        static IEnumerator CoDailyBonus()
        {
            using var req = new UnityWebRequest(Base + $"/api/auth/daily-bonus?playerId={Id}", UnityWebRequest.kHttpVerbPOST);
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes("{}"));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = 10;
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success) yield break;
            string body = req.downloadHandler.text;
            ApplyProfile(body);                                  // updates the wallet
            // parse the "bonus" field to notify the UI
            try { var dto = JsonUtility.FromJson<BonusDto>(body); if (dto != null && dto.granted && dto.bonus > 0) DailyBonusGranted?.Invoke(dto.bonus); } catch { }
        }
        [Serializable] class BonusDto { public bool granted; public int bonus; }

        public static void BuyPack(string packId, Action onOk, Action<string> onErr)
        {
            if (IsAdmin) { Bhara += 1000; Changed?.Invoke(); onOk?.Invoke(); return; }   // local, no DB
            Post("/api/auth/store/buy-pack", Json(("playerId", Id.ToString()), ("packId", packId)), onOk, onErr);
        }

        public static void BuyItem(string itemId, Action onOk, Action<string> onErr)
        {
            if (IsAdmin) { if (!string.IsNullOrEmpty(itemId)) _unlocks.Add(itemId); Changed?.Invoke(); onOk?.Invoke(); return; }
            Post("/api/auth/store/buy-item", Json(("playerId", Id.ToString()), ("itemId", itemId)), onOk, onErr);
        }

        public static void Equip(string itemId, Action onOk, Action<string> onErr)
        {
            if (IsAdmin) { EquipLocal(itemId); Changed?.Invoke(); onOk?.Invoke(); return; }
            Post("/api/auth/store/equip", Json(("playerId", Id.ToString()), ("itemId", itemId)), onOk, onErr);
        }

        // admin: equip locally (bus colours vs conductors by id prefix)
        static void EquipLocal(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return;
            if (itemId.StartsWith("bus_")) EquippedBus = itemId;
            else if (itemId.StartsWith("cond_")) EquippedConductor = itemId;
        }

        /// Award shift earnings to the account at shift end (taka → career + Bhara). Also reports career stats
        /// (fares collected this shift, whether you finished #1) so the server can unlock achievements; any newly
        /// unlocked ones come back in the response and fire AchievementUnlocked. Fire-and-forget is fine.
        public static void AwardEarnings(int earnings, int fares = 0, bool won = false, Action onOk = null, Action<string> onErr = null)
        {
            if (IsAdmin) { onOk?.Invoke(); return; }   // admin wallet is fixed-huge; nothing to persist
            if (Id <= 0) { onErr?.Invoke("Not logged in"); return; }
            Post($"/api/auth/shift/award?playerId={Id}&earnings={Mathf.Max(0, earnings)}&fares={Mathf.Max(0, fares)}&won={(won ? "true" : "false")}", "{}", onOk, onErr);
        }

        /// Fired when the server reports a newly-unlocked achievement at shift award (name, Bhara reward). HUD pings.
        public static event Action<string, int> AchievementUnlocked;

        public static void FetchCatalog(Action<string> onJson, Action<string> onErr)
            => GetRaw("/api/auth/store/catalog", onJson, onErr);

        /// Fetch the achievement catalog with this player's unlocked state (raw JSON) for the Achievements panel.
        public static void FetchAchievements(Action<string> onJson, Action<string> onErr)
        {
            if (Id <= 0) { onErr?.Invoke("Not logged in"); return; }
            GetRaw($"/api/auth/achievements/{Id}", onJson, onErr);
        }

        // ---------- payments (SSLCommerz) ----------
        /// Start a real-money pack purchase: returns the gateway URL to open in the browser + the transaction id.
        public static void InitiatePayment(string packId, Action<string, string> onReady, Action<string> onErr)
        {
            if (!LoggedIn) { onErr?.Invoke("Log in to buy Bhara"); return; }
            if (IsAdmin) { onErr?.Invoke("Admin wallet is unlimited — no purchase needed"); return; }
            PostRaw($"/api/pay/initiate?playerId={Id}&packId={packId}", json =>
            {
                var dto = SafeJson<PayInitDto>(json);
                if (dto != null && !string.IsNullOrEmpty(dto.gatewayUrl)) onReady?.Invoke(dto.gatewayUrl, dto.tranId);
                else onErr?.Invoke(ExtractError(json, "Could not start payment"));
            }, onErr);
        }

        /// Poll a payment's status: calls back with the status string ("PENDING"/"COMPLETED"/"FAILED").
        public static void PollPaymentStatus(string tranId, Action<string> onStatus, Action<string> onErr)
        {
            GetRaw($"/api/pay/status?tranId={tranId}", json =>
            {
                var dto = SafeJson<PayStatusDto>(json);
                onStatus?.Invoke(dto != null ? dto.status : "");
            }, onErr);
        }

        /// Poll the payment every ~2s until it COMPLETES or FAILS (or times out). On COMPLETED, refreshes the
        /// profile (so the new Bhara shows) before invoking onDone("COMPLETED"). Self-contained — needs no
        /// MonoBehaviour from the caller (runs on the account Runner).
        public static void WaitForPayment(string tranId, Action<string> onDone)
        {
            Runner.Run(CoWaitForPayment(tranId, onDone));
        }

        static IEnumerator CoWaitForPayment(string tranId, Action<string> onDone)
        {
            float deadline = 240f;   // give the player time to complete the browser checkout
            float elapsed = 0f;
            while (elapsed < deadline)
            {
                yield return new WaitForSecondsRealtime(2.5f);
                elapsed += 2.5f;
                string status = null; bool got = false;
                PollPaymentStatus(tranId, s => { status = s; got = true; }, _ => { got = true; });
                float w = 0f; while (!got && w < 12f) { w += Time.unscaledDeltaTime; yield return null; }
                if (status == "COMPLETED") { Refresh(); onDone?.Invoke("COMPLETED"); yield break; }
                if (status == "FAILED")    { onDone?.Invoke("FAILED"); yield break; }
            }
            onDone?.Invoke("TIMEOUT");
        }

        [Serializable] class PayInitDto { public string tranId; public string gatewayUrl; }
        [Serializable] class PayStatusDto { public string tranId; public string status; public int bhara; }
        static T SafeJson<T>(string s) where T : class { try { return JsonUtility.FromJson<T>(s); } catch { return null; } }

        // ---------- plumbing ----------

        // POST that returns the raw JSON to the caller WITHOUT applying it as a profile (used by payment initiate).
        static void PostRaw(string path, Action<string> onJson, Action<string> onErr)
            => Runner.Run(CoPostRaw(path, onJson, onErr));

        static IEnumerator CoPostRaw(string path, Action<string> onJson, Action<string> onErr)
        {
            using var req = new UnityWebRequest(Base + path, UnityWebRequest.kHttpVerbPOST);
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes("{}"));
            req.SetRequestHeader("Content-Type", "application/json");
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = 15;
            yield return req.SendWebRequest();
            string text = req.downloadHandler != null ? req.downloadHandler.text : "";
            if (req.result == UnityWebRequest.Result.Success) onJson?.Invoke(text);
            else onErr?.Invoke(ExtractError(text, req.error));
        }

        static void Post(string path, string body, Action onOk, Action<string> onErr, bool raw = false)
            => Runner.Run(Co(UnityWebRequest.kHttpVerbPOST, path, body, onOk, onErr));

        static void Get(string path, Action onOk, Action<string> onErr)
            => Runner.Run(Co(UnityWebRequest.kHttpVerbGET, path, null, onOk, onErr));

        static void GetRaw(string path, Action<string> onJson, Action<string> onErr)
            => Runner.Run(CoRaw(path, onJson, onErr));

        static string Base => ServerConfig.Offline ? "http://localhost:8090" : ServerConfig.HttpBase;

        static IEnumerator Co(string verb, string path, string body, Action onOk, Action<string> onErr)
        {
            using var req = new UnityWebRequest(Base + path, verb);
            if (body != null)
            {
                byte[] raw = Encoding.UTF8.GetBytes(body);
                req.uploadHandler = new UploadHandlerRaw(raw);
                req.SetRequestHeader("Content-Type", "application/json");
            }
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = 12;
            yield return req.SendWebRequest();

            string text = req.downloadHandler != null ? req.downloadHandler.text : "";
            if (req.result == UnityWebRequest.Result.Success)
            {
                ApplyProfile(text);
                onOk?.Invoke();
            }
            else
            {
                onErr?.Invoke(ExtractError(text, req.error));
            }
        }

        static IEnumerator CoRaw(string path, Action<string> onJson, Action<string> onErr)
        {
            using var req = UnityWebRequest.Get(Base + path);
            req.timeout = 12;
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success) onJson?.Invoke(req.downloadHandler.text);
            else onErr?.Invoke(req.error);
        }

        // ---------- (de)serialization (hand-rolled; no JSON lib dependency) ----------

        [Serializable] class ProfileDto {
            public long id; public string username; public string email; public int bhara;
            public long totalEarnings; public string unlocks; public string equippedBus; public string equippedConductor;
            public string equippedUpgrades;
        }
        [Serializable] class ErrDto { public string error; }

        static void ApplyProfile(string json)
        {
            if (string.IsNullOrEmpty(json) || json.IndexOf("\"id\"", StringComparison.Ordinal) < 0) return;
            ProfileDto p;
            try { p = JsonUtility.FromJson<ProfileDto>(json); } catch { return; }
            if (p == null || p.id <= 0) return;
            Id = p.id; Username = p.username ?? ""; Email = p.email ?? "";
            Bhara = p.bhara; TotalEarnings = p.totalEarnings;
            EquippedBus = string.IsNullOrEmpty(p.equippedBus) ? "bus_default" : p.equippedBus;
            EquippedConductor = string.IsNullOrEmpty(p.equippedConductor) ? "cond_default" : p.equippedConductor;
            _unlocks.Clear();
            if (!string.IsNullOrEmpty(p.unlocks))
                foreach (var t in p.unlocks.Split(',')) { var s = t.Trim(); if (s.Length > 0) _unlocks.Add(s); }
            _equippedUpgrades.Clear();
            if (!string.IsNullOrEmpty(p.equippedUpgrades))
                foreach (var t in p.equippedUpgrades.Split(',')) { var s = t.Trim(); if (s.Length > 0) _equippedUpgrades.Add(s); }
            PlayerPrefs.SetString(KeyId, Id.ToString()); PlayerPrefs.Save();
            Changed?.Invoke();

            FireUnlockedFrom(json);
        }

        // The shift-award response may carry an "unlocked":[{code,name,bhara},...] array of newly-earned
        // achievements. JsonUtility can't read an inline object array, so scan it lightly and fire a toast each.
        static void FireUnlockedFrom(string json)
        {
            if (AchievementUnlocked == null) return;
            int u = json.IndexOf("\"unlocked\"", StringComparison.Ordinal);
            if (u < 0) return;
            int arr = json.IndexOf('[', u); if (arr < 0) return;
            int end = json.IndexOf(']', arr); if (end < 0) return;
            string body = json.Substring(arr, end - arr);
            int i = 0;
            while (true)
            {
                int n = body.IndexOf("\"name\"", i, StringComparison.Ordinal); if (n < 0) break;
                int q1 = body.IndexOf('"', n + 6); if (q1 < 0) break;
                int q2 = body.IndexOf('"', q1 + 1); if (q2 < 0) break;
                string name = body.Substring(q1 + 1, q2 - q1 - 1);
                int bhara = 0;
                int b = body.IndexOf("\"bhara\"", q2, StringComparison.Ordinal);
                if (b >= 0)
                {
                    int c = body.IndexOf(':', b) + 1; int e2 = c;
                    while (e2 < body.Length && (char.IsDigit(body[e2]) || body[e2] == '-' || body[e2] == ' ')) e2++;
                    int.TryParse(body.Substring(c, e2 - c).Trim(), out bhara);
                }
                AchievementUnlocked.Invoke(name, bhara);
                i = q2 + 1;
            }
        }

        static string ExtractError(string body, string fallback)
        {
            if (!string.IsNullOrEmpty(body) && body.Contains("\"error\""))
            {
                try { var e = JsonUtility.FromJson<ErrDto>(body); if (!string.IsNullOrEmpty(e?.error)) return e.error; } catch { }
            }
            return string.IsNullOrEmpty(fallback) ? "Network error" : fallback;
        }

        static string Json(params (string k, string v)[] kv)
        {
            var sb = new StringBuilder("{");
            for (int i = 0; i < kv.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(kv[i].k).Append("\":");
                // numeric fields (playerId) emit bare; everything else quoted
                bool numeric = kv[i].k == "playerId";
                if (numeric) sb.Append(kv[i].v);
                else sb.Append('"').Append(Escape(kv[i].v)).Append('"');
            }
            return sb.Append('}').ToString();
        }

        static string Escape(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace("\\", "\\\\").Replace("\"", "\\\"");

        // ---------- coroutine runner (account calls aren't tied to any one screen) ----------
        class Runner : MonoBehaviour
        {
            static Runner _inst;
            public static void Run(IEnumerator co)
            {
                if (!Application.isPlaying) return;
                if (_inst == null)
                {
                    var go = new GameObject("PlayerAccountRunner");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    _inst = go.AddComponent<Runner>();
                }
                _inst.StartCoroutine(co);
            }
        }
    }
}
