using System.Text.Json.Serialization;

namespace PocketRankingsAccount.Models;

// AccountState is the in-memory view services work with, matching the
// pattern established in PoolLeagueWeb/Models/LeagueModels.cs: PostgreSQL
// stores these pieces in normalized tables for deployment; this aggregate
// is the working shape used in code.
public class AccountState
{
    public List<Person> People { get; set; } = new();
    public List<PersonCredential> Credentials { get; set; } = new();
    public List<PersonSession> Sessions { get; set; } = new();
    public List<Entitlement> Entitlements { get; set; } = new();
    public List<IssuedIdentityToken> IssuedTokens { get; set; } = new();
    public List<AccountAuditLogEntry> AuditLog { get; set; } = new();
}

// The one durable, cross-product identity described in
// PLATFORM_AGENTS.md Section 2.1. Id stays a local relational key, exactly
// like every other entity in PoolLeagueWeb; PersonId is the only value ever
// shared outside this database -- embedded in identity tokens and used as
// the key on Entitlement rows. Never expose Id to another product; never
// use PersonId as a local foreign key inside Account itself.
public class Person
{
    public int Id { get; set; }
    public Guid PersonId { get; set; }
    public string DisplayName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// Login credential for a Person. Kept as its own table (rather than fields
// on Person) so a future auth method -- SSO, passkeys -- can be added as
// another credential row without reshaping Person. PersonRecordId is a
// local foreign key to Person.Id (int), not the external PersonId (Guid) --
// credentials never leave this database.
public class PersonCredential
{
    public int Id { get; set; }
    public int PersonRecordId { get; set; }
    public string UserName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool MustChangePassword { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutUntil { get; set; }
    public DateTime? LastPasswordChangedAt { get; set; }
    public long SecurityGeneration { get; set; } = 1;
}

// A single active login session for a Person on the Account site itself,
// mirroring the single-active-session enforcement already used by
// PoolLeagueWeb's SecurityFoundationService. This is separate from the
// short-lived cross-product identity token issued to other products
// (see IssuedIdentityToken below).
public class PersonSession
{
    public int Id { get; set; }
    public int PersonRecordId { get; set; }
    public string SessionToken { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAt { get; set; }
}

// Keep product and source names in one place, matching the AccountRoles
// pattern in PoolLeagueWeb/Models/LeagueModels.cs.
public static class ProductTypes
{
    public const string League = "league";
    public const string Tournament = "tournament";
    public const string PlayerProfile = "player_profile";
    public const string Web = "web";
}

public static class EntitlementSources
{
    public const string BetaIncluded = "beta_included";
    public const string Purchase = "purchase";
    public const string ManualGrant = "manual_grant";
}

// One row answers exactly one question for one product: does this PersonId
// have current, unexpired access? Field shape matches
// PLATFORM_AGENTS.md Section 2.2 exactly -- do not rename these fields
// without updating that document in the same change.
public class Entitlement
{
    public int Id { get; set; }
    public Guid PersonId { get; set; }
    public string ProductType { get; set; } = ProductTypes.League;
    public string Tier { get; set; } = "standard";
    public string Source { get; set; } = EntitlementSources.BetaIncluded;
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
}

// Placeholder far-future expiration for beta-included access, per
// PLATFORM_AGENTS.md Section 3. This is not a real business commitment --
// update this policy explicitly, with owner approval, when beta pricing
// begins. Do not read this constant as a promise made to customers.
public static class BetaAccessPolicy
{
    public static readonly DateTime PlaceholderExpiresAt =
        new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc);
}

// Record of an issued cross-product identity token, kept for revocation and
// audit only -- this is metadata about a token, never the signed token
// contents itself. TokenId matches the jti-equivalent claim inside the
// actual signed token once token issuance is implemented.
public class IssuedIdentityToken
{
    public Guid TokenId { get; set; }
    public Guid PersonId { get; set; }
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string IssuedToProductType { get; set; } = "";
}

// Durable append-only audit event. Matches the shape of PoolLeagueWeb's
// AuditLogEntry (see LeagueModels.cs) and PLATFORM_AGENTS.md Section 7:
// actor, role, target, redacted before/after evidence, reason, request id,
// source, and time. Uses DateTime.UtcNow rather than League's original
// AuditLogEntry.DateTime.Now, matching the newer Integration-layer
// convention already present in the same League file -- appropriate here
// since Account's audit trail is inherently cross-product/cross-timezone.
public class AccountAuditLogEntry
{
    [JsonIgnore]
    public bool IsPersisted { get; set; }
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid? ActorPersonId { get; set; }
    public string ActorRole { get; set; } = "";
    public string Area { get; set; } = "";
    public string Action { get; set; } = "";
    public string Detail { get; set; } = "";
    public string TargetType { get; set; } = "";
    public string TargetId { get; set; } = "";
    public string BeforeJson { get; set; } = "";
    public string AfterJson { get; set; } = "";
    public string Reason { get; set; } = "";
    public string RequestId { get; set; } = "";
    public string Source { get; set; } = "";
}
