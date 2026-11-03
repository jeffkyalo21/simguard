using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SimGuard.Api.Models;

namespace SimGuard.Api.Common.Authorization;

// Issues the three kinds of token the API uses. Each kind has its own
// audience, so an MFA token is never accepted as an access token.
//   access token:          aud = Jwt:Audience,                   60 minutes
//   MFA challenge token:   aud = Jwt:Audience + ":mfa-challenge",  5 minutes
//   MFA enrolment token:   aud = Jwt:Audience + ":mfa-enrollment", 5 minutes
public sealed class TokenService(JwtSettings settings, TimeProvider timeProvider)
{
    public const string MfaEnrollmentScheme = "MfaEnrollment";

    public const string SubjectClaim = JwtRegisteredClaimNames.Sub;
    public const string EmailClaim = JwtRegisteredClaimNames.Email;
    public const string RoleClaim = "role";
    public const string PurposeClaim = "purpose";

    // A hash of the user's Identity security stamp. The stamp changes when the
    // password, MFA setup, role or enabled state changes, which invalidates
    // every token issued before.
    public const string SecurityStampClaim = "sst";

    public const string MfaChallengePurpose = "mfa_challenge";
    public const string MfaEnrollmentPurpose = "mfa_enrollment";

    private readonly JsonWebTokenHandler _handler = new() { MapInboundClaims = false };

    public string ChallengeAudience => settings.Audience + ":mfa-challenge";
    public string EnrollmentAudience => settings.Audience + ":mfa-enrollment";

    public IssuedToken CreateAccessToken(AppUser user, string role)
    {
        var claims = new List<Claim>
        {
            new(EmailClaim, user.Email ?? string.Empty),
            new(RoleClaim, role)
        };

        return Create(user, settings.Audience, TimeSpan.FromMinutes(settings.AccessTokenMinutes), claims);
    }

    public IssuedToken CreateMfaChallengeToken(AppUser user) =>
        Create(
            user,
            ChallengeAudience,
            TimeSpan.FromMinutes(settings.MfaTokenMinutes),
            [new Claim(PurposeClaim, MfaChallengePurpose)]);

    public IssuedToken CreateMfaEnrollmentToken(AppUser user) =>
        Create(
            user,
            EnrollmentAudience,
            TimeSpan.FromMinutes(settings.MfaTokenMinutes),
            [new Claim(PurposeClaim, MfaEnrollmentPurpose)]);

    // Returns the claims of a valid MFA challenge token, or null.
    public async Task<ClaimsIdentity?> ValidateMfaChallengeTokenAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var result = await _handler.ValidateTokenAsync(token, ValidationParameters(ChallengeAudience));

        return result.IsValid
               && result.ClaimsIdentity.HasClaim(PurposeClaim, MfaChallengePurpose)
            ? result.ClaimsIdentity
            : null;
    }

    public TokenValidationParameters ValidationParameters(string audience) =>
        new()
        {
            ValidateIssuer = true,
            ValidIssuer = settings.Issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = settings.SigningKey,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = settings.ClockSkew,
            NameClaimType = SubjectClaim,
            RoleClaimType = RoleClaim
        };

    public static string HashSecurityStamp(string? stamp)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(stamp ?? string.Empty));
        return Convert.ToBase64String(bytes, 0, 16);
    }

    public static int? GetUserId(ClaimsPrincipal principal) =>
        GetUserId(principal.FindFirst(SubjectClaim)?.Value);

    public static int? GetUserId(ClaimsIdentity identity) =>
        GetUserId(identity.FindFirst(SubjectClaim)?.Value);

    private static int? GetUserId(string? value) =>
        int.TryParse(value, out var id) ? id : null;

    private IssuedToken Create(AppUser user, string audience, TimeSpan lifetime, IEnumerable<Claim> extraClaims)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var expires = now.Add(lifetime);

        var claims = new List<Claim>
        {
            new(SubjectClaim, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(SecurityStampClaim, HashSecurityStamp(user.SecurityStamp))
        };
        claims.AddRange(extraClaims);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            Subject = new ClaimsIdentity(claims),
            SigningCredentials = new SigningCredentials(settings.SigningKey, SecurityAlgorithms.HmacSha256)
        };

        return new IssuedToken(_handler.CreateToken(descriptor), expires);
    }
}

public readonly record struct IssuedToken(string Token, DateTime ExpiresAt);
