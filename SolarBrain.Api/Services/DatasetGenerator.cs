using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using SolarBrain.Api.Models;
using SolarBrain.Api.Models.Dtos;

namespace SolarBrain.Api.Services;

/// <summary>
/// C# port of the Python dataset_generator.py. Produces a year-long,
/// 15-minute-resolution CSV tuned to Saudi Arabian climate conditions.
/// Seeded RNG → reproducible output across runs with identical config.
/// </summary>
public class DatasetGenerator : IDatasetGenerator
{
    private readonly ILogger<DatasetGenerator> _log;

    public DatasetGenerator(ILogger<DatasetGenerator> log) => _log = log;

    // ── Constants ─────────────────────────────────────────────────────────

    private static readonly Dictionary<int, string> SeasonMap = new()
    {
        [1]  = "winter",   [2]  = "winter",
        [3]  = "moderate", [4]  = "moderate", [5]  = "moderate",
        [6]  = "summer",   [7]  = "summer",   [8]  = "summer", [9] = "summer",
        [10] = "moderate", [11] = "moderate",
        [12] = "winter",
    };

    /// <summary>Peak irradiance by season (W/m² at solar noon)</summary>
    private static readonly Dictionary<string, double> PeakIrradiance = new()
    {
        ["summer"]   = 1000,
        ["moderate"] = 750,
        ["winter"]   = 430,
    };

    /// <summary>Ambient panel temp by season (day / night in °C)</summary>
    private static readonly Dictionary<string, (double Day, double Night)> PanelTempAmbient = new()
    {
        ["summer"]   = (65, 35),
        ["moderate"] = (48, 22),
        ["winter"]   = (32, 15),
    };

    /// <summary>Sunrise / sunset hour by season (approximate for SA)</summary>
    private static readonly Dictionary<string, (double Sunrise, double Sunset)> Daylight = new()
    {
        ["summer"]   = (5.5, 19.0),
        ["moderate"] = (6.0, 18.0),
        ["winter"]   = (6.5, 17.0),
    };

    // Hourly load shape multipliers (0-23) — industrial shift pattern
    private static readonly double[] FacilityLoadProfile =
    {
        0.45, 0.42, 0.40, 0.40, 0.42, 0.50,   // 00-05 night
        0.65, 0.82, 0.95, 1.00, 1.00, 1.00,   // 06-11 ramp
        0.98, 0.98, 1.00, 1.00, 0.98, 0.95,   // 12-17 peak
        0.85, 0.75, 0.68, 0.60, 0.52, 0.47,   // 18-23 wind down
    };

    private static readonly double[] FarmLoadProfile =
    {
        0.20, 0.18, 0.18, 0.18, 0.20, 0.30,
        0.50, 0.80, 1.00, 1.00, 1.00, 1.00,
        1.00, 1.00, 0.90, 0.80, 0.70, 0.55,
        0.40, 0.30, 0.25, 0.22, 0.20, 0.20,
    };

    private static readonly double[] ResidentialLoadProfile =
    {
        0.35, 0.30, 0.28, 0.28, 0.30, 0.38,
        0.50, 0.65, 0.70, 0.68, 0.65, 0.70,
        0.75, 0.80, 0.85, 0.90, 0.95, 1.00,
        0.95, 0.90, 0.82, 0.70, 0.58, 0.42,
    };

    // SA derating factors — identical to Python
    private const double TempDerating   = 0.82;
    private const double SoilingFactor  = 0.93;
    private const double SystemLosses   = 0.86;
    private const double GridCo2Factor  = 0.72;   // kg CO2 per kWh
    private const double IntervalH      = 0.25;

    // ── Helpers for the physical models ───────────────────────────────────

    private static double GetGridPrice(int hour, TariffDto tariff, string gridScenario)
    {
        if (gridScenario != "on_grid") return 0.0;
        if (hour >= 12 && hour <= 17) return tariff.PeakRateSarKwh;
        if (hour >= 22 || hour <= 6)  return tariff.OffpeakRateSarKwh;
        return tariff.RateSarKwh;
    }

