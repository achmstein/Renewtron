using Asic.Client.Models;
using Microsoft.EntityFrameworkCore;
using Renewtron.Abstractions;
using Renewtron.Data;

namespace Renewtron.Services;

/// <summary>
/// Business names for an ABN from our copy of the public register — a single indexed read
/// instead of a ~20s ASIC Connect session. The register has no ASIC account numbers and
/// doesn't say whether a name is due, so results must be confirmed with ASIC before payment
/// (<see cref="ISearchVerificationService"/>).
/// </summary>
public class LocalBusinessNameService(ApplicationDbContext db, ILogger<LocalBusinessNameService> logger) : IBusinessNameFallbackService
{
    private record Row(string Name, string RegistrationDate);

    public async Task<BusinessNamesResult> SearchByAbnAsync(string abn)
    {
        var cleanAbn = Modules.Helpers.NormalizeAbn(abn);
        try
        {
            // The CAST keeps the parameter varchar like the column; as nvarchar SQL Server
            // converts every row and scans millions of them instead of seeking the index.
            // NOLOCK: an import bulk-loads rows for a different ImportId alongside these, and
            // the filter on the active import already keeps those out — no reason to wait on it.
            var rows = await db.Database.SqlQuery<Row>($"""
                SELECT n.Name, n.RegistrationDate
                FROM RegisteredBusinessNames n WITH (NOLOCK)
                JOIN BusinessNameImports i ON i.Id = n.ImportId AND i.IsActive = 1
                WHERE n.Abn = CAST({cleanAbn} AS varchar(11))
                ORDER BY n.Name
                """).ToListAsync();

            if (rows.Count == 0)
                return BusinessNamesResult.Failed("No registered business names found for this ABN");

            return BusinessNamesResult.Succeeded(rows.Select(r => new BusinessName
            {
                Name = r.Name,
                AccountNumber = string.Empty,
                RegistrationDate = r.RegistrationDate,
            }).ToList());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Local business name lookup failed for ABN {Abn}", cleanAbn);
            return BusinessNamesResult.Failed($"Local lookup failed: {ex.Message}");
        }
    }
}
