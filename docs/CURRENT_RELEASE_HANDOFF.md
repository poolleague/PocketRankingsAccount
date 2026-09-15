# Current Release Handoff

Branch: `codex/player-data-privacy`  
Development version: 0.4.0
Deployment: none; no DNS, Sandbox, Production, tag, or GitHub Release.

Implemented: authenticated Account home, password-confirmed irreversible player-data opt-out, durable preference/request/three product targets, pending outbox messages, redacted audit entry, processing-status UI, a short-lived signed privacy-directive contract, an isolated-product installation/lifecycle schema, and a tested provider-neutral lifecycle state machine.

Not implemented: live signed outbox dispatch, acknowledgement persistence in Account, automatic completion, retry/dead-letter worker, operational alerting, cloud/DNS/payment-provider execution, and League consumer. Exact Tournament and Player Profile network destinations have not been approved. Until all in-scope targets acknowledge, the UI must remain processing and no erasure may be described as complete.

Verified 2026-09-15: Release build/tests passed 11/11, the full solution advisory scan reported no vulnerable packages, and Compose configuration resolved. PostgreSQL migration execution, browser/accessibility, recovery, and load evidence still must be completed for an exact deployment candidate.
