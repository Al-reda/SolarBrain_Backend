using Microsoft.EntityFrameworkCore;
using SolarBrain.Api.Data;
using SolarBrain.Api.Models.Dtos;
using SolarBrain.Api.Models.Entities;

namespace SolarBrain.Api.Services;

/// <summary>
/// C# port of the Python sizing_engine.py. Keeps identical formulas and
/// identical derating factors so Layer 1 numbers cross-check against the
/// original SolarBrain implementation.
/// </summary>
public class SizingEngine : ISizingEngine
{
    private readonly SolarBrainDbContext _db;
    private readonly ILogger<SizingEngine> _log;

    public SizingEngine(SolarBrainDbContext db, ILogger<SizingEngine> log)
    {
        _db  = db;
        _log = log;
    }

    // ══════════════════════════════════════════════════════════════════════
    // Main entry point — orchestrates the 10 steps
    // ══════════════════════════════════════════════════════════════════════

    public async Task<SystemDesignDto> SizeSystemAsync(FacilityProfileDto p)
    {
        // ── Invariants that [Range] can't express ──────────────────────
        // Residential systems in Saudi Arabia are on-grid only (SEC connection
        // is cheap + ubiquitous; off-grid would be a commercial nonsense).
        if (p.UserType == "residential" && p.GridScenario == "off_grid")
        {
            throw new InvalidOperationException(
                "Residential systems must be on-grid. Off-grid residential is not supported.");
        }

        // Generators only make sense when grid is absent.
        if (p.HasGenerator && p.GridScenario == "on_grid")
        {
            throw new InvalidOperationException(
                "Backup generators are only supported on off-grid systems.");
        }

        // Load everything we need from the DB in one pass
        var region     = await _db.Regions.FindAsync(p.Region)
                          ?? throw new InvalidOperationException($"Unknown region: {p.Region}");
        var tariff     = await _db.Tariffs.FindAsync(p.UserType)
                          ?? throw new InvalidOperationException($"Unknown user type: {p.UserType}");
        var derating   = await _db.DeratingFactors.FirstAsync();
        var constants  = await _db.SizingConstants.FirstAsync();
        var panels     = await _db.Panels.AsNoTracking().ToListAsync();
        var inverters  = await _db.Inverters.AsNoTracking().ToListAsync();
        var batteries  = await _db.Batteries.AsNoTracking().ToListAsync();
        var protection = await _db.ProtectionItems.AsNoTracking().ToListAsync();

        var ctx = new SizingContext(p, region, tariff, derating, constants,
                                     panels, inverters, batteries, protection);

        return BuildDesign(ctx);
    }

    // ══════════════════════════════════════════════════════════════════════
    // Orchestration — calls the 10 steps in order
    // ══════════════════════════════════════════════════════════════════════

