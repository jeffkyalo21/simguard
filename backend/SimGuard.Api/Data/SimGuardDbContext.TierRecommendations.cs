using Microsoft.EntityFrameworkCore;
using SimGuard.Api.Models;

namespace SimGuard.Api.Data;

// Tables of the tier recommendations (issue #14).
public partial class SimGuardDbContext
{
    public DbSet<TierRecommendationSettings> TierRecommendationSettings => Set<TierRecommendationSettings>();
    public DbSet<TierRecommendationRun> TierRecommendationRuns => Set<TierRecommendationRun>();
    public DbSet<TierRecommendation> TierRecommendations => Set<TierRecommendation>();

    private static void ConfigureTierRecommendations(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TierRecommendationSettings>(entity =>
        {
            entity.ToTable("tier_recommendation_settings", table =>
            {
                table.HasCheckConstraint("chk_tier_rec_settings_single_row", "id = 1");
                table.HasCheckConstraint("chk_tier_rec_settings_interval", "interval_days BETWEEN 90 AND 365");
            });

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(e => e.IntervalDays).HasColumnName("interval_days");
            entity.Property(e => e.UpdatedByUserId).HasColumnName("updated_by_user_id");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by").HasMaxLength(256);
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");

            entity.HasOne(e => e.UpdatedByUser)
                .WithMany()
                .HasForeignKey(e => e.UpdatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasData(new TierRecommendationSettings { Id = 1, IntervalDays = 90 });
        });

        modelBuilder.Entity<TierRecommendationRun>(entity =>
        {
            entity.ToTable("tier_recommendation_runs", table =>
            {
                table.HasCheckConstraint("chk_tier_rec_runs_trigger", "trigger IN ('manual', 'automatic')");
                table.HasCheckConstraint("chk_tier_rec_runs_status", "status IN ('running', 'succeeded', 'partial', 'failed')");
            });

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").UseSerialColumn();
            entity.Property(e => e.Trigger).HasColumnName("trigger").HasMaxLength(20).IsRequired();
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
            entity.Property(e => e.RequestedByUserId).HasColumnName("requested_by_user_id");
            entity.Property(e => e.RequestedBy).HasColumnName("requested_by").HasMaxLength(256).IsRequired();
            entity.Property(e => e.StartedAt).HasColumnName("started_at").HasColumnType("timestamp with time zone");
            entity.Property(e => e.FinishedAt).HasColumnName("finished_at").HasColumnType("timestamp with time zone");
            entity.Property(e => e.BillingCycle).HasColumnName("billing_cycle");
            entity.Property(e => e.SimsEvaluated).HasColumnName("sims_evaluated");
            entity.Property(e => e.RecommendationsCreated).HasColumnName("recommendations_created");
            entity.Property(e => e.ErrorSummary).HasColumnName("error_summary").HasMaxLength(1000);

            entity.HasIndex(e => e.StartedAt).HasDatabaseName("idx_tier_rec_runs_started_at");

            entity.HasOne(e => e.RequestedByUser)
                .WithMany()
                .HasForeignKey(e => e.RequestedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<TierRecommendation>(entity =>
        {
            entity.ToTable("tier_recommendations", table =>
            {
                table.HasCheckConstraint("chk_tier_recs_status", "status IN ('open', 'dismissed')");
                table.HasCheckConstraint("chk_tier_recs_recommendation", "recommendation IN ('upgrade', 'downgrade')");
                table.HasCheckConstraint("chk_tier_recs_current_tier", "current_tier IN ('field', 'manager', 'professional', 'support')");
                table.HasCheckConstraint("chk_tier_recs_recommended_tier", "recommended_tier IN ('field', 'manager', 'professional', 'support')");
            });

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").UseSerialColumn();
            entity.Property(e => e.SimId).HasColumnName("sim_id");
            entity.Property(e => e.RunId).HasColumnName("run_id");
            entity.Property(e => e.CurrentTier).HasColumnName("current_tier").HasMaxLength(20).IsRequired();
            entity.Property(e => e.RecommendedTier).HasColumnName("recommended_tier").HasMaxLength(20).IsRequired();
            entity.Property(e => e.Recommendation).HasColumnName("recommendation").HasMaxLength(20).IsRequired();
            entity.Property(e => e.Reason).HasColumnName("reason").HasMaxLength(500).IsRequired();
            entity.Property(e => e.BillingCycle).HasColumnName("billing_cycle");
            entity.Property(e => e.ProjectedOverageMb).HasColumnName("projected_overage_mb");
            entity.Property(e => e.DaysToDepletion).HasColumnName("days_to_depletion");
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
            entity.Property(e => e.DismissedAt).HasColumnName("dismissed_at").HasColumnType("timestamp with time zone");
            entity.Property(e => e.DismissedByUserId).HasColumnName("dismissed_by_user_id");

            entity.HasIndex(e => new { e.SimId, e.Status }).HasDatabaseName("idx_tier_recs_sim_status");
            entity.HasIndex(e => e.Status).HasDatabaseName("idx_tier_recs_status");

            // At most one open recommendation per SIM.
            entity.HasIndex(e => e.SimId)
                .HasDatabaseName("idx_tier_recs_one_open_per_sim")
                .IsUnique()
                .HasFilter("status = 'open'");

            entity.HasOne(e => e.Sim)
                .WithMany()
                .HasForeignKey(e => e.SimId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Run)
                .WithMany()
                .HasForeignKey(e => e.RunId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.DismissedByUser)
                .WithMany()
                .HasForeignKey(e => e.DismissedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
