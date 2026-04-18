using System.ComponentModel.DataAnnotations;

namespace SolarBrain.Api.Models.Entities;

/// <summary>
/// Battery storage unit. Chemistry is typically LiFePO4.
/// </summary>
public class Battery
{
    [Key]
    [MaxLength(100)]
    public string Id { get; set; } = string.Empty;

    [MaxLength(80)]  public string Brand { get; set; } = string.Empty;
    [MaxLength(120)] public string Model { get; set; } = string.Empty;

    public int    Tier          { get; set; }
    public double CapacityKwh   { get; set; }

    [MaxLength(30)] public string Chemistry { get; set; } = "LiFePO4";

    public int    CycleLife     { get; set; }
    public int    DodPct        { get; set; }
    public int    WarrantyYears { get; set; }
    public double PriceSar      { get; set; }
    public double VoltageV      { get; set; }
    public double MaxChargeKw   { get; set; }
    public double MaxDischargeKw{ get; set; }

    [MaxLength(16)] public string GridScenario { get; set; } = "both";

    public bool AvailableSa { get; set; }
}
