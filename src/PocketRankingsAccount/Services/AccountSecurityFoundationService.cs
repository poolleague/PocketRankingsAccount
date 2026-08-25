using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PocketRankingsAccount.Models;

namespace PocketRankingsAccount.Services;

public enum LoginStatus { Success, InvalidCredentials, LockedOut, AccountInactive }
public enum SignupStatus { Success, UserNameTaken, InvalidPassword }

public readonly record struct LoginResult(LoginStatus Status, string? SessionToken, Person? Person);
public readonly record struct SignupResult(SignupStatus Status, string? Error, Person? Person);

// Login/signup/session lifecycle closely mirrors
// PoolLeagueWeb/Services/SecurityFoundationService.cs: same keyed-hash
// approach (HMACSHA256, domain-separated), same random-secret format, same
// atomic lockout pattern. Deliberately narrower than League's version --
// see PLATFORM_AGENTS.md-adjacent reasoning in AGENTS.md for what's
// intentionally left out (IP-based abuse rate-limiting, password recovery,
// single-active-session enforcement) and why.
//
// Identity token issuance (the bottom third of this file) has no League
// equivalent -- League never needed to hand proof of identity to another
// product. Every design choice there (RS256, 5-minute lifetime, ephemeral
// key fallback) is this file's own, not a mirror of existing code, and is
// commented as such.
public sealed class AccountSecurityFoundationService
{
    // The cookie only needs to carry the raw session handle. Unlike
    // League's cookie (which also carries accountId + securityGeneration
    // as claims, checked against those same values in the DB row),
    // ValidateAndTouchSession's SQL join already checks the session's
    // stored generation against the credential's CURRENT generation
    // directly -- there is nothing a second cookie-supplied claim would
    // add, since the session handle itself (256 bits, unique, hashed) is
    // already the sole authenticator.
    public const string SessionCookieClaim = "PocketRankingsAccountSession";

    // Fixed for now -- there is no settings table yet (League's equivalent
    // values live in cnfg.league_settings, configurable per deployment).
    // Revisit as an explicit, configurable value if that's ever needed;
    // hardcoding a reasonable default is the right reversible/cost-free
    // choice until then, per PLATFORM_AGENTS.md Section 8.
    private const int LockoutThreshold = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan SessionInactivityWindow = TimeSpan.FromHours(24);

    // Identity tokens are meant as a short-lived, one-time handoff proof
    // when a person is redirected from Account to another product -- not a
    // long-lived bearer token used on every request (each product is
    // expected to establish its own local session/link row after
    // verifying one). Five minutes is generous for a redirect round trip
    // and tight for anything else. This is my own design choice, not
    // mirrored from anywhere -- reconsider if the actual redirect flow
    // needs longer.
    private static readonly TimeSpan IdentityTokenLifetime = TimeSpan.FromMinutes(5);

    private readonly AccountRepository _repository;
    private readonly byte[]? _hashKey;
    private readonly RSA _signingKey;
    private readonly bool _signingKeyIsEphemeral;
    private const string SigningKeyId = "account-1";

    public AccountSecurityFoundationService(AccountRepository repository, IConfiguration configuration)
    {
        _repository = repository;

        // Matches PoolLeagueWeb's Security:FoundationHashKey pattern
        // exactly: base64-encoded, must decode to >= 32 bytes (256 bits),
        // otherwise treated as not-configured rather than falling back to
        // a weak default. IsConfigured gates every method below that needs
        // it, same as League's.
        var configuredKey = configuration["Security:FoundationHashKey"];
        try
        {
            var decoded = string.IsNullOrWhiteSpace(configuredKey) ? null : Convert.FromBase64String(configuredKey);
            _hashKey = decoded is { Length: >= 32 } ? decoded : null;
        }
        catch (FormatException)
        {
            _hashKey = null;
        }

        // Token signing key: a real PEM-encoded RSA private key from
        // configuration if provided, otherwise an ephemeral in-memory
        // RSA-2048 key generated fresh on every app start. The ephemeral
        // path means: the app runs and can issue tokens with zero setup,
        // but every restart invalidates every previously-issued token, and
        // no other product could ever have a matching public key to verify
        // against. Fine for local dev; not something to run in Production
        // -- IsUsingEphemeralSigningKey below exists so a caller (or a
        // startup log line) can surface that loudly rather than silently.
        var configuredPem = configuration["Security:TokenSigningPrivateKeyPem"];
        if (!string.IsNullOrWhiteSpace(configuredPem))
        {
            _signingKey = RSA.Create();
            _signingKey.ImportFromPem(configuredPem);
            _signingKeyIsEphemeral = false;
        }
        else
        {
            _signingKey = RSA.Create(2048);
            _signingKeyIsEphemeral = true;
        }
    }

