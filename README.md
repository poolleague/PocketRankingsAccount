# Pocket Rankings — Account

Identity and entitlements service for the Pocket Rankings platform.

Owns:
- One global `PersonId` (GUID) per human, and login/auth for that person.
- Issuance of short-lived signed identity tokens consumed by other
  products (League, Tournament, Player Profile, Web).
- The entitlements table: which `PersonId` has access to which
  product/tier, and where that access came from. Full contract in
  `PocketRankingsPlatform/PLATFORM_AGENTS.md`, Section 2.

## Status

Scaffolding only — no runtime code yet. See `AGENTS.md` for repo-specific
rules; platform-wide rules live in `PocketRankingsPlatform`.
