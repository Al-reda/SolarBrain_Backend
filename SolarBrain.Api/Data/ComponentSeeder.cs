using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SolarBrain.Api.Models.Entities;

namespace SolarBrain.Api.Data;

/// <summary>
/// Seeds the SQLite database from the original components.json.
/// Runs at app startup. Idempotent: if any Panels already exist, seeding is skipped.
/// </summary>
public class ComponentSeeder
{
    private readonly SolarBrainDbContext _db;
    private readonly ILogger<ComponentSeeder> _log;
    private readonly IWebHostEnvironment _env;

    public ComponentSeeder(
        SolarBrainDbContext db,
        ILogger<ComponentSeeder> log,
        IWebHostEnvironment env)
    {
        _db  = db;
        _log = log;
        _env = env;
    }

    /// <summary>Apply migrations then seed if the catalogue is empty.</summary>
    public async Task SeedAsync()
    {
        // Ensure the schema exists
        await _db.Database.MigrateAsync();

        if (await _db.Panels.AnyAsync())
        {
            _log.LogInformation("Database already seeded — skipping.");
            return;
        }

        var jsonPath = ResolveSeedPath();
        if (jsonPath is null)
        {
            _log.LogCritical(
                "components.json not found at {Local} or sibling Python path. " +
                "The catalogue will be empty and all /Design calls will fail. " +
                "Make sure Data/components.json is included in the build output.",
                Path.Combine(_env.ContentRootPath, "Data", "components.json"));
            throw new FileNotFoundException(
                "components.json is required for seeding. See logs for the paths searched.");
        }

        _log.LogInformation("Seeding database from {Path}", jsonPath);

        await using var fs = File.OpenRead(jsonPath);
        using var doc = await JsonDocument.ParseAsync(fs);
        var root = doc.RootElement;

        SeedPanels(root);
        SeedInverters(root);
        SeedBatteries(root);
        SeedRegions(root);
        SeedTariffs(root);
        SeedDerating(root);
        SeedSizingConstants(root);
        SeedProtectionItems(root);

        var saved = await _db.SaveChangesAsync();
        _log.LogInformation("Seeding complete — {Count} records inserted.", saved);
    }

    /// <summary>
    /// Looks for components.json in two places (in order):
    ///   1. {ContentRoot}/Data/components.json         (self-contained copy — preferred)
    ///   2. {ContentRoot}/../../data/components.json   (sibling Python project fallback)
    /// </summary>
    private string? ResolveSeedPath()
    {
        var local = Path.Combine(_env.ContentRootPath, "Data", "components.json");
        if (File.Exists(local)) return local;

        var sibling = Path.GetFullPath(Path.Combine(
            _env.ContentRootPath, "..", "..", "..", "solarbrain", "data", "components.json"));
        if (File.Exists(sibling)) return sibling;

        return null;
    }

    // ── Component tables ──────────────────────────────────────────────────

    private void SeedPanels(JsonElement root)
    {
        foreach (var p in root.GetProperty("panels").EnumerateArray())
        {
            _db.Panels.Add(new Panel
            {
                Id                 = p.GetProperty("id").GetString()!,
                Brand              = p.GetProperty("brand").GetString() ?? "",
                Model              = p.GetProperty("model").GetString() ?? "",
                Tier               = p.GetProperty("tier").GetInt32(),
                PowerWp            = p.GetProperty("power_wp").GetInt32(),
                EfficiencyPct      = p.GetProperty("efficiency_pct").GetDouble(),
                AreaM2             = p.GetProperty("area_m2").GetDouble(),
                Type               = p.GetProperty("type").GetString() ?? "",
                WarrantyYears      = p.GetProperty("warranty_years").GetInt32(),
                PriceSar           = p.GetProperty("price_sar").GetDouble(),
                TempCoefficientPct = p.GetProperty("temp_coefficient_pct").GetDouble(),
                AvailableSa        = p.GetProperty("available_sa").GetBoolean(),
                SecApproved        = p.GetProperty("sec_approved").GetBoolean(),
                GridScenario       = p.GetProperty("grid_scenario").GetString() ?? "both",
            });
        }
    }

    private void SeedInverters(JsonElement root)
    {
        foreach (var i in root.GetProperty("inverters").EnumerateArray())
        {
            _db.Inverters.Add(new Inverter
            {
                Id            = i.GetProperty("id").GetString()!,
                Brand         = i.GetProperty("brand").GetString() ?? "",
                Model         = i.GetProperty("model").GetString() ?? "",
                Tier          = i.GetProperty("tier").GetInt32(),
                CapacityKw    = i.GetProperty("capacity_kw").GetDouble(),
                Type          = i.GetProperty("type").GetString() ?? "",
                EfficiencyPct = i.GetProperty("efficiency_pct").GetDouble(),
                WarrantyYears = i.GetProperty("warranty_years").GetInt32(),
                PriceSar      = i.GetProperty("price_sar").GetDouble(),
                MaxPvInputKw  = i.TryGetProperty("max_pv_input_kw", out var mp) ? mp.GetDouble() : 0,
                GridScenario  = i.GetProperty("grid_scenario").GetString() ?? "both",
                AvailableSa   = i.TryGetProperty("available_sa", out var av) && av.GetBoolean(),
                SecApproved   = i.TryGetProperty("sec_approved", out var sa) && sa.GetBoolean(),
            });
        }
    }

