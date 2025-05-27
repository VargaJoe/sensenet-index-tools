using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SenseNet.IndexTools.Core.Services;
using SenseNet.IndexTools.Core.Models;

var builder = WebApplication.CreateBuilder(args);

// Add user settings if they exist
string userSettingsPath = Path.Combine(builder.Environment.ContentRootPath, "usersettings.json");
if (File.Exists(userSettingsPath))
{
    builder.Configuration.AddJsonFile(userSettingsPath, optional: true, reloadOnChange: true);
}

// Add services to the container.
builder.Services.AddRazorPages();

// Configure report storage
builder.Services.Configure<ReportStorageOptions>(options =>
{
    options.ReportStorageDirectory = builder.Configuration.GetValue<string>("ReportStorage:Directory") ?? "Reports";
});

// Register core services
builder.Services.AddScoped<LastActivityIdService>();
builder.Services.AddScoped<ValidationService>();
builder.Services.AddScoped<SubtreeCheckerService>();
builder.Services.AddScoped<IndexListerService>();
builder.Services.AddScoped<DatabaseListerService>();
builder.Services.AddSingleton<ReportStorageService>();

// Configure SettingsService with IConfiguration
builder.Services.AddSingleton(sp => new SettingsService(
    sp.GetRequiredService<ILogger<SettingsService>>(),
    builder.Environment.ContentRootPath,
    sp.GetRequiredService<IOptionsMonitor<AppSettings>>(),
    sp.GetRequiredService<IConfiguration>()
));

// Configure application settings
builder.Services.Configure<AppSettings>(builder.Configuration.GetSection("AppSettings"));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseRouting();

app.UseAuthorization();

app.MapRazorPages();

app.Run();
