namespace Renewtron.Data;

public enum BulkRenewalStatus
{
    WaitingForRenewalWindow = 0,
    RenewalQueued = 1,
    RenewalCompleted = 2,
    RenewalFailed = 3,
    NotDueForRenewal = 4,
    Skipped = 5
}
