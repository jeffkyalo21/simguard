namespace SimGuard.Api.Common;

// ML plan tiers. Mirrors the CK_sims_tier check constraint, TIER_VALUES in
// ml-service/src/feature_engineering.py, the cohort keys in
// ml-service/artifacts/cohort_stats.json and ml-service/contracts.
public static class SimTiers
{
    public const string Field = "field";
    public const string Manager = "manager";
    public const string Professional = "professional";
    public const string Support = "support";

    public static readonly IReadOnlyList<string> All = [Field, Manager, Professional, Support];
}
