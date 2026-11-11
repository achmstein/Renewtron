using Asic.Integrations.Models;

namespace Renewtron.Abstractions;

public interface IBusinessNameFallbackService
{
    Task<BusinessNamesResult> SearchByAbnAsync(string abn);
}
