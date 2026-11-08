using Microsoft.EntityFrameworkCore;
using SimGuard.Api.Models;

namespace SimGuard.Api.Data;

// Tables of the scoring pipeline (issue #14).
public partial class SimGuardDbContext
{
    public DbSet<ScoringRun> ScoringRuns => Set<ScoringRun>();

    private static void ConfigureScoring(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ScoringRun>(entity =>
        {
            entity.ToTable("scoring_runs", table =>
            {
                table.HasCheckConstraint(
                    "chk_scoring_runs_trigger",
                    "trigger IN ('schedule', 'manual', 'import')");

                table.HasCheckConstraint(
                    "chk_scoring_runs_status",
                    "status IN ('queued', 'running', 'succeeded', 'partial', 'failed')");
            });

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").UseSerialColumn();
            entity.Property(e => e.Trigger).HasColumnName("trigger").HasMaxLength(20).IsRequired();
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
            entity.Property(e => e.Force).HasColumnName("force");
            entity.Property(e => e.ScopeSimIds).HasColumnName("scope_sim_ids");
            entity.Property(e => e.ScopeFrom).HasColumnName("scope_from").HasColumnType("date");
            entity.Property(e => e.ScopeTo).HasColumnName("scope_to").HasColumnType("date");
            entity.Property(e => e.MergedRequests).HasColumnName("merged_requests");
            entity.Property(e => e.RequestedByUserId).HasColumnName("requested_by_user_id");
            entity.Property(e => e.RequestedBy).HasColumnName("requested_by").HasMaxLength(256).IsRequired();
            entity.Property(e => e.RequestedAt).HasColumnName("requested_at").HasColumnType("timestamp with time zone");
            entity.Property(e => e.StartedAt).HasColumnName("started_at").HasColumnType("timestamp with time zone");
            entity.Property(e => e.FinishedAt).HasColumnName("finished_at").HasColumnType("timestamp with time zone");
            entity.Property(e => e.FirstDate).HasColumnName("first_date").HasColumnType("date");
            entity.Property(e => e.LastDate).HasColumnName("last_date").HasColumnType("date");
            entity.Property(e => e.DatesScored).HasColumnName("dates_scored");
            entity.Property(e => e.SimDaysScored).HasColumnName("sim_days_scored");
            entity.Property(e => e.SimDaysSkipped).HasColumnName("sim_days_skipped");
            entity.Property(e => e.Failures).HasColumnName("failures");
            entity.Property(e => e.AlertsCreated).HasColumnName("alerts_created");
            entity.Property(e => e.AlertsUpdated).HasColumnName("alerts_updated");
            entity.Property(e => e.ErrorSummary).HasColumnName("error_summary").HasMaxLength(1000);
            entity.Property(e => e.ModelVersion).HasColumnName("model_version").HasMaxLength(50);

            entity.HasIndex(e => e.RequestedAt).HasDatabaseName("idx_scoring_runs_requested_at");
            entity.HasIndex(e => e.Status).HasDatabaseName("idx_scoring_runs_status");

            entity.HasOne(e => e.RequestedByUser)
                .WithMany()
                .HasForeignKey(e => e.RequestedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
