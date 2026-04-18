namespace SolarBrain.Api.Models.Dtos;

/// <summary>Full Layer 1 response. Mirrors the FastAPI system_design dict.</summary>
public record SystemDesignDto(
    ProfileSummaryDto          Profile,
    SizingRequirementsDto      Requirements,
    List<RankedPanelDto>       Panels,
    List<RankedInverterDto>    Inverters,
    List<RankedBatteryDto>     Batteries,
    ProtectionSummaryDto       ProtectionItems,
    BosDto                     Bos,
    GeneratorSpecDto?          Generator,
    FinancialModelDto          Financials,
    CapexBreakdownDto          CapexBreakdown,
    SimulationConfigDto        SimulationConfig
);

/// <summary>Echoes and enriches the input profile with derived values.</summary>
public record ProfileSummaryDto(
    string UserType,
    string Region,
    string RegionName,
    string GridScenario,
    double MonthlyBillSar,
    double PeakLoadKw,
    double DailyLoadKwh,
    int    Tier,
    double Ghi,
    double CriticalLoadPct
);

/// <summary>Raw sizing requirements — input to component ranking.</summary>
public record SizingRequirementsDto(
    double PvKwpRequired,
    double BatteryKwhRequired,
    double InverterKwRequired,
    double AutonomyHours,
    double PerformanceRatio,
    double SafetyFactor
);
