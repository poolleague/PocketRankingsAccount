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
cp .env.example .env    # then edit the values, especially ACCOUNT_SECURITY_FOUNDATION_HASH_KEY
docker compose up --build
curl http://localhost:8081/health
```

Visit `http://localhost:8081/Account/Signup` to create an account, or
`/Account/Login` to sign in. Without `ACCOUNT_SECURITY_FOUNDATION_HASH_KEY`
set, both will throw.

## Status

Authenticated people can open `/Account/Privacy` from the Account home page to request irreversible player-data erasure. The Account and purchases remain active; durable, idempotent product targets are queued for League, Tournament, and Player Profile. A two-minute RS256, purpose/audience/installation-bound directive signer now defines the machine contract, but outbound network delivery remains disabled until exact non-League destinations are approved. Requests therefore remain visibly processing. See `docs/PLAYER_DATA_PRIVACY.md`.

The Account database also defines the provider-neutral League/Tournament installation registry and append-only lifecycle history. The tested state machine separates purchase, entitlement, queued provisioning, infrastructure, migration, validation, readiness, immediate cancellation lockout, 61-day retention, legal hold, and evidenced deletion. It does not call a cloud provider or delete infrastructure yet.

Login, signup, and sessions are implemented and wired to real cookie
authentication -- this is a working auth loop, not a stub. Identity token
issuance (JWT, RS256) is implemented with a published verification key at
`/.well-known/jwks.json`. Tournament and Player Profile now contain separate,
disabled-by-default verification receivers for the privacy directive contract.

Deliberately narrower than PoolLeagueWeb's SecurityFoundationService: no
IP-based abuse rate-limiting, no password recovery (needs email sending,
which doesn't exist), no single-active-session enforcement (a deliberate
divergence, not an omission -- see AGENTS.md). See AGENTS.md for the full
list of what's still open.

Not yet wired: Caddy routing / public DNS. This container is reachable
today only via the local port mapping in `docker-compose.yml`, not
publicly. That remains an explicit open decision — see
`PLATFORM_AGENTS.md` Section 1.
