namespace SimGuard.Api.Models;

// One CSV import commit attempt (previews are not recorded). Contract in
// backend/docs/imports.md.
public class ImportRun
{
    public int Id { get; set; }

    // departments, employees, sims or usage.
    public string Type { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    // Hex SHA-256 of the uploaded bytes. Null when the upload could not be read.
    public string? Sha256 { get; set; }

    public int? UploadedByUserId { get; set; }
    public string? UploadedByEmail { get; set; }

    public DateTime StartedAt { get; set; }
    public DateTime FinishedAt { get; set; }

    // committed, rejected or failed.
    public string Status { get; set; } = string.Empty;

    public bool SkipInvalid { get; set; }

    public int TotalRows { get; set; }
    public int ValidRows { get; set; }
    public int InvalidRows { get; set; }
    public int Inserted { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }

    // Why a run was rejected or failed, when the reason is file-wide.
    public string? Message { get; set; }

    // csv for uploads, "carrier feed" for runs of a carrier usage feed.
    public string Source { get; set; } = "csv";

    // Set for carrier feed runs.
    public string? CarrierName { get; set; }

    public AppUser? UploadedBy { get; set; }
}
