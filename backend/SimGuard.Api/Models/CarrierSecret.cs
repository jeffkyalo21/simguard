namespace SimGuard.Api.Models;

// A carrier's feed API key, protected with ASP.NET Data Protection. Only the
// DataProtectionCarrierSecretStore reads or writes this table; no endpoint
// returns it.
public class CarrierSecret
{
    public int CarrierId { get; set; }
    public string ProtectedValue { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}
