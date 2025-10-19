using Asic.Client.Models;

namespace Asic.Client.Abstractions;
public interface IAsicRegistrySearchClient
{
    Task<SearchResult<BusinessNamesResponse>> SearchAsync(string abn);
    Task<SearchResult<BusinessNameResponse>> SearchAsync(string abn, string name);
}