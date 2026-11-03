using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.OpenApi;
using SimGuard.Api.Data;
using SimGuard.Api.Models;

namespace SimGuard.Api.Common.Authorization;

public static class SecurityServiceExtensions
{
    public const string AuthRateLimitPolicy = "auth";
    public const string CorsPolicy = "Frontend";
    public const string DefaultAllowedOrigin = "http://localhost:3000";

    public static WebApplicationBuilder AddSimGuardSecurity(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;

        var jwtSettings = JwtSettings.FromConfiguration(configuration, builder.Environment);
        services.AddSingleton(jwtSettings);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<TokenService>();
        services.AddHttpContextAccessor();
        services.AddScoped<AuthEventLogger>();
        services.AddScoped<AuditWriter>();

        AddIdentity(services);
        AddJwtBearer(services, jwtSettings);
        services.AddSimGuardAuthorization();
        AddCors(services, configuration);
        AddRateLimiting(services, configuration);
        AddSwaggerWithBearer(services);

        return builder;
    }

    private static void AddIdentity(IServiceCollection services)
    {
        services
            .AddIdentityCore<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = true;

                // Length over composition rules (NIST SP 800-63B).
                options.Password.RequiredLength = 12;
                options.Password.RequiredUniqueChars = 4;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;

                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<IdentityRole<int>>()
            .AddEntityFrameworkStores<SimGuardDbContext>()
            .AddDefaultTokenProviders();
    }

    private static void AddJwtBearer(IServiceCollection services, JwtSettings jwtSettings)
    {
        var tokens = new TokenService(jwtSettings, TimeProvider.System);

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = tokens.ValidationParameters(jwtSettings.Audience);
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = ValidateAccessTokenUserAsync
                };
            })
            .AddJwtBearer(TokenService.MfaEnrollmentScheme, options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = tokens.ValidationParameters(tokens.EnrollmentAudience);
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = ValidateEnrollmentTokenUserAsync
                };
            });
    }

    // Runs on every request with an access token. A token stops working as
    // soon as the account is disabled or its security stamp changes.
    private static async Task ValidateAccessTokenUserAsync(TokenValidatedContext context)
    {
        var user = await FindUserAsync(context);

        if (user is null
            || user.IsDisabled
            || !user.TwoFactorEnabled
            || context.Principal?.FindFirst(TokenService.SecurityStampClaim)?.Value
                != TokenService.HashSecurityStamp(user.SecurityStamp))
        {
            context.Fail("The token is no longer valid for this account.");
            return;
        }

        if (user.MustChangePassword && context.Principal?.Identity is ClaimsIdentity identity)
        {
            identity.AddClaim(new Claim(AuthPolicies.MustChangePasswordClaim, "true"));
        }
    }

    // The enrolment token does not check the security stamp: starting
    // enrolment resets the authenticator key, which changes the stamp.
    private static async Task ValidateEnrollmentTokenUserAsync(TokenValidatedContext context)
    {
        var user = await FindUserAsync(context);

        if (user is null || user.IsDisabled || user.TwoFactorEnabled)
        {
            context.Fail("The enrolment token is no longer valid for this account.");
        }
    }

    private static async Task<AppUser?> FindUserAsync(TokenValidatedContext context)
    {
        var userId = context.Principal is null ? null : TokenService.GetUserId(context.Principal);

        if (userId is null)
        {
            return null;
        }

        var userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<AppUser>>();
        return await userManager.FindByIdAsync(userId.Value.ToString());
    }

    private static void AddCors(IServiceCollection services, IConfiguration configuration)
    {
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?.Select(o => o.Trim().TrimEnd('/'))
            .Where(o => o.Length > 0)
            .ToArray();

        if (origins is null || origins.Length == 0)
        {
            origins = [DefaultAllowedOrigin];
        }

        foreach (var origin in origins)
        {
            if (origin.Contains('*') || !Uri.TryCreate(origin, UriKind.Absolute, out _))
            {
                throw new InvalidOperationException(
                    $"Cors:AllowedOrigins contains '{origin}'. Use absolute origins such as " +
                    "https://simguard.example.com; wildcards are not allowed with credentials.");
            }
        }

        services.AddCors(options =>
        {
            options.AddPolicy(CorsPolicy, policy => policy
                .WithOrigins(origins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials());
        });
    }

    // Fixed window per client IP on the sign-in endpoints.
    private static void AddRateLimiting(IServiceCollection services, IConfiguration configuration)
    {
        var permitLimit = configuration.GetValue("RateLimiting:Auth:PermitLimit", 10);
        var window = TimeSpan.FromSeconds(configuration.GetValue("RateLimiting:Auth:WindowSeconds", 60));

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(AuthRateLimitPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permitLimit,
                        Window = window,
                        QueueLimit = 0
                    }));
        });
    }

    private static void AddSwaggerWithBearer(IServiceCollection services)
    {
        services.AddSwaggerGen(options =>
        {
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Access token from POST /api/auth/mfa/verify or /api/auth/mfa/enroll/confirm."
            });

            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", document)] = []
            });
        });
    }
}
