using PocketRankingsAccount.Services;

namespace PocketRankingsAccount.Tests;

public sealed class CustomerProductLifecycleServiceTests
{
    private readonly CustomerProductLifecycleService service=new();
    private static CustomerProductLifecycle Item(CustomerProductLifecycleState state=CustomerProductLifecycleState.Ready)=>new(Guid.NewGuid(),Guid.NewGuid(),"tournament",state);

    [Fact] public void CancellationDisablesAccessAndSetsExactDeadline(){var now=DateTimeOffset.UtcNow;var result=service.Cancel(Item(),now);Assert.Equal(CustomerProductLifecycleState.Retention61Days,result.State);Assert.Equal(now,result.AccessDisabledAt);Assert.Equal(now.AddDays(61),result.DeletionDueAt);}
    [Fact] public void RetentionDoesNotExpireEarly(){var now=DateTimeOffset.UtcNow;var item=service.Cancel(Item(),now);Assert.Equal(CustomerProductLifecycleState.Retention61Days,service.EvaluateRetention(item,now.AddDays(60)).State);}
    [Fact] public void LegalHoldPausesDeletion(){var now=DateTimeOffset.UtcNow;var item=service.SetLegalHold(service.Cancel(Item(),now),true,"Court order");Assert.Equal(CustomerProductLifecycleState.Retention61Days,service.EvaluateRetention(item,now.AddDays(90)).State);}
    [Fact] public void ProvisioningCannotSkipValidation(){Assert.Throws<InvalidOperationException>(()=>service.Advance(Item(CustomerProductLifecycleState.ProvisioningQueued),CustomerProductLifecycleState.Ready));}
    [Fact] public void DueDeletionCompletesInOrder(){var now=DateTimeOffset.UtcNow;var due=service.EvaluateRetention(service.Cancel(Item(),now),now.AddDays(61));var deleting=service.BeginDeletion(due);Assert.Equal(CustomerProductLifecycleState.Deleted,service.CompleteDeletion(deleting,now.AddDays(62)).State);}
}
