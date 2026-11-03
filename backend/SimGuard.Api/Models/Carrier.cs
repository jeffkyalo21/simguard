namespace SimGuard.Api.Models;

// A mobile network operator. Names are unique ignoring case (enforced by a
// unique index on lower(name)).
public class Carrier
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public ICollection<Sim> Sims { get; set; }
        = new List<Sim>();
}