    private SystemDesignDto BuildDesign(SizingContext c)
    {
        bool isRetrofit = c.Profile.ExistingPvKwp is > 0 && c.Profile.ExistingInverterKw is > 0;

        // Step 1 + 2: derive daily load + peak load
        var (dailyLoadKwh, peakLoadKw) = DeriveLoad(c);
        var tier = GetTier(peakLoadKw);

        // Step 3: PV array sizing (skip in retrofit — user provides actual kWp)
        var safetyFactor = c.Profile.GridScenario == "on_grid"
            ? c.Constants.SafetyFactorOnGrid
            : c.Constants.SafetyFactorOffGrid;
        var pvKwpRequired = isRetrofit
            ? c.Profile.ExistingPvKwp!.Value
            : Math.Round(
                (dailyLoadKwh / (c.Region.GhiKwhM2Day * c.Derating.PerformanceRatio)) * safetyFactor, 2);

        // Step 4: Battery sizing (user-type-aware)
        var (battKwhRequired, autonomyHours) = SizeBattery(c, peakLoadKw);

        // Step 5: Inverter sizing (skip in retrofit — user provides actual kW)
        var invKwRequired = isRetrofit
            ? c.Profile.ExistingInverterKw!.Value
            : Math.Round(peakLoadKw * c.Constants.InverterOversizeFactor, 2);

        // Step 6: Generator (off-grid only)
        var generatorSpec = BuildGeneratorSpec(c, invKwRequired);
        var generatorKva  = generatorSpec?.Kva ?? 0;

        // Step 7: Component selection
        List<RankedPanelDto> rankedPanels;
        List<RankedInverterDto> rankedInverters;
        List<RankedBatteryDto> rankedBatteries;

        bool noBattery = c.Profile.NoBattery;

        if (noBattery)
        {
            rankedBatteries = new List<RankedBatteryDto>
            {
                new(RecommendationLabel: "No battery", Score: 0,
                    Id: "no_battery", Brand: "None", Model: "No battery selected",
                    Tier: tier, CapacityKwh: 0, Chemistry: "N/A", CycleLife: 0,
                    DodPct: 0, WarrantyYears: 0, PriceSar: 0,
                    GridScenario: c.Profile.GridScenario,
                    UnitsRequired: 0, ActualKwh: 0, FloorSocPct: 0, BatteryCostSar: 0)
            };
        }
        else
        {
            rankedBatteries = SelectBatteries(c, tier, battKwhRequired);
        }

        if (isRetrofit)
        {
            // Create synthetic entries for existing equipment (cost = 0)
            rankedPanels = new List<RankedPanelDto>
            {
                new(RecommendationLabel: "Existing system", Score: 100,
                    Id: "existing_pv", Brand: "Existing", Model: "User PV array",
                    Tier: tier, PowerWp: 0, EfficiencyPct: 0, AreaM2: 0,
                    Type: "existing", WarrantyYears: 0, PriceSar: 0,
                    TempCoefficientPct: -0.35, GridScenario: c.Profile.GridScenario,
                    UnitsRequired: 0, ActualKwp: pvKwpRequired,
                    RoofAreaM2: 0, PanelsCostSar: 0, RoofLimited: false)
            };
            rankedInverters = new List<RankedInverterDto>
            {
                new(RecommendationLabel: "Existing system", Score: 100,
                    Id: "existing_inv", Brand: "Existing", Model: "User inverter",
                    Tier: tier, CapacityKw: invKwRequired, Type: "existing",
                    EfficiencyPct: 96, WarrantyYears: 0, PriceSar: 0,
                    MaxPvInputKw: pvKwpRequired, GridScenario: c.Profile.GridScenario,
                    UnitsRequired: 1, ActualKw: invKwRequired, InverterCostSar: 0)
            };
        }
        else
        {
            rankedPanels    = SelectPanels(c, tier, pvKwpRequired);
            rankedInverters = SelectInverters(c, tier, invKwRequired);
        }

        // Step 8: Protection + BoS (sized to the #1 ranked options)
        var bestPanel    = rankedPanels[0];
        var bestInverter = rankedInverters[0];
        var bestBattery  = rankedBatteries[0];

        var protectionSummary = BuildProtection(c, tier, bestInverter);
        var bos               = BuildBos(c, bestPanel, bestInverter);

        // Step 9: CAPEX — retrofit mode: panel + inverter cost = 0
        var panelCost    = isRetrofit ? 0 : bestPanel.PanelsCostSar;
        var inverterCost = isRetrofit ? 0 : bestInverter.InverterCostSar;
        var capex = new CapexBreakdownDto(
            PanelsSar:     panelCost,
            InverterSar:   inverterCost,
            BatterySar:    bestBattery.BatteryCostSar,
            ProtectionSar: protectionSummary.TotalSar,
            BosSar:        bos.TotalSar,
            TotalSar: Math.Round(
                panelCost + inverterCost +
                bestBattery.BatteryCostSar + protectionSummary.TotalSar + bos.TotalSar, 2)
        );

        // Step 10: Financials (uses actual PV kWp for production)
        var arrayKwpForFinancials = isRetrofit ? pvKwpRequired : bestPanel.ActualKwp;
        var financials = BuildFinancials(c, arrayKwpForFinancials, capex.TotalSar, generatorKva);

        return new SystemDesignDto(
            Profile: new ProfileSummaryDto(
                UserType: c.Profile.UserType, Region: c.Profile.Region,
                RegionName: c.Region.Name, GridScenario: c.Profile.GridScenario,
                MonthlyBillSar: c.Profile.MonthlyBillSar, PeakLoadKw: peakLoadKw,
                DailyLoadKwh: Math.Round(dailyLoadKwh, 1), Tier: tier,
                Ghi: c.Region.GhiKwhM2Day, CriticalLoadPct: c.Profile.CriticalLoadPct ?? 30),
            Requirements: new SizingRequirementsDto(
                PvKwpRequired: pvKwpRequired, BatteryKwhRequired: battKwhRequired,
                InverterKwRequired: invKwRequired, AutonomyHours: autonomyHours,
                PerformanceRatio: c.Derating.PerformanceRatio, SafetyFactor: safetyFactor),
            Panels:          rankedPanels,
            Inverters:       rankedInverters,
            Batteries:       rankedBatteries,
            ProtectionItems: protectionSummary,
            Bos:             bos,
            Generator:       generatorSpec,
            Financials:      financials,
            CapexBreakdown:  capex,
            SimulationConfig: BuildSimConfig(c, bestPanel, bestBattery, generatorKva, peakLoadKw)
        );
    }

