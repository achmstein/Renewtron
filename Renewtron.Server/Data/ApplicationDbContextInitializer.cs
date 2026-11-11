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
            // Some migrations index the register copy (millions of rows): give them longer than
            // the default 30 seconds, then put the normal timeout back.
            var timeout = _context.Database.GetCommandTimeout();
            _context.Database.SetCommandTimeout(TimeSpan.FromMinutes(15));
            await _context.Database.MigrateAsync();
            _context.Database.SetCommandTimeout(timeout);
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
        // Default users
        var administrator = new AppUser { UserName = "admin@renewtron.com.au" };

        if (_userManager.Users.All(u => u.UserName != administrator.UserName))
        {
            await _userManager.CreateAsync(administrator, "Administrator1!");
        }
    }
}
