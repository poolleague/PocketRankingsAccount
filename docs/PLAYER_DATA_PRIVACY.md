# Player Data Privacy

Version 0.3.0 adds an authenticated Account page for permanent player-data erasure. It keeps the Account, purchases, entitlements, and necessary security/transaction evidence active.

The request requires the current password, CSRF protection, and an explicit irreversible confirmation. Account stores no statistics; it creates a durable request, one target each for League, Tournament, and Player Profile, and an idempotent pending outbox message in the same transaction.

The UI reports processing until every product acknowledges completion. Live signed delivery is not activated in this repository phase because the durable Account signing/rollover contract and League consumer remain separately gated. No partial request may be presented as complete.

Database objects introduced in 0.3.0:

- `data.player_data_preferences`
- `data.player_data_requests`
- `data.player_data_request_targets`
- `data.privacy_outbox`
- `ux_active_player_data_request`
- `fk_player_data_preferences_request`

The destructive product actions have no data rollback. Code rollback must preserve queued directives and the opted-out preference. Restored backups must replay completed deletion directives before serving traffic.
