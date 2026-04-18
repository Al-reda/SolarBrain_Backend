using Microsoft.AspNetCore.Mvc;
using SolarBrain.Api.Models.Dtos;
using SolarBrain.Api.Services;

namespace SolarBrain.Api.Controllers;

/// <summary>
/// Layer 1 endpoint. Takes a facility profile, runs the sizing engine,
/// generates the synthetic dataset, and primes the simulation runner so
/// Layer 2 is ready to stream the moment the user switches views.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class DesignController : ControllerBase
{
    private readonly ISizingEngine       _sizingEngine;
    private readonly IDatasetGenerator   _datasetGenerator;
    private readonly ISimulationRunner   _runner;
    private readonly IDesignStore        _designStore;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<DesignController> _log;

    /// <summary>
    /// Serialises the full dataset-write-then-load sequence. The dataset
    /// is a single shared file (simulation.csv) and the sim runner is a
    /// singleton — concurrent /Design submissions would otherwise race
    /// between generator's flush and runner's re-read, causing partial-
    /// read CsvHelper exceptions (observed in scripts/test-hard.ts cat D).
    /// Single-slot semaphore keeps latency predictable: N parallel
    /// requests cost ~(N × sizing time) instead of crashing.
    /// </summary>
    private static readonly SemaphoreSlim _designLock = new(1, 1);

    public DesignController(
        ISizingEngine       sizingEngine,
        IDatasetGenerator   datasetGenerator,
        ISimulationRunner   runner,
        IDesignStore        designStore,
        IWebHostEnvironment env,
        ILogger<DesignController> log)
    {
        _sizingEngine     = sizingEngine;
        _datasetGenerator = datasetGenerator;
        _runner           = runner;
        _designStore      = designStore;
        _env              = env;
        _log              = log;
    }

    /// <summary>
    /// Run the sizing engine on a facility profile, generate the dataset,
    /// and prime the simulation runner.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(DesignResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DesignResponse>> SubmitProfile(
        [FromBody] FacilityProfileDto profile)
    {
        await _designLock.WaitAsync();
        try
        {
            _log.LogInformation(
                "Sizing — user_type={UserType} region={Region} scenario={Scenario} bill={Bill} SAR",
                profile.UserType, profile.Region, profile.GridScenario, profile.MonthlyBillSar);

            var design = await _sizingEngine.SizeSystemAsync(profile);

            var datasetPath = Path.Combine(_env.ContentRootPath, "simulation.csv");
            int rows = _datasetGenerator.GenerateAndSave(design.SimulationConfig, datasetPath);
            _log.LogInformation("Dataset generated with {Rows} rows", rows);

            _runner.Load(design.SimulationConfig, datasetPath);
            _designStore.SetCurrent(design);

            return Ok(new DesignResponse("ok", design));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title  = "Invalid input",
                Detail = ex.Message,
                Status = 400,
            });
        }
        finally
        {
            _designLock.Release();
        }
    }

    /// <summary>Return the last-computed design (no sizing re-run).</summary>
    [HttpGet("current")]
    [ProducesResponseType(typeof(DesignResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<DesignResponse> GetCurrent()
    {
        var current = _designStore.Current;
        if (current is null)
            return NotFound(new ProblemDetails
            {
                Title  = "No design loaded yet",
                Detail = "POST /api/design to generate one.",
                Status = 404,
            });
        return Ok(new DesignResponse("ok", current));
    }
}

/// <summary>Wrapper to keep the response shape consistent with the FastAPI contract.</summary>
public record DesignResponse(string Status, SystemDesignDto SystemDesign);
