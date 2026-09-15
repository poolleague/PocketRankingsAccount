using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PocketRankingsAccount.Services;

// Signs short-lived, purpose- and installation-bound privacy directives without persisting usable bearer tokens.
public sealed class PrivacyDirectiveSigner : IDisposable
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);
    private readonly RSA signingKey;

    public PrivacyDirectiveSigner(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var pem = configuration["Security:TokenSigningPrivateKeyPem"];
        if (string.IsNullOrWhiteSpace(pem) && !environment.IsDevelopment())
            throw new InvalidOperationException("Security:TokenSigningPrivateKeyPem is required for privacy delivery outside Development.");
        signingKey = RSA.Create();
        if (string.IsNullOrWhiteSpace(pem)) signingKey.KeySize = 2048;
        else signingKey.ImportFromPem(pem);
    }

    // Creates a fresh single-attempt token so delivery retries never reuse bearer material.
    public string Sign(Guid requestId, Guid personId, string targetProduct, string installationKey, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(targetProduct) || string.IsNullOrWhiteSpace(installationKey))
            throw new ArgumentException("A target product and installation are required.");
        var header = JsonSerializer.Serialize(new { alg = "RS256", typ = "JWT", kid = "account-privacy-1" });
        var payload = JsonSerializer.Serialize(new
        {
            iss = "pocketrankings-account",
            aud = $"pocketrankings-{targetProduct}",
            action = "person.player_data_erasure_requested.v1",
            request_id = requestId,
            person_id = personId,
            installation_key = installationKey,
            iat = now.ToUnixTimeSeconds(),
            exp = now.Add(Lifetime).ToUnixTimeSeconds(),
            jti = Guid.NewGuid()
        });
        var unsigned = $"{Encode(Encoding.UTF8.GetBytes(header))}.{Encode(Encoding.UTF8.GetBytes(payload))}";
        var signature = signingKey.SignData(Encoding.UTF8.GetBytes(unsigned), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{unsigned}.{Encode(signature)}";
    }

    // Exposes only the public key for controlled product configuration and test verification.
    public string ExportPublicKeyPem() => signingKey.ExportSubjectPublicKeyInfoPem();

    // Uses unpadded base64url so the value has standard JWT wire encoding without another dependency.
    private static string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public void Dispose() => signingKey.Dispose();
}
