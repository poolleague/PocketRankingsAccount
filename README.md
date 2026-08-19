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

## Status

Model layer implemented — `Person`, `PersonCredential`, `PersonSession`,
`Entitlement`, `IssuedIdentityToken`, `AccountAuditLogEntry` in
`src/PocketRankingsAccount/Models/AccountModels.cs`, matching
`PLATFORM_AGENTS.md` Section 2 field-for-field.

Not yet implemented: `Program.cs`/hosting, controllers, services, actual
database access. These are next-session work, per owner approval, per
`PLATFORM_AGENTS.md` Section 8. See `AGENTS.md` for repo-specific rules;
platform-wide rules live in `PocketRankingsPlatform`.
