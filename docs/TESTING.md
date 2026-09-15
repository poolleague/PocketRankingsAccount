# Testing

Run `dotnet test PocketRankingsAccount.slnx -c Release`.

Version 0.4.0 has eleven automated tests. In addition to the five privacy request tests, they verify signed-directive signature/audience/installation/lifetime and sequential provisioning, immediate cancellation lockout with an exact 61-day due date, legal-hold pause, premature-deletion refusal, and deletion completion. PostgreSQL integration, real Compose signup/login/privacy walkthrough, live delivery/acknowledgement/retry handling, accessibility, responsive browser checks, restore exercises, and load/security gates remain required before launch.
