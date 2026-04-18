using System.ComponentModel.DataAnnotations;

namespace SolarBrain.Api.Models.Entities;

/// <summary>
/// SEC electricity tariff by user type. Peak, shoulder, off-peak rates + export credit.
/// Key is "facility" | "farm" | "residential".
/// </summary>
public class Tariff
{
    [Key]
    [MaxLength(20)]
    public string UserType { get; set; } = string.Empty;

    public double RateSarKwh        { get; set; }  // shoulder / default rate
    public double PeakRateSarKwh    { get; set; }  // 12:00 – 17:00
    public double OffpeakRateSarKwh { get; set; }  // 22:00 – 06:00
    public double ExportRateSarKwh  { get; set; }  // net metering credit
}
