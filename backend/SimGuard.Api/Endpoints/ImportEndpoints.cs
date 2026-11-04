using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SimGuard.Api.Common;
using SimGuard.Api.Common.Authorization;
using SimGuard.Api.Data;
using SimGuard.Api.Dtos;
using SimGuard.Api.Imports;
using SimGuard.Api.Models;
using SimGuard.Api.Scoring;

namespace SimGuard.Api.Endpoints;

// CSV imports in the standard SimGuard format. Contract in
// backend/docs/imports.md. SecurityOfficer only (OrgWrite).
public static class ImportEndpoints
{
    public const string ModePreview = "preview";
    public const string ModeCommit = "commit";

    private const string FormField = "file";
    private const int FileNameMaxLength = 255;
    private const int MessageMaxLength = 500;

    public static IEndpointRouteBuilder MapImportEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/imports")
            .WithTags("Imports")
            .RequireAuthorization(AuthPolicies.OrgWrite);

        group.MapGet("/", GetImportRuns)
            .WithName("GetImportRuns")
            .Produces<PagedResult<ImportRunResponse>>()
            .ProducesValidationProblem();

        group.MapGet("/{type}/template", GetTemplate)
            .WithName("GetImportTemplate")
            .Produces(StatusCodes.Status200OK, contentType: "text/csv")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{type}", ImportFile)
            .WithName("ImportFile")
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<ImportResultResponse>()
            .Produces<ImportResultResponse>(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge);