    public bool IsConfigured => _hashKey is not null;
    public bool IsUsingEphemeralSigningKey => _signingKeyIsEphemeral;

    // ---- Signup ----------------------------------------------------

    public SignupResult Signup(string userName, string password, string displayName, DateTime now)
    {
        var passwordError = PasswordService.ValidateNewPassword(password);
        if (passwordError is not null)
            return new SignupResult(SignupStatus.InvalidPassword, passwordError, null);

        if (_repository.GetCredentialByUserName(userName) is not null)
            return new SignupResult(SignupStatus.UserNameTaken, "That username is already in use.", null);

        var person = _repository.CreatePerson(displayName);
        try
        {
            _repository.CreateCredential(person.Id, userName, PasswordService.Hash(password));
        }
        catch (Exception)
        {
            // Username uniqueness is ultimately enforced by the database
            // constraint, not the check above (that check is a UX
            // shortcut, not a race-free guarantee -- see the comment on
            // GetCredentialByUserName in AccountRepository). A concurrent
            // signup with the same username between the check and this
            // insert lands here.
            return new SignupResult(SignupStatus.UserNameTaken, "That username is already in use.", null);
        }

        AppendAudit(person.PersonId, "auth", "signup", $"New account created: {userName}", now);
        return new SignupResult(SignupStatus.Success, null, person);
    }

    // ---- Login / session lifecycle ----------------------------------

    public LoginResult Login(string userName, string password, DateTime now)
    {
        var credential = _repository.GetCredentialByUserName(userName);
        if (credential is null)
        {
            // Same PasswordService.Verify cost is not paid for an unknown
            // username here. This is a real, small timing difference from
            // League's approach -- flagging it rather than letting it be
            // silently different. Acceptable for now since usernames are
            // not secret the way passwords are; revisit if that
            // assumption changes.
            return new LoginResult(LoginStatus.InvalidCredentials, null, null);
        }

        if (credential.LockoutUntil is { } lockoutUntil && lockoutUntil > now)
            return new LoginResult(LoginStatus.LockedOut, null, null);

        var person = _repository.GetPersonById(credential.PersonRecordId);
        if (person is null || !person.IsActive)
            return new LoginResult(LoginStatus.AccountInactive, null, null);

        if (!PasswordService.Verify(password, credential.PasswordHash))
        {
            _repository.RecordFailedLogin(credential.Id, LockoutThreshold, LockoutDuration, now);
            AppendAudit(person.PersonId, "auth", "login_failed", $"Failed login attempt: {userName}", now);
            return new LoginResult(LoginStatus.InvalidCredentials, null, null);
        }

        _repository.ResetLoginFailures(credential.Id);

        if (_hashKey is null)
            throw new InvalidOperationException("The security foundation hash key is missing or invalid.");

        var handle = CreateRandomSecret();
        _repository.CreateSession(person.Id, Hash("session", handle), credential.SecurityGeneration, now);

        AppendAudit(person.PersonId, "auth", "login", $"Successful login: {userName}", now);
        return new LoginResult(LoginStatus.Success, handle, person);
    }

    public Person? ValidateSession(string sessionHandle, DateTime now)
    {
        if (_hashKey is null || string.IsNullOrWhiteSpace(sessionHandle)) return null;
        var personRecordId = _repository.ValidateAndTouchSession(Hash("session", sessionHandle), SessionInactivityWindow, now);
        return personRecordId is null ? null : _repository.GetPersonById(personRecordId.Value);
    }

    public void Logout(string sessionHandle, DateTime now)
    {
        if (_hashKey is null || string.IsNullOrWhiteSpace(sessionHandle)) return;
        _repository.RevokeSession(Hash("session", sessionHandle), now);
    }

