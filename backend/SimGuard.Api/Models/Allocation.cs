namespace SimGuard.Api.Models;

public class Allocation
{
    public int Id { get; set; }
    public int SimId { get; set; }
    public int EmployeeId { get; set; }
    public int AssignedBy { get; set; }
    public DateTime AssignmentDate { get; set; }
    public DateTime? ReturnDate { get; set; }

    public Sim Sim { get; set; } = null!;
    public Employee Employee { get; set; } = null!;
    public Employee AssignedByEmployee { get; set; } = null!;
}