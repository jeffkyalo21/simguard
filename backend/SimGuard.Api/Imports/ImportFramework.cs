using System.Globalization;
using SimGuard.Api.Common;
using SimGuard.Api.Data;

namespace SimGuard.Api.Imports;

public static class ImportTypes
{
    public const string Departments = "departments";
    public const string Employees = "employees";
    public const string Sims = "sims";
    public const string Usage = "usage";
}

public static class ImportRunStatuses
{
    public const string Committed = "committed";
    public const string Rejected = "rejected";
    public const string Failed = "failed";
}

public static class ImportLimits
{
    public const long MaxFileBytes = 25L * 1024 * 1024;
    public const int MaxRows = 250_000;
    public const int MaxListedIssues = 200;
}

// A row error or warning. Row is the CSV row (header = 1); Column is the
// header name. Both are null for a file-wide warning.
public sealed record ImportIssue(int? Row, string? Column, string Message);

// Keeps the first 200 issues and counts all of them.
public sealed class ImportIssues
{
    private readonly List<ImportIssue> _items = [];

    public int Count { get; private set; }

    public IReadOnlyList<ImportIssue> Items => _items;

    public void Add(int? row, string? column, string message)
    {
        Count++;

        if (_items.Count < ImportLimits.MaxListedIssues)
        {
            _items.Add(new ImportIssue(row, column, message));
        }
    }
}

public sealed class ImportContext(
    SimGuardDbContext db,
    AuditWriter audit,
    bool isCommit)
{
    public SimGuardDbContext Db { get; } = db;
    public AuditWriter Audit { get; } = audit;

    // False for a preview: the job still writes, inside a transaction that
    // is rolled back, so preview counts are exactly what a commit applies.
    // Jobs write per-row audit entries only when this is true.
    public bool IsCommit { get; } = isCommit;

    public ImportIssues Errors { get; } = new();
    public ImportIssues Warnings { get; } = new();
}

public sealed record SimCoverage(
    int SimId,
    string Iccid,
    DateOnly FirstDate,
    DateOnly LastDate,
    int Days,
    int GapCount,
    int MissingDays,
    int LatestRunDays,
    bool Scorable);

// Usage only. Scorable: the SIM's latest run of consecutive days is at least
// MinimumDays long (the ML lookback). NotEnoughHistory: shorter than that.
// Scorable + NotEnoughHistory = Sims. WithGaps counts SIMs with at least one
// missing day between their first and last date, whatever their latest run.
public sealed record ImportCoverage(
    int Sims,
    DateOnly? FirstDate,
    DateOnly? LastDate,
    int MinimumDays,
    int Scorable,
    int NotEnoughHistory,
    int WithGaps,
    IReadOnlyList<SimCoverage> Details);

public sealed record ImportApplyResult(
    int Inserted,
    int Updated,
    int Unchanged,
    ImportCoverage? Coverage = null);

// One import run of one type. The framework calls PrepareAsync once, then
// AddRow for every data row (values in Columns order), then, when there is
// something to write, ApplyAsync inside an open transaction.
public interface IImportJob
{
    Task PrepareAsync(CancellationToken cancellationToken);

    // Validates the row and keeps it when valid. Returns false and adds
    // errors to the context when it is not.
    bool AddRow(CsvRecord record);

    Task<ImportApplyResult> ApplyAsync(CancellationToken cancellationToken);
}

public interface IImportHandler
{
    string Type { get; }

    // Header names in template order.
    IReadOnlyList<string> Columns { get; }

    // Two example rows for the template.
    IReadOnlyList<string[]> SampleRows { get; }

    IImportJob CreateJob(ImportContext context);
}

