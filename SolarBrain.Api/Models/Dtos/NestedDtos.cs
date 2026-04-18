namespace SolarBrain.Api.Models.Dtos;

/// <summary>One line item in the protection / BoS breakdown.</summary>
public record ProtectionLineDto(
    string Key,            // "dc_mcb", "ac_mccb", etc.
    string Description,
    string Spec,
    int    Quantity,
    double UnitPriceSar,
    double TotalPriceSar
);

public record ProtectionSummaryDto(
    List<ProtectionLineDto> Items,
    double TotalSar
);

public record BosDto(
    ProtectionLineDto MountingStructure,
    ProtectionLineDto DcCable,
    ProtectionLineDto AcCable,
    double TotalSar
);

public record GeneratorSpecDto(
    double Kva,
    double KwOutput,
    double FuelConsumptionLph,
    double EstimatedAnnualCostSar
);

public record CapexBreakdownDto(
    double PanelsSar,
    double InverterSar,
    double BatterySar,
    double ProtectionSar,
    double BosSar,
    double TotalSar
);

public record FinancialYearDto(
    int    Year,
    double ProductionKwh,
    double GridSavingsSar,
    double ExportRevenueSar,
    double DieselSavingsSar,
    double TotalSavingsSar,
    double CumulativeSavingsSar,
    double BaselineCostSar
);

public record FinancialModelDto(
    double CapexTotalSar,
    double MonthlySavingsSar,
    double Year1SavingsSar,
    double Year5SavingsSar,
    double Year10SavingsSar,
    int?   BreakEvenYear,
    double Baseline10yrCostSar,
    double Net10yrBenefitSar,
    List<FinancialYearDto> YearlyData
);

/// <summary>
/// Config handed off to the dataset generator + brain (Layer 2).
/// Contains everything the simulation needs from the chosen system.
/// </summary>
public record SimulationConfigDto(
    string UserType,
    string Region,
    string GridScenario,
    double PeakLoadKw,
    double CriticalLoadPct,
    double ArrayKwp,
    double BatteryKwh,
    int    DodPct,
    double Ghi,
    TariffDto Tariff,
    double PanelTempCoeff,
    bool   HasGenerator,
    double GeneratorKva,
    int    SimulationYear
);

public record TariffDto(
    double RateSarKwh,
    double PeakRateSarKwh,
    double OffpeakRateSarKwh,
    double ExportRateSarKwh
);
