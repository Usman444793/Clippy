using Clippy.Components;
using Clippy.Components.Account;
using Clippy.Data;
using Clippy.Endpoints;
using Clippy.EndPoints;
using Clippy.Extensions;
using Clippy.Services.Analytics;
using Clippy.Services.Caching;
using Clippy.Services.Email;
using Clippy.Services.QrCode;
using Clippy.Services.Security;
using Clippy.Services.Shortening;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
if (args.Contains("--run-tests", StringComparer.OrdinalIgnoreCase))
{
    var report = await HealthEndpoints.RunEnterpriseSelfTestsAsync();
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    return;
}

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents()
    .AddAuthenticationStateSerialization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString, npgsqlOptions =>
        npgsqlOptions.EnableRetryOnFailure()));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.Configure<EmailSettingsOptions>(builder.Configuration.GetSection(EmailSettingsOptions.SectionName));
builder.Services.AddSingleton<ClippyEmailService>();
builder.Services.AddSingleton<IClippyEmailService>(sp => sp.GetRequiredService<ClippyEmailService>());
builder.Services.AddSingleton<IEmailSender<ApplicationUser>>(sp => sp.GetRequiredService<ClippyEmailService>());
builder.Services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<ClippyEmailService>());

builder.Services.Configure<SecurityOptions>(builder.Configuration.GetSection(SecurityOptions.SectionName));
builder.Services.Configure<ShortenerConfigOptions>(builder.Configuration.GetSection(ShortenerConfigOptions.SectionName));
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IOtpService, OtpService>();
builder.Services.AddSingleton<IShortCodeGenerator, SnowFlakeBase62CodeGenerator>();
builder.Services.AddSingleton<IUrlSafetyScanner, UrlSafetyScanner>();
builder.Services.AddSingleton<IQrCodeService, QrCodeService>();
builder.Services.AddSingleton<IUrlResolutionCache, UrlResolutionCache>();
builder.Services.AddScoped<IUrlShortenerService, UrlShortenerService>();
builder.Services.AddSingleton<IClickTelemetryQueue, ClickTelemetryQueue>();
builder.Services.AddHostedService<ClickAnalyticsBackgroundWorker>();
builder.Services.AddHostedService<ExpiredUrlCleanupWorker>();
builder.Services.AddClippyAuthentication(builder.Configuration);
builder.Services.AddAuthorization();
builder.Services.AddClippyRateLimiting();

var app = builder.Build();
try
{
    var dbFactory = app.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
    await using var db = await dbFactory.CreateDbContextAsync();
    if (db.Database.IsRelational())
    {
        await db.Database.MigrateAsync();
    }
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "Database migration check skipped on startup. Ensure PostgreSQL is running and the connection string is valid.");
}

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(Clippy.Client._Imports).Assembly);

app.MapAdditionalIdentityEndpoints();
app.MapClippyAuthEndpoints();
app.MapClippyUrlApiEndpoints();
app.MapClippyRedirectEndpoints();
app.MapClippyHealthEndpoints();
app.Run();
