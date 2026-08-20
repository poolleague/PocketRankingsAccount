# Pocket Rankings — Account

Identity and entitlements service for the Pocket Rankings platform.

Owns:
- One global `PersonId` (GUID) per human, and login/auth for that person.
- Issuance of short-lived signed identity tokens consumed by other
  products (League, Tournament, Player Profile, Web).
- The entitlements table: which `PersonId` has access to which
  product/tier, and where that access came from. Full contract in
  `PocketRankingsPlatform/PLATFORM_AGENTS.md`, Section 2.

## Structure

Matches `PoolLeagueWeb`'s layout: the actual project lives under
`src/PocketRankingsAccount/`, with `docs/` and `tests/` at repo root.

## Running locally

```
cp .env.example .env    # then edit the values
docker compose up --build
curl http://localhost:8081/health
```

## Status

Hosting pipeline running, with real PostgreSQL persistence
(`AccountRepository`) behind it. `docker-compose.yml` runs Account as an
isolated Compose project with its own PostgreSQL instance and its own
Docker network, separate from `PoolLeagueWeb`'s, per `PLATFORM_AGENTS.md`
Section 1. Startup creates the schema (`idn`, `secu`, `data`) and tables
automatically, matching `PoolLeagueWeb`'s documented behavior.

Not yet implemented: cookie authentication, identity token issuance/signing
service. `AccountRepository` exists and is registered in DI, but nothing
calls it from a real auth flow yet -- see `AGENTS.md`.

Not yet wired: Caddy routing / public DNS. This container is reachable
today only via the local port mapping in `docker-compose.yml`, not
publicly. That remains an explicit open decision — see
`PLATFORM_AGENTS.md` Section 1.