    private void SeedBatteries(JsonElement root)
    {
        foreach (var b in root.GetProperty("batteries").EnumerateArray())
        {
            _db.Batteries.Add(new Battery
            {
                Id             = b.GetProperty("id").GetString()!,
                Brand          = b.GetProperty("brand").GetString() ?? "",
                Model          = b.GetProperty("model").GetString() ?? "",
                Tier           = b.GetProperty("tier").GetInt32(),
                CapacityKwh    = b.GetProperty("capacity_kwh").GetDouble(),
                Chemistry      = b.GetProperty("chemistry").GetString() ?? "LiFePO4",
                CycleLife      = b.GetProperty("cycle_life").GetInt32(),
                DodPct         = b.GetProperty("dod_pct").GetInt32(),
                WarrantyYears  = b.GetProperty("warranty_years").GetInt32(),
                PriceSar       = b.GetProperty("price_sar").GetDouble(),
                VoltageV       = b.TryGetProperty("voltage_v", out var v) ? v.GetDouble() : 0,
                MaxChargeKw    = b.TryGetProperty("max_charge_kw", out var mc) ? mc.GetDouble() : 0,
                MaxDischargeKw = b.TryGetProperty("max_discharge_kw", out var md) ? md.GetDouble() : 0,
                GridScenario   = b.GetProperty("grid_scenario").GetString() ?? "both",
                AvailableSa    = b.TryGetProperty("available_sa", out var av) && av.GetBoolean(),
            });
        }
    }

    // ── Reference data ────────────────────────────────────────────────────

    private void SeedRegions(JsonElement root)
    {
        foreach (var kv in root.GetProperty("regions").EnumerateObject())
        {
            var v = kv.Value;
            var cities = string.Join(",", v.GetProperty("cities").EnumerateArray()
                                              .Select(c => c.GetString() ?? ""));
            _db.Regions.Add(new Region
            {
                Key         = kv.Name,
                Name        = v.GetProperty("name").GetString() ?? "",
                CitiesCsv   = cities,
                GhiKwhM2Day = v.GetProperty("ghi_kwh_m2_day").GetDouble(),
            });
        }
    }

    private void SeedTariffs(JsonElement root)
    {
        foreach (var kv in root.GetProperty("tariffs").EnumerateObject())
        {
            var v = kv.Value;
            _db.Tariffs.Add(new Tariff
            {
                UserType          = kv.Name,
                RateSarKwh        = v.GetProperty("rate_sar_kwh").GetDouble(),
                PeakRateSarKwh    = v.GetProperty("peak_rate_sar_kwh").GetDouble(),
                OffpeakRateSarKwh = v.GetProperty("offpeak_rate_sar_kwh").GetDouble(),
                ExportRateSarKwh  = v.GetProperty("export_rate_sar_kwh").GetDouble(),
            });
        }
    }

    private void SeedDerating(JsonElement root)
    {
        var d = root.GetProperty("derating_factors");
        _db.DeratingFactors.Add(new DeratingFactors
        {
            Id                = 1,
            TempDerating      = d.GetProperty("temp_derating").GetDouble(),
            SoilingFactor     = d.GetProperty("soiling_factor").GetDouble(),
            SystemLosses      = d.GetProperty("system_losses").GetDouble(),
            AnnualDegradation = d.GetProperty("annual_degradation").GetDouble(),
            PerformanceRatio  = d.GetProperty("performance_ratio").GetDouble(),
        });
    }

    private void SeedSizingConstants(JsonElement root)
    {
        var c = root.GetProperty("sizing_constants");
        _db.SizingConstants.Add(new SizingConstants
        {
            Id                           = 1,
            SafetyFactorOnGrid           = c.GetProperty("safety_factor_on_grid").GetDouble(),
            SafetyFactorOffGrid          = c.GetProperty("safety_factor_off_grid").GetDouble(),
            AutonomyHoursOnGrid          = c.GetProperty("autonomy_hours_on_grid").GetDouble(),
            AutonomyHoursOffGrid         = c.GetProperty("autonomy_hours_off_grid").GetDouble(),
            PeakLoadFactor               = c.GetProperty("peak_load_factor").GetDouble(),
            InverterOversizeFactor       = c.GetProperty("inverter_oversize_factor").GetDouble(),
            GeneratorPowerFactor         = c.GetProperty("generator_power_factor").GetDouble(),
            DieselConsumptionLphPerKva   = c.GetProperty("diesel_consumption_lph_per_kva").GetDouble(),
            DieselPriceSarPerLiter       = c.GetProperty("diesel_price_sar_per_liter").GetDouble(),
        });
    }

    private void SeedProtectionItems(JsonElement root)
    {
        foreach (var kv in root.GetProperty("protection_items").EnumerateObject())
        {
            var v = kv.Value;

            // JSON uses three different price field names depending on the item.
            double price = 0;
            string basis = "unit";
            if (v.TryGetProperty("price_sar_per_unit", out var pu))
            {
                price = pu.GetDouble(); basis = "unit";
            }
            else if (v.TryGetProperty("price_sar_per_panel", out var pp))
            {
                price = pp.GetDouble(); basis = "panel";
            }
            else if (v.TryGetProperty("price_sar_per_meter", out var pm))
            {
                price = pm.GetDouble(); basis = "meter";
            }

            _db.ProtectionItems.Add(new ProtectionItem
            {
                Key         = kv.Name,
                Description = v.GetProperty("description").GetString() ?? "",
                Note        = v.TryGetProperty("note", out var n) ? (n.GetString() ?? "") : "",
                PriceSar    = price,
                PriceBasis  = basis,
            });
        }
    }
}
