using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace BamePlastic.Net
{
    /// Tiny fire-and-forget client for the leaderboard endpoint (ShiftController POST /api/shift/result).
    /// Called once at shift end; failures are silently ignored (a missing backend must never block the summary).
    public static class LeaderboardApi
    {
        [System.Serializable] class Result {
            public string playerName; public int earnings; public int busHealth; public int durationSec; public string roomCode;
        }

        // ---- career leaderboard (global, per-player totals) ----
        [System.Serializable] public struct CareerRow { public int rank; public string username; public long totalEarnings; public int bhara; }
        [System.Serializable] class CareerWrap { public CareerRow[] rows; }

        /// Fetch the global career leaderboard (top players by lifetime taka). onOk gets the parsed rows
        /// (empty array on any error so the GUI can show "no data" rather than break).
        public static void FetchCareer(System.Action<CareerRow[]> onOk)
        {
            if (!Application.isPlaying) { onOk?.Invoke(System.Array.Empty<CareerRow>()); return; }
            if (ServerConfig.Offline) { onOk?.Invoke(System.Array.Empty<CareerRow>()); return; }
            Runner.Run(GetCareer(ServerConfig.HttpBase + "/api/auth/leaderboard/career", onOk));
        }

        static IEnumerator GetCareer(string url, System.Action<CareerRow[]> onOk)
        {
            using var req = UnityWebRequest.Get(url);
            req.timeout = 10;
            yield return req.SendWebRequest();
            CareerRow[] rows = System.Array.Empty<CareerRow>();
            if (req.result == UnityWebRequest.Result.Success)
            {
                // JsonUtility can't parse a top-level array → wrap it as {"rows":[...]}
                string body = req.downloadHandler.text;
                if (!string.IsNullOrEmpty(body) && body.TrimStart().StartsWith("["))
                {
                    try { rows = JsonUtility.FromJson<CareerWrap>("{\"rows\":" + body + "}").rows ?? rows; }
                    catch { rows = System.Array.Empty<CareerRow>(); }
                }
            }
            onOk?.Invoke(rows);
        }

        public static void PostResult(string playerName, int earnings, int busHealth, int durationSec, string roomCode)
        {
            if (!Application.isPlaying) return;
            if (ServerConfig.Offline) return;   // no server selected → nothing to post to
            var body = new Result {
                playerName = string.IsNullOrEmpty(playerName) ? "Anonymous" : playerName,
                earnings = Mathf.Max(0, earnings), busHealth = busHealth,
                durationSec = Mathf.Max(0, durationSec), roomCode = roomCode ?? ""
            };
            Runner.Run(Post(ServerConfig.HttpBase + "/api/shift/result", JsonUtility.ToJson(body)));
        }

        static IEnumerator Post(string url, string json)
        {
            using var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = 8;
            yield return req.SendWebRequest();
            // ignore result — best effort
        }

        class Runner : MonoBehaviour
        {
            static Runner _inst;
            public static void Run(IEnumerator co)
            {
                if (_inst == null)
                {
                    var go = new GameObject("LeaderboardApiRunner");
                    Object.DontDestroyOnLoad(go);
                    _inst = go.AddComponent<Runner>();
                }
                _inst.StartCoroutine(co);
            }
        }
    }
}
