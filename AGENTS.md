# AGENTS.md — PocketRankingsAccount

This repo inherits every rule in `PLATFORM_AGENTS.md`
(`PocketRankingsPlatform` repo). This file covers only what's specific to
Account.

## Scope of this product

- Human identity (`PersonId`), authentication, and the entitlements table
  described in PLATFORM_AGENTS.md Section 2.
- Issuing and signing cross-product identity tokens.
- The current-stage entitlement policy (PLATFORM_AGENTS.md Section 3) —
  beta-included access today, real checkout later behind the
  `IPaymentProvider` abstraction (Section 4).

## Explicitly out of scope here

- League, Tournament, or Player Profile domain data. Account never stores
  match results, rosters, or bracket state — only identity and
  entitlement.

## Status

Model layer (`src/PocketRankingsAccount/Models/AccountModels.cs`) and a
running hosting pipeline (`Program.cs`, placeholder `HomeController`,
`/health`) are implemented. Account now runs as its own isolated
`docker-compose.yml` project — see README.md for local run instructions.

Not yet implemented, and each is its own runtime-code phase requiring
proposal + owner approval per PLATFORM_AGENTS.md Section 8:

- Cookie authentication / session validation (needs `AccountRepository`
  first — do not wire a cookie scheme with nothing real behind it).
- `AccountRepository` — real PostgreSQL access for the model layer.
- Identity token issuance/signing.
- Caddy routing / public DNS for `account.pocketrankings.com` — this
  touches PoolLeagueWeb's own repo (Caddy currently lives there) and needs
  its own explicit decision; see PLATFORM_AGENTS.md Section 1.

Not build-verified: this environment has no .NET SDK available to run
`dotnet build`. Field and pipeline patterns were hand-matched against
`PoolLeagueWeb`, which does compile, but a real build/run check is still
owed before this is fully trusted.
