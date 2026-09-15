using PocketRankingsAccount.Models;

namespace PocketRankingsAccount.Services;

public enum PlayerDataOptOutResult { Accepted, AlreadyRequested, ConfirmationRequired, InvalidPassword, AccountNotFound }

public interface IPlayerDataPrivacyStore
{
    PlayerDataPrivacyStatus GetPlayerDataPrivacyStatus(Guid personId);
    Guid CreatePlayerDataOptOutRequest(Guid personId, DateTime now);
}

public interface IRecentPasswordVerifier
{
    bool PersonExists(Guid personId);
    bool VerifyCurrentPassword(Guid personId, string password);
}

// Coordinates the destructive request while keeping password verification and persistence independently testable.
public sealed class PlayerDataPrivacyService(
    IPlayerDataPrivacyStore store,
    IRecentPasswordVerifier security)
{
    // Requires both explicit irreversible consent and the current credential before creating any directive.
    public PlayerDataOptOutResult RequestOptOut(Guid personId, PlayerDataOptOutInput input, DateTime now, out Guid? requestId)
    {
        requestId = null;
        if (!input.ConfirmPermanentDeletion) return PlayerDataOptOutResult.ConfirmationRequired;
        if (!security.PersonExists(personId)) return PlayerDataOptOutResult.AccountNotFound;
        if (!security.VerifyCurrentPassword(personId, input.CurrentPassword)) return PlayerDataOptOutResult.InvalidPassword;

        var current = store.GetPlayerDataPrivacyStatus(personId);
        if (current.IsOptedOut)
        {
            requestId = current.RequestId;
            return PlayerDataOptOutResult.AlreadyRequested;
        }

        requestId = store.CreatePlayerDataOptOutRequest(personId, now);
        return PlayerDataOptOutResult.Accepted;
    }
}
