using Microsoft.EntityFrameworkCore;
using SimGuard.Api.Common;
using SimGuard.Api.Models;

namespace SimGuard.Api.Imports;

// Columns: iccid, phone_number, carrier, tier, status. Upsert by ICCID.
// Never creates carriers or allocations. See backend/docs/imports.md.
public sealed class SimImportHandler : IImportHandler
{
    // New SIMs need a plan type and a monthly cost, which the file does not
    // carry: the plan type is the tier and the cost is 0 until edited.
    public const decimal NewSimMonthlyCost = 0m;

    public string Type => ImportTypes.Sims;

    public IReadOnlyList<string> Columns { get; } = ["iccid", "phone_number", "carrier", "tier", "status"];

    public IReadOnlyList<string[]> SampleRows { get; } =
    [
        ["8925402000000000017", "+254712000001", "Safaricom", "field", "available"],
        ["8925403000000000025", "+254733000002", "Airtel", "manager", ""]
    ];

    public IImportJob CreateJob(ImportContext context) => new Job(context);

    // Status is null when the cell was empty: keep the current status.
    private sealed record Row(string Iccid, string PhoneNumber, int CarrierId, string Tier, string? Status);

    private sealed record CarrierInfo(int Id, string Name);

    private sealed record Change(Sim Sim, object? Before, bool TierChanged, string? OldTier, bool StatusChanged, string OldStatus);

    private sealed class Job(ImportContext ctx) : IImportJob
    {
        private readonly Dictionary<string, int> _iccidsInFile = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (int Row, string Iccid)> _phonesInFile = new(StringComparer.Ordinal);
        private readonly List<Row> _rows = [];
        private Dictionary<string, CarrierInfo> _carriersByName = [];
        private Dictionary<int, string> _carrierNames = [];
        private Dictionary<string, Sim> _simsByIccid = [];
        private Dictionary<string, string> _iccidByPhone = [];
        private HashSet<int> _allocated = [];