    // ══════════════════════════════════════════════════════════════════════
    // Step 1 + 2 — derive daily load and peak load from the profile
    // ══════════════════════════════════════════════════════════════════════

    private (double dailyLoadKwh, double peakLoadKw) DeriveLoad(SizingContext c)
    {
        var rate = c.Tariff.RateSarKwh;
        var bill = c.Profile.MonthlyBillSar;
        double dailyLoadKwh, peakLoadKw;

        if (c.Profile.UserType == "residential")
        {
            // Residential: AC-driven load model for Saudi Arabia
            dailyLoadKwh = (bill / rate) / 30.0;
            if (c.Profile.AcUnits is int acUnits && acUnits > 0)
            {
                // AC power depends on type:
                //   split (wall-mounted):  2.5 kW per unit (typical 18,000–24,000 BTU)
                //   central (ducted):      4.5 kW per unit (typical 36,000–48,000 BTU)
                bool isCentral = c.Profile.AcType == "central";
                double kwPerAc = isCentral ? 4.5 : 2.5;

                // Saudi coincidence factor: in summer, most ACs run simultaneously
                // during 12pm–6pm peak. Central systems have higher coincidence (0.85)
                // because they serve the whole house; splits may not all be on (0.75).
                double coincidence = isCentral ? 0.85 : 0.75;
                double acKw = acUnits * kwPerAc * coincidence;

                // Base (non-AC) household load: lighting, appliances, water heater
                double baseKw = 2.0 + (0.4 * acUnits);  // more AC = larger house

                peakLoadKw = Math.Round(Math.Clamp(acKw + baseKw, 2.0, 60.0), 1);

                // Daily energy: AC runs heavily in Saudi climate
                // Default 14 hours/day (6am–8pm in summer, less in winter → average 14)
                double acHours = c.Profile.AcHoursDay ?? 14.0;
                // AC duty cycle: 70% average (compressor cycles on/off)
                double acDutyCycle = 0.70;
                double acDailyKwh = acUnits * kwPerAc * acHours * acDutyCycle;
                double baseDailyKwh = baseKw * 18.0;  // base loads ~18 hrs/day
                dailyLoadKwh = Math.Round(acDailyKwh + baseDailyKwh, 1);
            }
            else
            {
                double avgHours = 8.0;
                peakLoadKw = Math.Round(Math.Clamp(
                    (dailyLoadKwh / avgHours) * 0.6 * c.Constants.PeakLoadFactor, 2.0, 30.0), 1);
            }
        }
        else if (c.Profile.UserType == "farm" && c.Profile.PumpPowerKw is double pumpKw)
        {
            double pumpHours = c.Profile.PumpHoursDay ?? 8;
            dailyLoadKwh = pumpKw * pumpHours;
            peakLoadKw   = Math.Round(pumpKw, 2);
        }
        else
        {
            dailyLoadKwh = (bill / rate) / 30.0;
            if (c.Profile.PeakLoadKw is double explicitPeak)
            {
                peakLoadKw = explicitPeak;
            }
            else
            {
                double opHours = c.Profile.OperatingHours ?? 10;
                peakLoadKw = Math.Round((dailyLoadKwh / opHours) * c.Constants.PeakLoadFactor, 2);
            }
        }

        return (dailyLoadKwh, peakLoadKw);
    }

    // Tier 1 ≤ 30 kW, Tier 2 ≤ 500 kW, Tier 3 above
    private static int GetTier(double peakLoadKw) =>
        peakLoadKw <= 30 ? 1 : peakLoadKw <= 500 ? 2 : 3;

