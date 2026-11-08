using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using SimGuard.Api.Common;
using SimGuard.Api.Data;
using SimGuard.Api.Models;

namespace SimGuard.Api.Feed;

// Where carrier feed API keys are kept. Write-only from the API's point of
// view: nothing returns a key to a caller, only the feed client reads it.
// Replace the implementation (for example with AWS SSM Parameter Store) by
// registering another ICarrierSecretStore; nothing else changes.
public interface ICarrierSecretStore
{
    Task SetAsync(int carrierId, string secret, CancellationToken cancellationToken);

    Task<string?> GetAsync(int carrierId, CancellationToken cancellationToken);

    Task DeleteAsync(int carrierId, CancellationToken cancellationToken);
}

// Keys are encrypted with ASP.NET Data Protection and stored in
// carrier_secrets. In production persist and share the key ring (see
// backend/docs/carrier-feed.md), or saved keys cannot be read after a
// restart or on another instance.
public sealed class DataProtectionCarrierSecretStore(
    SimGuardDbContext db,
    IDataProtectionProvider provider) : ICarrierSecretStore
{
    private const string Purpose = "SimGuard.CarrierFeed.ApiKey.v1";

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    public async Task SetAsync(int carrierId, string secret, CancellationToken cancellationToken)
    {
        var protectedValue = _protector.Protect(secret);
        var row = await db.CarrierSecrets.FirstOrDefaultAsync(s => s.CarrierId == carrierId, cancellationToken);

        if (row is null)
        {
            db.CarrierSecrets.Add(new CarrierSecret
            {
                CarrierId = carrierId,
                ProtectedValue = protectedValue,
                UpdatedAt = Timestamps.UtcNow()
            });
        }
        else
        {
            row.ProtectedValue = protectedValue;
            row.UpdatedAt = Timestamps.UtcNow();
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    // Null when no key is saved or the stored value cannot be unprotected
    // (for example the key ring was lost): the caller treats both as "no key".
    public async Task<string?> GetAsync(int carrierId, CancellationToken cancellationToken)
    {
        var value = await db.CarrierSecrets.AsNoTracking()
            .Where(s => s.CarrierId == carrierId)
            .Select(s => s.ProtectedValue)
            .FirstOrDefaultAsync(cancellationToken);

        if (value is null)
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(value);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    public async Task DeleteAsync(int carrierId, CancellationToken cancellationToken)
    {
        await db.CarrierSecrets.Where(s => s.CarrierId == carrierId).ExecuteDeleteAsync(cancellationToken);
    }
}
