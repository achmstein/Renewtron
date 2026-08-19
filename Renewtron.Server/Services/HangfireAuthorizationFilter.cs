using Hangfire.Dashboard;

namespace Renewtron.Services;

public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();

        // Cookie-authenticated humans only. The X-Api-Key machine scheme also satisfies
        // IsAuthenticated, but a machine credential must not grant an interactive dashboard
        // that can enqueue/delete arbitrary jobs.
        return httpContext.User.Identity is { IsAuthenticated: true, AuthenticationType: "Identity.Application" };
    }
}