    // ══════════════════════════════════════════════════════════════════════
    // Step 4 — Battery sizing (user-type + scenario aware)
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Matches the patched Python branches exactly:
    ///   - residential on-grid: 3.5h of essential load (capped 5–20 kWh)
    ///   - farm on-grid:       1.5h of critical load (capped 10–80 kWh)
    ///   - farm off-grid:      8h of critical load (capped 20–300 kWh)
    ///   - facility/farm off-grid: autonomy_hours_off_grid × peak_load
    ///   - facility on-grid:    autonomy_hours_on_grid × peak_load
    /// </summary>
    private (double battKwhRequired, double autonomyHours) SizeBattery(SizingContext c, double peakLoadKw)
    {
        const double defaultDod = 0.90;
        double criticalFraction = (c.Profile.CriticalLoadPct ?? 30) / 100.0;

        if (c.Profile.UserType == "residential")
        {
            double essentialKw = Math.Min(peakLoadKw * criticalFraction, 5.0);
            double batt = Math.Round(Math.Clamp(
                (essentialKw * 3.5) / defaultDod, 5.0, 20.0), 2);
            return (batt, 3.5);
        }

        if (c.Profile.UserType == "farm" && c.Profile.GridScenario == "on_grid")
        {
            double farmEssential = peakLoadKw * Math.Min(criticalFraction, 0.25);
            double batt = Math.Round(Math.Clamp(
                (farmEssential * 1.5) / defaultDod, 10.0, 80.0), 2);
            return (batt, 1.5);
        }

        if (c.Profile.UserType == "farm" && c.Profile.GridScenario == "off_grid")
        {
            double farmEssential = peakLoadKw * Math.Min(criticalFraction, 0.30);
            double batt = Math.Round(Math.Clamp(
                (farmEssential * 8.0) / defaultDod, 20.0, 300.0), 2);
            return (batt, 8.0);
        }

        // Facility path — autonomy × critical load (not full peak)
        double autonomy = c.Profile.GridScenario == "off_grid"
            ? c.Constants.AutonomyHoursOffGrid
            : c.Constants.AutonomyHoursOnGrid;
        double loadForBatt = c.Profile.GridScenario == "off_grid"
            ? peakLoadKw * criticalFraction   // off-grid: only critical loads need autonomy
            : peakLoadKw;
        double facilityBatt = Math.Round(Math.Max(5.0, (loadForBatt * autonomy) / defaultDod), 2);
        return (facilityBatt, autonomy);
    }

    // ══════════════════════════════════════════════════════════════════════
    // Step 6 — Generator (off-grid + has_generator only)
    // ══════════════════════════════════════════════════════════════════════

    private GeneratorSpecDto? BuildGeneratorSpec(SizingContext c, double invKwRequired)
    {
        if (!c.Profile.HasGenerator || c.Profile.GridScenario != "off_grid") return null;

        double kva = c.Profile.GeneratorKva is double g && g > 0
            ? g
            : Math.Round(invKwRequired / c.Constants.GeneratorPowerFactor, 1);

        double fuelLph = kva * 0.8 * c.Constants.DieselConsumptionLphPerKva;
        // Annual cost: rough estimate — 30% of the year at running consumption
        double annualCost = Math.Round(fuelLph * 8760 * 0.3 * c.Constants.DieselPriceSarPerLiter, 0);

        return new GeneratorSpecDto(
            Kva: Math.Round(kva, 1),
            KwOutput: Math.Round(kva * 0.8, 1),
            FuelConsumptionLph: Math.Round(fuelLph, 2),
            EstimatedAnnualCostSar: annualCost
        );
    }

    // ══════════════════════════════════════════════════════════════════════
    // Step 7 — Component selection (filter → score → top 3 → derive units)
    // ══════════════════════════════════════════════════════════════════════

    private static readonly string[] Labels =
        { "Best value", "Best performance", "Budget option" };

    /// <summary>Filter by tier + compatible grid_scenario ("both" always passes).</summary>
    private static IEnumerable<T> FilterByTierAndScenario<T>(
        IEnumerable<T> items, int tier, string gridScenario, Func<T, int> getTier,
        Func<T, string> getScenario) =>
        items.Where(x => getTier(x) == tier
                         && (getScenario(x) == "both" || getScenario(x) == gridScenario));

