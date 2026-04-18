using SolarBrain.Api.Models.Dtos;

namespace SolarBrain.Api.Services;

/// <summary>
/// Tiny in-memory cache for the last-computed system design.
/// Registered as a singleton so the frontend can refresh or reconnect
/// and re-read the latest design without resubmitting the form.
/// </summary>
public interface IDesignStore
{
    SystemDesignDto? Current { get; }
    void SetCurrent(SystemDesignDto design);
}

public class DesignStore : IDesignStore
{
    private readonly object _gate = new();
    private SystemDesignDto? _current;

    public SystemDesignDto? Current
    {
        get { lock (_gate) return _current; }
    }

    public void SetCurrent(SystemDesignDto design)
    {
        lock (_gate) { _current = design; }
    }
}
