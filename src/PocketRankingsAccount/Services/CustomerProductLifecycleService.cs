namespace PocketRankingsAccount.Services;

public enum CustomerProductLifecycleState
{
    PurchaseConfirmed,EntitlementRecorded,ProvisioningQueued,InfrastructureCreated,Migrating,Validating,Ready,
    AccessDisabled,Retention61Days,DeletionDue,Deleting,Deleted,NeedsAttention
}

public sealed record CustomerProductLifecycle(
    Guid InstallationId,Guid OwnerPersonId,string ProductType,CustomerProductLifecycleState State,
    DateTimeOffset? AccessDisabledAt=null,DateTimeOffset? DeletionDueAt=null,DateTimeOffset? DeletedAt=null,
    bool LegalHoldActive=false,string LegalHoldReason="");

// Encodes the approved purchase-to-deletion sequence without performing infrastructure or provider actions itself.
public sealed class CustomerProductLifecycleService
{
    private static readonly CustomerProductLifecycleState[] Provisioning =
    [CustomerProductLifecycleState.PurchaseConfirmed,CustomerProductLifecycleState.EntitlementRecorded,CustomerProductLifecycleState.ProvisioningQueued,CustomerProductLifecycleState.InfrastructureCreated,CustomerProductLifecycleState.Migrating,CustomerProductLifecycleState.Validating,CustomerProductLifecycleState.Ready];

    // Allows only the next provisioning state so retries cannot skip migration or validation.
    public CustomerProductLifecycle Advance(CustomerProductLifecycle item,CustomerProductLifecycleState next)
    {
        var index=Array.IndexOf(Provisioning,item.State);
        if(index<0||index==Provisioning.Length-1||Provisioning[index+1]!=next)throw new InvalidOperationException("The requested provisioning transition is not allowed.");
        return item with{State=next};
    }

    // Disables access at cancellation and starts the exact 61-calendar-day recovery period.
    public CustomerProductLifecycle Cancel(CustomerProductLifecycle item,DateTimeOffset cancelledAt)
    {
        if(item.State is CustomerProductLifecycleState.Deleting or CustomerProductLifecycleState.Deleted)throw new InvalidOperationException("A deleting or deleted installation cannot be cancelled again.");
        return item with{State=CustomerProductLifecycleState.Retention61Days,AccessDisabledAt=cancelledAt,DeletionDueAt=cancelledAt.AddDays(61)};
    }

    // Moves an expired installation to deletion due unless a specifically recorded legal hold pauses disposal.
    public CustomerProductLifecycle EvaluateRetention(CustomerProductLifecycle item,DateTimeOffset now)
    {
        if(item.State!=CustomerProductLifecycleState.Retention61Days||item.DeletionDueAt is null||now<item.DeletionDueAt||item.LegalHoldActive)return item;
        return item with{State=CustomerProductLifecycleState.DeletionDue};
    }

    // Legal holds require a reason and never silently change the original deletion deadline.
    public CustomerProductLifecycle SetLegalHold(CustomerProductLifecycle item,bool active,string reason)
    {
        if(active&&string.IsNullOrWhiteSpace(reason))throw new ArgumentException("A legal-hold reason is required.",nameof(reason));
        return item with{LegalHoldActive=active,LegalHoldReason=active?reason.Trim():""};
    }

    // Separates destructive execution from eligibility so an executor cannot delete an early or held installation.
    public CustomerProductLifecycle BeginDeletion(CustomerProductLifecycle item)=>item.State==CustomerProductLifecycleState.DeletionDue&&!item.LegalHoldActive?item with{State=CustomerProductLifecycleState.Deleting}:throw new InvalidOperationException("Deletion is not due or is held.");

    // Records completion only after a future executor has independently verified every scoped resource is gone.
    public CustomerProductLifecycle CompleteDeletion(CustomerProductLifecycle item,DateTimeOffset completedAt)=>item.State==CustomerProductLifecycleState.Deleting?item with{State=CustomerProductLifecycleState.Deleted,DeletedAt=completedAt}:throw new InvalidOperationException("Only a deleting installation can be completed.");
}
