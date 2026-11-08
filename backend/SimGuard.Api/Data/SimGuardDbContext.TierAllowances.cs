using Microsoft.EntityFrameworkCore;
using SimGuard.Api.Models;

namespace SimGuard.Api.Data;

// Tier allowance table (issue #14): the bundle, voice, SMS and cost of each
// plan tier, used by the billing-cycle usage, the usage analytics and the
// SIM responses.
public partial class SimGuardDbContext
{
    public DbSet<TierAllowance> TierAllowances => Set<TierAllowance>();

    private static void ConfigureTierAllowances(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TierAllowance>(entity =>
        {
            entity.ToTable("tier_allowances", table =>
            {
                table.HasCheckConstraint(
                    "CK_tier_allowances_tier",
                    "tier IN ('field', 'manager', 'professional', 'support')");
                table.HasCheckConstraint(
                    "CK_tier_allowances_values",
                    "data_mb > 0 AND voice_minutes >= 0 AND sms >= 0 AND monthly_cost >= 0");
            });

            entity.HasKey(e => e.Tier);
            entity.Property(e => e.Tier).HasColumnName("tier").HasMaxLength(20);
            entity.Property(e => e.DataMb).HasColumnName("data_mb");
            entity.Property(e => e.VoiceMinutes).HasColumnName("voice_minutes");
            entity.Property(e => e.Sms).HasColumnName("sms");
            entity.Property(e => e.MonthlyCost).HasColumnName("monthly_cost").HasPrecision(10, 2);

            // Data bundles are TIER_BUNDLE_GB in ml-service/src/prophet_forecasting.py.
            // Voice and SMS are the cycle totals in the demo usage (used plus
            // remaining). The costs are demo values: the source data has none.
            entity.HasData(
                new TierAllowance { Tier = "support", DataMb = 20 * 1024, VoiceMinutes = 100, Sms = 70, MonthlyCost = 1500m },
                new TierAllowance { Tier = "professional", DataMb = 40 * 1024, VoiceMinutes = 200, Sms = 135, MonthlyCost = 2500m },
                new TierAllowance { Tier = "manager", DataMb = 60 * 1024, VoiceMinutes = 300, Sms = 205, MonthlyCost = 3500m },
                new TierAllowance { Tier = "field", DataMb = 100 * 1024, VoiceMinutes = 500, Sms = 340, MonthlyCost = 5000m });
        });

        modelBuilder.Entity<Sim>()
            .HasOne(s => s.TierAllowance)
            .WithMany()
            .HasForeignKey(s => s.Tier)
            .HasPrincipalKey(t => t.Tier)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
