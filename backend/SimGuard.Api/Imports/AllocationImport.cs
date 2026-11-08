using Microsoft.EntityFrameworkCore;
using SimGuard.Api.Common;
using SimGuard.Api.Models;

namespace SimGuard.Api.Imports;

// Columns: iccid, employee_email, start_date (optional). Creates active
// allocations with the same rules as POST /api/allocations: the SIM must be
// available and has at most one active allocation, and its status becomes
// assigned. See backend/docs/imports.md.
public sealed class AllocationImportHandler : IImportHandler
{
    public const int EmailMaxLength = 150;

    public string Type => ImportTypes.Allocations;

    public IReadOnlyList<string> Columns { get; } = ["iccid", "employee_email", "start_date"];

    public IReadOnlyList<string[]> SampleRows { get; } =
    [
        ["8925402000000000017", "amina.odhiambo@example.com", "2026-01-15"],
        ["8925403000000000025", "brian.mwangi@example.com", ""]
    ];

    public IImportJob CreateJob(ImportContext context) => new Job(context);

    private sealed record Row(Sim Sim, int EmployeeId, DateTime? StartDate);

    private sealed class Job(ImportContext ctx) : IImportJob
    {
        private readonly Dictionary<string, int> _iccidsInFile = new(StringComparer.Ordinal);
        private readonly List<Row> _rows = [];
        private int _unchanged;
        private Dictionary<string, Sim> _sims = [];
        private Dictionary<string, int> _employees = [];
        private Dictionary<int, int> _activeBySim = [];
        private int? _callerEmployeeId;

        public async Task PrepareAsync(CancellationToken cancellationToken)
        {
            _sims = (await ctx.Db.Sims.ToListAsync(cancellationToken))
                .ToDictionary(s => s.Iccid, StringComparer.Ordinal);

            var employees = await ctx.Db.Employees.AsNoTracking()
                .Select(e => new { e.Id, e.Email })
                .ToListAsync(cancellationToken);
            _employees = employees
                .GroupBy(e => e.Email.ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);

            _activeBySim = (await ctx.Db.Allocations.AsNoTracking()
                    .Where(a => a.ReturnDate == null)
                    .Select(a => new { a.SimId, a.EmployeeId })
                    .ToListAsync(cancellationToken))
                .GroupBy(a => a.SimId)
                .ToDictionary(g => g.Key, g => g.First().EmployeeId);

            if (ctx.UserId is { } userId)
            {
                _callerEmployeeId = await ctx.Db.Users.AsNoTracking()
                    .Where(u => u.Id == userId)
                    .Select(u => u.EmployeeId)
                    .FirstOrDefaultAsync(cancellationToken);
            }
        }

        public bool AddRow(CsvRecord record)
        {
            var v = record.Values;
            var iccid = v[0];
            var email = v[1];
            var startValue = v[2];
            var ok = true;
            Sim? sim = null;
            var employeeId = 0;

            if (!ImportCells.Iccid(ctx, record, "iccid", iccid))
            {
                ok = false;
            }
            else if (!_iccidsInFile.TryAdd(iccid, record.RowNumber))
            {
                ctx.Errors.Add(record.RowNumber, "iccid",
                    $"ICCID {iccid} is already in row {_iccidsInFile[iccid]} of this file.");
                ok = false;
            }
            else if (!_sims.TryGetValue(iccid, out sim))
            {
                ctx.Errors.Add(record.RowNumber, "iccid", $"There is no SIM with ICCID {iccid}. Import SIMs first.");
                ok = false;
            }

            if (!ImportCells.Required(ctx, record, "employee_email", email, EmailMaxLength))
            {
                ok = false;
            }
            else if (!_employees.TryGetValue(email.ToLowerInvariant(), out employeeId))
            {
                ctx.Errors.Add(record.RowNumber, "employee_email",
                    $"There is no employee with email '{email}'. Import employees first.");
                ok = false;
            }

            DateTime? start = null;

            if (startValue.Length > 0)
            {
                if (ImportCells.Date(ctx, record, "start_date", startValue, out var date))
                {
                    start = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                }
                else
                {
                    ok = false;
                }
            }

            if (!ok || sim is null)
            {
                return false;
            }

            if (_activeBySim.TryGetValue(sim.Id, out var currentEmployee))
            {
                if (currentEmployee == employeeId)
                {
                    // Already allocated to this employee: nothing to do.
                    _unchanged++;
                    return true;
                }

                ctx.Errors.Add(record.RowNumber, "employee_email",
                    $"SIM {iccid} already has an active allocation to another employee. Return it first.");
                return false;
            }

            if (sim.Status != SimStatuses.Available)
            {
                ctx.Errors.Add(record.RowNumber, "iccid",
                    $"SIM {iccid} is '{sim.Status}' and cannot be assigned. Only available SIMs can be assigned.");
                return false;
            }

            _rows.Add(new Row(sim, employeeId, start));
            return true;
        }

        public async Task<ImportApplyResult> ApplyAsync(CancellationToken cancellationToken)
        {
            var now = Timestamps.UtcNow();
            var created = new List<(Allocation Allocation, string OldStatus)>();

            foreach (var row in _rows)
            {
                var allocation = new Allocation
                {
                    SimId = row.Sim.Id,
                    EmployeeId = row.EmployeeId,
                    AssignedBy = _callerEmployeeId ?? row.EmployeeId,
                    AssignmentDate = row.StartDate ?? now
                };

                ctx.Db.Allocations.Add(allocation);
                created.Add((allocation, row.Sim.Status));
                row.Sim.Status = SimStatuses.Assigned;
            }

            await ctx.Db.SaveChangesAsync(cancellationToken);

            if (ctx.IsCommit && created.Count > 0)
            {
                foreach (var (allocation, oldStatus) in created)
                {
                    ctx.Audit.Add(
                        AuditActions.AllocationCreated,
                        allocation.SimId,
                        new { simStatus = oldStatus },
                        new
                        {
                            allocationId = allocation.Id,
                            employeeId = allocation.EmployeeId,
                            assignedBy = allocation.AssignedBy,
                            assignmentDate = allocation.AssignmentDate,
                            simStatus = SimStatuses.Assigned
                        });
                }

                await ctx.Db.SaveChangesAsync(cancellationToken);
            }

            return new ImportApplyResult(created.Count, 0, _unchanged);
        }
    }
}