    private List<RankedPanelDto> SelectPanels(SizingContext c, int tier, double pvKwpRequired)
    {
        var candidates = FilterByTierAndScenario(c.Panels, tier, c.Profile.GridScenario,
                                                   p => p.Tier, p => p.GridScenario).ToList();
        var top3 = candidates
            .Select(p => (panel: p, score: ScorePanel(p)))
            .OrderByDescending(x => x.score)
            .Take(3).ToList();

        var result = new List<RankedPanelDto>();
        for (int i = 0; i < top3.Count; i++)
        {
            var (p, score) = top3[i];
            int unitsRequired = (int)Math.Ceiling(pvKwpRequired * 1000 / p.PowerWp);
            double actualKwp  = Math.Round(unitsRequired * p.PowerWp / 1000.0, 2);
            double roofArea   = Math.Round(unitsRequired * p.AreaM2, 1);
            double panelsCost = unitsRequired * p.PriceSar;
            bool roofLimited  = false;

            // Cap by roof area if provided (residential)
            if (c.Profile.RoofAreaM2 is double maxRoof && roofArea > maxRoof)
            {
                int cappedUnits = (int)(maxRoof / p.AreaM2);
                unitsRequired = cappedUnits;
                actualKwp     = Math.Round(cappedUnits * p.PowerWp / 1000.0, 2);
                roofArea      = Math.Round(cappedUnits * p.AreaM2, 1);
                panelsCost    = cappedUnits * p.PriceSar;
                roofLimited   = true;
            }

            result.Add(new RankedPanelDto(
                RecommendationLabel: Labels[i], Score: Math.Round(score, 4),
                Id: p.Id, Brand: p.Brand, Model: p.Model, Tier: p.Tier,
                PowerWp: p.PowerWp, EfficiencyPct: p.EfficiencyPct, AreaM2: p.AreaM2,
                Type: p.Type, WarrantyYears: p.WarrantyYears, PriceSar: p.PriceSar,
                TempCoefficientPct: p.TempCoefficientPct, GridScenario: p.GridScenario,
                UnitsRequired: unitsRequired, ActualKwp: actualKwp,
                RoofAreaM2: roofArea, PanelsCostSar: panelsCost, RoofLimited: roofLimited));
        }
        return result;
    }

    private List<RankedInverterDto> SelectInverters(SizingContext c, int tier, double invKwRequired)
    {
        var candidates = FilterByTierAndScenario(c.Inverters, tier, c.Profile.GridScenario,
                                                   i => i.Tier, i => i.GridScenario).ToList();
        var top3 = candidates
            .Select(i => (inv: i, score: ScoreInverter(i)))
            .OrderByDescending(x => x.score)
            .Take(3).ToList();

        var result = new List<RankedInverterDto>();
        for (int idx = 0; idx < top3.Count; idx++)
        {
            var (inv, score) = top3[idx];
            int units     = (int)Math.Ceiling(invKwRequired / inv.CapacityKw);
            double actual = Math.Round(units * inv.CapacityKw, 1);
            double cost   = units * inv.PriceSar;

            result.Add(new RankedInverterDto(
                RecommendationLabel: Labels[idx], Score: Math.Round(score, 4),
                Id: inv.Id, Brand: inv.Brand, Model: inv.Model, Tier: inv.Tier,
                CapacityKw: inv.CapacityKw, Type: inv.Type,
                EfficiencyPct: inv.EfficiencyPct, WarrantyYears: inv.WarrantyYears,
                PriceSar: inv.PriceSar, MaxPvInputKw: inv.MaxPvInputKw,
                GridScenario: inv.GridScenario, UnitsRequired: units,
                ActualKw: actual, InverterCostSar: cost));
        }
        return result;
    }