// Matches a header to the handler's columns: case, spaces and the order of
// columns do not matter, but the set must be exact.
public static class ImportHeader
{
    public static string Normalize(string name) =>
        new string(name.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToLowerInvariant();

    // Returns, for each handler column, the index of that column in the file.
    public static int[] Map(string[] header, IReadOnlyList<string> columns)
    {
        var positions = new Dictionary<string, int>();
        var duplicates = new List<string>();
        var unknown = new List<string>();
        var expected = columns.ToHashSet();

        for (var i = 0; i < header.Length; i++)
        {
            var name = Normalize(header[i]);

            if (name.Length == 0)
            {
                unknown.Add($"(empty, column {i + 1})");
            }
            else if (!expected.Contains(name))
            {
                unknown.Add(header[i]);
            }
            else if (!positions.TryAdd(name, i))
            {
                duplicates.Add(header[i]);
            }
        }

        var missing = columns.Where(c => !positions.ContainsKey(c)).ToList();

        if (missing.Count > 0 || unknown.Count > 0 || duplicates.Count > 0)
        {
            var parts = new List<string>();

            if (missing.Count > 0)
            {
                parts.Add($"missing columns: {string.Join(", ", missing)}");
            }

            if (unknown.Count > 0)
            {
                parts.Add($"unknown columns: {string.Join(", ", unknown)}");
            }

            if (duplicates.Count > 0)
            {
                parts.Add($"repeated columns: {string.Join(", ", duplicates)}");
            }

            throw new CsvFormatException(
                $"The header does not match the template ({string.Join("; ", parts)}). Expected: {string.Join(",", columns)}.");
        }

        return columns.Select(c => positions[c]).ToArray();
    }
}

// Cell parsers. Each returns false and adds a row error when the value is
// not valid. Cells are already trimmed.
public static class ImportCells
{
    public static bool Required(ImportContext ctx, CsvRecord row, string column, string value, int maxLength)
    {
        if (value.Length == 0)
        {
            ctx.Errors.Add(row.RowNumber, column, $"{column} is required.");
            return false;
        }

        if (value.Length > maxLength)
        {
            ctx.Errors.Add(row.RowNumber, column, $"{column} must be at most {maxLength} characters.");
            return false;
        }

        return true;
    }

    // ISO yyyy-mm-dd, a calendar (UTC) date.
    public static bool Date(ImportContext ctx, CsvRecord row, string column, string value, out DateOnly date)
    {
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return true;
        }

        ctx.Errors.Add(row.RowNumber, column, $"{column} must be a date in the form yyyy-mm-dd.");
        return false;
    }

    public static bool Number(
        ImportContext ctx, CsvRecord row, string column, string value, bool allowNegative, out double number)
    {
        if (value.Length == 0)
        {
            number = 0;
            ctx.Errors.Add(row.RowNumber, column, $"{column} is required.");
            return false;
        }

        if (!double.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
                CultureInfo.InvariantCulture, out number) || !double.IsFinite(number))
        {
            ctx.Errors.Add(row.RowNumber, column, $"{column} must be a number (use a dot for decimals).");
            return false;
        }

        if (!allowNegative && number < 0)
        {
            ctx.Errors.Add(row.RowNumber, column, $"{column} must not be negative.");
            return false;
        }

        return true;
    }

    public static bool Integer(
        ImportContext ctx, CsvRecord row, string column, string value, bool allowNegative, out long number)
    {
        if (value.Length == 0)
        {
            number = 0;
            ctx.Errors.Add(row.RowNumber, column, $"{column} is required.");
            return false;
        }

        if (!long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out number))
        {
            ctx.Errors.Add(row.RowNumber, column, $"{column} must be a whole number.");
            return false;
        }

        if (!allowNegative && number < 0)
        {
            ctx.Errors.Add(row.RowNumber, column, $"{column} must not be negative.");
            return false;
        }

        return true;
    }

    public static bool Tier(ImportContext ctx, CsvRecord row, string column, string value, out string tier)
    {
        tier = value.ToLowerInvariant();

        if (SimTiers.All.Contains(tier))
        {
            return true;
        }

        ctx.Errors.Add(row.RowNumber, column, $"{column} must be one of: {string.Join(", ", SimTiers.All)}.");
        return false;
    }

    // 18 to 22 digits.
    public static bool Iccid(ImportContext ctx, CsvRecord row, string column, string value)
    {
        if (value.Length is >= 18 and <= 22 && value.All(char.IsAsciiDigit))
        {
            return true;
        }

        ctx.Errors.Add(row.RowNumber, column, $"{column} must be 18 to 22 digits.");
        return false;
    }

    // E.164 only (+ and 8 to 15 digits; Kenyan numbers +2547/+2541 and 8 digits).
    public static bool E164(ImportContext ctx, CsvRecord row, string column, string value)
    {
        if (value.StartsWith('+') && PhoneNumbers.Normalize(value) == value)
        {
            return true;
        }

        ctx.Errors.Add(row.RowNumber, column, $"{column} must be in E.164 form, for example +254712345678.");
        return false;
    }
}
