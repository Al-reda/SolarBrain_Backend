using SolarBrain.Api.Models;
using SolarBrain.Api.Models.Dtos;

namespace SolarBrain.Api.Services;

/// <summary>
/// The stateful 7-mode decision engine. One instance per active simulation.
/// Fed a row every 15-min tick; returns the full system state and updates
/// its own running totals, decision log, and battery SOC.
/// </summary>
public interface IBrain
{
    /// <summary>Process one dataset row and return the full system state.</summary>
    SimulationStateDto Step(DatasetRow row, int intervalCount, double progressPct, int currentIndex);

    /// <summary>Inject a scenario event (grid outage, cloud cover, etc.).</summary>
    void InjectScenario(string scenario, double? value = null);

    /// <summary>Reset all state back to initial values.</summary>
    void Reset();

    /// <summary>Current mode — for summary endpoint without a full step.</summary>
    string CurrentMode { get; }

    /// <summary>Battery SOC % — for summary endpoint without a full step.</summary>
    double BatterySocPct { get; }

    /// <summary>Running totals — exposed so the runner can build the summary DTO.</summary>
    double TotalCo2SavedKg   { get; }
    double TotalCostSar      { get; }
    double TotalSolarKwh     { get; }
    double TotalGridKwh      { get; }
    double TotalNetMeterSar  { get; }
    int    IntervalCount     { get; }
}
