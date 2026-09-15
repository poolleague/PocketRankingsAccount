using PocketRankingsAccount.Models;
using PocketRankingsAccount.Services;

namespace PocketRankingsAccount.Tests;

public sealed class PlayerDataPrivacyServiceTests
{
    // Proves that a checkbox alone cannot trigger destructive cross-product work.
    [Fact]
    public void RequestOptOut_RequiresCurrentPassword()
    {
        var store = new FakeStore();
        var service = new PlayerDataPrivacyService(store, new FakeVerifier(true, false));
        var result = service.RequestOptOut(Guid.NewGuid(), new() { CurrentPassword = "wrong", ConfirmPermanentDeletion = true }, DateTime.UtcNow, out _);
        Assert.Equal(PlayerDataOptOutResult.InvalidPassword, result);
        Assert.Equal(0, store.CreateCount);
    }

    // Proves that password possession cannot bypass the explicit irreversible acknowledgment.
    [Fact]
    public void RequestOptOut_RequiresPermanentConfirmation()
    {
        var store = new FakeStore();
        var service = new PlayerDataPrivacyService(store, new FakeVerifier(true, true));
        var result = service.RequestOptOut(Guid.NewGuid(), new() { CurrentPassword = "valid" }, DateTime.UtcNow, out _);
        Assert.Equal(PlayerDataOptOutResult.ConfirmationRequired, result);
        Assert.Equal(0, store.CreateCount);
    }

    // Creates one request after both independent confirmation factors succeed.
    [Fact]
    public void RequestOptOut_AcceptsConfirmedReauthenticatedPerson()
    {
        var store = new FakeStore();
        var service = new PlayerDataPrivacyService(store, new FakeVerifier(true, true));
        var result = service.RequestOptOut(Guid.NewGuid(), new() { CurrentPassword = "valid", ConfirmPermanentDeletion = true }, DateTime.UtcNow, out var requestId);
        Assert.Equal(PlayerDataOptOutResult.Accepted, result);
        Assert.NotNull(requestId);
        Assert.Equal(1, store.CreateCount);
    }

    // Treats a repeat submission as success without generating a second destructive request.
    [Fact]
    public void RequestOptOut_IsIdempotent()
    {
        var existing = Guid.NewGuid();
        var store = new FakeStore(new(true, existing, "processing", DateTime.UtcNow, []));
        var service = new PlayerDataPrivacyService(store, new FakeVerifier(true, true));
        var result = service.RequestOptOut(Guid.NewGuid(), new() { CurrentPassword = "valid", ConfirmPermanentDeletion = true }, DateTime.UtcNow, out var requestId);
        Assert.Equal(PlayerDataOptOutResult.AlreadyRequested, result);
        Assert.Equal(existing, requestId);
        Assert.Equal(0, store.CreateCount);
    }

    private sealed class FakeVerifier(bool exists, bool passwordValid) : IRecentPasswordVerifier
    {
        public bool PersonExists(Guid personId) => exists;
        public bool VerifyCurrentPassword(Guid personId, string password) => passwordValid;
    }

    private sealed class FakeStore(PlayerDataPrivacyStatus? current = null) : IPlayerDataPrivacyStore
    {
        public int CreateCount { get; private set; }
        public PlayerDataPrivacyStatus GetPlayerDataPrivacyStatus(Guid personId) => current ?? new(false, null, "not_requested", null, []);
        public Guid CreatePlayerDataOptOutRequest(Guid personId, DateTime now) { CreateCount++; return Guid.NewGuid(); }
    }
}
