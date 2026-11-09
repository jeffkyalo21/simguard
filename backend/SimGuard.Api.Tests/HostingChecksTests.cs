using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using SimGuard.Api.Common;

namespace SimGuard.Api.Tests;

public sealed class HostingChecksTests
{
    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "t";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static IConfiguration Config(params (string, string?)[] pairs) =>
        new ConfigurationBuilder().AddInMemoryCollection(pairs.Select(p => new KeyValuePair<string, string?>(p.Item1, p.Item2))).Build();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("changeme")]
    [InlineData("<generate with openssl rand -hex 32>")]
    [InlineData("your-secret-here-and-more-text-1234")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void Placeholders_are_detected(string? value) => Assert.True(HostingChecks.IsPlaceholder(value));

    [Fact]
    public void A_random_hex_value_is_not_a_placeholder() =>
        Assert.False(HostingChecks.IsPlaceholder("3f9a1c7e5b2d48a0c6e1f7b39d4a2e8c5f0b7d1a3c9e6482b5d0f7a1c3e9b6d2"));

    [Fact]
    public void Production_refuses_a_missing_ml_secret()
    {
        var config = Config(("Jwt:Key", "3f9a1c7e5b2d48a0c6e1f7b39d4a2e8c5f0b7d1a3c9e6482b5d0f7a1c3e9b6d2"));
        var error = Assert.Throws<InvalidOperationException>(() => HostingChecks.RequireProductionSecrets(config, new Env("Production")));
        Assert.Contains("MlService:SharedSecret", error.Message);
        Assert.DoesNotContain("Jwt:Key", error.Message);
    }

    [Fact]
    public void Production_refuses_a_placeholder_jwt_key()
    {
        var config = Config(("Jwt:Key", "changeme"), ("MlService:SharedSecret", "3f9a1c7e5b2d48a0c6e1f7b39d4a2e8c"));
        var error = Assert.Throws<InvalidOperationException>(() => HostingChecks.RequireProductionSecrets(config, new Env("Production")));
        Assert.Contains("Jwt:Key", error.Message);
    }

    [Fact]
    public void Production_accepts_real_secrets_and_development_skips_the_check()
    {
        var good = Config(
            ("Jwt:Key", "3f9a1c7e5b2d48a0c6e1f7b39d4a2e8c5f0b7d1a3c9e6482b5d0f7a1c3e9b6d2"),
            ("MlService:SharedSecret", "8c1e4a7d0b3f6925e8a1d4c7f0b3a69d"));
        HostingChecks.RequireProductionSecrets(good, new Env("Production"));
        HostingChecks.RequireProductionSecrets(Config(), new Env("Development"));
    }

    [Fact]
    public void Known_networks_and_proxies_are_trusted()
    {
        var config = Config(("ForwardedHeaders:KnownNetworks:0", "172.28.0.0/16"), ("ForwardedHeaders:KnownProxies:0", "10.1.2.3"));
        var options = new ForwardedHeadersOptions();
        HostingChecks.ConfigureForwardedHeaders(options, config);
        Assert.Contains(options.KnownProxies, p => p.ToString() == "10.1.2.3");
        Assert.Contains(options.KnownIPNetworks, n => n.ToString() == "172.28.0.0/16");
        Assert.Equal(1, options.ForwardLimit);
    }
}
