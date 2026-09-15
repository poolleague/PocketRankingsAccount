# Current Release Handoff

Branch: `codex/player-data-privacy`  
Development version: 0.3.0  
Deployment: none; no DNS, Sandbox, Production, tag, or GitHub Release.

Implemented: authenticated Account home, password-confirmed irreversible player-data opt-out, durable preference/request/three product targets, pending outbox messages, redacted audit entry, processing-status UI, and focused automated tests.

Not implemented: signed outbox dispatch, product acknowledgement receiver, automatic completion, retry worker, operational alerting, and League consumer. Until those exist, the UI must remain in processing and no erasure may be described as complete.

Verified 2026-09-15: `dotnet test PocketRankingsAccount.slnx -c Release` passed 5/5.
