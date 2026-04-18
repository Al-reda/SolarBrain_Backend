using Microsoft.AspNetCore.Mvc;
using SolarBrain.Api.Models.Dtos;
using SolarBrain.Api.Services;

namespace SolarBrain.Api.Controllers;

/// <summary>
/// Layer 2 endpoints. Drives the live dashboard:
/// step / history / summary / scenario / speed / reset / jump.
/// Requires /api/design to have been called first to prime the runner.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class SimulationController : ControllerBase
{
    private readonly ISimulationRunner _runner;

    public SimulationController(ISimulationRunner runner) => _runner = runner;

    // ── Step forward ─────────────────────────────────────────────────────

    /// <summary>Advance the simulation one (or `speed`) interval and return the latest state.</summary>
    [HttpGet("next")]
    public ActionResult<object> Next()
    {
        if (!_runner.IsLoaded)
            return BadRequest(new ProblemDetails
            {
                Title  = "Simulation not ready",
                Detail = "Call POST /api/design first to prime the runner.",
                Status = 400,
            });

        var state = _runner.NextStep();
        if (state is null)
            return Ok(new { status = "complete", message = "Simulation reached end of dataset" });

        return Ok(new { status = "ok", state });
    }

    /// <summary>Return the last N states for the 24h history chart (default 96 = 24h).</summary>
    [HttpGet("history")]
    public ActionResult<object> History([FromQuery] int lastN = 96)
    {
        if (!_runner.IsLoaded) return RunnerNotReady();
        var history = _runner.GetHistory(lastN);
        return Ok(new { status = "ok", history });
    }

    /// <summary>Cumulative KPI totals for the running simulation.</summary>
    [HttpGet("summary")]
    public ActionResult<object> Summary()
    {
        if (!_runner.IsLoaded) return RunnerNotReady();
        return Ok(new { status = "ok", summary = _runner.GetSummary() });
    }

    // ── Scenario injection ──────────────────────────────────────────────

    private static readonly HashSet<string> ValidScenarios = new()
    {
        "grid_outage", "grid_restore",
        "season_summer", "season_moderate", "season_winter", "season_reset",
        "load_spike", "load_restore",
        "cloud_cover", "cloud_restore",
        "low_battery", "low_battery_restore",
    };

    /// <summary>Inject a scenario event (grid outage, cloud cover, load spike, …).</summary>
    [HttpPost("scenario")]
    public ActionResult<object> Scenario([FromBody] ScenarioRequestDto req)
    {
        if (!_runner.IsLoaded) return RunnerNotReady();
        if (!ValidScenarios.Contains(req.Scenario))
            return BadRequest(new ProblemDetails
            {
                Title  = "Unknown scenario",
                Detail = $"'{req.Scenario}' is not a recognised scenario.",
                Status = 400,
            });

        _runner.InjectScenario(req.Scenario, req.Value);
        return Ok(new {
            status   = "ok",
            scenario = req.Scenario,
            message  = $"Scenario '{req.Scenario}' injected successfully",
        });
    }

    // ── Controls ─────────────────────────────────────────────────────────

    /// <summary>Set playback speed: 1 = normal, 5 = fast, 10 = superfast (clamped 1–20).</summary>
    [HttpPost("speed")]
    public ActionResult<object> Speed([FromBody] SpeedRequestDto req)
    {
        if (!_runner.IsLoaded) return RunnerNotReady();
        _runner.SetSpeed(req.Speed);
        return Ok(new { status = "ok", speed = req.Speed });
    }

    /// <summary>Reset the simulation to the beginning, preserving the current design.</summary>
    [HttpPost("reset")]
    public ActionResult<object> Reset()
    {
        if (!_runner.IsLoaded) return RunnerNotReady();
        _runner.Reset();
        return Ok(new { status = "ok", message = "Simulation reset to start" });
    }

    /// <summary>Jump to the first row of the given season: summer | moderate | winter.</summary>
    [HttpPost("jump/season/{season}")]
    public ActionResult<object> JumpToSeason(string season)
    {
        if (!_runner.IsLoaded) return RunnerNotReady();
        _runner.JumpToSeason(season.ToLowerInvariant());
        return Ok(new { status = "ok", season });
    }

    /// <summary>Jump forward to the next row matching a specific hour of the day (0–23).</summary>
    [HttpPost("jump/hour/{hour:int}")]
    public ActionResult<object> JumpToHour(int hour)
    {
        if (!_runner.IsLoaded) return RunnerNotReady();
        if (hour < 0 || hour > 23)
            return BadRequest(new ProblemDetails { Title = "Invalid hour", Status = 400 });
        _runner.JumpToHour(hour);
        return Ok(new { status = "ok", hour });
    }

    // ── Helper ───────────────────────────────────────────────────────────

    private ObjectResult RunnerNotReady() => BadRequest(new ProblemDetails
    {
        Title  = "Simulation not running",
        Detail = "Call POST /api/design first to prime the runner.",
        Status = 400,
    });
}
