namespace SimGuard.Api.Common;

// Mirrors the CK_sims_status check constraint.
public static class SimStatuses
{
    public const string Available = "available";
    public const string Assigned = "assigned";
    public const string Inactive = "inactive";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string> { Available, Assigned, Inactive };
}
