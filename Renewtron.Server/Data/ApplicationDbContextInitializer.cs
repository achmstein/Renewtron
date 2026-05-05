using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Renewtron.Data;

public class ApplicationDbContextInitialiser
{
    private readonly ILogger<ApplicationDbContextInitialiser> _logger;
    private readonly ApplicationDbContext _context;
    private readonly UserManager<AppUser> _userManager;

    public ApplicationDbContextInitialiser(ILogger<ApplicationDbContextInitialiser> logger,
        ApplicationDbContext context, 
        UserManager<AppUser> userManager)
    {
        _logger = logger;
        _context = context;
        _userManager = userManager;
    }

    public async Task InitialiseAsync()
    {
        try
        {
            await _context.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while initialising the database.");
            throw;
        }
    }

    public async Task SeedAsync()
    {
        try
        {
            await TrySeedAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while seeding the database.");
            throw;
        }
    }

    public async Task TrySeedAsync()
    {
        const string email = "admin@renewtron.com.au";
        const string password = "Administrator1!";

        var existing = await _userManager.FindByNameAsync(email);
        if (existing is null)
        {
            var administrator = new AppUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
            };
            await _userManager.CreateAsync(administrator, password);
        }
        else if (string.IsNullOrEmpty(existing.Email))
        {
            // Patch users seeded by the previous initialiser, which set UserName only.
            // Without an Email, Identity's /api/login (FindByEmailAsync) returns null
            // and the operator can't sign in.
            existing.Email = email;
            existing.EmailConfirmed = true;
            await _userManager.UpdateAsync(existing);
        }
    }
}
