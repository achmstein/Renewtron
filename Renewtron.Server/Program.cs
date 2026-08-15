using Asic.Client.Abstractions;
using Carter;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Renewtron.Abstractions;
using Renewtron.Data;
using Renewtron.Identity;
using Renewtron.Services;
using Renewtron.Settings;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Writable overrides layer for runtime-mutable settings.
// Lives outside the immutable container image so the SettingsService can persist
// admin changes; reloadOnChange flushes IOptionsMonitor consumers automatically.
var overridesPath = builder.Configuration["Storage:OverridesPath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "settings.overrides.json");
builder.Configuration.AddJsonFile(overridesPath, optional: true, reloadOnChange: true);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddCarter();

// Aspire injects the connection string under "RenewtronDb"; fall back to a local
// "DefaultConnection" so the Server project can still be run on its own.
var rawConnectionString = builder.Configuration.GetConnectionString("RenewtronDb")
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("No SQL Server connection string ('RenewtronDb' or 'DefaultConnection') found.");

// SQL Server containers can take 30-60s to accept connections on first boot; the default
// 15s pre-login timeout isn't enough. Bump the pool size too — Hangfire's schedulers,
// the Hangfire server workers, and EF requests all share this pool.
var csb = new SqlConnectionStringBuilder(rawConnectionString)
{
    ConnectTimeout = 60,
    TrustServerCertificate = true,
    MaxPoolSize = 300,
    Pooling = true,
    ApplicationName = "Renewtron.Server",
};
var connectionString = csb.ConnectionString;

// Hangfire opens its own pool and uses different connection-string knobs than EF; give it
// a separate logical pool by tagging the application name.
var hangfireCsb = new SqlConnectionStringBuilder(connectionString) { ApplicationName = "Renewtron.Hangfire" };
var hangfireConnectionString = hangfireCsb.ConnectionString;

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(
        maxRetryCount: 6,
        maxRetryDelay: TimeSpan.FromSeconds(10),
        errorNumbersToAdd: null)));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

var authentication = builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme);
authentication.AddIdentityCookies();
authentication.AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
    ApiKeyAuthenticationHandler.SchemeName, null);

// Default policy accepts either the SPA's Identity cookie or the X-Api-Key header,
// so every existing RequireAuthorization() endpoint works for both callers.
builder.Services.AddAuthorization(options =>
{
    options.DefaultPolicy = new AuthorizationPolicyBuilder(
            IdentityConstants.ApplicationScheme,
            ApiKeyAuthenticationHandler.SchemeName)
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddIdentityCore<AppUser>(options => options.SignIn.RequireConfirmedAccount = false)
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders()
    .AddApiEndpoints();

builder.Services.AddSingleton<IEmailSender<AppUser>, IdentityNoOpEmailSender>();

builder.Services.AddScoped<ApplicationDbContextInitialiser>();

builder.Services.AddSingleton<IEncryptionService, EncryptionService>();

builder.Services.AddOptions<StripeSettings>().BindConfiguration("Stripe").ValidateDataAnnotations();
builder.Services.AddOptions<SendGridSettings>().BindConfiguration("SendGrid").ValidateDataAnnotations();
builder.Services.AddOptions<AsicSettings>().BindConfiguration("Asic").ValidateDataAnnotations();
builder.Services.AddOptions<PricingSettings>().BindConfiguration("Pricing").ValidateDataAnnotations();
builder.Services.AddOptions<OntraportSettings>().BindConfiguration("Ontraport").ValidateDataAnnotations();
builder.Services.AddOptions<WinBackSettings>().BindConfiguration("WinBack");
builder.Services.AddOptions<TrackingSettings>().BindConfiguration("Tracking");

builder.Services.AddScoped<IStripePaymentService, StripePaymentService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<ILeadEmailService, LeadEmailService>();
builder.Services.AddHttpClient<IBusinessNameFallbackService, DataGovBusinessNameService>();
builder.Services.AddScoped<ILeadService, LeadService>();
builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddScoped<IRenewalProcessingService, RenewalProcessingService>();
builder.Services.AddHttpClient<IOntraportSalesService, OntraportSalesService>();
builder.Services.AddScoped<IBulkRenewalService, BulkRenewalService>();
builder.Services.AddScoped<IWinBackService, WinBackService>();

builder.Services.AddHangfire(configuration => configuration
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(hangfireConnectionString, new SqlServerStorageOptions
    {
        CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
        SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
        // QueuePollInterval=Zero relies on SQL Server's resource-governor polling; safe with
        // the bigger pool above. Don't set it lower than 1s on a containerised SQL.
        QueuePollInterval = TimeSpan.FromSeconds(15),
        UseRecommendedIsolationLevel = true,
        DisableGlobalLocks = true,
    }));

builder.Services.AddHangfireServer(options =>
{
    // Default = min(20, ProcessorCount * 5). On a 16-core dev box this pegs ~80 threads,
    // each holding a SqlConnection while idle-polling. 5 is plenty for the recurring jobs
    // we have (Ontraport sync, bulk renewal, ASIC renewals).
    options.WorkerCount = 5;
});

builder.Services.AddMemoryCache();
builder.Services.AddDistributedMemoryCache();

builder.Services.AddHttpContextAccessor();

builder.Services.AddAsic(builder.Configuration, services =>
{
    services.AddHttpClient<ISmsProvider, OntraportSmsProvider>();
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(
                "http://localhost:5173",
                "https://localhost:5173",
                "https://businessnames.applyforanabn.au")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var initialiser = scope.ServiceProvider.GetRequiredService<ApplicationDbContextInitialiser>();
    await initialiser.InitialiseAsync();
    await initialiser.SeedAsync();
}

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseMigrationsEndPoint();
}

app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [new HangfireAuthorizationFilter()]
});

RecurringJob.AddOrUpdate<IOntraportSalesService>(
    "ontraport-sales-sync",
    service => service.SyncSalesAsync(),
    "0 6 * * *",
    new RecurringJobOptions { TimeZone = TimeZoneInfo.FindSystemTimeZoneById("AUS Eastern Standard Time") });

RecurringJob.AddOrUpdate<IOntraportSalesService>(
    "ontraport-process-renewals",
    service => service.ProcessEligibleRenewalsAsync(),
    "0 7 * * *",
    new RecurringJobOptions { TimeZone = TimeZoneInfo.FindSystemTimeZoneById("AUS Eastern Standard Time") });

RecurringJob.AddOrUpdate<IBulkRenewalService>(
    "bulk-renewal-process",
    service => service.ProcessEligibleRenewalsAsync(),
    "30 7 * * *",
    new RecurringJobOptions { TimeZone = TimeZoneInfo.FindSystemTimeZoneById("AUS Eastern Standard Time") });

app.MapGroup("/api").MapIdentityApi<AppUser>();

app.MapCarter();

app.MapDefaultEndpoints();

app.UseFileServer();

app.Run();