    private List<RankedBatteryDto> SelectBatteries(SizingContext c, int tier, double battKwhRequired)
    {
        var candidates = FilterByTierAndScenario(c.Batteries, tier, c.Profile.GridScenario,
                                                   b => b.Tier, b => b.GridScenario).ToList();
        var top3 = candidates
            .Select(b => (bat: b, score: ScoreBattery(b)))
            .OrderByDescending(x => x.score)
            .Take(3).ToList();

        var result = new List<RankedBatteryDto>();
        for (int idx = 0; idx < top3.Count; idx++)
        {
            var (bat, score) = top3[idx];
            int units       = (int)Math.Ceiling(battKwhRequired / bat.CapacityKwh);
            double actual   = Math.Round(units * bat.CapacityKwh, 1);
            double floorSoc = Math.Round((double)(100 - bat.DodPct), 1);
            double cost     = units * bat.PriceSar;

            result.Add(new RankedBatteryDto(
                RecommendationLabel: Labels[idx], Score: Math.Round(score, 4),
                Id: bat.Id, Brand: bat.Brand, Model: bat.Model, Tier: bat.Tier,
                CapacityKwh: bat.CapacityKwh, Chemistry: bat.Chemistry,
                CycleLife: bat.CycleLife, DodPct: bat.DodPct,
                WarrantyYears: bat.WarrantyYears, PriceSar: bat.PriceSar,
                GridScenario: bat.GridScenario, UnitsRequired: units,
                ActualKwh: actual, FloorSocPct: floorSoc, BatteryCostSar: cost));
        }
        return result;
    }

    // ══════════════════════════════════════════════════════════════════════
    // Scoring — identical weightings to the Python implementation
    // ══════════════════════════════════════════════════════════════════════

    private static double ScorePanel(Panel p)
    {
        // Efficiency 40% + price-per-watt 40% + warranty 20%
        double eff       = p.EfficiencyPct / 25.0;
        double pricePerW = p.PriceSar / p.PowerWp;
        double priceScore= 1 - (pricePerW / 1.0);
        double warranty  = p.WarrantyYears / 30.0;
        return (eff * 0.40) + (priceScore * 0.40) + (warranty * 0.20);
    }

    private static double ScoreInverter(Inverter i)
    {
        // Efficiency 40% + price-per-kW 40% + warranty 20%
        double eff        = i.EfficiencyPct / 100.0;
        double pricePerKw = i.PriceSar / i.CapacityKw;
        double priceScore = 1 - (pricePerKw / 500.0);
        double warranty   = i.WarrantyYears / 10.0;
        return (eff * 0.40) + (priceScore * 0.40) + (warranty * 0.20);
    }

    private static double ScoreBattery(Battery b)
    {
        // Cycle life 40% + price-per-kWh 40% + DoD 20%
        double cycle        = b.CycleLife / 8000.0;
        double pricePerKwh  = b.PriceSar / b.CapacityKwh;
        double priceScore   = 1 - (pricePerKwh / 1500.0);
        double dod          = b.DodPct / 100.0;
        return (cycle * 0.40) + (priceScore * 0.40) + (dod * 0.20);
    }

    // ══════════════════════════════════════════════════════════════════════
    // Step 8 — Protection items (DC MCB, AC MCCB, SPD, disconnect, meter)
    // ══════════════════════════════════════════════════════════════════════

    private ProtectionSummaryDto BuildProtection(SizingContext c, int tier, RankedInverterDto bestInv)
    {
        ProtectionItem Get(string key) =>
            c.Protection.FirstOrDefault(p => p.Key == key)
                ?? throw new InvalidOperationException($"Missing protection item: {key}");

        var items = new List<ProtectionLineDto>();
        double total = 0;

        // DC MCB — one per inverter unit, sized to 1.25× inverter DC input current @ 600 V
        int dcAmps = (int)Math.Round((bestInv.ActualKw * 1000 / 600) * 1.25);
        var dcMcb  = Get("dc_mcb");
        items.Add(Line(dcMcb, $"{dcAmps}A DC", bestInv.UnitsRequired));
        total += items[^1].TotalPriceSar;

        // AC MCCB — one per inverter unit, sized to 1.25× output current @ 380 V 3-phase
        int acAmps = (int)Math.Round((bestInv.ActualKw * 1000 / 380) * 1.25);
        var acMccb = Get("ac_mccb");
        items.Add(Line(acMccb, $"{acAmps}A AC 3-phase", bestInv.UnitsRequired));
        total += items[^1].TotalPriceSar;

        // Surge protection — Type I+II for Tier 3, Type II otherwise
        var spd = tier == 3 ? Get("spd_type1_2") : Get("spd_type2");
        items.Add(Line(spd, tier == 3 ? "Type I+II" : "Type II", 1));
        total += items[^1].TotalPriceSar;

        // On-grid only: AC disconnect + bidirectional SEC meter
        if (c.Profile.GridScenario == "on_grid")
        {
            var disc = Get("ac_disconnect");
            items.Add(Line(disc, "SEC anti-islanding required", 1));
            total += items[^1].TotalPriceSar;

            var meter = Get("bidirectional_meter");
            items.Add(Line(meter, "Bidirectional — SEC net metering", 1));
            total += items[^1].TotalPriceSar;
        }
        else
        {
            var meter = Get("standalone_meter");
            items.Add(Line(meter, "Standalone consumption meter", 1));
            total += items[^1].TotalPriceSar;
        }

        return new ProtectionSummaryDto(items, Math.Round(total, 2));
    }

