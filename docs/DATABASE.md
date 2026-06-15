# Database — Bame Plastic

A **PostgreSQL** database called **`bame_plastic_db`**. It's deliberately lean: **4 tables**, each one actually
used by the game. The full design lives in **`Backend/backend/db/schema.sql`** (and `populate.sql` for demo data).

Think of the database as the game's **long-term memory**: accounts, the leaderboard, achievements, and payment
records. The live game + lobby run in the server's RAM (fast, temporary); only these durable things are saved.

---

## Setup (one time)

1. Make a database named `bame_plastic_db`.
2. Run `Backend/backend/db/schema.sql` (creates the 4 tables).
3. Optional: run `populate.sql` to seed 3 demo accounts + played matches.

The backend connects using the credentials in `application.properties`. (Or, for a zero-Postgres demo, the
backend's H2 profile creates the same tables in a file — see `DEMO_SETUP.md`.)

---

## The tables

| Table | Holds | Used by |
|---|---|---|
| `players` | The account + economy record: username, email, **BCrypt password hash**, role, **Bhara** wallet, career stats (`total_earnings`, `fares_collected`, `shifts_played`, `best_shift`), owned `unlocks`, equipped bus/conductor/upgrades, daily-bonus date. | login/signup, shop, loadout, achievements |
| `shift_results` | One finished shift's score (player, earnings, bus health, duration, room). | the global leaderboard |
| `player_achievements` | Which achievements a player has unlocked (one row per (player, code)). | the Achievements screen + shift-end unlock check |
| `payment_transactions` | SSLCommerz Bhara-pack purchases — pending → completed/failed, with the Bhara awarded. | the Get-Bhara store |

That's the whole database. Earlier versions carried ~30 extra "future" tables (users/roles/rooms/wallets/
items/friends/telemetry…) and 5 single-player scaffolding tables (buses/routes/sessions/passengers/events) that
nothing ever wrote — all removed so the schema reflects exactly what the game does.

---

## Security
- **Passwords** are stored only as a **BCrypt hash** (the app hashes on signup; never plaintext).
- **Payments** are server-validated (price comes from `StoreCatalog`, not the client) and Bhara is credited only
  on a confirmed SSLCommerz callback.

## Key points
- One file (`schema.sql`) defines the whole database — easy to recreate anywhere; `validate`-clean against the
  JPA entities.
- `populate.sql` makes the database look like a few matches have been played (accounts, leaderboard,
  achievements, payments) for a demo.

Setup + demo accounts: see `Backend/backend/db/README.md`.
