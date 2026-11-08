namespace SimGuard.CarrierMock;

public static class Faults
{
    public const string Slow = "slow";
    public const string ServerError = "server_error";
    public const string EmptyDay = "empty_day";
    public const string RateLimit = "rate_limit";

    public static readonly string[] All = [Slow, ServerError, EmptyDay, RateLimit];
}

// The moving parts of the demo: the date served up to and the fault
// switches. Faults are off by default. A fault with a count switches itself
// off after that many requests, so a client that retries recovers.
public sealed class MockState(DateOnly initialDate)
{
    private readonly object _lock = new();
    private readonly Dictionary<string, int?> _faults = new();
    private DateOnly _currentDate = initialDate;
    private int _slowDelayMs = 3000;

    public int SlowDelayMs
    {
        get { lock (_lock) { return _slowDelayMs; } }
    }

    public DateOnly CurrentDate
    {
        get { lock (_lock) { return _currentDate; } }
        set { lock (_lock) { _currentDate = value; } }
    }

    public void SetFault(string fault, bool enabled, int? count, int? delayMs)
    {
        lock (_lock)
        {
            if (!enabled)
            {
                _faults.Remove(fault);
                return;
            }

            _faults[fault] = count is > 0 ? count : null;

            if (fault == Faults.Slow && delayMs is > 0)
            {
                _slowDelayMs = delayMs.Value;
            }
        }
    }

    public void ClearFaults()
    {
        lock (_lock) { _faults.Clear(); }
    }

    // True when the fault is on; uses up one of its requests.
    public bool Trigger(string fault)
    {
        lock (_lock)
        {
            if (!_faults.TryGetValue(fault, out var left))
            {
                return false;
            }

            if (left is not null)
            {
                if (left <= 1)
                {
                    _faults.Remove(fault);
                }
                else
                {
                    _faults[fault] = left - 1;
                }
            }

            return true;
        }
    }

    public object Snapshot()
    {
        lock (_lock)
        {
            return new
            {
                currentDate = _currentDate.ToString("yyyy-MM-dd"),
                faults = _faults.ToDictionary(p => p.Key, p => p.Value),
                slowDelayMs = _slowDelayMs
            };
        }
    }
}
