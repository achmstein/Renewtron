namespace Renewtron.Data;

/// <summary>
/// One row of our local copy of ASIC's public business names register (data.gov.au),
/// so the wizard can list a customer's names without waiting on ASIC Connect. Only
/// currently registered names that carry an ABN are kept. Rows belong to one
/// <see cref="BusinessNameImport"/>; lookups read the active import only, so a new
/// import loads alongside the old one and swaps in with a single flag change.
/// </summary>
public class RegisteredBusinessName
{
    public long Id { get; set; }
    public Guid ImportId { get; set; }
    public string Abn { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>dd/MM/yyyy, as published.</summary>
    public string RegistrationDate { get; set; } = string.Empty;
}
