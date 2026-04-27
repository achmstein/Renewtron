using Hangfire.Dashboard;

namespace Renewtron.Services;

public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();

        // Only allow access to authenticated admin users
        return httpContext.User.Identity?.IsAuthenticated == true;
    }
}
