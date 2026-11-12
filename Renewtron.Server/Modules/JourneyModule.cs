using Carter;
using Renewtron.Services;

namespace Renewtron.Modules;

/// <summary>What happened around a record, as one story every record page in it shares.</summary>
public sealed class JourneyModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/journey").RequireAuthorization().WithTags("Admin.Journey");

        // kind: lead, search, run (an ASIC run), order (a paid renewal) or sale (an Ontraport sale).
        group.MapGet("/{kind}/{id:guid}", async (string kind, Guid id, IJourneyService journeys, CancellationToken ct) =>
        {
            if (!Enum.TryParse<JourneyRecord>(kind, ignoreCase: true, out var record) || record == JourneyRecord.Email)
                return Results.BadRequest(new { error = "kind is one of lead, search, run, order, sale." });
            var journey = await journeys.ForAsync(record, id, ct);
            return journey is null
                ? Results.NotFound()
                : Results.Ok(new
                {
                    events = journey.Events.Select(e => new
                    {
                        at = e.At,
                        text = e.Text,
                        detail = e.Detail,
                        meaning = e.Meaning,
                        record = e.Record.ToString().ToLowerInvariant(),
                        recordId = e.RecordId,
                        group = e.Group,
                        next = e.Next,
                    }),
                    // What a line shows when it's opened: its record's key facts and what staff can do now.
                    records = journey.Records.Select(c => new
                    {
                        record = c.Record.ToString().ToLowerInvariant(),
                        id = c.Id,
                        title = c.Title,
                        facts = c.Facts.Select(f => new { label = f.Label, value = f.Value, mono = f.Mono }),
                        actions = c.Actions,
                    }),
                });
        });
    }
}
