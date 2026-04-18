using System.ComponentModel.DataAnnotations;

namespace SolarBrain.Api.Models.Entities;

/// <summary>
/// Balance-of-system / protection items. Key matches the JSON key
/// (e.g. "dc_mcb", "spd_type1_2", "mounting_structure_per_panel").
/// PriceBasis tells the sizing engine whether to multiply by units, panels, or meters.
/// </summary>
public class ProtectionItem
{
    [Key]
    [MaxLength(60)]
    public string Key { get; set; } = string.Empty;

    [MaxLength(120)] public string Description { get; set; } = string.Empty;
    [MaxLength(500)] public string Note        { get; set; } = string.Empty;

    public double PriceSar { get; set; }

    /// <summary>"unit" | "panel" | "meter" — drives the sizing engine multiplier.</summary>
    [MaxLength(10)]
    public string PriceBasis { get; set; } = "unit";
}
