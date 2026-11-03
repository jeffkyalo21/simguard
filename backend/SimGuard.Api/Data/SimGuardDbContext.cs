using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SimGuard.Api.Common;
using SimGuard.Api.Models;

namespace SimGuard.Api.Data;

public class SimGuardDbContext : IdentityDbContext<AppUser, IdentityRole<int>, int>
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
    public DbSet<RiskScore> RiskScores => Set<RiskScore>();
    public DbSet<AuthEvent> AuthEvents => Set<AuthEvent>();

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
                    "CK_alerts_type",
                    "type IN ('inactiveBilling', 'sharing', 'reselling', 'ghostUsage', 'other')");

                table.HasCheckConstraint(
                    "CK_alerts_risk_level",
                    "risk_level IN ('high', 'medium', 'low')");

                table.HasCheckConstraint(
                    "CK_alerts_status",
                    "status IN ('flagged', 'escalated', 'cleared')");

                table.HasCheckConstraint(
                    "CK_alerts_risk_score",
                    "risk_score >= 0 AND risk_score <= 1");

                table.HasCheckConstraint(
                    "CK_alerts_resolution",
                    "resolution IS NULL OR resolution IN ('confirmedMisuse', 'falsePositive')");
            });

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasColumnName("id")
                .UseSerialColumn();

            entity.Property(e => e.SimId)
                .HasColumnName("sim_id");

            entity.Property(e => e.Type)
                .HasColumnName("type")
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

            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .HasColumnType("timestamp with time zone")
                .HasDefaultValueSql("NOW()");

            entity.Property(e => e.Status)
                .HasColumnName("status")
                .HasMaxLength(20)
                .HasDefaultValue("flagged")
                .IsRequired();

            entity.Property(e => e.Reasons)
                .HasColumnName("reasons")
                .HasColumnType("jsonb")
                .HasDefaultValueSql("'[]'::jsonb")
                .IsRequired();

            entity.Property(e => e.ModelVersion)
                .HasColumnName("model_version")
                .HasMaxLength(50);

            entity.Property(e => e.ResolvedAt)
                .HasColumnName("resolved_at")
                .HasColumnType("timestamp with time zone");

            entity.Property(e => e.Resolution)
                .HasColumnName("resolution")
                .HasMaxLength(20);

            entity.Property(e => e.ResolutionNote)
                .HasColumnName("resolution_note")
                .HasMaxLength(AlertResolutions.MaxNoteLength);

            entity.Property(e => e.ResolvedByUserId)
                .HasColumnName("resolved_by_user_id");

            entity.HasIndex(e => e.SimId)
                .HasDatabaseName("idx_alerts_sim");

            entity.HasIndex(e => e.Status)
                .HasDatabaseName("idx_alerts_status");

            entity.HasIndex(e => e.CreatedAt)
                .HasDatabaseName("idx_alerts_created_at");

            entity.HasIndex(e => e.ModelVersion)
                .HasDatabaseName("idx_alerts_model_version");

            entity.HasOne(e => e.Sim)
                .WithMany(s => s.Alerts)
                .HasForeignKey(e => e.SimId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(e => e.ResolvedByUser)
                .WithMany()
                .HasForeignKey(e => e.ResolvedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Risk scores
        modelBuilder.Entity<RiskScore>(entity =>
        {
            entity.ToTable("risk_scores", table =>
            {
                table.HasCheckConstraint(
                    "CK_risk_scores_bucket",
                    "bucket IN ('clear', 'escalated', 'flag')");

                table.HasCheckConstraint(
                    "CK_risk_scores_probability",
                    "probability >= 0 AND probability <= 1");
            });

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasColumnName("id")
                .UseSerialColumn();

            entity.Property(e => e.SimId)
                .HasColumnName("sim_id");

            entity.Property(e => e.Date)
                .HasColumnName("date")
                .HasColumnType("date");

            entity.Property(e => e.Probability)
                .HasColumnName("probability")
                .HasPrecision(5, 4)
                .IsRequired();

            entity.Property(e => e.Bucket)
                .HasColumnName("bucket")
                .HasMaxLength(20)
                .IsRequired();

            entity.Property(e => e.Reasons)
                .HasColumnName("reasons")
                .HasColumnType("jsonb")
                .HasDefaultValueSql("'[]'::jsonb")
                .IsRequired();

            entity.Property(e => e.ModelVersion)
                .HasColumnName("model_version")
                .HasMaxLength(50);

            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .HasColumnType("timestamp with time zone")
                .HasDefaultValueSql("NOW()");

            // One score per SIM per day. Date is descending so the same
            // index also serves the latest-first lookups for a SIM.
            entity.HasIndex(e => new { e.SimId, e.Date })
                .IsUnique()
                .IsDescending(false, true)
                .HasDatabaseName("idx_risk_scores_sim_date");

            entity.HasIndex(e => e.Date)
                .HasDatabaseName("idx_risk_scores_date");

            entity.HasIndex(e => e.ModelVersion)
                .HasDatabaseName("idx_risk_scores_model_version");

            entity.HasOne(e => e.Sim)
                .WithMany(s => s.RiskScores)
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

            entity.Property(e => e.ActorUserId)
                .HasColumnName("actor_user_id");

            entity.Property(e => e.ActorEmail)
                .HasColumnName("actor_email")
                .HasMaxLength(256);

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

            entity.HasIndex(e => e.ActorUserId)
                .HasDatabaseName("idx_audit_logs_actor");

            entity.HasIndex(e => e.AffectedSimId)
                .HasDatabaseName("idx_audit_logs_sim");

            entity.HasIndex(e => e.Action)
                .HasDatabaseName("idx_audit_logs_action");

            entity.HasOne(e => e.User)
                .WithMany(e => e.AuditLogs)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(e => e.ActorUser)
                .WithMany()
                .HasForeignKey(e => e.ActorUserId)
                .OnDelete(DeleteBehavior.SetNull);

            // Audit history outlives the SIM it describes.
            entity.HasOne(e => e.AffectedSim)
                .WithMany(s => s.AuditLogs)
                .HasForeignKey(e => e.AffectedSimId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        ConfigureIdentity(modelBuilder);

        // Auth events
        modelBuilder.Entity<AuthEvent>(entity =>
        {
            entity.ToTable("auth_events");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasColumnName("id")
                .UseSerialColumn();

            entity.Property(e => e.UserId)
                .HasColumnName("user_id");

            entity.Property(e => e.EmailAttempted)
                .HasColumnName("email_attempted")
                .HasMaxLength(256);

            entity.Property(e => e.EventType)
                .HasColumnName("event_type")
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.Success)
                .HasColumnName("success");

            entity.Property(e => e.IpAddress)
                .HasColumnName("ip_address")
                .HasMaxLength(64);

            entity.Property(e => e.UserAgent)
                .HasColumnName("user_agent")
                .HasMaxLength(512);

            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .HasColumnType("timestamp with time zone")
                .HasDefaultValueSql("NOW()");

            entity.HasIndex(e => e.CreatedAt)
                .HasDatabaseName("idx_auth_events_created_at");

            entity.HasIndex(e => e.EventType)
                .HasDatabaseName("idx_auth_events_event_type");

            entity.HasIndex(e => e.UserId)
                .HasDatabaseName("idx_auth_events_user");

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.NoAction);
        });
    }

    // ASP.NET Core Identity tables. Renamed to app_* with snake_case columns
    // to match the rest of the schema.
    private static void ConfigureIdentity(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("app_users");

            entity.Property(e => e.Id)
                .UseSerialColumn();

            entity.Property(e => e.Name)
                .HasMaxLength(150)
                .IsRequired();

            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp with time zone")
                .HasDefaultValueSql("NOW()");

            // One account per employee; PostgreSQL allows many NULLs in a unique index.
            entity.HasIndex(e => e.EmployeeId)
                .IsUnique()
                .HasDatabaseName("idx_app_users_employee");

            entity.HasOne(e => e.Employee)
                .WithMany()
                .HasForeignKey(e => e.EmployeeId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<IdentityRole<int>>(entity =>
        {
            entity.ToTable("app_roles");

            entity.Property(e => e.Id)
                .UseSerialColumn();
        });

        modelBuilder.Entity<IdentityUserRole<int>>().ToTable("app_user_roles");
        modelBuilder.Entity<IdentityUserClaim<int>>().ToTable("app_user_claims");
        modelBuilder.Entity<IdentityUserLogin<int>>().ToTable("app_user_logins");
        modelBuilder.Entity<IdentityUserToken<int>>().ToTable("app_user_tokens");
        modelBuilder.Entity<IdentityRoleClaim<int>>().ToTable("app_role_claims");

        var identityTypes = new[]
        {
            typeof(AppUser),
            typeof(IdentityRole<int>),
            typeof(IdentityUserRole<int>),
            typeof(IdentityUserClaim<int>),
            typeof(IdentityUserLogin<int>),
            typeof(IdentityUserToken<int>),
            typeof(IdentityRoleClaim<int>)
        };

        foreach (var clrType in identityTypes)
        {
            var entityType = modelBuilder.Model.FindEntityType(clrType);

            if (entityType is null)
            {
                continue;
            }

            foreach (var property in entityType.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }
        }
    }

    private static string ToSnakeCase(string name)
    {
        var builder = new System.Text.StringBuilder(name.Length + 8);

        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];

            if (char.IsUpper(c) && i > 0)
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }
}