using Asic.Integrations.Renewal;


namespace Renewtron.Abstractions;

public interface IBusinessNameFallbackService
{
    Task<BusinessNamesResult> SearchByAbnAsync(string abn);
}
