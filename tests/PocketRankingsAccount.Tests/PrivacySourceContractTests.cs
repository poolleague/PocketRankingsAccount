namespace PocketRankingsAccount.Tests;

public sealed class PrivacySourceContractTests
{
    // Keeps schema and controller safeguards visible even before a disposable PostgreSQL integration environment exists.
    [Fact]
    public void SourceContainsRequiredPrivacyContracts()
    {
        var root = FindRoot();
        var repository = File.ReadAllText(Path.Combine(root,"src","PocketRankingsAccount","Services","AccountRepository.cs"));
        var controller = File.ReadAllText(Path.Combine(root,"src","PocketRankingsAccount","Controllers","AccountController.cs"));
        Assert.Contains("data.player_data_requests", repository);
        Assert.Contains("data.player_data_request_targets", repository);
        Assert.Contains("data.privacy_outbox", repository);
        Assert.Contains("WHERE status = 'processing'", repository);
        Assert.Contains("[Authorize]", controller);
        Assert.Contains("[ValidateAntiForgeryToken]", controller);
    }

    // Finds the repository without embedding a workstation-specific path in test logic.
    private static string FindRoot() { var directory=new DirectoryInfo(AppContext.BaseDirectory); while(directory is not null&&!File.Exists(Path.Combine(directory.FullName,"PocketRankingsAccount.slnx")))directory=directory.Parent; return directory?.FullName??throw new DirectoryNotFoundException(); }
}
