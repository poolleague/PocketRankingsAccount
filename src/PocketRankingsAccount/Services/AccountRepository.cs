using System.Data;
using Npgsql;
using PocketRankingsAccount.Models;

namespace PocketRankingsAccount.Services;

// PostgreSQL persistence for Account's identity/entitlement models, matching
// the shape and conventions of PoolLeagueWeb/Services/LeagueRepository.cs:
// schema-per-domain, idempotent CREATE SCHEMA/TABLE IF NOT EXISTS DDL run at
// startup, parameterized queries via shared Read*/Add helpers, and a
// whole-state Get()/Save() cycle.
//
// Deliberately different from LeagueRepository in one way: there is no JSON
// fallback here. LeagueRepository supports a Storage:Provider toggle so it
// can run without PostgreSQL for local dev; Account does not (yet) --
// ConnectionStrings:PostgresDatabase is required, and the constructor
// throws if it is missing. Adding a JSON fallback later, if wanted, is a
// separate, explicitly-flagged decision, not silently included here.
public class AccountRepository
{
    private readonly string _postgresConnectionString;

    public AccountRepository(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("PostgresDatabase");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:PostgresDatabase is required. AccountRepository has no JSON fallback.");
        }

        _postgresConnectionString = connectionString;
        EnsurePostgresStore();
    }

    // Schema-per-domain, matching PLATFORM_AGENTS.md Section 6 (coding
    // consistency) and reusing PoolLeagueWeb's own schema names where the
    // domain concept is genuinely the same:
    //   idn  -- identity/entitlement core: people, entitlements, issued
    //           identity tokens. New here -- League has no equivalent,
    //           since generic cross-product identity is what Account exists
    //           to own.
    //   secu -- security/credentials: person_credentials, person_sessions.
    //           Reuses League's exact "secu" name; same domain concept.
    //   data -- durable data: audit_log. Reuses League's exact "data" name
    //           and exact table name (data.audit_log).
    private void EnsurePostgresStore()
    {
        using var connection = new NpgsqlConnection(_postgresConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE SCHEMA IF NOT EXISTS idn;
            CREATE SCHEMA IF NOT EXISTS secu;
            CREATE SCHEMA IF NOT EXISTS data;

            -- Id-allocation sequences for the app-assigned-PK tables below.
            -- These are standalone sequences, NOT ""GENERATED AS IDENTITY""
            -- on the column itself -- that was tried in an earlier version
            -- of this file and was a real bug (see AGENTS.md/commit
            -- history): an explicit NULL does not trigger identity
            -- generation, only an omitted column or the DEFAULT keyword
            -- does, and the whole-list Save() upsert path always passes an
            -- explicit id. A standalone sequence gives atomic, race-free id
            -- allocation for the new single-row Create* methods below
            -- without touching that already-working upsert path at all.
            CREATE SEQUENCE IF NOT EXISTS idn.people_id_seq;
            CREATE SEQUENCE IF NOT EXISTS secu.person_credentials_id_seq;
            CREATE SEQUENCE IF NOT EXISTS secu.person_sessions_id_seq;

            -- Plain integer PK, not GENERATED AS IDENTITY -- matching
            -- League's secu.login_accounts. Callers must assign a unique,
            -- non-zero Id before adding a new Person to AccountState and
            -- calling Save(); Repository does not allocate ids itself.
            CREATE TABLE IF NOT EXISTS idn.people
            (
                id integer PRIMARY KEY,
                person_id uuid NOT NULL UNIQUE,
                display_name text NOT NULL,
                is_active boolean NOT NULL DEFAULT true,
                created_at timestamp with time zone NOT NULL DEFAULT NOW()
            );

            -- Same app-assigned-id contract as idn.people above.
            CREATE TABLE IF NOT EXISTS secu.person_credentials
            (
                id integer PRIMARY KEY,
                person_record_id integer NOT NULL REFERENCES idn.people(id) ON DELETE CASCADE,
                user_name text NOT NULL UNIQUE,
                password_hash text NOT NULL,
                must_change_password boolean NOT NULL DEFAULT false,
                failed_login_count integer NOT NULL DEFAULT 0,
                lockout_until timestamp with time zone NULL,
                last_password_changed_at timestamp with time zone NULL,
                security_generation bigint NOT NULL DEFAULT 1 CHECK (security_generation >= 1)
            );

            -- session_token holds a keyed hash, never the raw cookie value,
            -- matching the principle behind League's
            -- secu.account_sessions.session_hash (see POSTGRES_SCHEMA_LAYOUT.md).
            -- Unlike League, this table keeps a surrogate integer id rather
            -- than making the hash itself the primary key; the CHECK below
            -- still enforces the same ""always a 64-char hash, never a raw
            -- token"" property. Worth aligning to hash-as-PK in a future
            -- models pass if byte-for-byte parity with League matters more
            -- than the current model shape.
            -- Same app-assigned-id contract as idn.people above.
            -- security_generation is bound to
            -- secu.person_credentials.security_generation at creation time;
            -- a session is only valid while the two still match, matching
            -- how PoolLeagueWeb ties account_sessions.security_generation
            -- to login_accounts.security_generation. This does NOT enforce
            -- single-active-session -- see the comment on the
            -- PersonSession model for why Account deliberately differs
            -- from League there.
            CREATE TABLE IF NOT EXISTS secu.person_sessions
            (
                id integer PRIMARY KEY,
                person_record_id integer NOT NULL REFERENCES idn.people(id) ON DELETE CASCADE,
                session_token text NOT NULL UNIQUE CHECK (length(session_token) = 64),
                created_at timestamp with time zone NOT NULL DEFAULT NOW(),
                last_activity_at timestamp with time zone NOT NULL DEFAULT NOW(),
                revoked_at timestamp with time zone NULL,
                security_generation bigint NOT NULL DEFAULT 1 CHECK (security_generation >= 1),
                CHECK (revoked_at IS NULL OR revoked_at >= created_at)
            );

            -- UNIQUE (person_id, product_type): one current entitlement row
            -- per person per product. This constraint is not explicitly
            -- stated in PLATFORM_AGENTS.md Section 2.2 -- it is a repository
            -- design decision inferred from ""does this PersonId have a
            -- current, unexpired entitlement row for this product"" reading
            -- most naturally as one row, updated in place, rather than a
            -- growing history. Flag it if that reading is wrong.
            -- Same app-assigned-id contract as idn.people above.
            CREATE TABLE IF NOT EXISTS idn.entitlements
            (
                id integer PRIMARY KEY,
                person_id uuid NOT NULL REFERENCES idn.people(person_id) ON DELETE CASCADE,
                product_type text NOT NULL CHECK (product_type IN ('league', 'tournament', 'player_profile', 'web')),
                tier text NOT NULL DEFAULT 'standard',
                source text NOT NULL CHECK (source IN ('beta_included', 'purchase', 'manual_grant')),
                granted_at timestamp with time zone NOT NULL DEFAULT NOW(),
                expires_at timestamp with time zone NULL,
                UNIQUE (person_id, product_type)
            );

            CREATE TABLE IF NOT EXISTS idn.issued_identity_tokens
            (
                token_id uuid PRIMARY KEY,
                person_id uuid NOT NULL REFERENCES idn.people(person_id) ON DELETE CASCADE,
                issued_at timestamp with time zone NOT NULL DEFAULT NOW(),
                expires_at timestamp with time zone NOT NULL,
                revoked_at timestamp with time zone NULL,
                issued_to_product_type text NOT NULL DEFAULT '',
                CHECK (expires_at > issued_at)
            );

            CREATE TABLE IF NOT EXISTS data.audit_log
            (
                id integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                created_at timestamp with time zone NOT NULL DEFAULT NOW(),
                actor_person_id uuid NULL,
                actor_role text NOT NULL DEFAULT '',
                area text NOT NULL DEFAULT '',
                action text NOT NULL DEFAULT '',
                detail text NOT NULL DEFAULT '',
                target_type text NOT NULL DEFAULT '',
                target_id text NOT NULL DEFAULT '',
                before_json jsonb NULL,
                after_json jsonb NULL,
                reason text NOT NULL DEFAULT '',
                request_id text NOT NULL DEFAULT '',
                source text NOT NULL DEFAULT ''
            );";
        command.ExecuteNonQuery();
    }

    private static void SetSchemaSearchPath(NpgsqlConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SET search_path TO idn, secu, data, public;";
        command.ExecuteNonQuery();
    }

    public AccountState Get()
    {
        using var connection = new NpgsqlConnection(_postgresConnectionString);
        connection.Open();
        SetSchemaSearchPath(connection);

        return new AccountState
        {
            People = LoadPeople(connection),
            Credentials = LoadCredentials(connection),
            Sessions = LoadSessions(connection),
            Entitlements = LoadEntitlements(connection),
            IssuedTokens = LoadIssuedTokens(connection),
            AuditLog = LoadAuditLog(connection)
        };
    }

    // People, Credentials, Sessions, Entitlements, and IssuedTokens are
    // mutable entities and are upserted whole-list (ON CONFLICT DO UPDATE),
    // matching LeagueRepository.SyncAccounts. AuditLog is append-only and
    // uses a different strategy -- see SyncAuditLog below, matching
    // LeagueRepository.SyncAuditLog's IsPersisted-filtered insert-only
    // pattern exactly.
    public void Save(AccountState state)
    {
        using var connection = new NpgsqlConnection(_postgresConnectionString);
        connection.Open();
        SetSchemaSearchPath(connection);
        using var transaction = connection.BeginTransaction();

        SyncPeople(connection, transaction, state.People);
        SyncCredentials(connection, transaction, state.Credentials);
        SyncSessions(connection, transaction, state.Sessions);
        SyncEntitlements(connection, transaction, state.Entitlements);
        SyncIssuedTokens(connection, transaction, state.IssuedTokens);
        SyncAuditLog(connection, transaction, state.AuditLog);

        transaction.Commit();
    }

    // ---- People ----------------------------------------------------

    private static List<Person> LoadPeople(NpgsqlConnection connection)
    {
        var people = new List<Person>();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT id, person_id, display_name, is_active, created_at
            FROM idn.people
            ORDER BY id;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            people.Add(new Person
            {
                Id = ReadInt(reader, "id"),
                PersonId = ReadGuid(reader, "person_id"),
                DisplayName = ReadString(reader, "display_name"),
                IsActive = ReadBool(reader, "is_active", true),
                CreatedAt = ReadDateTime(reader, "created_at") ?? DateTime.UtcNow
            });
        }
        return people;
    }

    private static void SyncPeople(NpgsqlConnection connection, NpgsqlTransaction transaction, List<Person> people)
    {
        foreach (var person in people)
        {
            using var command = NewCommand(connection, transaction, @"
                INSERT INTO idn.people (id, person_id, display_name, is_active, created_at)
                VALUES (@id, @person_id, @display_name, @is_active, @created_at)
                ON CONFLICT (id) DO UPDATE SET
                    display_name = EXCLUDED.display_name,
                    is_active = EXCLUDED.is_active;");
            Add(command, "@id", person.Id);
            Add(command, "@person_id", person.PersonId);
            Add(command, "@display_name", person.DisplayName);
            Add(command, "@is_active", person.IsActive);
            Add(command, "@created_at", person.CreatedAt);
            command.ExecuteNonQuery();
        }
    }

    // ---- PersonCredential --------------------------------------------

    private static List<PersonCredential> LoadCredentials(NpgsqlConnection connection)
    {
        var credentials = new List<PersonCredential>();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT id, person_record_id, user_name, password_hash, must_change_password,
                   failed_login_count, lockout_until, last_password_changed_at, security_generation
            FROM secu.person_credentials
            ORDER BY id;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            credentials.Add(new PersonCredential
            {
                Id = ReadInt(reader, "id"),
                PersonRecordId = ReadInt(reader, "person_record_id"),
                UserName = ReadString(reader, "user_name"),
                PasswordHash = ReadString(reader, "password_hash"),
                MustChangePassword = ReadBool(reader, "must_change_password"),
                FailedLoginCount = ReadInt(reader, "failed_login_count"),
                LockoutUntil = ReadDateTime(reader, "lockout_until"),
                LastPasswordChangedAt = ReadDateTime(reader, "last_password_changed_at"),
                SecurityGeneration = Convert.ToInt64(reader["security_generation"])
            });
        }
        return credentials;
    }

    private static void SyncCredentials(NpgsqlConnection connection, NpgsqlTransaction transaction, List<PersonCredential> credentials)
    {
        foreach (var credential in credentials)
        {
            using var command = NewCommand(connection, transaction, @"
                INSERT INTO secu.person_credentials (
                    id, person_record_id, user_name, password_hash, must_change_password,
                    failed_login_count, lockout_until, last_password_changed_at, security_generation
                )
                VALUES (
                    @id, @person_record_id, @user_name, @password_hash, @must_change_password,
                    @failed_login_count, @lockout_until, @last_password_changed_at, @security_generation
                )
                ON CONFLICT (id) DO UPDATE SET
                    password_hash = EXCLUDED.password_hash,
                    must_change_password = EXCLUDED.must_change_password,
                    failed_login_count = EXCLUDED.failed_login_count,
                    lockout_until = EXCLUDED.lockout_until,
                    last_password_changed_at = EXCLUDED.last_password_changed_at,
                    security_generation = GREATEST(secu.person_credentials.security_generation, EXCLUDED.security_generation);");
            Add(command, "@id", credential.Id);
            Add(command, "@person_record_id", credential.PersonRecordId);
            Add(command, "@user_name", credential.UserName);
            Add(command, "@password_hash", credential.PasswordHash);
            Add(command, "@must_change_password", credential.MustChangePassword);
            Add(command, "@failed_login_count", credential.FailedLoginCount);
            Add(command, "@lockout_until", credential.LockoutUntil);
            Add(command, "@last_password_changed_at", credential.LastPasswordChangedAt);
            Add(command, "@security_generation", Math.Max(1, credential.SecurityGeneration));
            command.ExecuteNonQuery();
        }
    }

    // ---- PersonSession -------------------------------------------------

    private static List<PersonSession> LoadSessions(NpgsqlConnection connection)
    {
        var sessions = new List<PersonSession>();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT id, person_record_id, session_token, created_at, last_activity_at, revoked_at, security_generation
            FROM secu.person_sessions
            ORDER BY id;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            sessions.Add(new PersonSession
            {
                Id = ReadInt(reader, "id"),
                PersonRecordId = ReadInt(reader, "person_record_id"),
                SessionToken = ReadString(reader, "session_token"),
                CreatedAt = ReadDateTime(reader, "created_at") ?? DateTime.UtcNow,
                LastActivityAt = ReadDateTime(reader, "last_activity_at") ?? DateTime.UtcNow,
                RevokedAt = ReadDateTime(reader, "revoked_at"),
                SecurityGeneration = Convert.ToInt64(reader["security_generation"])
            });
        }
        return sessions;
    }

    private static void SyncSessions(NpgsqlConnection connection, NpgsqlTransaction transaction, List<PersonSession> sessions)
    {
        foreach (var session in sessions)
        {
            using var command = NewCommand(connection, transaction, @"
                INSERT INTO secu.person_sessions (
                    id, person_record_id, session_token, created_at, last_activity_at, revoked_at, security_generation
                )
                VALUES (
                    @id, @person_record_id, @session_token, @created_at, @last_activity_at, @revoked_at, @security_generation
                )
                ON CONFLICT (id) DO UPDATE SET
                    last_activity_at = EXCLUDED.last_activity_at,
                    revoked_at = EXCLUDED.revoked_at;");
            Add(command, "@id", session.Id);
            Add(command, "@person_record_id", session.PersonRecordId);
            Add(command, "@session_token", session.SessionToken);
            Add(command, "@created_at", session.CreatedAt);
            Add(command, "@last_activity_at", session.LastActivityAt);
            Add(command, "@revoked_at", session.RevokedAt);
            Add(command, "@security_generation", Math.Max(1, session.SecurityGeneration));
            command.ExecuteNonQuery();
        }
    }

    // ---- Entitlement -----------------------------------------------

    private static List<Entitlement> LoadEntitlements(NpgsqlConnection connection)
    {
        var entitlements = new List<Entitlement>();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT id, person_id, product_type, tier, source, granted_at, expires_at
            FROM idn.entitlements
            ORDER BY id;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            entitlements.Add(new Entitlement
            {
                Id = ReadInt(reader, "id"),
                PersonId = ReadGuid(reader, "person_id"),
                ProductType = ReadString(reader, "product_type", ProductTypes.League),
                Tier = ReadString(reader, "tier", "standard"),
                Source = ReadString(reader, "source", EntitlementSources.BetaIncluded),
                GrantedAt = ReadDateTime(reader, "granted_at") ?? DateTime.UtcNow,
                ExpiresAt = ReadDateTime(reader, "expires_at")
            });
        }
        return entitlements;
    }

    private static void SyncEntitlements(NpgsqlConnection connection, NpgsqlTransaction transaction, List<Entitlement> entitlements)
    {
        foreach (var entitlement in entitlements)
        {
            using var command = NewCommand(connection, transaction, @"
                INSERT INTO idn.entitlements (id, person_id, product_type, tier, source, granted_at, expires_at)
                VALUES (@id, @person_id, @product_type, @tier, @source, @granted_at, @expires_at)
                ON CONFLICT (id) DO UPDATE SET
                    tier = EXCLUDED.tier,
                    expires_at = EXCLUDED.expires_at;");
            Add(command, "@id", entitlement.Id);
            Add(command, "@person_id", entitlement.PersonId);
            Add(command, "@product_type", entitlement.ProductType);
            Add(command, "@tier", entitlement.Tier);
            Add(command, "@source", entitlement.Source);
            Add(command, "@granted_at", entitlement.GrantedAt);
            Add(command, "@expires_at", entitlement.ExpiresAt);
            command.ExecuteNonQuery();
        }
    }

    // ---- IssuedIdentityToken -------------------------------------------

    private static List<IssuedIdentityToken> LoadIssuedTokens(NpgsqlConnection connection)
    {
        var tokens = new List<IssuedIdentityToken>();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT token_id, person_id, issued_at, expires_at, revoked_at, issued_to_product_type
            FROM idn.issued_identity_tokens
            ORDER BY issued_at;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            tokens.Add(new IssuedIdentityToken
            {
                TokenId = ReadGuid(reader, "token_id"),
                PersonId = ReadGuid(reader, "person_id"),
                IssuedAt = ReadDateTime(reader, "issued_at") ?? DateTime.UtcNow,
                ExpiresAt = ReadDateTime(reader, "expires_at") ?? DateTime.UtcNow,
                RevokedAt = ReadDateTime(reader, "revoked_at"),
                IssuedToProductType = ReadString(reader, "issued_to_product_type")
            });
        }
        return tokens;
    }

    private static void SyncIssuedTokens(NpgsqlConnection connection, NpgsqlTransaction transaction, List<IssuedIdentityToken> tokens)
    {
        foreach (var token in tokens)
        {
            using var command = NewCommand(connection, transaction, @"
                INSERT INTO idn.issued_identity_tokens (
                    token_id, person_id, issued_at, expires_at, revoked_at, issued_to_product_type
                )
                VALUES (
                    @token_id, @person_id, @issued_at, @expires_at, @revoked_at, @issued_to_product_type
                )
                ON CONFLICT (token_id) DO UPDATE SET
                    revoked_at = EXCLUDED.revoked_at;");
            Add(command, "@token_id", token.TokenId);
            Add(command, "@person_id", token.PersonId);
            Add(command, "@issued_at", token.IssuedAt);
            Add(command, "@expires_at", token.ExpiresAt);
            Add(command, "@revoked_at", token.RevokedAt);
            Add(command, "@issued_to_product_type", token.IssuedToProductType);
            command.ExecuteNonQuery();
        }
    }

    // ---- AccountAuditLogEntry -------------------------------------------
    //
    // Insert-only, filtered by the transient IsPersisted flag -- matching
    // LeagueRepository.SyncAuditLog exactly. Audit rows are never updated
    // once written, so there is no ON CONFLICT clause at all: a row is
    // either new (not yet in the database) or already there and untouched.

    private static List<AccountAuditLogEntry> LoadAuditLog(NpgsqlConnection connection)
    {
        var entries = new List<AccountAuditLogEntry>();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT id, created_at, actor_person_id, actor_role, area, action, detail,
                   target_type, target_id, before_json, after_json, reason, request_id, source
            FROM data.audit_log
            ORDER BY created_at, id;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(new AccountAuditLogEntry
            {
                IsPersisted = true,
                Id = ReadInt(reader, "id"),
                CreatedAt = ReadDateTime(reader, "created_at") ?? DateTime.UtcNow,
                ActorPersonId = reader.IsDBNull(reader.GetOrdinal("actor_person_id"))
                    ? null
                    : ReadGuid(reader, "actor_person_id"),
                ActorRole = ReadString(reader, "actor_role"),
                Area = ReadString(reader, "area"),
                Action = ReadString(reader, "action"),
                Detail = ReadString(reader, "detail"),
                TargetType = ReadString(reader, "target_type"),
                TargetId = ReadString(reader, "target_id"),
                BeforeJson = ReadString(reader, "before_json"),
                AfterJson = ReadString(reader, "after_json"),
                Reason = ReadString(reader, "reason"),
                RequestId = ReadString(reader, "request_id"),
                Source = ReadString(reader, "source")
            });
        }
        return entries;
    }

    private static void SyncAuditLog(NpgsqlConnection connection, NpgsqlTransaction transaction, List<AccountAuditLogEntry> entries)
    {
        foreach (var entry in entries.Where(entry => !entry.IsPersisted))
        {
            using var command = NewCommand(connection, transaction, @"
                INSERT INTO data.audit_log (
                    created_at, actor_person_id, actor_role, area, action, detail,
                    target_type, target_id, before_json, after_json, reason, request_id, source
                )
                VALUES (
                    @created_at, @actor_person_id, @actor_role, @area, @action, @detail,
                    @target_type, @target_id,
                    CAST(NULLIF(@before_json, '') AS jsonb),
                    CAST(NULLIF(@after_json, '') AS jsonb),
                    @reason, @request_id, @source
                )
                RETURNING id;");
            Add(command, "@created_at", entry.CreatedAt);
            Add(command, "@actor_person_id", entry.ActorPersonId);
            Add(command, "@actor_role", entry.ActorRole);
            Add(command, "@area", entry.Area);
            Add(command, "@action", entry.Action);
            Add(command, "@detail", entry.Detail);
            Add(command, "@target_type", entry.TargetType);
            Add(command, "@target_id", entry.TargetId);
            Add(command, "@before_json", entry.BeforeJson);
            Add(command, "@after_json", entry.AfterJson);
            Add(command, "@reason", entry.Reason);
            Add(command, "@request_id", entry.RequestId);
            Add(command, "@source", entry.Source);

            var newId = command.ExecuteScalar();
            entry.Id = Convert.ToInt32(newId);
            entry.IsPersisted = true;
        }
    }

    // ---- Targeted methods for the auth loop -----------------------------
    //
    // Everything above (Get/Save) loads or upserts the WHOLE list -- fine
    // for admin/debug visibility, wrong for per-request work like "does
    // this session hash exist and is it valid". These mirror
    // LeagueRepository.Security.cs's targeted, single-query methods
    // instead: one indexed lookup, not "load everything, search in
    // memory". id allocation uses the sequences created in
    // EnsurePostgresStore, matching this repository's existing
    // app-assigned-id contract for these tables rather than reopening that
    // design.

    public Person CreatePerson(string displayName)
    {
        using var connection = new NpgsqlConnection(_postgresConnectionString);
        connection.Open();
        SetSchemaSearchPath(connection);
        using var transaction = connection.BeginTransaction();

        var newId = (int)(long)NewCommand(connection, transaction,
            "SELECT nextval('idn.people_id_seq');").ExecuteScalar()!;
        var person = new Person
        {
            Id = newId,
            PersonId = Guid.NewGuid(),
            DisplayName = displayName,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        using (var command = NewCommand(connection, transaction, @"
            INSERT INTO idn.people (id, person_id, display_name, is_active, created_at)
            VALUES (@id, @person_id, @display_name, @is_active, @created_at);"))
        {
            Add(command, "@id", person.Id);
            Add(command, "@person_id", person.PersonId);
            Add(command, "@display_name", person.DisplayName);
            Add(command, "@is_active", person.IsActive);
            Add(command, "@created_at", person.CreatedAt);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
        return person;
    }

    public PersonCredential CreateCredential(int personRecordId, string userName, string passwordHash)
    {
        using var connection = new NpgsqlConnection(_postgresConnectionString);
        connection.Open();
        SetSchemaSearchPath(connection);
        using var transaction = connection.BeginTransaction();

        var newId = (int)(long)NewCommand(connection, transaction,
            "SELECT nextval('secu.person_credentials_id_seq');").ExecuteScalar()!;
        var credential = new PersonCredential
        {
            Id = newId,
            PersonRecordId = personRecordId,
            UserName = userName,
            PasswordHash = passwordHash,
            SecurityGeneration = 1
        };

        using (var command = NewCommand(connection, transaction, @"
            INSERT INTO secu.person_credentials (id, person_record_id, user_name, password_hash, security_generation)
            VALUES (@id, @person_record_id, @user_name, @password_hash, @security_generation);"))
        {
            Add(command, "@id", credential.Id);
            Add(command, "@person_record_id", credential.PersonRecordId);
            Add(command, "@user_name", credential.UserName);
            Add(command, "@password_hash", credential.PasswordHash);
            Add(command, "@security_generation", credential.SecurityGeneration);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
        return credential;
    }

    // user_name uniqueness relies on the UNIQUE constraint in the DDL above;
    // this is a plain lookup, not itself a race-free "claim this username"
    // operation -- CreateCredential's INSERT is what actually enforces
    // uniqueness (it throws on conflict), so the caller (Signup) must
    // handle that exception, not rely on checking-then-inserting.
    public PersonCredential? GetCredentialByUserName(string userName)
    {
        using var connection = new NpgsqlConnection(_postgresConnectionString);
        connection.Open();
        SetSchemaSearchPath(connection);
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT id, person_record_id, user_name, password_hash, must_change_password,
                   failed_login_count, lockout_until, last_password_changed_at, security_generation
            FROM secu.person_credentials
            WHERE user_name = @user_name;";
        Add(command, "@user_name", userName);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new PersonCredential
        {
            Id = ReadInt(reader, "id"),
            PersonRecordId = ReadInt(reader, "person_record_id"),
            UserName = ReadString(reader, "user_name"),
            PasswordHash = ReadString(reader, "password_hash"),
            MustChangePassword = ReadBool(reader, "must_change_password"),
            FailedLoginCount = ReadInt(reader, "failed_login_count"),
            LockoutUntil = ReadDateTime(reader, "lockout_until"),
            LastPasswordChangedAt = ReadDateTime(reader, "last_password_changed_at"),
            SecurityGeneration = Convert.ToInt64(reader["security_generation"])
        };
    }

    public Person? GetPersonById(int id)
    {
        using var connection = new NpgsqlConnection(_postgresConnectionString);
        connection.Open();
        SetSchemaSearchPath(connection);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, person_id, display_name, is_active, created_at FROM idn.people WHERE id = @id;";
        Add(command, "@id", id);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new Person
        {
            Id = ReadInt(reader, "id"),
            PersonId = ReadGuid(reader, "person_id"),
            DisplayName = ReadString(reader, "display_name"),
            IsActive = ReadBool(reader, "is_active", true),
            CreatedAt = ReadDateTime(reader, "created_at") ?? DateTime.UtcNow
        };
    }

    public Person? GetPersonByPersonId(Guid personId)
    {
        using var connection = new NpgsqlConnection(_postgresConnectionString);
        connection.Open();
        SetSchemaSearchPath(connection);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, person_id, display_name, is_active, created_at FROM idn.people WHERE person_id = @person_id;";
        Add(command, "@person_id", personId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new Person
        {
            Id = ReadInt(reader, "id"),
            PersonId = ReadGuid(reader, "person_id"),
            DisplayName = ReadString(reader, "display_name"),
            IsActive = ReadBool(reader, "is_active", true),
            CreatedAt = ReadDateTime(reader, "created_at") ?? DateTime.UtcNow
        };
    }

    // Atomic increment-then-maybe-lock in one UPDATE, matching
    // LeagueRepository.cs's failed_login_count CASE pattern -- avoids a
    // read-then-write race between concurrent failed attempts.
    public void RecordFailedLogin(int credentialId, int lockoutThreshold, TimeSpan lockoutDuration, DateTime now)
    {
        var threshold = Math.Max(1, lockoutThreshold);
        using var connection = new NpgsqlConnection(_postgresConnectionString);
        connection.Open();
        SetSchemaSearchPath(connection);
        using var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE secu.person_credentials
            SET failed_login_count = CASE
                    WHEN failed_login_count + 1 >= @threshold THEN 0
                    ELSE failed_login_count + 1
                END,
                lockout_until = CASE
                    WHEN failed_login_count + 1 >= @threshold THEN @lockout_until
                    ELSE lockout_until
                END
            WHERE id = @id;";
        Add(command, "@threshold", threshold);
        Add(command, "@lockout_until", now.Add(lockoutDuration));
        Add(command, "@id", credentialId);
        command.ExecuteNonQuery();
    }

    public void ResetLoginFailures(int credentialId)
    {
        using var connection = new NpgsqlConnection(_postgresConnectionString);
        connection.Open();
        SetSchemaSearchPath(connection);
        using var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE secu.person_credentials
            SET failed_login_count = 0,
                lockout_until = NULL
            WHERE id = @id
              AND (failed_login_count <> 0 OR lockout_until IS NOT NULL);";
        Add(command, "@id", credentialId);
        command.ExecuteNonQuery();
    }

    // Bumping the generation instantly invalidates every session created
    // under the old value, since ValidateAndTouchSession below requires an
    // exact match -- real "log out everywhere" capability, matching
    // PoolLeagueWeb's use of login_accounts.security_generation.
    public void BumpCredentialSecurityGeneration(int credentialId)
    {
        using var connection = new NpgsqlConnection(_postgresConnectionString);
        connection.Open();
        SetSchemaSearchPath(connection);
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE secu.person_credentials SET security_generation = security_generation + 1 WHERE id = @id;";
        Add(command, "@id", credentialId);
        command.ExecuteNonQuery();
    }

    public PersonSession CreateSession(int personRecordId, string sessionTokenHash, long securityGeneration, DateTime now)
    {
        using var connection = new NpgsqlConnection(_postgresConnectionString);
        connection.Open();
        SetSchemaSearchPath(connection);
        using var transaction = connection.BeginTransaction();

        var newId = (int)(long)NewCommand(connection, transaction,
            "SELECT nextval('secu.person_sessions_id_seq');").ExecuteScalar()!;
        var session = new PersonSession
        {
            Id = newId,
            PersonRecordId = personRecordId,
            SessionToken = sessionTokenHash,
            CreatedAt = now,
            LastActivityAt = now,
            SecurityGeneration = securityGeneration
        };

        using (var command = NewCommand(connection, transaction, @"
            INSERT INTO secu.person_sessions (id, person_record_id, session_token, created_at, last_activity_at, security_generation)
            VALUES (@id, @person_record_id, @session_token, @created_at, @last_activity_at, @security_generation);"))
        {
            Add(command, "@id", session.Id);
            Add(command, "@person_record_id", session.PersonRecordId);
            Add(command, "@session_token", session.SessionToken);
            Add(command, "@created_at", session.CreatedAt);
            Add(command, "@last_activity_at", session.LastActivityAt);
            Add(command, "@security_generation", session.SecurityGeneration);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
        return session;
    }

    // Single query does both the validity check AND the sliding-window
    // refresh (bumping last_activity_at forward), matching the spirit of
    // LeagueRepository's ValidateAccountSession CTE -- one round trip, no
    // separate read-then-write. Returns the owning PersonRecordId if (and
    // only if) the session is unrevoked, within the inactivity window, and
    // still matches the credential's CURRENT security generation.
    public int? ValidateAndTouchSession(string sessionTokenHash, TimeSpan inactivityWindow, DateTime now)
    {
        using var connection = new NpgsqlConnection(_postgresConnectionString);
        connection.Open();
        SetSchemaSearchPath(connection);
        using var command = connection.CreateCommand();
        command.CommandText = @"
            WITH valid AS (
                SELECT sessions.id, sessions.person_record_id
                FROM secu.person_sessions sessions
                JOIN secu.person_credentials credentials
                  ON credentials.person_record_id = sessions.person_record_id
                JOIN idn.people people
                  ON people.id = sessions.person_record_id
                WHERE sessions.session_token = @session_token
                  AND sessions.revoked_at IS NULL
                  AND sessions.last_activity_at > @cutoff
                  AND sessions.security_generation = credentials.security_generation
                  AND people.is_active
                FOR UPDATE OF sessions
            ),
            touched AS (
                UPDATE secu.person_sessions sessions
                SET last_activity_at = @now
                FROM valid
                WHERE sessions.id = valid.id
                RETURNING sessions.person_record_id
            )
            SELECT person_record_id FROM touched;";
        Add(command, "@session_token", sessionTokenHash);
        Add(command, "@cutoff", now.Subtract(inactivityWindow));
        Add(command, "@now", now);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadInt(reader, "person_record_id") : null;
    }

    public void RevokeSession(string sessionTokenHash, DateTime now)
    {
        using var connection = new NpgsqlConnection(_postgresConnectionString);
        connection.Open();
        SetSchemaSearchPath(connection);
        using var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE secu.person_sessions
            SET revoked_at = @now
            WHERE session_token = @session_token
              AND revoked_at IS NULL;";
        Add(command, "@now", now);
        Add(command, "@session_token", sessionTokenHash);
        command.ExecuteNonQuery();
    }

    public IssuedIdentityToken RecordIssuedToken(IssuedIdentityToken token)
    {
        using var connection = new NpgsqlConnection(_postgresConnectionString);
        connection.Open();
        SetSchemaSearchPath(connection);
        using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO idn.issued_identity_tokens (token_id, person_id, issued_at, expires_at, issued_to_product_type)
            VALUES (@token_id, @person_id, @issued_at, @expires_at, @issued_to_product_type);";
        Add(command, "@token_id", token.TokenId);
        Add(command, "@person_id", token.PersonId);
        Add(command, "@issued_at", token.IssuedAt);
        Add(command, "@expires_at", token.ExpiresAt);
        Add(command, "@issued_to_product_type", token.IssuedToProductType);
        command.ExecuteNonQuery();
        return token;
    }

    // Single-entry convenience wrapper around the same insert-only logic
    // SyncAuditLog uses, for callers (the auth service) that want to log
    // one event without building a whole AccountState.
    public void AppendAuditEntry(AccountAuditLogEntry entry)
    {
        using var connection = new NpgsqlConnection(_postgresConnectionString);
        connection.Open();
        SetSchemaSearchPath(connection);
        using var transaction = connection.BeginTransaction();
        SyncAuditLog(connection, transaction, new List<AccountAuditLogEntry> { entry });
        transaction.Commit();
    }

    // ---- Shared helpers, matching LeagueRepository's exactly -------------

    private static NpgsqlCommand NewCommand(NpgsqlConnection connection, NpgsqlTransaction transaction, string commandText) =>
        new(commandText, connection, transaction);

    private static void Add(NpgsqlCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static string ReadString(IDataRecord reader, string name, string fallback = "") =>
        reader.IsDBNull(reader.GetOrdinal(name)) ? fallback : reader.GetString(reader.GetOrdinal(name));

    private static int ReadInt(IDataRecord reader, string name, int fallback = 0) =>
        reader.IsDBNull(reader.GetOrdinal(name)) ? fallback : Convert.ToInt32(reader.GetValue(reader.GetOrdinal(name)));

    private static bool ReadBool(IDataRecord reader, string name, bool fallback = false) =>
        reader.IsDBNull(reader.GetOrdinal(name)) ? fallback : Convert.ToBoolean(reader.GetValue(reader.GetOrdinal(name)));

    private static Guid ReadGuid(IDataRecord reader, string name) =>
        reader.IsDBNull(reader.GetOrdinal(name)) ? Guid.Empty : (Guid)reader.GetValue(reader.GetOrdinal(name));

    private static DateTime? ReadDateTime(IDataRecord reader, string name) =>
        reader.IsDBNull(reader.GetOrdinal(name)) ? null : DateTime.SpecifyKind(Convert.ToDateTime(reader.GetValue(reader.GetOrdinal(name))), DateTimeKind.Utc);
}
