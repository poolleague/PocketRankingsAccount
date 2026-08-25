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

The auth loop is implemented and real, not stubbed: signup, login, cookie
sessions (with per-request database re-validation via `OnValidatePrincipal`,
matching PoolLeagueWeb's pattern), logout, lockout after repeated failed
attempts, and identity token issuance (JWT/RS256, published at
`/.well-known/jwks.json`).

Key files: `Services/PasswordService.cs` (near-verbatim from
PoolLeagueWeb), `Services/AccountSecurityFoundationService.cs` (login/
signup/session logic + token issuance -- the token issuance half has no
League precedent; every design choice there is commented as this file's
own), `Controllers/AccountController.cs` + `Views/Account/*.cshtml`
(minimal, unstyled, functional -- no UI/UX design has happened yet).

**Deliberate divergences from PoolLeagueWeb's SecurityFoundationService,**
each reasoned through rather than accidental:

- **No single-active-session enforcement.** League forces one session at a
  time, appropriate for its shared-terminal context. Account is a general
  identity provider; multi-device login (phone + laptop) is normal and
  expected here, so a person may hold several valid concurrent sessions.
- **No IP-based abuse rate-limiting** (League's `AbuseCounters` /
  `RegisterAbuseAttempt`). Real hardening, reasonable to layer in later;
  the simpler per-credential lockout (`RecordFailedLogin`,
  `ResetLoginFailures`) is what's implemented now.
- **No password recovery / one-time tokens.** Needs email-sending
  capability, which does not exist for Account at all yet -- a
  prerequisite, not just unbuilt UI.
- **Fixed, not-yet-configurable constants**: lockout threshold (5) and
  duration (15 min, matching League's `LoginBlock`), session inactivity
  window (24h). League reads equivalents from a settings table
  (`cnfg.league_settings`); Account has no settings table yet, so these
  are hardcoded with a comment, per PLATFORM_AGENTS.md Section 8's
  "reversible, cost-free defaults" guidance.
- **No entitlement-granting on signup.** Whether/how a new Account signup
  should receive League/Tournament/Player Profile entitlements (tied to a
  linked PoolLeagueWeb account? granted directly?) has not been designed.
  Signup here creates identity only.

**Identity token issuance is genuinely new** (Section 2.1's "short-lived
signed token" from PLATFORM_AGENTS.md, made concrete): RS256, 5-minute
lifetime (a one-time handoff proof for a redirect, not a bearer token used
on every request -- reconsider if that assumption is wrong), manually
constructed (no JWT library dependency added -- a few dozen lines built
directly on `System.Security.Cryptography`, readable start to finish
rather than a library call). Signing key: a configured PEM if provided,
otherwise an ephemeral in-memory RSA-2048 key regenerated every restart,
with a loud startup warning in the latter case. No other product verifies
a token yet.

**Two required secrets**, documented in `.env.example` with generation
commands: `ACCOUNT_SECURITY_FOUNDATION_HASH_KEY` (required -- login/
signup/sessions throw without it) and `ACCOUNT_TOKEN_SIGNING_PRIVATE_KEY_PEM`
(optional -- falls back to the ephemeral key above).

Still not implemented, each its own runtime-code phase requiring proposal
+ owner approval per PLATFORM_AGENTS.md Section 8:

- Caddy routing / public DNS for `account.pocketrankings.com` — touches
  PoolLeagueWeb's own repo; see PLATFORM_AGENTS.md Section 1.
- Any other product actually verifying an identity token.
- Entitlement-granting flow (see above).
- No JSON fallback (unchanged from before) — `ConnectionStrings:PostgresDatabase`
  is required.

Not build-verified: this environment has no .NET SDK available to run
`dotnet build`. This is the largest, most security-sensitive change yet --
password hashing and the keyed-hash/session pattern were matched
line-by-line against `PoolLeagueWeb`'s working code; identity token
issuance has no such precedent to check against. A real
`docker compose up` + manual signup/login walkthrough, ideally plus a real
`dotnet build`, is owed before this is trusted with real credentials.
