using System.Linq.Expressions;
using SimGuard.Api.Imports;
using SimGuard.Api.Models;

namespace SimGuard.Api.Dtos;

// Response of POST /api/imports/{type}. Status is preview, committed or
// rejected. For a preview the counts are what a commit of the same file
// would apply now; for a commit they are what was applied.
public record ImportResultResponse(
    string Type,
    string Mode,
    string Status,
    int? ImportRunId,
    string FileName,
    string Sha256,
    bool SkipInvalid,
    int TotalRows,
    int ValidRows,
    int InvalidRows,
    int Inserted,
    int Updated,
    int Unchanged,
    int ErrorCount,
    IReadOnlyList<ImportIssue> Errors,
    int WarningCount,
    IReadOnlyList<ImportIssue> Warnings,
    ImportCoverage? Coverage);

// An item of GET /api/imports.
public record ImportRunResponse(
    int Id,
    string Type,
    string FileName,
    string? Sha256,
    int? UploadedByUserId,
    string? UploadedByEmail,
    DateTime StartedAt,
    DateTime FinishedAt,
    string Status,
    bool SkipInvalid,
    int TotalRows,
    int ValidRows,
    int InvalidRows,
    int Inserted,
    int Updated,
    int Unchanged,
    int ErrorCount,
    int WarningCount,
    string? Message,
    string Source = "csv",
    string? CarrierName = null)
{
    public static readonly Expression<Func<ImportRun, ImportRunResponse>> Projection =
        r => new ImportRunResponse(
            r.Id, r.Type, r.FileName, r.Sha256, r.UploadedByUserId, r.UploadedByEmail,
            r.StartedAt, r.FinishedAt, r.Status, r.SkipInvalid,
            r.TotalRows, r.ValidRows, r.InvalidRows, r.Inserted, r.Updated, r.Unchanged,
            r.ErrorCount, r.WarningCount, r.Message, r.Source, r.CarrierName);
}
