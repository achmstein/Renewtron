using Asic.Client.Models;

namespace Renewtron.Abstractions;

public interface IBusinessNameFallbackService
{
    Task<BusinessNamesResult> SearchByAbnAsync(string abn);
}
