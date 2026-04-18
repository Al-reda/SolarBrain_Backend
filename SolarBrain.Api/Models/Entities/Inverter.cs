using System.ComponentModel.DataAnnotations;

namespace SolarBrain.Api.Models.Entities;

/// <summary>
/// Inverter. Type field stores "Hybrid" | "Grid-tie" | "Off-grid".
/// GridScenario is the eligibility filter: "both" | "on_grid" | "off_grid".
/// </summary>
public class Inverter
{
    [Key]
    [MaxLength(100)]
    public string Id { get; set; } = string.Empty;

    [MaxLength(80)]  public string Brand { get; set; } = string.Empty;
    [MaxLength(120)] public string Model { get; set; } = string.Empty;

    public int    Tier          { get; set; }
    public double CapacityKw    { get; set; }

    [MaxLength(40)] public string Type { get; set; } = string.Empty;

    public double EfficiencyPct { get; set; }
    public int    WarrantyYears { get; set; }
    public double PriceSar      { get; set; }
    public double MaxPvInputKw  { get; set; }

    [MaxLength(16)] public string GridScenario { get; set; } = "both";

    public bool AvailableSa { get; set; }
    public bool SecApproved { get; set; }
}