    // "Log out everywhere" -- bumps the credential's generation, which
    // instantly invalidates every session created under the previous
    // value (see ValidateAndTouchSession's generation match requirement).
    // Not yet called from anywhere (no "change password" or "sign out all
    // devices" UI exists yet) -- exposed now since it is cheap,
    // self-contained, and directly completes the SecurityGeneration
    // mechanism the model/schema already commit to.
    public void RevokeAllSessions(int credentialId, Guid personId, DateTime now)
    {
        _repository.BumpCredentialSecurityGeneration(credentialId);
        AppendAudit(personId, "auth", "revoke_all_sessions", "All sessions revoked", now);
    }

    // ---- Identity token issuance -------------------------------------
    //
    // Everything below is new design, not a mirror of League. A minimal,
    // dependency-free JWT (RS256) implementation: no
    // System.IdentityModel.Tokens.Jwt package, built directly from
    // System.Security.Cryptography so the whole signing path is a few
    // dozen lines anyone can read start to finish, rather than a black-box
    // library call -- appropriate for a from-scratch security-critical
    // piece with no existing pattern to defer to.

    public string IssueIdentityToken(Person person, string issuedToProductType, DateTime now)
    {
        var tokenId = Guid.NewGuid();
        var expiresAt = now.Add(IdentityTokenLifetime);

        var header = JsonSerializer.Serialize(new { alg = "RS256", typ = "JWT", kid = SigningKeyId });
        var payload = JsonSerializer.Serialize(new
        {
            iss = "pocketrankings-account",
            sub = person.PersonId.ToString(),
            aud = issuedToProductType,
            iat = ToUnixSeconds(now),
            exp = ToUnixSeconds(expiresAt),
            jti = tokenId.ToString()
        });

        var unsigned = $"{Base64UrlEncode(header)}.{Base64UrlEncode(payload)}";
        var signature = _signingKey.SignData(
            Encoding.UTF8.GetBytes(unsigned),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        var jwt = $"{unsigned}.{Base64UrlEncode(signature)}";

        _repository.RecordIssuedToken(new IssuedIdentityToken
        {
            TokenId = tokenId,
            PersonId = person.PersonId,
            IssuedAt = now,
            ExpiresAt = expiresAt,
            IssuedToProductType = issuedToProductType
        });
        AppendAudit(person.PersonId, "auth", "token_issued", $"Identity token issued for {issuedToProductType}", now);

        return jwt;
    }

    // JWK (RFC 7517) representation of the public half of the signing key,
    // for a /.well-known/jwks.json endpoint -- this is the "published
    // public key" PLATFORM_AGENTS.md Section 2.1 describes, so other
    // products can verify a token's signature locally without calling
    // Account. If IsUsingEphemeralSigningKey is true, this key is only
    // valid until the next restart -- fine to expose (there is nothing
    // secret in a public key), but not yet meaningfully verifiable by
    // another long-running product.
    public object GetJsonWebKeySet()
    {
        var parameters = _signingKey.ExportParameters(false);
        return new
        {
            keys = new[]
            {
                new
                {
                    kty = "RSA",
                    use = "sig",
                    alg = "RS256",
                    kid = SigningKeyId,
                    n = Base64UrlEncode(parameters.Modulus!),
                    e = Base64UrlEncode(parameters.Exponent!)
                }
            }
        };
    }

    // ---- Shared helpers, matching SecurityFoundationService's exactly ----

    private string Hash(string domain, string value)
    {
        if (_hashKey is null)
            throw new InvalidOperationException("The security foundation hash key is missing or invalid.");
        using var hmac = new HMACSHA256(_hashKey);
        var bytes = Encoding.UTF8.GetBytes($"{domain}\n{value}");
        return Convert.ToHexString(hmac.ComputeHash(bytes)).ToLowerInvariant();
    }

    private static string CreateRandomSecret() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string Base64UrlEncode(string value) => Base64UrlEncode(Encoding.UTF8.GetBytes(value));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static long ToUnixSeconds(DateTime value) => new DateTimeOffset(value, TimeSpan.Zero).ToUnixTimeSeconds();

    private void AppendAudit(Guid personId, string area, string action, string detail, DateTime now) =>
        _repository.AppendAuditEntry(new AccountAuditLogEntry
        {
            CreatedAt = now,
            ActorPersonId = personId,
            ActorRole = "person",
            Area = area,
            Action = action,
            Detail = detail,
            Source = "AccountSecurityFoundationService"
        });
}
