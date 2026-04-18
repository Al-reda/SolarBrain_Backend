namespace SolarBrain.Api.Models.Dtos;

/// <summary>
/// Full live state returned by the brain after each 15-min tick.
/// Mirrors the Python brain.step() return dict exactly so the frontend
/// contract stays identical across both implementations.
/// </summary>
public record SimulationStateDto(
    string Timestamp,
    int    Hour,
    string Season,
    int    Interval,

    // Mode + display
    string Mode,
    string ModeColor,
    string ModeDescription,

    // Raw sensor values for this tick
    double PvOutputKw,
    double LoadKw,
    double BatterySocPct,
    bool   GridAvailable,

    // Energy allocation this tick
    double SolarKw,
    double BatteryDischargeKw,
    double GridKw,
    double GeneratorKw,
    double BatteryChargeKw,
    double GridExportKw,

    // Interval KPIs
    double Co2SavedKg,
    double CostSar,
    double NetMeteringRevenueSar,
    double GeneratorFuelCostSar,
    double SolarUtilizationPct,
    double GridDependencyPct,

    // Running totals
    double TotalCo2SavedKg,
    double TotalCostSar,
    double TotalSolarKwh,
    double TotalGridKwh,
    double TotalNetMeterSar,

    // Last ten mode transitions
    List<DecisionLogEntryDto> RecentDecisions,

    // Runner metadata
    double ProgressPct,
    int    CurrentIndex
);

public record DecisionLogEntryDto(
    string Timestamp,
    string FromMode,
    string ToMode,
    string Reason,
    double BatterySoc
);
