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

Model layer, hosting pipeline, and PostgreSQL persistence
(`Services/AccountRepository.cs`) are implemented. Schema:

- `idn.people`, `idn.entitlements`, `idn.issued_identity_tokens`
- `secu.person_credentials`, `secu.person_sessions`
- `data.audit_log`

`AccountRepository` is registered as a DI singleton and eagerly resolved
in `Program.cs` at startup specifically so schema creation actually runs on
boot (a lazy singleton would otherwise never construct, since nothing
calls it from a real auth flow yet).

**Caller contract, not yet enforced by any service (none exists yet):**
`Person.Id`, `PersonCredential.Id`, `PersonSession.Id`, and
`Entitlement.Id` are plain integer primary keys, not auto-generated —
matching `PoolLeagueWeb.secu.login_accounts`. Whoever creates a new row
must assign a unique, non-zero `Id` before adding it to `AccountState` and
calling `Save()`. `IssuedIdentityToken.TokenId` (Guid) has no such gap —
`Guid.NewGuid()` before insert is enough.

Not yet implemented, and each is its own runtime-code phase requiring
proposal + owner approval per PLATFORM_AGENTS.md Section 8:

- Cookie authentication / session validation — the service that calls
  `AccountRepository` to check real credentials/sessions.
- Identity token issuance/signing.
- Caddy routing / public DNS for `account.pocketrankings.com` — touches
  PoolLeagueWeb's own repo (Caddy currently lives there); see
  PLATFORM_AGENTS.md Section 1.
- No JSON fallback (unlike `LeagueRepository`'s `Storage:Provider` toggle)
  — `ConnectionStrings:PostgresDatabase` is required; the constructor
  throws without it. Add a fallback later only as its own explicit
  decision.

Not build-verified: this environment has no .NET SDK available to run
`dotnet build`. Patterns were hand-matched line-by-line against
`PoolLeagueWeb/Services/LeagueRepository.cs`, which does compile, and one
real bug was already caught and fixed this way (an invalid
`GENERATED AS IDENTITY` + explicit-NULL pattern on four tables). A real
build/run check — ideally actually exercising `docker compose up` against
a live Postgres — is still owed before this is fully trusted.
