namespace SimGuard.Api.Models;

public class Employee
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int DepartmentId { get; set; }
    public string JobRole { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public Department Department { get; set; } = null!;

    public ICollection<Allocation> Allocations { get; set; }
        = new List<Allocation>();

    public ICollection<Allocation> AllocationsAssigned { get; set; }
        = new List<Allocation>();

    public ICollection<AuditLog> AuditLogs { get; set; }
        = new List<AuditLog>();
}