// Dashboard config — edit this if your backend runs on a different host/port.
window.HUB_CONFIG = {
  // The Spring Boot backend base URL. The dashboard reads /api/admin/* from here.
  // NOTE: 8090 because EnterpriseDB/pgAdmin's bundled Apache holds 8080 on this machine.
  BACKEND_HTTP: "http://localhost:8090",
  // The game's WebSocket endpoint (shown for reference / copy).
  BACKEND_WS:   "ws://localhost:8090/ws/session",
  // How often (ms) to poll the admin endpoints when LIVE.
  POLL_MS: 2000,
};
