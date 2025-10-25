using Asic.Client.Captcha;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Renewtron.Abstractions;
using Renewtron.Components;
using Renewtron.Components.Account;
using Renewtron.Data;
using Renewtron.Services;
using Renewtron.Settings;
using Soenneker.Blazor.CreditCards.Registrars;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityUserAccessor>();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = IdentityConstants.ApplicationScheme;
    options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
})
.AddIdentityCookies();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityCore<AppUser>(options => options.SignIn.RequireConfirmedAccount = false)
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddSingleton<IEmailSender<AppUser>, IdentityNoOpEmailSender>();

builder.Services.AddScoped<ApplicationDbContextInitialiser>();

// Encryption service
builder.Services.AddSingleton<IEncryptionService, EncryptionService>();

// Configure settings
builder.Services.AddOptions<TwoCaptchaSettings>()
    .BindConfiguration("TwoCaptcha")
    .ValidateDataAnnotations();

builder.Services.AddOptions<StripeSettings>()
    .BindConfiguration("Stripe")
    .ValidateDataAnnotations();

builder.Services.AddOptions<SendGridSettings>()
    .BindConfiguration("SendGrid")
    .ValidateDataAnnotations();

builder.Services.AddOptions<AsicCreditCardSettings>()
    .BindConfiguration("AsicCreditCard")
    .ValidateDataAnnotations();

builder.Services.AddOptions<PricingSettings>()
    .BindConfiguration("Pricing")
    .ValidateDataAnnotations();

// Payment and Email services
builder.Services.AddScoped<IStripePaymentService, StripePaymentService>();
builder.Services.AddScoped<IEmailService, EmailService>();

// Settings management service
builder.Services.AddScoped<ISettingsService, SettingsService>();

// Renewal retry service
builder.Services.AddScoped<IRenewalRetryService, RenewalRetryService>();

// Background job processing service
builder.Services.AddScoped<IRenewalProcessingService, RenewalProcessingService>();

// Add Hangfire services for background job processing
builder.Services.AddHangfire(configuration => configuration
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(connectionString, new SqlServerStorageOptions
    {
        CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
        SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
        QueuePollInterval = TimeSpan.Zero,
        UseRecommendedIsolationLevel = true,
        DisableGlobalLocks = true
    }));

// Add the processing server as IHostedService
builder.Services.AddHangfireServer();

// Add in-memory cache for ABN search results
builder.Services.AddMemoryCache();

// Add Session support for tracking users
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(2);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Add HttpContextAccessor to access user info
builder.Services.AddHttpContextAccessor();

builder.Services.AddHttpContextAccessor();

builder.Services.AddAsic(builder.Configuration);

builder.Services.AddCreditCardsInteropAsScoped();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();

app.UseSession();

// Add Hangfire Dashboard (only accessible to authenticated admin users)
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireAuthorizationFilter() }
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

using var scope = app.Services.CreateScope();

var initialiser = scope.ServiceProvider.GetRequiredService<ApplicationDbContextInitialiser>();

await initialiser.InitialiseAsync();
await initialiser.SeedAsync();

app.Run();