    // ── Solar irradiance model (Gaussian bell curve) ──────────────────────

    private static double CalculateIrradiance(double hourDecimal, string season, Random rng)
    {
        var (sunrise, sunset) = Daylight[season];
        if (hourDecimal < sunrise || hourDecimal > sunset) return 0.0;

        double dayLen = sunset - sunrise;
        double midDay = sunrise + dayLen / 2.0;
        double pos    = (hourDecimal - midDay) / (dayLen / 2.0);

        double raw = Math.Exp(-2.5 * pos * pos);
        double peak = PeakIrradiance[season];
        double irradiance = raw * peak;

        // ±8 % cloud noise
        double noise = (rng.NextDouble() * 0.16) - 0.08;
        irradiance *= (1 + noise);

        return Math.Max(0.0, irradiance);
    }

    private static double CalculatePvOutput(double irradianceWm2, double panelTempC,
                                             double arrayKwp, double panelTempCoeff)
    {
        if (irradianceWm2 <= 0) return 0.0;

        // Temperature derating — extra loss above 25°C STC
        double tempAboveStc = Math.Max(0, panelTempC - 25);
        double tempLoss = 1 + (panelTempCoeff / 100.0) * tempAboveStc;
        tempLoss = Math.Max(0.5, tempLoss);   // physical floor

        double pvOutput = (irradianceWm2 / 1000.0) * arrayKwp
                          * tempLoss * SoilingFactor * SystemLosses;
        return Math.Max(0.0, Math.Round(pvOutput, 3));
    }

    private static double CalculatePanelTemp(double hourDecimal, string season,
                                              double irradianceWm2, Random rng)
    {
        var (day, night) = PanelTempAmbient[season];
        if (irradianceWm2 <= 0) return night;

        double irrFactor = irradianceWm2 / 1000.0;
        double panelTemp = night + (day - night) * irrFactor;
        panelTemp += (rng.NextDouble() * 4.0) - 2.0;     // ±2°C noise
        return Math.Round(panelTemp, 1);
    }

    // ── Load profile with season + weekend + noise ────────────────────────

    private static double CalculateLoad(int hour, string userType, double peakLoadKw,
                                         string season, bool isWeekend, Random rng)
    {
        var profile = userType switch
        {
            "farm"        => FarmLoadProfile,
            "residential" => ResidentialLoadProfile,
            _             => FacilityLoadProfile,
        };
        double load = profile[hour] * peakLoadKw;

        // Seasonal adjustments
        if (season == "summer" && (userType == "residential" || userType == "facility"))
            load *= 1.15;
        else if (season == "winter")
            load *= 1.05;

        // Weekend reduction
        if (isWeekend)
            load *= (userType == "facility" ? 0.70 : 0.80);

        // ±5 % random noise
        double noise = (rng.NextDouble() * 0.10) - 0.05;
        load *= (1 + noise);

        return Math.Round(Math.Max(0.1, load), 3);
    }

    // ══════════════════════════════════════════════════════════════════════
    // Main generator — builds the full year dataset and writes it to CSV
    // ══════════════════════════════════════════════════════════════════════

