using System.Globalization;
using Asic.Client.Abstractions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Renewtron.Abstractions;
using Renewtron.Data;
using Renewtron.Settings;

namespace Renewtron.Services;

public class BulkRenewalService : IBulkRenewalService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly IAsicRenewalClient _asicClient;
    private readonly IOptionsSnapshot<PricingSettings> _pricingSettings;
    private readonly ILogger<BulkRenewalService> _logger;

    public BulkRenewalService(
        ApplicationDbContext dbContext,
        IBackgroundJobClient backgroundJobClient,
        IAsicRenewalClient asicClient,
        IOptionsSnapshot<PricingSettings> pricingSettings,
        ILogger<BulkRenewalService> logger)
    {
        _dbContext = dbContext;
        _backgroundJobClient = backgroundJobClient;
        _asicClient = asicClient;
        _pricingSettings = pricingSettings;
        _logger = logger;
    }

    public async Task<BulkUploadResult> UploadAsync(Stream fileStream, string fileName)
    {
        var result = new BulkUploadResult();
        var rows = ParseRows(fileStream, result);
        result.TotalRows = rows.Count;

        if (rows.Count == 0)
            return result;

        // Load existing ABNs for dedup (across BulkRenewalUploads + OntraportSales)
        var incomingAbns = rows.Select(r => r.Abn).Distinct().ToList();
        var existingBulk = await _dbContext.BulkRenewalUploads
            .Where(b => incomingAbns.Contains(b.Abn))
            .Select(b => b.Abn)
            .ToListAsync();
        var existingOntraport = await _dbContext.OntraportSales
            .Where(o => incomingAbns.Contains(o.Abn))
            .Select(o => o.Abn)
            .ToListAsync();
        var existing = existingBulk.Concat(existingOntraport).ToHashSet();

        var pricing = _pricingSettings.Value;
        var now = DateTime.UtcNow;

        foreach (var row in rows)
        {
            if (existing.Contains(row.Abn))
            {
                result.SkippedDuplicates++;
                continue;
            }

            var daysUntilDue = row.RenewalDueDate.HasValue
                ? (row.RenewalDueDate.Value - now).TotalDays
                : (double?)null;

            var status = BulkRenewalStatus.WaitingForRenewalWindow;
            if (daysUntilDue.HasValue && daysUntilDue.Value <= 29)
                status = BulkRenewalStatus.WaitingForRenewalWindow; // processor will pick up on next run

            var upload = new BulkRenewalUpload
            {
                Id = Guid.NewGuid(),
                BusinessName = row.BusinessName,
                Abn = row.Abn,
                OwnerName = row.OwnerName,
                RenewalDueDate = row.RenewalDueDate,
                RenewalYears = row.RenewalYears,
                Amount = pricing.GetCustomerPrice(row.RenewalYears),
                Status = status,
                UploadedAt = now,
                SourceFile = fileName
            };

            _dbContext.BulkRenewalUploads.Add(upload);
            existing.Add(row.Abn); // prevent duplicates within the same file
            result.ImportedCount++;
        }

        await _dbContext.SaveChangesAsync();
        _logger.LogInformation(
            "Bulk renewal upload from {File}: {Imported} imported, {Dup} duplicates, {Invalid} invalid",
            fileName, result.ImportedCount, result.SkippedDuplicates, result.SkippedInvalid);

        return result;
    }

    public async Task ProcessEligibleRenewalsAsync()
    {
        _logger.LogInformation("Processing eligible bulk renewals");
        var now = DateTime.UtcNow;

        var eligibleUploads = await _dbContext.BulkRenewalUploads
            .Where(b => b.Status == BulkRenewalStatus.WaitingForRenewalWindow)
            .Where(b => b.RenewalDueDate != null)
            .Where(b => b.RenewalRequestId == null)
            .ToListAsync();

        foreach (var upload in eligibleUploads)
        {
            var daysUntilDue = (upload.RenewalDueDate!.Value - now).TotalDays;

            if (daysUntilDue > 29)
                continue; // still outside the window

            if (daysUntilDue < -30)
            {
                upload.Status = BulkRenewalStatus.NotDueForRenewal;
                upload.ErrorMessage = "Renewal due date is more than 30 days in the past";
                continue;
            }

            try
            {
                _logger.LogInformation("Queuing bulk renewal for {BusinessName} (ABN: {Abn}), due in {Days} days",
                    upload.BusinessName, upload.Abn, (int)daysUntilDue);

                // Resolve the real ASIC account number (not the ABN) by searching ASIC and matching the business name.
                var asicSearch = await _asicClient.SearchByAbnAsync(upload.Abn);
                if (!asicSearch.Success || asicSearch.BusinessNames == null || asicSearch.BusinessNames.Count == 0)
                {
                    upload.Status = BulkRenewalStatus.RenewalFailed;
                    upload.ErrorMessage = $"ASIC search failed: {asicSearch.ErrorMessage ?? "no business names returned"}";
                    _logger.LogWarning("ASIC search failed for ABN {Abn}: {Message}", upload.Abn, upload.ErrorMessage);
                    continue;
                }

                var matched = asicSearch.BusinessNames.FirstOrDefault(b =>
                    string.Equals(
                        (b.Name ?? "").Trim(),
                        (upload.BusinessName ?? "").Trim(),
                        StringComparison.OrdinalIgnoreCase));

                if (matched == null)
                {
                    upload.Status = BulkRenewalStatus.RenewalFailed;
                    upload.ErrorMessage = $"Business name '{upload.BusinessName}' not found under ABN {upload.Abn} at ASIC";
                    _logger.LogWarning("No ASIC match for {BusinessName} under ABN {Abn}", upload.BusinessName, upload.Abn);
                    continue;
                }

                var searchLog = new SearchLog
                {
                    Id = Guid.NewGuid(),
                    Abn = upload.Abn,
                    SearchedAt = now,
                    Success = true,
                    ResultsCount = 1,
                    InitiatedBy = SearchInitiator.System
                };
                _dbContext.SearchLogs.Add(searchLog);

                var searchResult = new SearchResult
                {
                    Id = Guid.NewGuid(),
                    SearchLogId = searchLog.Id,
                    BusinessName = matched.Name,
                    AccountNumber = matched.AccountNumber,
                    RegistrationDate = matched.RegistrationDate ?? upload.RenewalDueDate?.ToString("dd/MM/yyyy") ?? ""
                };
                _dbContext.SearchResults.Add(searchResult);

                var renewalRequest = new RenewalRequest
                {
                    Id = Guid.NewGuid(),
                    SearchResultId = searchResult.Id,
                    InitiatedAt = now,
                    RenewalYears = upload.RenewalYears,
                    Email = null,
                    MobileNumber = null,
                    DateOfBirth = null,
                    PaymentType = PaymentType.External,
                    Amount = upload.Amount,
                    Status = RenewalStatus.Pending,
                    Source = RenewalSource.BulkUpload
                };
                _dbContext.RenewalRequests.Add(renewalRequest);

                upload.RenewalRequestId = renewalRequest.Id;
                upload.Status = BulkRenewalStatus.RenewalQueued;

                await _dbContext.SaveChangesAsync();

                _backgroundJobClient.Enqueue<IRenewalProcessingService>(
                    service => service.ProcessRenewalAsync(renewalRequest.Id));

                _logger.LogInformation("Queued renewal {RenewalId} for bulk upload {UploadId}",
                    renewalRequest.Id, upload.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error queuing renewal for bulk upload {UploadId}", upload.Id);
                upload.Status = BulkRenewalStatus.RenewalFailed;
                upload.ErrorMessage = ex.Message;
            }
        }

        await _dbContext.SaveChangesAsync();
    }

    private List<ParsedRow> ParseRows(Stream fileStream, BulkUploadResult result)
    {
        var rows = new List<ParsedRow>();

        using var doc = SpreadsheetDocument.Open(fileStream, false);
        var workbookPart = doc.WorkbookPart ?? throw new InvalidOperationException("Workbook missing");
        var sheet = workbookPart.Workbook.Sheets?.Elements<Sheet>().FirstOrDefault()
            ?? throw new InvalidOperationException("No sheets in workbook");
        var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id!);
        var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;

        var sheetData = worksheetPart.Worksheet.Elements<SheetData>().First();
        var rowIndex = 0;

        foreach (var row in sheetData.Elements<Row>())
        {
            rowIndex++;
            if (rowIndex == 1) continue; // header

            var cells = row.Elements<Cell>().ToDictionary(c => GetColumnLetter(c.CellReference?.Value ?? ""));

            var businessName = GetCellString(cells.GetValueOrDefault("A"), sharedStrings);
            var abnRaw = GetCellString(cells.GetValueOrDefault("B"), sharedStrings);
            var dueDateRaw = GetCellString(cells.GetValueOrDefault("C"), sharedStrings);
            var ownerName = GetCellString(cells.GetValueOrDefault("D"), sharedStrings);
            var termRaw = GetCellString(cells.GetValueOrDefault("E"), sharedStrings);

            if (string.IsNullOrWhiteSpace(businessName) && string.IsNullOrWhiteSpace(abnRaw))
                continue; // blank row

            var abn = new string((abnRaw ?? "").Where(char.IsDigit).ToArray());
            if (string.IsNullOrWhiteSpace(businessName) || string.IsNullOrEmpty(abn))
            {
                result.SkippedInvalid++;
                result.Errors.Add($"Row {rowIndex}: missing business name or ABN");
                continue;
            }

            var dueDate = ParseExcelDate(dueDateRaw);
            var years = ParseRenewalTerm(termRaw);

            rows.Add(new ParsedRow
            {
                BusinessName = businessName!.Trim(),
                Abn = abn,
                OwnerName = string.IsNullOrWhiteSpace(ownerName) ? null : ownerName!.Trim(),
                RenewalDueDate = dueDate,
                RenewalYears = years
            });
        }

        return rows;
    }

    private static string GetColumnLetter(string cellRef)
    {
        var letters = new string(cellRef.Where(char.IsLetter).ToArray());
        return letters;
    }

    private static string? GetCellString(Cell? cell, SharedStringTable? sharedStrings)
    {
        if (cell == null) return null;
        var value = cell.CellValue?.InnerText;
        if (string.IsNullOrEmpty(value)) return cell.InnerText;

        if (cell.DataType?.Value == CellValues.SharedString && sharedStrings != null)
        {
            if (int.TryParse(value, out var idx) && idx >= 0 && idx < sharedStrings.ChildElements.Count)
                return sharedStrings.ChildElements[idx].InnerText;
        }

        return value;
    }

    private static DateTime? ParseExcelDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var serial) && serial > 0)
        {
            try { return DateTime.FromOADate(serial); }
            catch { }
        }

        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return parsed;

        if (DateTime.TryParseExact(raw, new[] { "dd/MM/yyyy", "d/MM/yyyy", "yyyy-MM-dd" },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            return parsed;

        return null;
    }

    private static int ParseRenewalTerm(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return 1;
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (int.TryParse(digits, out var years) && years > 0)
            return years;
        return 1;
    }

    private sealed class ParsedRow
    {
        public string BusinessName { get; set; } = "";
        public string Abn { get; set; } = "";
        public string? OwnerName { get; set; }
        public DateTime? RenewalDueDate { get; set; }
        public int RenewalYears { get; set; } = 1;
    }
}