        return routes;
    }

    private static async Task<IResult> GetImportRuns(
        string? type,
        int? page,
        int? pageSize,
        SimGuardDbContext db,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        var paging = PageRequest.FromQuery(page, pageSize, errors);
        var typeFilter = type?.Trim().ToLowerInvariant();

        if (!string.IsNullOrEmpty(typeFilter) && !ImportHandlers.ByType.ContainsKey(typeFilter))
        {
            errors.Add("type", $"type must be one of: {string.Join(", ", ImportHandlers.ByType.Keys)}.");
        }

        if (errors.HasErrors)
        {
            return errors.ToProblem();
        }

        var query = db.ImportRuns.AsNoTracking();

        if (!string.IsNullOrEmpty(typeFilter))
        {
            query = query.Where(r => r.Type == typeFilter);
        }

        var result = await query
            .OrderByDescending(r => r.StartedAt)
            .ThenByDescending(r => r.Id)
            .Select(ImportRunResponse.Projection)
            .ToPagedResultAsync(paging, cancellationToken);

        return TypedResults.Ok(result);
    }

    private static IResult GetTemplate(string type)
    {
        if (!ImportHandlers.ByType.TryGetValue(type.ToLowerInvariant(), out var handler))
        {
            return UnknownType(type);
        }

        var text = new StringBuilder();
        text.Append(string.Join(',', handler.Columns)).Append("\r\n");

        foreach (var row in handler.SampleRows)
        {
            text.Append(string.Join(',', row.Select(Escape))).Append("\r\n");
        }

        return TypedResults.File(
            Encoding.UTF8.GetBytes(text.ToString()),
            "text/csv",
            $"{handler.Type}-template.csv");
    }

    private static async Task<IResult> ImportFile(
        string type,
        string? mode,
        bool? skipInvalid,
        HttpContext httpContext,
        SimGuardDbContext db,
        AuditWriter audit,
        ScoringImportTrigger scoringTrigger,
        CancellationToken cancellationToken)
    {
        if (!ImportHandlers.ByType.TryGetValue(type.ToLowerInvariant(), out var handler))
        {
            return UnknownType(type);
        }

        var resolvedMode = string.IsNullOrWhiteSpace(mode) ? ModePreview : mode.Trim().ToLowerInvariant();

        if (resolvedMode is not (ModePreview or ModeCommit))
        {
            var errors = new ValidationErrors();
            errors.Add("mode", "mode must be preview or commit.");
            return errors.ToProblem();
        }

        var isCommit = resolvedMode == ModeCommit;
        var skip = skipInvalid ?? false;
        var started = Timestamps.UtcNow();
        var run = new RunInfo(handler.Type, skip, started, httpContext);

        // Read the multipart form by hand: no IFormFile binding, so no
        // antiforgery requirement, and bad bodies become a clear 400.
        IFormFile? file;

        try
        {
            if (!httpContext.Request.HasFormContentType)
            {
                return BadFile("Send the file as multipart/form-data in a field named 'file'.");
            }

            var form = await httpContext.Request.ReadFormAsync(cancellationToken);
            file = form.Files.GetFile(FormField);
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return TooLarge();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or BadHttpRequestException)
        {
            return BadFile("The upload could not be read as multipart/form-data.");
        }

        if (file is null)
        {
            return BadFile("Send the file as multipart/form-data in a field named 'file'.");
        }

        var fileName = Path.GetFileName(file.FileName ?? string.Empty).Trim();
        run.FileName = fileName.Length > FileNameMaxLength ? fileName[..FileNameMaxLength] : fileName;

        if (!fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            return await RejectFileAsync(db, run, isCommit, "Only .csv files can be imported.", cancellationToken);
        }

        if (file.Length == 0)
        {
            return await RejectFileAsync(db, run, isCommit, "The file is empty.", cancellationToken);
        }

        if (file.Length > ImportLimits.MaxFileBytes)
        {
            await RecordFileRejectionAsync(db, run, isCommit, "The file is larger than 25 MB.", cancellationToken);
            return TooLarge();
        }

        await using (var hashStream = file.OpenReadStream())
        {
            run.Sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(hashStream, cancellationToken));
        }

        var context = new ImportContext(db, audit, isCommit);
        var job = handler.CreateJob(context);
        int totalRows;
        int validRows;

        try
        {
            await job.PrepareAsync(cancellationToken);
            (totalRows, validRows) = await ReadRowsAsync(file, handler, job, cancellationToken);
        }
        catch (CsvFormatException ex)
        {
            return await RejectFileAsync(db, run, isCommit, ex.Message, cancellationToken);
        }

        var invalidRows = totalRows - validRows;
        var errorsBlockCommit = isCommit && context.Errors.Count > 0 && !skip;

        if (errorsBlockCommit)
        {
            var rejected = await SaveRunAsync(db, run, ImportRunStatuses.Rejected, totalRows, validRows,
                new ImportApplyResult(0, 0, 0), context,
                $"{context.Errors.Count} row errors. Fix them, or commit with skipInvalid=true to import only the valid rows.",
                cancellationToken);

            return TypedResults.UnprocessableEntity(BuildResult(
                handler.Type, resolvedMode, ImportRunStatuses.Rejected, rejected.Id, run,
                totalRows, validRows, new ImportApplyResult(0, 0, 0), context));
        }

        ImportApplyResult applied;
        int? runId = null;

        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            try
            {
                applied = validRows > 0
                    ? await job.ApplyAsync(cancellationToken)
                    : new ImportApplyResult(0, 0, 0);

                if (isCommit)
                {
                    await db.SaveChangesAsync(cancellationToken);

                    var saved = AddRun(db, run, ImportRunStatuses.Committed, totalRows, validRows, applied, context, null);
                    await db.SaveChangesAsync(cancellationToken);
                    runId = saved.Id;

                    audit.Add(AuditActions.ImportCommitted, null, null, new
                    {
                        importRunId = saved.Id,
                        type = handler.Type,
                        fileName = run.FileName,
                        sha256 = run.Sha256,
                        skipInvalid = skip,
                        totalRows,
                        validRows,
                        invalidRows,
                        inserted = applied.Inserted,
                        updated = applied.Updated,
                        unchanged = applied.Unchanged
                    });
                    await db.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                }
                else
                {
                    // Preview: everything the job wrote is thrown away.
                    await transaction.RollbackAsync(cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                db.ChangeTracker.Clear();

                if (isCommit)
                {
                    await SaveRunAsync(db, run, ImportRunStatuses.Failed, totalRows, validRows,
                        new ImportApplyResult(0, 0, 0), context, "The import failed and nothing was changed.",
                        CancellationToken.None);
                }

                throw;
            }
        }

        db.ChangeTracker.Clear();

        if (isCommit && applied.Coverage is { } coverage && applied.Inserted + applied.Updated > 0)
        {
            await scoringTrigger.AfterUsageImportAsync(coverage, CancellationToken.None);
        }

        return TypedResults.Ok(BuildResult(
            handler.Type,
            resolvedMode,
            isCommit ? ImportRunStatuses.Committed : ModePreview,
            runId,
            run,
            totalRows,
            validRows,
            applied,
            context));
    }

    // Streams the file once. Every data row goes to the job with its values
    // in the handler's column order.
    private static async Task<(int Total, int Valid)> ReadRowsAsync(
        IFormFile file,
        IImportHandler handler,
        IImportJob job,
        CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        var reader = CsvReader.Open(stream);

        var header = await reader.ReadRecordAsync(cancellationToken)
            ?? throw new CsvFormatException("The file has no header row.");

        var map = ImportHeader.Map(header.Values, handler.Columns);
        var total = 0;
        var valid = 0;

        while (await reader.ReadRecordAsync(cancellationToken) is { } record)
        {
            total++;

            if (total > ImportLimits.MaxRows)
            {
                throw new CsvFormatException($"The file has more than {ImportLimits.MaxRows} data rows. Split it into smaller files.");
            }

            if (record.Values.Length != header.Values.Length)
            {
                throw new CsvFormatException(
                    $"Row {record.RowNumber} has {record.Values.Length} values but the header has {header.Values.Length}.");
            }

            var values = new string[map.Length];

            for (var i = 0; i < map.Length; i++)
            {
                values[i] = record.Values[map[i]];
            }

            if (job.AddRow(new CsvRecord(record.RowNumber, values)))
            {
                valid++;
            }
        }

        if (total == 0)
        {
            throw new CsvFormatException("The file has a header but no data rows.");
        }

        return (total, valid);
    }

    private static ImportResultResponse BuildResult(
        string type,
        string mode,
        string status,
        int? runId,
        RunInfo run,
        int totalRows,
        int validRows,
        ImportApplyResult applied,
        ImportContext context) =>
        new(
            type,
            mode,
            status,
            runId,
            run.FileName,
            run.Sha256 ?? string.Empty,
            run.SkipInvalid,
            totalRows,
            validRows,
            totalRows - validRows,
            applied.Inserted,
            applied.Updated,
            applied.Unchanged,
            context.Errors.Count,
            context.Errors.Items,
            context.Warnings.Count,
            context.Warnings.Items,
            applied.Coverage);

    private static ImportRun AddRun(
        SimGuardDbContext db,
        RunInfo run,
        string status,
        int totalRows,
        int validRows,
        ImportApplyResult applied,
        ImportContext? context,
        string? message)
    {
        var row = new ImportRun
        {
            Type = run.Type,
            FileName = run.FileName,
            Sha256 = run.Sha256,
            UploadedByUserId = run.UserId,
            UploadedByEmail = run.Email,
            StartedAt = run.StartedAt,
            FinishedAt = Timestamps.UtcNow(),
            Status = status,
            SkipInvalid = run.SkipInvalid,
            TotalRows = totalRows,
            ValidRows = validRows,
            InvalidRows = totalRows - validRows,
            Inserted = applied.Inserted,
            Updated = applied.Updated,
            Unchanged = applied.Unchanged,
            ErrorCount = context?.Errors.Count ?? 0,
            WarningCount = context?.Warnings.Count ?? 0,
            Message = message is { Length: > MessageMaxLength } ? message[..MessageMaxLength] : message
        };

        db.ImportRuns.Add(row);
        return row;
    }

    private static async Task<ImportRun> SaveRunAsync(
        SimGuardDbContext db,
        RunInfo run,
        string status,
        int totalRows,
        int validRows,
        ImportApplyResult applied,
        ImportContext? context,
        string? message,
        CancellationToken cancellationToken)
    {
        var row = AddRun(db, run, status, totalRows, validRows, applied, context, message);
        await db.SaveChangesAsync(cancellationToken);
        return row;
    }

    // A commit whose file was refused before any row was read is still
    // recorded, as rejected with zero counts.
    private static async Task RecordFileRejectionAsync(
        SimGuardDbContext db,
        RunInfo run,
        bool isCommit,
        string message,
        CancellationToken cancellationToken)
    {
        if (!isCommit)
        {
            return;
        }

        db.ChangeTracker.Clear();
        await SaveRunAsync(db, run, ImportRunStatuses.Rejected, 0, 0, new ImportApplyResult(0, 0, 0), null,
            message, cancellationToken);
    }

    private static async Task<IResult> RejectFileAsync(
        SimGuardDbContext db,
        RunInfo run,
        bool isCommit,
        string message,
        CancellationToken cancellationToken)
    {
        await RecordFileRejectionAsync(db, run, isCommit, message, cancellationToken);
        return BadFile(message);
    }

    private static IResult BadFile(string detail) =>
        TypedResults.Problem(
            title: "Invalid import file",
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest);

    private static IResult TooLarge() =>
        TypedResults.Problem(
            title: "File too large",
            detail: "The file is larger than 25 MB. Split it into smaller files.",
            statusCode: StatusCodes.Status413PayloadTooLarge);

    private static IResult UnknownType(string type) =>
        ApiProblems.NotFound(
            $"There is no import type '{type}'. Use one of: {string.Join(", ", ImportHandlers.ByType.Keys)}.");

    // Template cells are plain text; quote the ones that need it.
    private static string Escape(string value) =>
        value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;

    private sealed class RunInfo(string type, bool skipInvalid, DateTime startedAt, HttpContext httpContext)
    {
        public string Type { get; } = type;
        public bool SkipInvalid { get; } = skipInvalid;
        public DateTime StartedAt { get; } = startedAt;
        public int? UserId { get; } = TokenService.GetUserId(httpContext.User);

        public string? Email { get; } = Truncate(httpContext.User.FindFirst(TokenService.EmailClaim)?.Value, 256);

        public string FileName { get; set; } = string.Empty;
        public string? Sha256 { get; set; }

        private static string? Truncate(string? value, int max) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Length > max ? value[..max] : value.Trim();
    }
}
