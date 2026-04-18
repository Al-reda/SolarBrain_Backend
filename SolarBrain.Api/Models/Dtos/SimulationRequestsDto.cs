using System.ComponentModel.DataAnnotations;

namespace SolarBrain.Api.Models.Dtos;

/// <summary>Scenario injection payload.</summary>
public class ScenarioRequestDto
{
    /// <summary>
    /// One of:
    ///   grid_outage, grid_restore,
    ///   season_summer, season_moderate, season_winter, season_reset,
    ///   load_spike, load_restore,
    ///   cloud_cover, cloud_restore,
    ///   low_battery, low_battery_restore
    /// </summary>
    [Required] public string Scenario { get; set; } = "";

    /// <summary>Optional numeric parameter (e.g. load spike size in kW).</summary>
    public double? Value { get; set; }
}

public class SpeedRequestDto
{
    [Range(1, 20)]
    public int Speed { get; set; } = 1;
}

/// <summary>Cumulative summary totals for the running simulation.</summary>
public record SimulationSummaryDto(
    int    IntervalsRun,
    double HoursSimulated,
    double TotalCo2SavedKg,
    double TotalCostSar,
    double TotalSolarKwh,
    double TotalGridKwh,
    double TotalNetMeterSar,
    string CurrentMode,
    double BatterySocPct,
    double SolarFractionPct
);
