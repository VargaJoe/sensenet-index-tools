using System.Text.Json;
using System.Runtime.Loader;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using WebApp.Models;
using WebApp.Services;

// Local lab only: use the application's actual protection and storage implementation.
// No production startup hook, endpoint or plaintext configurations.json is needed.
AssemblyLoadContext.Default.Resolving += (context, name) =>
{
    var file = Path.Combine("/app/web", name.Name + ".dll");
    return File.Exists(file) ? context.LoadFromAssemblyPath(file) : null;
};
try
{
    var provider = DataProtectionProvider.Create(new DirectoryInfo("/state/keys"),
        options => options.SetApplicationName("SenseNetIndexTools"));
    var service = new ConfigurationService(NullLogger<ConfigurationService>.Instance, new LabEnvironment(), provider);
    var requested = JsonSerializer.Deserialize<List<ConfigurationOptions>>(File.ReadAllText("/run/secrets/profiles.json"))!;
    foreach (var profile in requested)
    {
        var existing = (await service.GetAllConfigurationsAsync()).SingleOrDefault(p => p.Name == profile.Name);
        if (existing != null && !existing.Tags.Contains("local-multirepo-lab"))
            throw new InvalidOperationException("Refusing to replace a profile not owned by this lab.");
        var result = existing == null
            ? await service.CreateConfigurationAsync(profile)
            : await service.UpdateConfigurationAsync(existing.Id, profile);
        if (!result.Success) throw new InvalidOperationException("Lab profile could not be saved.");
    }
    var persisted = File.ReadAllText("/state/data/configurations.json");
    foreach (var profile in requested)
        if (persisted.Contains(profile.ConnectionString!) || persisted.Contains(profile.ApiKey!))
            throw new InvalidOperationException("Lab credentials were not protected.");
    Console.WriteLine("LAB_PROFILES_SAVED_AND_PROTECTED " + requested.Count);
}
catch (Exception error)
{
    Console.Error.WriteLine("LAB_PROFILE_SETUP_FAILED " + error.GetType().Name);
    if (error is System.IO.FileLoadException load) Console.Error.WriteLine("Assembly: " + load.FileName);
    return 1;
}
return 0;

sealed class LabEnvironment : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "WebApp";
    public string EnvironmentName { get; set; } = "Production";
    public string ContentRootPath { get; set; } = "/app/web";
    public string WebRootPath { get; set; } = "/app/web/wwwroot";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
