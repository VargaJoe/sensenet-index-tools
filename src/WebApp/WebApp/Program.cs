using WebApp.Components;
using WebApp.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.DataProtection;
using System.Net;
using SenseNetIndexTools;

var builder = WebApplication.CreateBuilder(args);
var settings = RuntimeSettings.Load();
foreach (var directory in new[] { settings.DataDirectory, settings.OutputDirectory, settings.KeyDirectory, settings.SnapshotDirectory, settings.BackupDirectory }.Where(p => p != null))
{
    Directory.CreateDirectory(directory!);
    var probe = Path.Combine(directory!, ".startup-" + Guid.NewGuid().ToString("N"));
    File.WriteAllText(probe, ""); File.Delete(probe);
}
if (settings.SourcePath != null) IndexSnapshot.Resolve(settings.SourcePath);
WebAccess.ConfigureForwarding(builder);
var protection = builder.Services.AddDataProtection().SetApplicationName("SenseNetIndexTools")
    .PersistKeysToFileSystem(new DirectoryInfo(settings.KeyDirectory ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys")));
if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

// Configure security policy
builder.Services.Configure<IISServerOptions>(options =>
{
    options.AllowSynchronousIO = true;
});

builder.Services.Configure<RouteOptions>(options =>
{
    options.LowercaseUrls = true;
});

// Add LastActivityIdService
builder.Services.AddScoped<LastActivityIdService>();

// Add IndexValidationService
builder.Services.AddScoped<IndexValidationService>();

// Add SubtreeCheckingService
builder.Services.AddScoped<SubtreeCheckingService>();

// Add ConfigurationService
builder.Services.AddScoped<ConfigurationService>();

// Add ReportStorageService
builder.Services.AddScoped<ReportStorageService>();

// Add RebuildIndexService
builder.Services.AddHttpClient<RebuildIndexService>();

// Add logging
builder.Services.AddLogging(logging =>
{
    logging.ClearProviders();
    logging.AddProvider(new RedactingConsoleLoggerProvider());
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseWhen(context => context.Connection.RemoteIpAddress != null, branch => branch.UseForwardedHeaders());
WebAccess.UseAccess(app);
if (app.Configuration.GetValue<bool?>("WebAccess:HttpsRedirect") ?? true) app.UseHttpsRedirection();
app.MapGet("/healthz", () => Results.Text("healthy"));

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(WebApp.Client._Imports).Assembly);

app.Run();

public partial class Program { }
