using SolarBrain.Api.Models.Dtos;

namespace SolarBrain.Api.Services;

/// <summary>
/// Generates a synthetic but physically accurate year-long energy dataset
/// shaped to a specific facility config. Output is a 27-column CSV at
/// 15-min intervals (35,040 rows) that feeds straight into the brain.
/// </summary>
public interface IDatasetGenerator
{
    /// <summary>
    /// Generate the full dataset and write it to <paramref name="outputPath"/>.
    /// Returns the number of rows written.
    /// </summary>
    int GenerateAndSave(SimulationConfigDto config, string outputPath);
}
