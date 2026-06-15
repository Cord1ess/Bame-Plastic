using UnityEngine;

namespace BamePlastic.Net
{
    /// Which backend the game connects to — chosen IN-GAME from the Play Online server picker (no rebuild).
    /// The server itself still runs separately (a WebGL game can't launch the Java backend); this just selects
    /// WHICH running server to dial. Persisted in PlayerPrefs so the choice sticks between sessions.
    public static class ServerConfig
    {
        public struct Preset { public string label; public string host; public int port; public bool offline; }

        // Built-in choices shown in the picker. "Offline" = the in-memory stub (no server needed — solo/practice).
        public static readonly Preset[] Presets =
        {
            new Preset { label = "Localhost (8090)", host = "localhost", port = 8090, offline = false },
            new Preset { label = "LAN / Custom",     host = "",          port = 8090, offline = false }, // host filled by the field
            new Preset { label = "Offline (no server)", host = "",       port = 0,    offline = true  },
        };

        const string KeyHost = "net.host";
        const string KeyPort = "net.port";
        const string KeyOffline = "net.offline";   // shared with the old fallback key

        public static string Host
        {
            get => PlayerPrefs.GetString(KeyHost, "localhost");
            set { PlayerPrefs.SetString(KeyHost, value); PlayerPrefs.Save(); }
        }
        public static int Port
        {
            get => PlayerPrefs.GetInt(KeyPort, 8090);
            set { PlayerPrefs.SetInt(KeyPort, value); PlayerPrefs.Save(); }
        }
        public static bool Offline
        {
            get => PlayerPrefs.GetInt(KeyOffline, 0) == 1;
            set { PlayerPrefs.SetInt(KeyOffline, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        // WEBGL same-origin: when the game is a WebGL build SERVED BY the backend (http://host:8090/), the REST +
        // WebSocket live on the SAME origin as the page. We derive host/scheme/port from the page URL so players
        // don't type an IP, and we MATCH the page scheme (https page → wss, http page → ws) to avoid the browser's
        // mixed-content block. The in-game server field is then unnecessary (and ignored) on WebGL.
        public static bool SameOriginWebGL =>
            Application.platform == RuntimePlatform.WebGLPlayer && !Offline && TryParseOrigin(out _, out _, out _);

        static bool TryParseOrigin(out string scheme, out string host, out int port)
        {
            scheme = "http"; host = "localhost"; port = 8090;
            string url = Application.absoluteURL;   // e.g. http://192.168.0.42:8090/index.html
            if (string.IsNullOrEmpty(url)) return false;
            try
            {
                var u = new System.Uri(url);
                scheme = u.Scheme;                  // http or https
                host = u.Host;
                port = u.Port > 0 ? u.Port : (scheme == "https" ? 443 : 80);
                return true;
            }
            catch { return false; }
        }

        /// The full WebSocket URL the client connects to (game session endpoint).
        public static string WsUrl
        {
            get
            {
                if (SameOriginWebGL)
                {
                    TryParseOrigin(out string s, out string oh, out int op);
                    string wsScheme = s == "https" ? "wss" : "ws";
                    return $"{wsScheme}://{oh}:{op}/ws/session";   // SAME origin + matching scheme (no mixed-content)
                }
                string h = string.IsNullOrWhiteSpace(Host) ? "localhost" : Host.Trim();
                // allow the user to paste a full ws://… or wss://… URL into the host field
                if (h.StartsWith("ws://") || h.StartsWith("wss://")) return h.Contains("/ws/") ? h : h.TrimEnd('/') + "/ws/session";
                return $"ws://{h}:{Port}/ws/session";
            }
        }
        /// Matching http base (for a quick reachability ping / REST).
        public static string HttpBase
        {
            get
            {
                if (SameOriginWebGL)
                {
                    TryParseOrigin(out string s, out string oh, out int op);
                    return $"{s}://{oh}:{op}";       // same origin as the page → no CORS, no IP to type
                }
                string h = string.IsNullOrWhiteSpace(Host) ? "localhost" : Host.Trim();
                if (h.StartsWith("ws://"))  return "http://"  + h.Substring(5).Split('/')[0];
                if (h.StartsWith("wss://")) return "https://" + h.Substring(6).Split('/')[0];
                return $"http://{h}:{Port}";
            }
        }

        public static void Apply(Preset p)
        {
            Offline = p.offline;
            if (!p.offline)
            {
                if (!string.IsNullOrEmpty(p.host)) Host = p.host;
                if (p.port > 0) Port = p.port;
            }
        }
    }
}
