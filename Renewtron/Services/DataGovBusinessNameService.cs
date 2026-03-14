using System.Text.Json;
using Asic.Client.Models;
using Renewtron.Abstractions;

namespace Renewtron.Services;

public class DataGovBusinessNameService : IBusinessNameFallbackService
{
    private const string ResourceId = "b87f81c1-593d-4cf7-b4c4-85d2a04b1d6e";
    private const string Url = "https://data.gov.au/data/datatables/ajax/" + ResourceId;

    private readonly HttpClient _httpClient;
    private readonly ILogger<DataGovBusinessNameService> _logger;

    public DataGovBusinessNameService(HttpClient httpClient, ILogger<DataGovBusinessNameService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<BusinessNamesResult> SearchByAbnAsync(string abn)
    {
        try
        {
            var cleanAbn = abn.Replace(" ", "");

            var formData = new Dictionary<string, string>
            {
                ["draw"] = "1",
                ["columns[0][data]"] = "_id",
                ["columns[0][name]"] = "_id",
                ["columns[0][searchable]"] = "false",
                ["columns[0][orderable]"] = "true",
                ["columns[8][data]"] = "BN_ABN",
                ["columns[8][name]"] = "BN_ABN",
                ["columns[8][searchable]"] = "true",
                ["columns[8][orderable]"] = "true",
                ["order[0][column]"] = "0",
                ["order[0][dir]"] = "asc",
                ["start"] = "0",
                ["length"] = "100",
                ["search[value]"] = cleanAbn,
                ["search[regex]"] = "false"
            };

            var response = await _httpClient.PostAsync(Url, new FormUrlEncodedContent(formData));
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var root = doc.RootElement;
            var data = root.GetProperty("data");
            var businessNames = new List<BusinessName>();

            foreach (var record in data.EnumerateArray())
            {
                var status = record.GetProperty("BN_STATUS").GetString() ?? "";
                var cancelDate = record.GetProperty("BN_CANCEL_DT").GetString() ?? "";

                // Only include currently registered, non-cancelled business names
                if (!status.Equals("Registered", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.IsNullOrWhiteSpace(cancelDate))
                    continue;

                var name = record.GetProperty("BN_NAME").GetString() ?? "";
                var regDate = record.GetProperty("BN_REG_DT").GetString() ?? "";
                var stateNum = record.GetProperty("BN_STATE_NUM").GetString() ?? "";

                businessNames.Add(new BusinessName
                {
                    Name = name,
                    AccountNumber = stateNum, // May be empty - ASIC portal is needed for full account numbers
                    RegistrationDate = regDate
                });
            }

            if (businessNames.Count == 0)
            {
                return BusinessNamesResult.Failed("No registered business names found for this ABN");
            }

            _logger.LogInformation(
                "data.gov.au fallback found {Count} business names for ABN {Abn}",
                businessNames.Count, cleanAbn);

            return BusinessNamesResult.Succeeded(businessNames);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "data.gov.au fallback search failed for ABN {Abn}", abn);
            return BusinessNamesResult.Failed($"Fallback search failed: {ex.Message}");
        }
    }
}
