namespace Renewtron.Settings;

/// <summary>
/// Default ATO tax agent that the onboarding pipeline registers new clients
/// against. The Ato.Api gRPC service authenticates with myID and exposes one
/// or more agents the identity is registered for; this setting pins which one
/// Renewtron should use by default for every business-name onboarding job.
/// </summary>
public class AtoAgentSettings
{
    public string DefaultAgentAbn { get; set; } = string.Empty;
    public string DefaultAgentName { get; set; } = string.Empty;
}
