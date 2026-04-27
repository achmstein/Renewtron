using System.Security.Claims;
using Carter;
using Microsoft.AspNetCore.Identity;
using Renewtron.Data;

namespace Renewtron.Modules;

public sealed class AccountModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").WithTags("Account");

        group.MapPost("/logout", async (SignInManager<AppUser> signInManager) =>
        {
            await signInManager.SignOutAsync();
            return Results.Ok();
        }).RequireAuthorization();

        group.MapGet("/me", (ClaimsPrincipal user) =>
        {
            if (user.Identity?.IsAuthenticated != true) return Results.Unauthorized();
            return Results.Ok(new { email = user.Identity.Name, name = user.Identity.Name });
        }).RequireAuthorization();
    }
}