    public int GenerateAndSave(SimulationConfigDto config, string outputPath)
    {
        var rng = new Random(42);   // reproducible across runs

        double arrayKwp       = config.ArrayKwp;
        double batteryKwh     = config.BatteryKwh;
        double dodFraction    = config.DodPct / 100.0;
        double ghi            = config.Ghi;
        double peakLoadKw     = config.PeakLoadKw;
        double criticalPct    = config.CriticalLoadPct / 100.0;
        double panelTempCoeff = config.PanelTempCoeff;
        bool   hasGenerator   = config.HasGenerator;
        double genKwMax       = hasGenerator ? config.GeneratorKva * 0.8 : 0.0;
        double degradation    = Math.Pow(0.99, config.SimulationYear);

        // Battery state tracked through the whole simulation
        double floorSoc   = (1 - dodFraction) * 100.0;
        double batterySoc = 80.0;
        double usableKwh  = batteryKwh * dodFraction;

        var rows = new List<DatasetRow>(capacity: 35_136);
        var start = new DateTime(2024, 1, 1, 0, 0, 0);
        var end   = new DateTime(2024, 12, 31, 23, 45, 0);

        for (var ts = start; ts <= end; ts = ts.AddMinutes(15))
        {
            int month        = ts.Month;
            int hour         = ts.Hour;
            int minute       = ts.Minute;
            double hourDec   = hour + minute / 60.0;
            string season    = SeasonMap[month];
            bool isWeekend   = ts.DayOfWeek == DayOfWeek.Saturday || ts.DayOfWeek == DayOfWeek.Sunday;

            // Solar — scaled to local GHI (model uses 6.0 base)
            double irradiance           = CalculateIrradiance(hourDec, season, rng) * (ghi / 6.0);
            double irradianceEffective  = irradiance * degradation;
            double panelTemp            = CalculatePanelTemp(hourDec, season, irradiance, rng);
            double pvOutput             = CalculatePvOutput(irradianceEffective, panelTemp,
                                                             arrayKwp, panelTempCoeff);

            // Load
            double load           = CalculateLoad(hour, config.UserType, peakLoadKw, season, isWeekend, rng);
            double criticalLoad   = Math.Round(load * criticalPct, 3);
            double shiftableLoad  = Math.Round(load * 0.25, 3);

            // Grid
            double gridPrice     = GetGridPrice(hour, config.Tariff, config.GridScenario);
            bool   isPeakHour    = hour >= 12 && hour <= 17;
            bool   gridAvailable = config.GridScenario == "on_grid";

            // ── Simplified mode + energy split (no hysteresis — the real brain overrides at runtime) ──
            double batteryChargeKw    = 0;
            double batteryDischargeKw = 0;
            double solarToLoad        = Math.Min(pvOutput, load);
            double solarSurplus       = Math.Max(0, pvOutput - load);
            double loadDeficit        = Math.Max(0, load - pvOutput);
            double gridDrawKw         = 0;
            double gridExportKw       = 0;
            double genOutputKw        = 0;
            string mode               = "GRID_ONLY";

            if (!gridAvailable)   // off-grid
            {
                double availableBattKwh = (batterySoc - floorSoc) / 100.0 * batteryKwh;
                double battCanCover     = availableBattKwh / IntervalH;

                if (pvOutput >= load * 0.9 && batterySoc > floorSoc + 10)
                {
                    mode = "SOLAR_ONLY";
                    batteryChargeKw = Math.Min(solarSurplus, usableKwh / IntervalH * 0.2);
                }
                else if (hasGenerator && batterySoc <= floorSoc)
                {
                    mode = "GENERATOR_BACKUP";
                    genOutputKw = Math.Min(genKwMax, load - pvOutput);
                }
                else if (battCanCover > 0)
                {
                    mode = "HYBRID";
                    batteryDischargeKw = Math.Min(loadDeficit,
                                                   Math.Min(battCanCover, usableKwh / IntervalH));
                }
            }
            else   // on-grid
            {
                if (pvOutput >= load * 0.92 && batterySoc > floorSoc + 10)
                {
                    mode = "SOLAR_ONLY";
                    if (solarSurplus > 0)
                    {
                        if (batterySoc < 95) batteryChargeKw = Math.Min(solarSurplus, usableKwh / IntervalH * 0.3);
                        else                 gridExportKw    = solarSurplus;
                    }
                }
                else if (isPeakHour && batterySoc > 42)
                {
                    mode = "BATTERY_BACKUP";
                    batteryDischargeKw = Math.Min(loadDeficit, usableKwh / IntervalH * 0.4);
                    gridDrawKw         = Math.Max(0, load - pvOutput - batteryDischargeKw);
                }
                else if (pvOutput >= load * 0.52)
                {
                    mode = "HYBRID";
                    gridDrawKw = Math.Max(0, load - pvOutput);
                    if (solarSurplus > 0 && batterySoc < 95)
                        batteryChargeKw = Math.Min(solarSurplus, usableKwh / IntervalH * 0.2);
                    else if (solarSurplus > 0)
                        gridExportKw = solarSurplus;
                }
                else if ((hour <= 6 || hour >= 22) && batterySoc < 23)
                {
                    mode = "CHARGE_MODE";
                    double chargeAmt = Math.Min(usableKwh / IntervalH * 0.3,
                                                 (100 - batterySoc) / 100.0 * batteryKwh / IntervalH);
                    gridDrawKw        = load + chargeAmt;
                    batteryChargeKw   = chargeAmt;
                }
                else
                {
                    mode = "GRID_ONLY";
                    gridDrawKw = load;
                }
            }

            // ── Battery SOC update ──
            double energyIn  = batteryChargeKw    * IntervalH;
            double energyOut = batteryDischargeKw * IntervalH;
            if (batteryKwh > 0)
            {
                batterySoc += (energyIn  / batteryKwh * 100.0)
                            - (energyOut / batteryKwh * 100.0);
                batterySoc = Math.Clamp(batterySoc, floorSoc, 100.0);
            }

            // ── KPIs ──
            double co2Saved       = Math.Round(solarToLoad * IntervalH * GridCo2Factor, 4);
            double costSar        = Math.Round(gridDrawKw * IntervalH * gridPrice, 4);
            double genFuelSar     = genOutputKw > 0 ? Math.Round(genOutputKw * IntervalH * 0.25 * 0.75, 4) : 0.0;
            double netMeterRev    = Math.Round(gridExportKw * IntervalH * config.Tariff.ExportRateSarKwh, 4);
            double solarUtilPct   = pvOutput > 0 ? Math.Round(solarToLoad / pvOutput * 100.0, 1) : 0.0;
            double gridDepPct     = load > 0     ? Math.Round(gridDrawKw   / load * 100.0, 1)     : 0.0;

            rows.Add(new DatasetRow
            {
                Timestamp             = ts,
                Season                = season,
                Month                 = month,
                HourOfDay             = hour,
                IsWeekend             = isWeekend,
                IsPeakHour            = isPeakHour,
                SolarIrradianceWm2    = Math.Round(irradiance, 2),
                PanelTempCelsius      = panelTemp,
                PvOutputKw            = pvOutput,
                FacilityLoadKw        = load,
                CriticalLoadKw        = criticalLoad,
                ShiftableLoadKw       = shiftableLoad,
                BatterySocPct         = Math.Round(batterySoc, 2),
                BatteryChargeKw       = Math.Round(batteryChargeKw, 3),
                BatteryDischargeKw    = Math.Round(batteryDischargeKw, 3),
                GridDrawKw            = Math.Round(gridDrawKw, 3),
                GridExportKw          = Math.Round(gridExportKw, 3),
                GridPriceSarKwh       = gridPrice,
                GridAvailable         = gridAvailable,
                GeneratorOutputKw     = Math.Round(genOutputKw, 3),
                GeneratorFuelCostSar  = genFuelSar,
                EnergySourceMode      = mode,
                Co2SavedKg            = co2Saved,
                CostSar               = costSar,
                NetMeteringRevenueSar = netMeterRev,
                SolarUtilizationPct   = solarUtilPct,
                GridDependencyPct     = gridDepPct,
            });
        }

        // ── Write CSV ─────────────────────────────────────────────────────
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var csvCfg = new CsvConfiguration(CultureInfo.InvariantCulture) { HasHeaderRecord = true };
        using (var writer = new StreamWriter(outputPath))
        using (var csv    = new CsvWriter(writer, csvCfg))
        {
            csv.WriteRecords(rows);
        }

        _log.LogInformation("Dataset generated — {Rows} rows → {Path}", rows.Count, outputPath);
        return rows.Count;
    }
}
