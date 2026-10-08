using WebApp.Components;
using WebApp.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.DataProtection;
using System.Net;

var builder = WebApplication.CreateBuilder(args);
var protection = builder.Services.AddDataProtection().SetApplicationName("SenseNetIndexTools")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys")));
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
    logging.AddConsole();
    logging.AddDebug();
    logging.SetMinimumLevel(LogLevel.Debug);
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

// This maintenance UI has no user authentication. Keep it local unless explicitly enabled by the operator.
app.Use(async (context, next) =>
{
    var remote = context.Connection.RemoteIpAddress;
    if (!app.Configuration.GetValue<bool>("AllowRemoteAccess") &&
        (remote == null || !IPAddress.IsLoopback(remote) || context.Request.Headers.ContainsKey("X-Forwarded-For")))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }
    await next(context);
});

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(WebApp.Client._Imports).Assembly);

app.Run();

public partial class Program { }
