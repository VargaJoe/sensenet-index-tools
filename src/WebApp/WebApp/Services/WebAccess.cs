using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using SenseNetIndexTools;

namespace WebApp.Services;

public static class WebAccess
{
    public static void ConfigureForwarding(WebApplicationBuilder builder)
    {
        builder.Services.Configure<ForwardedHeadersOptions>(options => {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownNetworks.Clear(); options.KnownProxies.Clear();
            foreach (var value in (builder.Configuration["WebAccess:TrustedProxies"] ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
                options.KnownProxies.Add(IPAddress.Parse(value));
            // Empty lists trust all proxies in ASP.NET Core; use a non-routable sentinel instead.
            if (options.KnownProxies.Count == 0) options.KnownProxies.Add(IPAddress.None);
        });
    }

    public static void UseAccess(WebApplication app)
    {
        var tokenFile = app.Configuration["WebAccess:TokenFile"] ?? Environment.GetEnvironmentVariable("INDEXTOOLS_WEB_TOKEN_FILE");
        var token = RuntimeSettings.ReadSecret(tokenFile);
        var allowRemote = app.Configuration.GetValue<bool>("AllowRemoteAccess");
        if (allowRemote && token.Length < 32) throw new InvalidOperationException("Remote access requires a web token secret file with at least 32 characters.");
        var protector = app.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("WebAccess.Cookie.v1");
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        app.Use(async (context, next) => {
            if (context.Request.Path == "/healthz") { await next(context); return; }
            var remote = context.Connection.RemoteIpAddress;
            if (!allowRemote && (remote == null || !IPAddress.IsLoopback(remote) || context.Request.Headers.ContainsKey("X-Forwarded-For")))
            { context.Response.StatusCode = 403; return; }
            if (token.Length == 0) { await next(context); return; }
            context.Response.Headers.CacheControl = "no-store";
            if (context.Request.Path == "/login")
            {
                if (HttpMethods.IsPost(context.Request.Method) && context.Request.HasFormContentType)
                {
                    // Reject cross-origin login posts; no credential is placed in a URL.
                    var origin = context.Request.Headers.Origin.ToString();
                    if (origin.Length > 0 && origin != $"{context.Request.Scheme}://{context.Request.Host}") { context.Response.StatusCode = 403; return; }
                    var supplied = (await context.Request.ReadFormAsync())["token"].ToString();
                    if (CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(supplied)), Convert.FromHexString(fingerprint)))
                    {
                        var expires = DateTimeOffset.UtcNow.AddHours(8);
                        context.Response.Cookies.Append("IndexToolsSession", protector.Protect(fingerprint + "|" + expires.ToUnixTimeSeconds()),
                            new CookieOptions { HttpOnly = true, Secure = context.Request.IsHttps, SameSite = SameSiteMode.Strict, Expires = expires, Path = "/" });
                        context.Response.Redirect("/"); return;
                    }
                    context.Response.StatusCode = 401;
                }
                context.Response.ContentType = "text/html";
                await context.Response.WriteAsync("<!doctype html><html><title>Index Tools sign in</title><h1>Index Tools sign in</h1><form method=post><label>Access token <input type=password name=token autocomplete=current-password required></label><button>Sign in</button></form></html>");
                return;
            }
            try
            {
                var parts = protector.Unprotect(context.Request.Cookies["IndexToolsSession"] ?? "").Split('|');
                if (parts.Length == 2 && parts[0] == fingerprint && long.TryParse(parts[1], out var expiry) && expiry > DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                { await next(context); return; }
            }
            catch (CryptographicException) { }
            context.Response.Redirect("/login");
        });
    }
}
