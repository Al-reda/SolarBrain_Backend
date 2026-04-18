using System.ComponentModel.DataAnnotations;

namespace SolarBrain.Api.Models.Entities;

/// <summary>
/// Single-row table. SA-specific PV derating factors.
/// PerformanceRatio is the combined PR used by the sizing engine (~0.655).
/// </summary>
public class DeratingFactors
{
    [Key]
    public int Id { get; set; } = 1;  // singleton row

    public double TempDerating      { get; set; } = 0.82;
    public double SoilingFactor     { get; set; } = 0.93;
    public double SystemLosses      { get; set; } = 0.86;
    public double AnnualDegradation { get; set; } = 0.99;
    public double PerformanceRatio  { get; set; } = 0.655;
}
