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

Scaffolding only. No controllers/services/models implemented yet — real
implementation starts next session, per owner approval, per
PLATFORM_AGENTS.md Section 8 (Approval Boundaries).
