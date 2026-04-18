using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SolarBrain.Api.Data;

namespace SolarBrain.Api.Controllers;

/// <summary>
/// Read-only catalogue endpoints. Used by the frontend to show the user
/// all available component options (not just the top 3 ranked ones).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class ComponentsController : ControllerBase
{
    private readonly SolarBrainDbContext _db;

    public ComponentsController(SolarBrainDbContext db) => _db = db;

    /// <summary>Full catalogue in a single response — panels, inverters, batteries, regions, tariffs, protection items, derating factors, sizing constants.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var panels     = await _db.Panels.AsNoTracking().OrderBy(p => p.Tier).ThenBy(p => p.PriceSar).ToListAsync();
        var inverters  = await _db.Inverters.AsNoTracking().OrderBy(i => i.Tier).ThenBy(i => i.PriceSar).ToListAsync();
        var batteries  = await _db.Batteries.AsNoTracking().OrderBy(b => b.Tier).ThenBy(b => b.PriceSar).ToListAsync();
        var regions    = await _db.Regions.AsNoTracking().ToListAsync();
        var tariffs    = await _db.Tariffs.AsNoTracking().ToListAsync();
        var protection = await _db.ProtectionItems.AsNoTracking().ToListAsync();
        var derating   = await _db.DeratingFactors.AsNoTracking().FirstAsync();
        var constants  = await _db.SizingConstants.AsNoTracking().FirstAsync();

        return Ok(new
        {
            status = "ok",
            components = new
            {
                panels,
                inverters,
                batteries,
                regions,
                tariffs,
                protectionItems   = protection,
                deratingFactors   = derating,
                sizingConstants   = constants,
            },
        });
    }

    /// <summary>Quick record counts — used as a smoke check that seeding worked.</summary>
    [HttpGet("summary")]
    public async Task<IActionResult> Summary() => Ok(new
    {
        panels          = await _db.Panels.CountAsync(),
        inverters       = await _db.Inverters.CountAsync(),
        batteries       = await _db.Batteries.CountAsync(),
        regions         = await _db.Regions.CountAsync(),
        tariffs         = await _db.Tariffs.CountAsync(),
        protectionItems = await _db.ProtectionItems.CountAsync(),
        deratingRows    = await _db.DeratingFactors.CountAsync(),
        sizingRows      = await _db.SizingConstants.CountAsync(),
    });

    [HttpGet("panels")]    public async Task<IActionResult> Panels()    => Ok(await _db.Panels.AsNoTracking().OrderBy(p => p.Tier).ToListAsync());
    [HttpGet("inverters")] public async Task<IActionResult> Inverters() => Ok(await _db.Inverters.AsNoTracking().OrderBy(i => i.Tier).ToListAsync());
    [HttpGet("batteries")] public async Task<IActionResult> Batteries() => Ok(await _db.Batteries.AsNoTracking().OrderBy(b => b.Tier).ToListAsync());
    [HttpGet("regions")]   public async Task<IActionResult> Regions()   => Ok(await _db.Regions.AsNoTracking().ToListAsync());
    [HttpGet("tariffs")]   public async Task<IActionResult> Tariffs()   => Ok(await _db.Tariffs.AsNoTracking().ToListAsync());
}
