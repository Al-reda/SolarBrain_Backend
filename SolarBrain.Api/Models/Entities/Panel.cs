using System.ComponentModel.DataAnnotations;

namespace SolarBrain.Api.Models.Entities;

/// <summary>
/// Solar PV panel. Primary key matches the id from components.json
/// (e.g. "panel_t1_jinko_410") so seeding is idempotent.
/// </summary>
public class Panel
{
    [Key]
    [MaxLength(100)]
    public string Id { get; set; } = string.Empty;

    [MaxLength(80)]  public string Brand { get; set; } = string.Empty;
    [MaxLength(120)] public string Model { get; set; } = string.Empty;

    public int    Tier              { get; set; }
    public int    PowerWp           { get; set; }
    public double EfficiencyPct     { get; set; }
    public double AreaM2            { get; set; }

    [MaxLength(40)] public string Type { get; set; } = string.Empty;

    public int    WarrantyYears     { get; set; }
    public double PriceSar          { get; set; }
    public double TempCoefficientPct{ get; set; }
    public bool   AvailableSa       { get; set; }
    public bool   SecApproved       { get; set; }

    /// <summary>"both" | "on_grid" | "off_grid"</summary>
    [MaxLength(16)] public string GridScenario { get; set; } = "both";
}
