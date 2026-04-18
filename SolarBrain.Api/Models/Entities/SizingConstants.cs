using System.ComponentModel.DataAnnotations;

namespace SolarBrain.Api.Models.Entities;

/// <summary>
/// Single-row table. Constants used by the sizing engine formulas
/// (safety factors, autonomy hours, generator/fuel parameters).
/// </summary>
public class SizingConstants
{
    [Key]
    public int Id { get; set; } = 1;  // singleton row

    public double SafetyFactorOnGrid          { get; set; } = 1.2;
    public double SafetyFactorOffGrid         { get; set; } = 1.35;
    public double AutonomyHoursOnGrid         { get; set; } = 5;
    public double AutonomyHoursOffGrid        { get; set; } = 60;
    public double PeakLoadFactor              { get; set; } = 1.3;
    public double InverterOversizeFactor      { get; set; } = 1.25;
    public double GeneratorPowerFactor        { get; set; } = 0.8;
    public double DieselConsumptionLphPerKva  { get; set; } = 0.25;
    public double DieselPriceSarPerLiter      { get; set; } = 0.75;
}
