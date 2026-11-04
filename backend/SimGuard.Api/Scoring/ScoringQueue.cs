using Microsoft.EntityFrameworkCore;
using SimGuard.Api.Common;
using SimGuard.Api.Data;
using SimGuard.Api.Models;

namespace SimGuard.Api.Scoring;

// What a trigger asks for. SimIds null means every SIM; From and To null
// mean no limit on that side.
public sealed record ScoringRequest(
    string Trigger,
    bool Force,
    IReadOnlyCollection<int>? SimIds,
    DateOnly? From,
    DateOnly? To,
    int? UserId,
    string RequestedBy);

public sealed record EnqueueResult(int RunId, bool Merged);

// Only one scoring run executes at a time (ScoringWorker). A trigger that
// arrives while a run is queued is merged into that queued run, so at most
// one run waits behind the running one. The queue lives in scoring_runs
// (status queued), so it survives a restart.
public sealed class ScoringQueue(IServiceScopeFactory scopes)
{
    // Guards "find the queued run or create one" and the worker's
    // "take the queued run".
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _signal = new(0, 1);

    public async Task<EnqueueResult> EnqueueAsync(ScoringRequest request, CancellationToken cancellationToken)
    {
        if (!ScoringTriggers.All.Contains(request.Trigger))
        {
            throw new ArgumentException($"Unknown trigger '{request.Trigger}'.", nameof(request));
        }

        EnqueueResult result;
        await _gate.WaitAsync(cancellationToken);

        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SimGuardDbContext>();

            var queued = await db.ScoringRuns
                .Where(r => r.Status == ScoringRunStatuses.Queued)
                .OrderBy(r => r.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (queued is not null)
            {
                Merge(queued, request);
                await db.SaveChangesAsync(cancellationToken);
                result = new EnqueueResult(queued.Id, true);
            }
            else
            {
                var run = new ScoringRun
                {
                    Trigger = request.Trigger,
                    Status = ScoringRunStatuses.Queued,
                    Force = request.Force,
                    ScopeSimIds = request.SimIds is null ? null : request.SimIds.Distinct().Order().ToArray(),
                    ScopeFrom = request.From,
                    ScopeTo = request.To,
                    RequestedByUserId = request.UserId,
                    RequestedBy = request.RequestedBy,
                    RequestedAt = Timestamps.UtcNow()
                };

                db.ScoringRuns.Add(run);
                await db.SaveChangesAsync(cancellationToken);
                result = new EnqueueResult(run.Id, false);
            }
        }
        finally
        {
            _gate.Release();
        }

        Signal();
        return result;
    }

    // Moves the oldest queued run to running and returns its id, or null.
    public async Task<int?> TakeNextAsync(SimGuardDbContext db, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var next = await db.ScoringRuns
                .Where(r => r.Status == ScoringRunStatuses.Queued)
                .OrderBy(r => r.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (next is null)
            {
                return null;
            }

            next.Status = ScoringRunStatuses.Running;
            next.StartedAt = Timestamps.UtcNow();
            await db.SaveChangesAsync(cancellationToken);
            return next.Id;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Signal()
    {
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already signalled.
        }
    }

    public Task<bool> WaitForWorkAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        _signal.WaitAsync(timeout, cancellationToken);

    // Union of the two scopes; an unlimited side stays unlimited. Force if
    // either asked for it. The trigger shown is the most explicit one
    // (manual, then schedule, then import).
    internal static void Merge(ScoringRun queued, ScoringRequest request)
    {
        queued.ScopeSimIds = queued.ScopeSimIds is null || request.SimIds is null
            ? null
            : queued.ScopeSimIds.Concat(request.SimIds).Distinct().Order().ToArray();

        queued.ScopeFrom = queued.ScopeFrom is null || request.From is null
            ? null
            : queued.ScopeFrom < request.From ? queued.ScopeFrom : request.From;

        queued.ScopeTo = queued.ScopeTo is null || request.To is null
            ? null
            : queued.ScopeTo > request.To ? queued.ScopeTo : request.To;

        queued.Force |= request.Force;
        queued.MergedRequests++;

        if (Rank(request.Trigger) < Rank(queued.Trigger))
        {
            queued.Trigger = request.Trigger;
            queued.RequestedBy = request.RequestedBy;
            queued.RequestedByUserId = request.UserId;
        }
    }

    private static int Rank(string trigger) => trigger switch
    {
        ScoringTriggers.Manual => 0,
        ScoringTriggers.Schedule => 1,
        _ => 2
    };
}

// Scoring runs and tier recommendation runs both read daily usage and call
// the ML service; they never overlap.
public sealed class PipelineLock
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public Task WaitAsync(CancellationToken cancellationToken) => _lock.WaitAsync(cancellationToken);

    public Task<bool> TryWaitAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        _lock.WaitAsync(timeout, cancellationToken);

    public void Release() => _lock.Release();

    public bool IsHeld => _lock.CurrentCount == 0;
}
