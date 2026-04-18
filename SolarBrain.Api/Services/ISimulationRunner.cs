using SolarBrain.Api.Models;
using SolarBrain.Api.Models.Dtos;

namespace SolarBrain.Api.Services;

/// <summary>
/// Manages a live simulation session — the brain, the dataset, and the
/// current read position. Exposes the operations a UI needs: step forward,
/// jump to a season or hour, inject a scenario, get history, reset.
/// </summary>
public interface ISimulationRunner
{
    /// <summary>Load the dataset from a CSV path and initialise the brain.</summary>
    void Load(SimulationConfigDto config, string datasetPath);

    /// <summary>Whether a dataset is loaded and ready to simulate.</summary>
    bool IsLoaded { get; }

    /// <summary>Advance by the currently configured speed (1–20 intervals) and return the last state.</summary>
    SimulationStateDto? NextStep();

    /// <summary>Return the last N states produced by the brain (for the 24h history chart).</summary>
    IReadOnlyList<SimulationStateDto> GetHistory(int lastN);

    /// <summary>Return the cumulative totals snapshot.</summary>
    SimulationSummaryDto GetSummary();

    /// <summary>Inject a named scenario. Some (cloud_cover) are handled here; the rest go to the brain.</summary>
    void InjectScenario(string scenario, double? value = null);

    /// <summary>Set how many rows to advance per NextStep() call (1–20).</summary>
    void SetSpeed(int speed);

    /// <summary>Jump to the first row of the given season and apply the force_season override.</summary>
    void JumpToSeason(string season);

    /// <summary>Jump forward to the next row matching a given hour-of-day.</summary>
    void JumpToHour(int hour);

    /// <summary>Reset brain + position to start.</summary>
    void Reset();
}