    private static ProtectionLineDto Line(ProtectionItem item, string spec, int qty) => new(
        Key:           item.Key,
        Description:   item.Description,
        Spec:          spec,
        Quantity:      qty,
        UnitPriceSar:  item.PriceSar,
        TotalPriceSar: Math.Round(item.PriceSar * qty, 2)
    );

    // ══════════════════════════════════════════════════════════════════════
    // BoS — mounting structure + DC cable + AC cable
    // ══════════════════════════════════════════════════════════════════════

    private BosDto BuildBos(SizingContext c, RankedPanelDto bestPanel, RankedInverterDto bestInv)
    {
        ProtectionItem Get(string key) =>
            c.Protection.FirstOrDefault(p => p.Key == key)
                ?? throw new InvalidOperationException($"Missing BoS item: {key}");

        // Mounting structure — priced per panel
        var mountItem = Get("mounting_structure_per_panel");
        double mountTotal = bestPanel.UnitsRequired * mountItem.PriceSar;
        var mountLine = new ProtectionLineDto(
            Key: mountItem.Key, Description: "Rooftop mounting structure",
            Spec: $"{bestPanel.UnitsRequired} panels",
            Quantity: bestPanel.UnitsRequired, UnitPriceSar: mountItem.PriceSar,
            TotalPriceSar: Math.Round(mountTotal, 2));

        // DC cable — ~15 m per panel string
        var dcCable = Get("dc_cable_per_meter");
        int dcMeters = bestPanel.UnitsRequired * 15;
        double dcTotal = dcMeters * dcCable.PriceSar;
        var dcLine = new ProtectionLineDto(
            Key: dcCable.Key, Description: "DC solar cable 6mm²",
            Spec: $"{dcMeters} m", Quantity: dcMeters,
            UnitPriceSar: dcCable.PriceSar, TotalPriceSar: Math.Round(dcTotal, 2));

        // AC cable — 20 m per inverter (rounded up to the nearest 50 kW block)
        var acCable = Get("ac_cable_per_meter");
        int acMeters = 20 * (int)Math.Ceiling(bestInv.ActualKw / 50.0);
        double acTotal = acMeters * acCable.PriceSar;
        var acLine = new ProtectionLineDto(
            Key: acCable.Key, Description: "AC output cable 10mm²",
            Spec: $"{acMeters} m", Quantity: acMeters,
            UnitPriceSar: acCable.PriceSar, TotalPriceSar: Math.Round(acTotal, 2));

        return new BosDto(
            MountingStructure: mountLine, DcCable: dcLine, AcCable: acLine,
            TotalSar: Math.Round(mountTotal + dcTotal + acTotal, 2));
    }

    // ══════════════════════════════════════════════════════════════════════
    // Step 10 — Financial model (10-year projection with degradation)
    // ══════════════════════════════════════════════════════════════════════

