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

Model layer implemented (`src/PocketRankingsAccount/Models/AccountModels.cs`):
`Person`, `PersonCredential`, `PersonSession`, `Entitlement`,
`IssuedIdentityToken`, `AccountAuditLogEntry`, plus the `ProductTypes`,
`EntitlementSources`, and `BetaAccessPolicy` constant classes. Field names
on `Person` and `Entitlement` match PLATFORM_AGENTS.md Section 2 exactly —
keep them in sync if either changes.

Not yet implemented: `Program.cs`/hosting pipeline, controllers, services,
actual PostgreSQL access (`AccountRepository`-equivalent), token
signing/verification. All of these are next-session work and each is its
own runtime-code phase requiring proposal + owner approval per
PLATFORM_AGENTS.md Section 8 — do not add controllers or wire up hosting
without that step.

Not build-verified: this environment has no .NET SDK available to run
`dotnet build`. Field patterns were hand-matched against
`PoolLeagueWeb/Models/LeagueModels.cs`, which does compile, but a real
build check is still owed before this is fully trusted.
