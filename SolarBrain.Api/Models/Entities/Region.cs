using System.ComponentModel.DataAnnotations;

namespace SolarBrain.Api.Models.Entities;

/// <summary>
/// Saudi Arabian region with its GHI average and major cities.
/// Key is "eastern" | "central" | "western" (matches form input).
/// </summary>
public class Region
{
    [Key]
    [MaxLength(20)]
    public string Key { get; set; } = string.Empty;

    [MaxLength(60)]  public string Name         { get; set; } = string.Empty;
    [MaxLength(300)] public string CitiesCsv    { get; set; } = string.Empty;

    public double GhiKwhM2Day { get; set; }
}
