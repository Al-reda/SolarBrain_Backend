using System.ComponentModel.DataAnnotations;

namespace SolarBrain.Api.Models.Dtos;

/// <summary>
/// User input from the Layer 1 form. Optional fields are null when not supplied;
/// the sizing engine derives sensible defaults (e.g. peak_load from monthly bill).
/// Mirrors the FastAPI FacilityProfile model so the frontend contract is identical.
///
/// Every numeric field has a bound — a caller bypassing the frontend (curl,
/// Postman, SDK) still gets an early, readable 400 instead of a nonsense design.
/// </summary>
public class FacilityProfileDto
{
    /// <summary>"facility" | "farm" | "residential"</summary>
    [Required]
    [RegularExpression("^(facility|farm|residential)$",
        ErrorMessage = "userType must be one of: facility, farm, residential")]
    public string UserType { get; set; } = "facility";

    /// <summary>"eastern" | "central" | "western"</summary>
    [Required]
    [RegularExpression("^(eastern|central|western)$",
        ErrorMessage = "region must be one of: eastern, central, western")]
    public string Region { get; set; } = "eastern";

    /// <summary>"on_grid" | "off_grid"  (residential is on_grid only — enforced server-side)</summary>
    [Required]
    [RegularExpression("^(on_grid|off_grid)$",
        ErrorMessage = "gridScenario must be either on_grid or off_grid")]
    public string GridScenario { get; set; } = "on_grid";

    [Range(100, 10_000_000, ErrorMessage = "monthlyBillSar must be between 100 and 10,000,000 SAR")]
    public double MonthlyBillSar { get; set; }

    // Optional — derived if null. Generous upper bounds but finite.
    [Range(0.1, 10_000)]   public double? PeakLoadKw      { get; set; }
    [Range(1,   24)]       public double? OperatingHours  { get; set; } = 10;
    [Range(10,  100_000)]  public double? BuildingSizeM2  { get; set; }
    [Range(5,   100)]      public double? CriticalLoadPct { get; set; } = 30;
    [Range(1,   100_000)]  public double? RoofAreaM2      { get; set; }

    // Farm-specific
    [Range(0.1, 5_000)]    public double? PumpPowerKw   { get; set; }
    [Range(1,   24)]       public double? PumpHoursDay  { get; set; } = 8;

    // Residential-specific
    [Range(0, 50)]         public int? AcUnits { get; set; }

    // Off-grid
    public bool    HasGenerator { get; set; } = false;
    [Range(0, 5_000)]      public double? GeneratorKva { get; set; } = 0;
}
