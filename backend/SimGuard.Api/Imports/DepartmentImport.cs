using Microsoft.EntityFrameworkCore;
using SimGuard.Api.Models;

namespace SimGuard.Api.Imports;

// Columns: name. Upsert by name ignoring case: a new name is inserted; a
// name that exists with other casing is updated to the file's casing; the
// same name is unchanged.
public sealed class DepartmentImportHandler : IImportHandler
{
    public const int NameMaxLength = 100;

    public string Type => ImportTypes.Departments;

    public IReadOnlyList<string> Columns { get; } = ["name"];

    public IReadOnlyList<string[]> SampleRows { get; } =
    [
        ["Field Operations"],
        ["Finance"]
    ];

    public IImportJob CreateJob(ImportContext context) => new Job(context);

    private sealed class Job(ImportContext ctx) : IImportJob
    {
        private readonly Dictionary<string, int> _seen = new(StringComparer.Ordinal);
        private readonly List<string> _rows = [];
        private Dictionary<string, Department> _existing = [];

        public async Task PrepareAsync(CancellationToken cancellationToken)
        {
            var departments = await ctx.Db.Departments.ToListAsync(cancellationToken);
            _existing = departments
                .GroupBy(d => d.Name.ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        }

        public bool AddRow(CsvRecord record)
        {
            var name = record.Values[0];

            if (!ImportCells.Required(ctx, record, "name", name, NameMaxLength))
            {
                return false;
            }

            var key = name.ToLowerInvariant();

            if (!_seen.TryAdd(key, record.RowNumber))
            {
                ctx.Errors.Add(record.RowNumber, "name", $"Department '{name}' is already in row {_seen[key]} of this file.");
                return false;
            }

            _rows.Add(name);
            return true;
        }

        public async Task<ImportApplyResult> ApplyAsync(CancellationToken cancellationToken)
        {
            int inserted = 0, updated = 0, unchanged = 0;

            foreach (var name in _rows)
            {
                if (!_existing.TryGetValue(name.ToLowerInvariant(), out var department))
                {
                    ctx.Db.Departments.Add(new Department { Name = name });
                    inserted++;
                }
                else if (department.Name != name)
                {
                    department.Name = name;
                    updated++;
                }
                else
                {
                    unchanged++;
                }
            }

            await ctx.Db.SaveChangesAsync(cancellationToken);
            return new ImportApplyResult(inserted, updated, unchanged);
        }
    }
}
