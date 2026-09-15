using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using PocketRankingsAccount.Services;

namespace PocketRankingsAccount.Tests;

public sealed class PrivacyDirectiveSignerTests
{
    [Fact]
    public void DirectiveIsSignedShortLivedAndBoundToPurposeAudienceAndInstallation()
    {
        using var rsa = RSA.Create(2048);
        using var signer = new PrivacyDirectiveSigner(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Security:TokenSigningPrivateKeyPem"] = rsa.ExportPkcs8PrivateKeyPem() }).Build(), new TestEnvironment());
        var now = DateTimeOffset.UtcNow;
        var token = signer.Sign(Guid.NewGuid(), Guid.NewGuid(), "tournament", "installation-7", now);
        var parts = token.Split('.');
        Assert.Equal(3, parts.Length);
        using var payload = JsonDocument.Parse(Decode(parts[1]));
        Assert.Equal("pocketrankings-tournament", payload.RootElement.GetProperty("aud").GetString());
        Assert.Equal("installation-7", payload.RootElement.GetProperty("installation_key").GetString());
        Assert.Equal("person.player_data_erasure_requested.v1", payload.RootElement.GetProperty("action").GetString());
        Assert.InRange(payload.RootElement.GetProperty("exp").GetInt64()-payload.RootElement.GetProperty("iat").GetInt64(), 1, 120);
        using var publicKey = RSA.Create(); publicKey.ImportFromPem(signer.ExportPublicKeyPem());
        Assert.True(publicKey.VerifyData(Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}"), DecodeBytes(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    private static string Decode(string value) => Encoding.UTF8.GetString(DecodeBytes(value));
    private static byte[] DecodeBytes(string value) { value=value.Replace('-','+').Replace('_','/'); return Convert.FromBase64String(value.PadRight(value.Length+(4-value.Length%4)%4,'=')); }
    private sealed class TestEnvironment : IWebHostEnvironment { public string EnvironmentName { get; set; }="Development"; public string ApplicationName { get; set; }="Tests"; public string WebRootPath { get; set; }=""; public IFileProvider WebRootFileProvider { get; set; }=new NullFileProvider(); public string ContentRootPath { get; set; }=""; public IFileProvider ContentRootFileProvider { get; set; }=new NullFileProvider(); }
}
