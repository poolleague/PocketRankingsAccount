# Testing

Run `dotnet test PocketRankingsAccount.slnx -c Release`.

Version 0.3.0 has five automated tests covering current-password rejection, required irreversible confirmation, accepted request creation, repeated-request idempotency, and required controller/schema guards. PostgreSQL integration, real Compose signup/login/privacy walkthrough, delivery signing, acknowledgement handling, accessibility, responsive browser checks, and load/security gates remain required before launch.
