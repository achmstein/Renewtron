using Microsoft.EntityFrameworkCore;
using Renewtron.Data;

namespace Renewtron.Services;

public interface ILeadService
{
    Task<Lead> CreateLeadAsync(CreateLeadDto dto);
    Task<Lead?> GetLeadAsync(Guid leadId);
    Task<Lead?> GetLeadWithSearchResultsAsync(Guid leadId);
    Task<Lead> UpdateLeadOutcomeAsync(Guid leadId, LeadOutcome outcome, string? message = null);
    Task<Lead> LinkSearchLogAsync(Guid leadId, Guid searchLogId);
    Task MarkConvertedAsync(Guid leadId);
    Task UpdateReminderOptInAsync(Guid leadId, bool optIn);
}

public class CreateLeadDto
{
    public required string Abn { get; set; }
    public required string FullName { get; set; }
    public required string Email { get; set; }
    public required string MobileNumber { get; set; }
    public required DateOnly DateOfBirth { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? SessionId { get; set; }
}

public class LeadService : ILeadService
{
    private readonly ApplicationDbContext _dbContext;

    public LeadService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Lead> CreateLeadAsync(CreateLeadDto dto)
    {
        var lead = new Lead
        {
            Id = Guid.NewGuid(),
            Abn = dto.Abn.Replace(" ", ""),
            FullName = dto.FullName,
            Email = dto.Email,
            MobileNumber = dto.MobileNumber,
            DateOfBirth = dto.DateOfBirth,
            IpAddress = dto.IpAddress,
            UserAgent = dto.UserAgent,
            SessionId = dto.SessionId,
            CreatedAt = DateTime.UtcNow,
            Outcome = LeadOutcome.Pending
        };

        _dbContext.Leads.Add(lead);
        await _dbContext.SaveChangesAsync();

        return lead;
    }

    public async Task<Lead?> GetLeadAsync(Guid leadId)
    {
        return await _dbContext.Leads
            .FirstOrDefaultAsync(l => l.Id == leadId);
    }

    public async Task<Lead?> GetLeadWithSearchResultsAsync(Guid leadId)
    {
        return await _dbContext.Leads
            .Include(l => l.SearchLog)
                .ThenInclude(s => s!.Results)
            .Include(l => l.RenewalRequests)
            .FirstOrDefaultAsync(l => l.Id == leadId);
    }

    public async Task<Lead> UpdateLeadOutcomeAsync(Guid leadId, LeadOutcome outcome, string? message = null)
    {
        var lead = await _dbContext.Leads.FindAsync(leadId)
            ?? throw new InvalidOperationException($"Lead {leadId} not found");

        lead.Outcome = outcome;
        lead.OutcomeMessage = message;

        await _dbContext.SaveChangesAsync();
        return lead;
    }

    public async Task<Lead> LinkSearchLogAsync(Guid leadId, Guid searchLogId)
    {
        var lead = await _dbContext.Leads.FindAsync(leadId)
            ?? throw new InvalidOperationException($"Lead {leadId} not found");

        lead.SearchLogId = searchLogId;
        await _dbContext.SaveChangesAsync();

        return lead;
    }

    public async Task MarkConvertedAsync(Guid leadId)
    {
        var lead = await _dbContext.Leads.FindAsync(leadId)
            ?? throw new InvalidOperationException($"Lead {leadId} not found");

        lead.ConvertedToRenewal = true;
        lead.ConvertedAt = DateTime.UtcNow;
        lead.Outcome = LeadOutcome.RenewalCompleted;

        await _dbContext.SaveChangesAsync();
    }

    public async Task UpdateReminderOptInAsync(Guid leadId, bool optIn)
    {
        var lead = await _dbContext.Leads.FindAsync(leadId)
            ?? throw new InvalidOperationException($"Lead {leadId} not found");

        lead.ReminderOptIn = optIn;
        await _dbContext.SaveChangesAsync();
    }
}
