using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace SimGuard.Api.Common.Authorization;

// Bound from the "Jwt" configuration section.
public sealed class JwtSettings
{
    public const string SectionName = "Jwt";
    public const int MinimumKeyBytes = 32;

    public string Issuer { get; init; } = "SimGuard";
    public string Audience { get; init; } = "simguard-api";
    public int AccessTokenMinutes { get; init; } = 60;
    public int MfaTokenMinutes { get; init; } = 5;
    public TimeSpan ClockSkew { get; init; } = TimeSpan.FromSeconds(30);

    public required SymmetricSecurityKey SigningKey { get; init; }

    // True when no key was configured in Development and a random one was
    // generated. Tokens then stop working when the API restarts.
    public bool UsesEphemeralKey { get; init; }

    // Reads and checks the settings. Outside Development a missing key stops
    // the API at startup; a key shorter than 32 bytes is rejected everywhere.
    public static JwtSettings FromConfiguration(IConfiguration configuration, IHostEnvironment environment)
    {
        var section = configuration.GetSection(SectionName);
        var key = section["Key"];
        var ephemeral = false;
        byte[] keyBytes;

        if (string.IsNullOrWhiteSpace(key))
        {
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    "Jwt:Key is not configured. Set it with an environment variable (Jwt__Key) " +
                    $"or a secret store. It must be at least {MinimumKeyBytes} bytes.");
            }

            keyBytes = RandomNumberGenerator.GetBytes(MinimumKeyBytes * 2);
            ephemeral = true;
        }
        else
        {
            keyBytes = Encoding.UTF8.GetBytes(key);

            if (keyBytes.Length < MinimumKeyBytes)
            {
                throw new InvalidOperationException(
                    $"Jwt:Key must be at least {MinimumKeyBytes} bytes.");
            }
        }

        var settings = new JwtSettings
        {
            Issuer = section["Issuer"] is { Length: > 0 } issuer ? issuer : "SimGuard",
            Audience = section["Audience"] is { Length: > 0 } audience ? audience : "simguard-api",
            AccessTokenMinutes = section.GetValue("AccessTokenMinutes", 60),
            MfaTokenMinutes = section.GetValue("MfaTokenMinutes", 5),
            ClockSkew = TimeSpan.FromSeconds(section.GetValue("ClockSkewSeconds", 30)),
            SigningKey = new SymmetricSecurityKey(keyBytes),
            UsesEphemeralKey = ephemeral
        };

        if (settings.AccessTokenMinutes is < 1 or > 24 * 60)
        {
            throw new InvalidOperationException("Jwt:AccessTokenMinutes must be between 1 and 1440.");
        }

        if (settings.MfaTokenMinutes is < 1 or > 15)
        {
            throw new InvalidOperationException("Jwt:MfaTokenMinutes must be between 1 and 15.");
        }

        return settings;
    }
}