        public async Task PrepareAsync(CancellationToken cancellationToken)
        {
            var carriers = await ctx.Db.Carriers.AsNoTracking()
                .Select(c => new CarrierInfo(c.Id, c.Name))
                .ToListAsync(cancellationToken);

            _carriersByName = carriers
                .GroupBy(c => c.Name.ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            _carrierNames = carriers.ToDictionary(c => c.Id, c => c.Name);

            var sims = await ctx.Db.Sims.ToListAsync(cancellationToken);
            _simsByIccid = sims.ToDictionary(s => s.Iccid, StringComparer.Ordinal);
            _iccidByPhone = sims.ToDictionary(s => s.PhoneNumber, s => s.Iccid, StringComparer.Ordinal);

            _allocated = (await ctx.Db.Allocations.AsNoTracking()
                    .Where(a => a.ReturnDate == null)
                    .Select(a => a.SimId)
                    .ToListAsync(cancellationToken))
                .ToHashSet();
        }

        public bool AddRow(CsvRecord record)
        {
            var v = record.Values;
            var iccid = v[0];
            var phone = v[1];
            var carrierName = v[2];
            var tierValue = v[3];
            var statusValue = v[4];
            var ok = true;

            if (!ImportCells.Iccid(ctx, record, "iccid", iccid))
            {
                ok = false;
            }
            else if (!_iccidsInFile.TryAdd(iccid, record.RowNumber))
            {
                ctx.Errors.Add(record.RowNumber, "iccid", $"ICCID {iccid} is already in row {_iccidsInFile[iccid]} of this file.");
                ok = false;
                iccid = string.Empty;
            }

            if (!ImportCells.E164(ctx, record, "phone_number", phone))
            {
                ok = false;
            }
            else if (_phonesInFile.TryGetValue(phone, out var earlier))
            {
                ctx.Errors.Add(record.RowNumber, "phone_number", $"Phone number {phone} is already in row {earlier.Row} of this file.");
                ok = false;
            }
            else
            {
                _phonesInFile[phone] = (record.RowNumber, iccid);

                if (_iccidByPhone.TryGetValue(phone, out var owner) && owner != iccid)
                {
                    ctx.Errors.Add(record.RowNumber, "phone_number", $"Phone number {phone} belongs to another SIM (ICCID {owner}).");
                    ok = false;
                }
            }

            CarrierInfo? carrier = null;

            if (carrierName.Length == 0)
            {
                ctx.Errors.Add(record.RowNumber, "carrier", "carrier is required.");
                ok = false;
            }
            else if (!_carriersByName.TryGetValue(carrierName.ToLowerInvariant(), out carrier))
            {
                ctx.Errors.Add(record.RowNumber, "carrier",
                    $"There is no carrier named '{carrierName}'. Add it with POST /api/carriers first.");
                ok = false;
            }

            if (!ImportCells.Tier(ctx, record, "tier", tierValue, out var tier))
            {
                ok = false;
            }

            string? status = null;

            if (statusValue.Length > 0)
            {
                status = statusValue.ToLowerInvariant();

                if (status == SimStatuses.Assigned)
                {
                    ctx.Errors.Add(record.RowNumber, "status",
                        "status cannot be 'assigned'. An import never creates allocations; leave status empty or use available or inactive.");
                    ok = false;
                }
                else if (status is not (SimStatuses.Available or SimStatuses.Inactive))
                {
                    ctx.Errors.Add(record.RowNumber, "status", "status must be empty, available or inactive.");
                    ok = false;
                }
                else if (ok && _simsByIccid.TryGetValue(iccid, out var existing)
                         && existing.Status != status && _allocated.Contains(existing.Id))
                {
                    ctx.Errors.Add(record.RowNumber, "status",
                        $"SIM {iccid} has an active allocation. Return it before changing the status.");
                    ok = false;
                }
            }

            if (ok)
            {
                _rows.Add(new Row(iccid, phone, carrier!.Id, tier, status));
            }

            return ok;
        }

        public async Task<ImportApplyResult> ApplyAsync(CancellationToken cancellationToken)
        {
            int inserted = 0, updated = 0, unchanged = 0;
            var now = Timestamps.UtcNow();
            var changes = new List<Change>();

            foreach (var row in _rows)
            {
                if (!_simsByIccid.TryGetValue(row.Iccid, out var sim))
                {
                    ctx.Db.Sims.Add(new Sim
                    {
                        Iccid = row.Iccid,
                        PhoneNumber = row.PhoneNumber,
                        CarrierId = row.CarrierId,
                        PlanType = row.Tier,
                        MonthlyCost = NewSimMonthlyCost,
                        Status = row.Status ?? SimStatuses.Available,
                        Tier = row.Tier,
                        CreatedAt = now
                    });
                    inserted++;
                    continue;
                }

                var newStatus = row.Status ?? sim.Status;
                var detailsChanged = sim.PhoneNumber != row.PhoneNumber || sim.CarrierId != row.CarrierId;
                var tierChanged = sim.Tier != row.Tier;
                var statusChanged = sim.Status != newStatus;

                if (!detailsChanged && !tierChanged && !statusChanged)
                {
                    unchanged++;
                    continue;
                }

                var before = detailsChanged ? Snapshot(sim) : null;
                var oldTier = sim.Tier;
                var oldStatus = sim.Status;

                sim.PhoneNumber = row.PhoneNumber;
                sim.CarrierId = row.CarrierId;
                sim.Tier = row.Tier;
                sim.Status = newStatus;
                updated++;

                changes.Add(new Change(sim, before, tierChanged, oldTier, statusChanged, oldStatus));
            }

            await ctx.Db.SaveChangesAsync(cancellationToken);

            // Per SIM audit entries, in the same transaction, commits only:
            // sim.updated for phone or carrier, sim.tier_changed and
            // sim.status_changed in the same shapes as the SIM endpoints.
            if (ctx.IsCommit && changes.Count > 0)
            {
                foreach (var change in changes)
                {
                    var sim = change.Sim;

                    if (change.Before is not null)
                    {
                        ctx.Audit.Add(AuditActions.SimUpdated, sim.Id, change.Before, Snapshot(sim));
                    }

                    if (change.TierChanged)
                    {
                        ctx.Audit.Add(AuditActions.SimTierChanged, sim.Id, new { tier = change.OldTier }, new { tier = sim.Tier });
                    }

                    if (change.StatusChanged)
                    {
                        ctx.Audit.Add(AuditActions.SimStatusChanged, sim.Id, new { status = change.OldStatus }, new { status = sim.Status });
                    }
                }

                await ctx.Db.SaveChangesAsync(cancellationToken);
            }

            return new ImportApplyResult(inserted, updated, unchanged);
        }

        // Same shape as the SIM endpoints' audit snapshot.
        private object Snapshot(Sim sim) => new
        {
            id = sim.Id,
            phoneNumber = sim.PhoneNumber,
            iccid = sim.Iccid,
            carrierId = sim.CarrierId,
            @operator = _carrierNames.GetValueOrDefault(sim.CarrierId),
            planType = sim.PlanType,
            monthlyCost = sim.MonthlyCost,
            status = sim.Status
        };
    }
}
