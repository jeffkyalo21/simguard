using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;

namespace SimGuard.Api.Common;

// Checks and settings for running behind a reverse proxy in a container.
// See docs/deployment.md.
public static class HostingChecks
{
    private static readonly string[] PlaceholderMarkers =
    [
        "changeme", "change-me", "change_me", "replace", "placeholder", "example",
        "your-", "your_", "todo", "xxxx", "<", ">"
    ];

    // True for an empty value, a template marker such as "changeme" or
    // "<generate>", or one repeated character.
    public static bool IsPlaceholder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var text = value.Trim();
        return text.Distinct().Count() == 1
            || PlaceholderMarkers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    // Outside Development the API refuses to start with a placeholder (or
    // missing) JWT key or ML shared secret. A missing JWT key is already
    // refused by JwtSettings; this adds the placeholder check and the secret.
    public static void RequireProductionSecrets(IConfiguration configuration, IHostEnvironment environment)
    {
        if (environment.IsDevelopment())
        {
            return;
        }

        var problems = new List<string>();

        if (IsPlaceholder(configuration["Jwt:Key"]))
        {
            problems.Add("Jwt:Key (environment variable Jwt__Key) is missing or a placeholder.");
        }

        if (IsPlaceholder(configuration["MlService:SharedSecret"]))
        {
            problems.Add("MlService:SharedSecret (environment variable MlService__SharedSecret) is missing or a placeholder.");
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                "Refusing to start outside Development: " + string.Join(" ", problems) +
                " Generate each with: openssl rand -hex 32");
        }
    }

    // Trusts X-Forwarded-For from loopback, the addresses in
    // ForwardedHeaders:KnownProxies and the CIDR ranges in
    // ForwardedHeaders:KnownNetworks (for example the private Compose network).
    public static void ConfigureForwardedHeaders(ForwardedHeadersOptions options, IConfiguration configuration)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
        options.ForwardLimit = 1;

        foreach (var proxy in configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
        {
            if (IPAddress.TryParse(proxy, out var address))
            {
                options.KnownProxies.Add(address);
            }
        }

        foreach (var network in configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [])
        {
            if (System.Net.IPNetwork.TryParse(network, out var parsed))
            {
                options.KnownIPNetworks.Add(parsed);
            }
        }
    }

    // With Hosting:DataProtectionKeysPath set, the key ring (it encrypts the
    // stored carrier API keys) lives in that folder, which must be a volume.
    public static void ConfigureDataProtection(WebApplicationBuilder builder)
    {
        var path = builder.Configuration["Hosting:DataProtectionKeysPath"];

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        Directory.CreateDirectory(path);
        builder.Services.AddDataProtection()
            .SetApplicationName("SimGuard")
            .PersistKeysToFileSystem(new DirectoryInfo(path));
    }
}
