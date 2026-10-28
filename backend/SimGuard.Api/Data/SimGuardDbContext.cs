using Microsoft.EntityFrameworkCore;
using SimGuard.Api.Models;

namespace SimGuard.Api.Data;

public class SimGuardDbContext : DbContext
{
    public SimGuardDbContext(
        DbContextOptions<SimGuardDbContext> options)
        : base(options)
    {
    }

    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Sim> Sims => Set<Sim>();
    public DbSet<Allocation> Allocations => Set<Allocation>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Departments
        modelBuilder.Entity<Department>(entity =>
        {
            entity.ToTable("departments");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasColumnName("id")
                .UseSerialColumn();

            entity.Property(e => e.Name)
                .HasColumnName("name")
                .HasMaxLength(100)
                .IsRequired();

            entity.HasIndex(e => e.Name)
                .IsUnique();
        });

        // Employees
        modelBuilder.Entity<Employee>(entity =>
        {
            entity.ToTable("employees");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasColumnName("id")
                .UseSerialColumn();

            entity.Property(e => e.Name)
                .HasColumnName("name")
                .HasMaxLength(150)
                .IsRequired();

            entity.Property(e => e.Email)
                .HasColumnName("email")
                .HasMaxLength(150)
                .IsRequired();

            entity.Property(e => e.DepartmentId)
                .HasColumnName("department_id");

            entity.Property(e => e.JobRole)
                .HasColumnName("job_role")
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .HasColumnType("timestamp with time zone")
                .HasDefaultValueSql("NOW()");

            entity.HasIndex(e => e.Email)
                .IsUnique();

            entity.HasIndex(e => e.DepartmentId)
                .HasDatabaseName("idx_employees_department");

            entity.HasOne(e => e.Department)
                .WithMany(d => d.Employees)
                .HasForeignKey(e => e.DepartmentId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        // SIMs
        modelBuilder.Entity<Sim>(entity =>
        {
            entity.ToTable("sims", table =>
            {
                table.HasCheckConstraint(
                    "CK_sims_status",
                    "status IN ('available', 'assigned', 'inactive')");
            });

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasColumnName("id")
                .UseSerialColumn();

            entity.Property(e => e.PhoneNumber)
                .HasColumnName("phone_number")
                .HasMaxLength(20)
                .IsRequired();

            entity.Property(e => e.Iccid)
                .HasColumnName("iccid")
                .HasMaxLength(30)
                .IsRequired();

            entity.Property(e => e.Operator)
                .HasColumnName("operator")
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.PlanType)
                .HasColumnName("plan_type")
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.MonthlyCost)
                .HasColumnName("monthly_cost")
                .HasPrecision(10, 2)
                .IsRequired();

            entity.Property(e => e.Status)
                .HasColumnName("status")
                .HasMaxLength(20)
                .HasDefaultValue("available")
                .IsRequired();

            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .HasColumnType("timestamp with time zone")
                .HasDefaultValueSql("NOW()");

            entity.HasIndex(e => e.PhoneNumber)
                .IsUnique();

            entity.HasIndex(e => e.Iccid)
                .IsUnique();

            entity.HasIndex(e => e.Status)
                .HasDatabaseName("idx_sims_status");
        });

        // Allocations
        modelBuilder.Entity<Allocation>(entity =>
        {
            entity.ToTable("allocations");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasColumnName("id")
                .UseSerialColumn();

            entity.Property(e => e.SimId)
                .HasColumnName("sim_id");

            entity.Property(e => e.EmployeeId)
                .HasColumnName("employee_id");

            entity.Property(e => e.AssignedBy)
                .HasColumnName("assigned_by");

            entity.Property(e => e.AssignmentDate)
                .HasColumnName("assignment_date")
                .HasColumnType("timestamp with time zone")
                .HasDefaultValueSql("NOW()");

            entity.Property(e => e.ReturnDate)
                .HasColumnName("return_date")
                .HasColumnType("timestamp with time zone");

            entity.HasIndex(e => e.SimId)
                .HasDatabaseName("idx_allocations_sim");

            entity.HasIndex(e => e.EmployeeId)
                .HasDatabaseName("idx_allocations_employee");

            entity.HasOne(e => e.Sim)
                .WithMany(s => s.Allocations)
                .HasForeignKey(e => e.SimId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(e => e.Employee)
                .WithMany(e => e.Allocations)
                .HasForeignKey(e => e.EmployeeId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(e => e.AssignedByEmployee)
                .WithMany(e => e.AllocationsAssigned)
                .HasForeignKey(e => e.AssignedBy)
                .OnDelete(DeleteBehavior.NoAction);
        });

        // Alerts
        modelBuilder.Entity<Alert>(entity =>
        {
            entity.ToTable("alerts", table =>
            {
                table.HasCheckConstraint(
                    "CK_alerts_risk_level",
                    "risk_level IN ('low', 'medium', 'high', 'critical')");

                table.HasCheckConstraint(
                    "CK_alerts_status",
                    "status IN ('open', 'investigating', 'resolved')");
            });

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasColumnName("id")
                .UseSerialColumn();

            entity.Property(e => e.SimId)
                .HasColumnName("sim_id");

            entity.Property(e => e.AlertType)
                .HasColumnName("alert_type")
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.RiskScore)
                .HasColumnName("risk_score")
                .HasPrecision(5, 2)
                .IsRequired();

            entity.Property(e => e.RiskLevel)
                .HasColumnName("risk_level")
                .HasMaxLength(20)
                .IsRequired();

            entity.Property(e => e.DetectedAt)
                .HasColumnName("detected_at")
                .HasColumnType("timestamp with time zone")
                .HasDefaultValueSql("NOW()");

            entity.Property(e => e.Status)
                .HasColumnName("status")
                .HasMaxLength(20)
                .HasDefaultValue("open")
                .IsRequired();

            entity.HasIndex(e => e.SimId)
                .HasDatabaseName("idx_alerts_sim");

            entity.HasIndex(e => e.Status)
                .HasDatabaseName("idx_alerts_status");

            entity.HasOne(e => e.Sim)
                .WithMany(s => s.Alerts)
                .HasForeignKey(e => e.SimId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        // Audit logs
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("audit_logs");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasColumnName("id")
                .UseSerialColumn();

            entity.Property(e => e.UserId)
                .HasColumnName("user_id");

            entity.Property(e => e.Action)
                .HasColumnName("action")
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(e => e.AffectedSimId)
                .HasColumnName("affected_sim_id");

            entity.Property(e => e.OldValues)
                .HasColumnName("old_values")
                .HasColumnType("jsonb");

            entity.Property(e => e.NewValues)
                .HasColumnName("new_values")
                .HasColumnType("jsonb");

            entity.Property(e => e.Timestamp)
                .HasColumnName("timestamp")
                .HasColumnType("timestamp with time zone")
                .HasDefaultValueSql("NOW()");

            entity.HasIndex(e => e.Timestamp)
                .HasDatabaseName("idx_audit_logs_timestamp");

            entity.HasIndex(e => e.UserId)
                .HasDatabaseName("idx_audit_logs_user");

            entity.HasOne(e => e.User)
                .WithMany(e => e.AuditLogs)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(e => e.AffectedSim)
                .WithMany(s => s.AuditLogs)
                .HasForeignKey(e => e.AffectedSimId)
                .OnDelete(DeleteBehavior.NoAction);
        });
    }
}