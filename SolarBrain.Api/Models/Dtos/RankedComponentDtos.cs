namespace SolarBrain.Api.Models.Dtos;

/// <summary>
/// One panel option with ranking metadata and how many units it takes to
/// meet the required kWp. RoofLimited is true when the user's roof area
/// capped the unit count (residential scenario).
/// </summary>
public record RankedPanelDto(
    string RecommendationLabel,   // "Best value" | "Best performance" | "Budget option"
    double Score,
    // Catalogue fields
    string Id,
    string Brand,
    string Model,
    int    Tier,
    int    PowerWp,
    double EfficiencyPct,
    double AreaM2,
    string Type,
    int    WarrantyYears,
    double PriceSar,
    double TempCoefficientPct,
    string GridScenario,
    // Derived fields
    int    UnitsRequired,
    double ActualKwp,
    double RoofAreaM2,
    double PanelsCostSar,
    bool   RoofLimited
);

public record RankedInverterDto(
    string RecommendationLabel,
    double Score,
    string Id,
    string Brand,
    string Model,
    int    Tier,
    double CapacityKw,
    string Type,
    double EfficiencyPct,
    int    WarrantyYears,
    double PriceSar,
    double MaxPvInputKw,
    string GridScenario,
    int    UnitsRequired,
    double ActualKw,
    double InverterCostSar
);

public record RankedBatteryDto(
    string RecommendationLabel,
    double Score,
    string Id,
    string Brand,
    string Model,
    int    Tier,
    double CapacityKwh,
    string Chemistry,
    int    CycleLife,
    int    DodPct,
    int    WarrantyYears,
    double PriceSar,
    string GridScenario,
    int    UnitsRequired,
    double ActualKwh,
    double FloorSocPct,
    double BatteryCostSar
);
