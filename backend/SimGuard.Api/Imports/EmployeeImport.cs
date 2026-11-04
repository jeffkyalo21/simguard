using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using SimGuard.Api.Common;
using SimGuard.Api.Models;

namespace SimGuard.Api.Imports;

// Columns: name, email, department, job_role. Upsert by email ignoring
// case. The department must already exist (matched ignoring case).
public sealed class EmployeeImportHandler : IImportHandler
{
    public const int NameMaxLength = 150;
    public const int EmailMaxLength = 150;
    public const int JobRoleMaxLength = 100;

    public string Type => ImportTypes.Employees;

    public IReadOnlyList<string> Columns { get; } = ["name", "email", "department", "job_role"];

    public IReadOnlyList<string[]> SampleRows { get; } =
    [
        ["Amina Odhiambo", "amina.odhiambo@example.com", "Field Operations", "Field Engineer"],
        ["Brian Mwangi", "brian.mwangi@example.com", "Finance", "Accountant"]
    ];

    public IImportJob CreateJob(ImportContext context) => new Job(context);

    private sealed record Row(string Name, string Email, int DepartmentId, string JobRole);

    private sealed class Job(ImportContext ctx) : IImportJob
    {
        private readonly Dictionary<string, int> _seen = new(StringComparer.Ordinal);
        private readonly List<Row> _rows = [];
        private Dictionary<string, int> _departments = [];
        private Dictionary<string, Employee> _existing = [];

        public async Task PrepareAsync(CancellationToken cancellationToken)
        {
            var departments = await ctx.Db.Departments.AsNoTracking()
                .Select(d => new { d.Id, d.Name })
                .ToListAsync(cancellationToken);

            _departments = departments
                .GroupBy(d => d.Name.ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);

            var employees = await ctx.Db.Employees.ToListAsync(cancellationToken);
            _existing = employees
                .GroupBy(e => e.Email.ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        }

        public bool AddRow(CsvRecord record)
        {
            var v = record.Values;
            var name = v[0];
            var email = v[1];
            var department = v[2];
            var jobRole = v[3];

            var ok = ImportCells.Required(ctx, record, "name", name, NameMaxLength);

            if (!ImportCells.Required(ctx, record, "email", email, EmailMaxLength))
            {
                ok = false;
            }
            else if (!IsValidEmail(email))
            {
                ctx.Errors.Add(record.RowNumber, "email", "email must be a valid email address.");
                ok = false;
            }
            else if (!_seen.TryAdd(email.ToLowerInvariant(), record.RowNumber))
            {
                ctx.Errors.Add(record.RowNumber, "email",
                    $"Email '{email}' is already in row {_seen[email.ToLowerInvariant()]} of this file.");
                ok = false;
            }

            var departmentId = 0;

            if (!ImportCells.Required(ctx, record, "department", department, DepartmentImportHandler.NameMaxLength))
            {
                ok = false;
            }
            else if (!_departments.TryGetValue(department.ToLowerInvariant(), out departmentId))
            {
                ctx.Errors.Add(record.RowNumber, "department",
                    $"There is no department named '{department}'. Import or add it first.");
                ok = false;
            }

            if (!ImportCells.Required(ctx, record, "job_role", jobRole, JobRoleMaxLength))
            {
                ok = false;
            }

            if (ok)
            {
                _rows.Add(new Row(name, email, departmentId, jobRole));
            }

            return ok;
        }

        public async Task<ImportApplyResult> ApplyAsync(CancellationToken cancellationToken)
        {
            int inserted = 0, updated = 0, unchanged = 0;
            var now = Timestamps.UtcNow();

            foreach (var row in _rows)
            {
                if (!_existing.TryGetValue(row.Email.ToLowerInvariant(), out var employee))
                {
                    ctx.Db.Employees.Add(new Employee
                    {
                        Name = row.Name,
                        Email = row.Email,
                        DepartmentId = row.DepartmentId,
                        JobRole = row.JobRole,
                        CreatedAt = now
                    });
                    inserted++;
                }
                else if (employee.Name != row.Name || employee.Email != row.Email
                         || employee.DepartmentId != row.DepartmentId || employee.JobRole != row.JobRole)
                {
                    employee.Name = row.Name;
                    employee.Email = row.Email;
                    employee.DepartmentId = row.DepartmentId;
                    employee.JobRole = row.JobRole;
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

        // Same rule as the employee endpoints.
        private static bool IsValidEmail(string email) =>
            !email.Any(char.IsWhiteSpace)
            && MailAddress.TryCreate(email, out var address)
            && address.Address == email;
    }
}