    private FinancialModelDto BuildFinancials(SizingContext c, double arrayKwp,
                                                double capexTotal, double generatorKva)
    {
        const double annualDegradation = 0.99;
        const int years = 10;

        var yearly = new List<FinancialYearDto>();
        double cumulative = 0.0;
        int?   breakEvenYear = null;

        double annualBaseline = c.Profile.MonthlyBillSar * 12;

        for (int yr = 1; yr <= years; yr++)
        {
            double degFactor = Math.Pow(annualDegradation, yr);
            double productionKwh = arrayKwp * c.Region.GhiKwhM2Day * 365
                                   * c.Derating.PerformanceRatio * degFactor;

            double gridSavings = productionKwh * c.Tariff.RateSarKwh;

            // Cap: savings can't exceed what you actually spend on electricity
            gridSavings = Math.Min(gridSavings, annualBaseline);

            double exportRevenue = 0.0;
            if (c.Profile.GridScenario == "on_grid")
            {
                double exportKwh = productionKwh * 0.20;  // assume 20% exported
                exportRevenue = exportKwh * c.Tariff.ExportRateSarKwh;
            }

            double dieselSavings = 0.0;
            if (c.Profile.HasGenerator && c.Profile.GridScenario == "off_grid")
            {
                double genKw       = generatorKva * 0.8;
                double hoursReplaced = 365 * 6 * degFactor;     // ~6 h/day offset by solar
                double fuelSavedL  = genKw * hoursReplaced * c.Constants.DieselConsumptionLphPerKva;
                dieselSavings      = fuelSavedL * c.Constants.DieselPriceSarPerLiter;
            }

            double totalSavings = Math.Round(gridSavings + exportRevenue + dieselSavings, 2);
            cumulative = Math.Round(cumulative + totalSavings, 2);

            yearly.Add(new FinancialYearDto(
                Year: yr, ProductionKwh: Math.Round(productionKwh, 0),
                GridSavingsSar: Math.Round(gridSavings, 2),
                ExportRevenueSar: Math.Round(exportRevenue, 2),
                DieselSavingsSar: Math.Round(dieselSavings, 2),
                TotalSavingsSar: totalSavings,
                CumulativeSavingsSar: cumulative,
                BaselineCostSar: Math.Round(annualBaseline * yr, 2)));

            if (breakEvenYear is null && cumulative >= capexTotal) breakEvenYear = yr;
        }

        double GetCum(int y) => yearly.FirstOrDefault(d => d.Year == y)?.CumulativeSavingsSar ?? 0;

        return new FinancialModelDto(
            CapexTotalSar:       Math.Round(capexTotal, 2),
            MonthlySavingsSar:   Math.Round(yearly[0].TotalSavingsSar / 12, 2),
            Year1SavingsSar:     GetCum(1),
            Year5SavingsSar:     GetCum(5),
            Year10SavingsSar:    GetCum(10),
            BreakEvenYear:       breakEvenYear,
            Baseline10yrCostSar: Math.Round(annualBaseline * 10, 2),
            Net10yrBenefitSar:   Math.Round(GetCum(10) - capexTotal, 2),
            YearlyData:          yearly
        );
    }

    // ══════════════════════════════════════════════════════════════════════
    // Handoff to Layer 2 — simulation config built from selected components
    // ══════════════════════════════════════════════════════════════════════

    private SimulationConfigDto BuildSimConfig(SizingContext c,
                                                RankedPanelDto panel,
                                                RankedBatteryDto battery,
                                                double generatorKva,
                                                double peakLoadKw)
    {
        return new SimulationConfigDto(
            UserType:        c.Profile.UserType,
            Region:          c.Profile.Region,
            GridScenario:    c.Profile.GridScenario,
            PeakLoadKw:      peakLoadKw,
            CriticalLoadPct: c.Profile.CriticalLoadPct ?? 30,
            ArrayKwp:        panel.ActualKwp,
            BatteryKwh:      battery.ActualKwh,
            DodPct:          battery.DodPct,
            Ghi:             c.Region.GhiKwhM2Day,
            Tariff:          new TariffDto(
                                 RateSarKwh:        c.Tariff.RateSarKwh,
                                 PeakRateSarKwh:    c.Tariff.PeakRateSarKwh,
                                 OffpeakRateSarKwh: c.Tariff.OffpeakRateSarKwh,
                                 ExportRateSarKwh:  c.Tariff.ExportRateSarKwh),
            PanelTempCoeff:  panel.TempCoefficientPct,
            HasGenerator:    c.Profile.HasGenerator,
            GeneratorKva:    generatorKva,
            SimulationYear:  1
        );
    }
}

/// <summary>
/// Private bag passed through the internal sizing steps so we don't
/// re-query the DB on every step or pass 8 arguments to every helper.
/// </summary>
internal sealed record SizingContext(
    FacilityProfileDto       Profile,
    Region                   Region,
    Tariff                   Tariff,
    DeratingFactors          Derating,
    SizingConstants          Constants,
    List<Panel>              Panels,
    List<Inverter>           Inverters,
    List<Battery>            Batteries,
    List<ProtectionItem>     Protection
);
