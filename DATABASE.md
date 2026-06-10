# Database — Bame Plastic

Simple explanation of the database, what's in it, and why.

---

## What it is

A **PostgreSQL** database called **`bame_plastic_db`**. It stores everything the game needs to remember:
player accounts, game results, the leaderboard, and room/match history. The full design lives in one file:
**`Backend/backend/db/schema.sql`** (run once to create all the tables).

Think of the database as the game's **long-term memory**. The live game and the lobby run in the server's
RAM (fast, temporary); the database is where things are saved permanently.

---

## How it's set up (one time)

1. Make a database named `bame_plastic_db` in pgAdmin.
2. Open `Backend/backend/db/schema.sql` in pgAdmin's Query Tool and run it.
3. That creates **33 tables**. Done.

The backend then connects to it using the username/password in `application.properties`.

---

## The tables, grouped by purpose

We built more tables than we use today, **on purpose** — so the database is ready for future features
(login, friends, shops) without redesigning it later. Empty tables are normal.

### 1. Accounts & login (security)
| Table | Holds |
|---|---|
| `users` | Real accounts — username, email, **password as a hash** (never the real password), status |
| `roles`, `user_roles` | Who is a normal player vs admin vs moderator |
| `permissions`, `role_permissions` | Fine-grained "who can do what" (for later) |
| `oauth_accounts` | "Sign in with Google/Discord" links (for later) |
| `auth_tokens` | Login sessions — stored as a hash so a leak can't be reused |
| `verification_tokens` | Email-verify / password-reset codes (one-time, expiring) |
| `audit_log` | Security history: logins, failed attempts, admin actions |

> **Security note:** passwords are stored only as a scrambled **hash**, and login tokens are also stored as
> hashes. Even if someone stole the database, they couldn't read passwords or reuse logins.

### 2. The game itself (what the running app uses now)
| Table | Holds |
|---|---|
| `players` | The original simple player record (the teammate's first version) |
| `buses` | A bus's state — lane, speed, capacity, condition |
| `routes` | Bus routes — start, end, stops, fare |
| `game_sessions` | A play session — its code, status (lobby/active/finished), money |
| `session_players` | Which players are in which session |
| `passengers` | Passengers — type, paid or not, seat |
| `game_events` | In-game events (checkpoints, traffic, etc.) |
| `shift_results` | **A finished shift's score — this powers the leaderboard** |

### 3. Multiplayer rooms (history)
| Table | Holds |
|---|---|
| `rooms` | A multiplayer room — code, host, seed, status |
| `room_members` | Who sat in which role (driver / conductor) |
| `match_results`, `match_participants` | The outcome of a finished match + each player's contribution |

### 4. Progress, money & social (future features)
`player_stats` (lifetime stats, rating, level), `wallets` + `wallet_transactions` (in-game money + a record
of every change), `items` + `user_items` (bus skins, horns), `achievements` + `user_achievements`,
`friendships`, `leaderboard_entries` (saved leaderboards for weekly/seasonal boards).

### 5. Monitoring (for the dashboard & balancing)
`analytics_events` (a flexible event log for game data), `error_reports` (crash reports), `server_config`
(settings you can change without restarting the server).

---

## Two ID styles (and why)

- **Game tables** (players, buses…) use simple counting numbers (1, 2, 3…). Fast and small.
- **Account / room tables** use long random IDs (UUIDs). These are **safe to show in a URL** because nobody
  can guess the next one — you can't go to `user 5` and snoop on someone.

---

## Key points to explain
- One file (`schema.sql`) defines the whole database — easy to recreate anywhere.
- Designed for the **future** (login, friends, shops) so we never have to rebuild it.
- **Security first**: passwords and tokens are hashes, IDs are non-guessable where it matters.
- The database = permanent memory; the live game runs in the server's RAM and only **saves results** here.

Setup + commands: see `Backend/backend/db/README.md`.
