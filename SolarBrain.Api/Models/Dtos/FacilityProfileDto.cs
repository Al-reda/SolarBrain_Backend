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
    /// <remarks>
    /// Initialised to null! so missing JSON fields leave the property null
    /// and [Required] actually triggers a 400. If we defaulted to "facility",
    /// any caller that forgets userType would silently get a facility design
    /// (real bug caught by scripts/test-hard.ts, category B).
    /// </remarks>
    [Required(AllowEmptyStrings = false, ErrorMessage = "userType is required")]
    [RegularExpression("^(facility|farm|residential)$",
        ErrorMessage = "userType must be one of: facility, farm, residential")]
    public string UserType { get; set; } = null!;

    /// <summary>"eastern" | "central" | "western"</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "region is required")]
    [RegularExpression("^(eastern|central|western)$",
        ErrorMessage = "region must be one of: eastern, central, western")]
    public string Region { get; set; } = null!;

    /// <summary>"on_grid" | "off_grid"  (residential is on_grid only — enforced server-side)</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "gridScenario is required")]
    [RegularExpression("^(on_grid|off_grid)$",
        ErrorMessage = "gridScenario must be either on_grid or off_grid")]
    public string GridScenario { get; set; } = null!;

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
    /// <summary>"split" (1.5–3.5 kW each) or "central" (3.5–5 kW ducted unit)</summary>
    [RegularExpression("^(split|central)$")]
    public string? AcType { get; set; } = "split";
    /// <summary>Average daily AC runtime hours (default: 14 for Saudi climate)</summary>
    [Range(1, 24)]         public double? AcHoursDay { get; set; }

    // Off-grid
    public bool    HasGenerator { get; set; } = false;
    [Range(0, 5_000)]      public double? GeneratorKva { get; set; } = 0;

    // ── Battery retrofit mode ────────────────────────────────────────
    // When both ExistingPvKwp and ExistingInverterKw are provided,
    // the sizing engine skips panel/inverter selection and only
    // recommends batteries. CAPEX excludes panel + inverter cost.
    [Range(0.1, 50_000)]   public double? ExistingPvKwp      { get; set; }
    [Range(0.1, 50_000)]   public double? ExistingInverterKw  { get; set; }

    // ── No battery mode ──────────────────────────────────────────────
    // When true, the sizing engine skips battery selection entirely.
    // CAPEX excludes battery cost. Useful for grid-connected systems
    // where the user doesn't want energy storage.
    public bool NoBattery { get; set; } = false;
}
