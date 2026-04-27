namespace Renewtron.Data;

public enum OntraportSaleStatus
{
    Synced = 0,
    WaitingForRenewalWindow = 1,
    RenewalQueued = 2,
    RenewalCompleted = 3,
    RenewalFailed = 4,
    NotDueForRenewal = 5
}
