using SolarBrain.Api.Models;
using SolarBrain.Api.Models.Dtos;

namespace SolarBrain.Api.Services;

/// <summary>
/// C# port of the Python brain.py. Stateful 7-mode switching engine with
/// 2-cycle hysteresis confirmation and scenario injection.
/// </summary>
public class Brain : IBrain
{
    // ── Constants ─────────────────────────────────────────────────────────

    public const string ModeSolarOnly       = "SOLAR_ONLY";
    public const string ModeHybrid          = "HYBRID";
    public const string ModeBatteryBackup   = "BATTERY_BACKUP";
    public const string ModeEmergency       = "EMERGENCY";
    public const string ModeChargeMode      = "CHARGE_MODE";
    public const string ModeGridOnly        = "GRID_ONLY";
    public const string ModeGeneratorBackup = "GENERATOR_BACKUP";

    // Hysteresis thresholds — enter vs exit to prevent flicker
    private const double SolarEnterPct     = 92;
    private const double SolarExitPct      = 85;
    private const double HybridEnterPct    = 52;
    private const double HybridExitPct     = 45;
    private const double BatteryEnterSoc   = 42;
    private const double BatteryExitSoc    = 35;
    private const double ChargeEnterSoc    = 23;
    private const double ChargeExitSoc     = 30;

    private const int    HysteresisCycles    = 2;
    private const double GridCo2KgPerKwh     = 0.72;
    private const double IntervalH           = 0.25;
    private const int    PeakHoursStart      = 12;
    private const int    PeakHoursEnd        = 17;
    private static readonly HashSet<int> OffPeakHours =
        new HashSet<int>(Enumerable.Range(0, 7).Concat(Enumerable.Range(22, 2)));

    private static readonly Dictionary<string, string> ModeColors = new()
    {
        [ModeSolarOnly]       = "#BA7517",
        [ModeHybrid]          = "#1D9E75",
        [ModeBatteryBackup]   = "#534AB7",
        [ModeEmergency]       = "#A32D2D",
        [ModeChargeMode]      = "#185FA5",
        [ModeGridOnly]        = "#6B7280",
        [ModeGeneratorBackup] = "#374151",
    };

    private static readonly Dictionary<string, string> ModeDescriptions = new()
    {
        [ModeSolarOnly]       = "Solar covering full load",
        [ModeHybrid]          = "Solar + grid top-up",
        [ModeBatteryBackup]   = "Battery avoiding peak price",
        [ModeEmergency]       = "Grid down — battery protecting critical loads",
        [ModeChargeMode]      = "Charging battery on cheap off-peak grid",
        [ModeGridOnly]        = "Grid covering full load",
        [ModeGeneratorBackup] = "Generator backup — battery critically low",
    };

    // ── Configuration (from SimulationConfigDto) ──────────────────────────

    private readonly string _gridScenario;
    private readonly double _batteryKwh;
    private readonly double _dodPct;
    private readonly double _peakLoadKw;
    private readonly bool   _hasGenerator;
    private readonly double _genKwMax;
    private readonly string _userType;
    private readonly TariffDto _tariff;
    private readonly double _criticalLoadFraction;
    private readonly double _floorSoc;

    // ── State ─────────────────────────────────────────────────────────────

    private double _batterySoc      = 80.0;
    private string _currentMode     = ModeGridOnly;
    private string _previousMode    = ModeGridOnly;
    private int    _hysteresisCount = 0;

    // Scenario overrides
    private bool   _forceGridDown = false;
    private string? _forceSeason   = null;
    private double _loadSpikeKw   = 0.0;

    // Running totals
    private double _totalCo2SavedKg    = 0.0;
    private double _totalCostSar       = 0.0;
    private double _totalSolarKwh      = 0.0;
    private double _totalGridKwh       = 0.0;
    private double _totalExportKwh     = 0.0;
    private double _totalNetMeterSar   = 0.0;
    private double _totalGenFuelSar    = 0.0;

    private readonly List<DecisionLogEntryDto> _decisionLog = new();

    // ── Public properties (from IBrain) ───────────────────────────────────

    public string CurrentMode       => _currentMode;
    public double BatterySocPct     => Math.Round(_batterySoc, 1);
    public double TotalCo2SavedKg   => _totalCo2SavedKg;
    public double TotalCostSar      => _totalCostSar;
    public double TotalSolarKwh     => _totalSolarKwh;
    public double TotalGridKwh      => _totalGridKwh;
    public double TotalNetMeterSar  => _totalNetMeterSar;
    public int    IntervalCount     { get; private set; } = 0;

    public Brain(SimulationConfigDto cfg)
    {
        _gridScenario         = cfg.GridScenario;
        _batteryKwh           = cfg.BatteryKwh;
        _dodPct               = cfg.DodPct;
        _peakLoadKw           = cfg.PeakLoadKw;
        _hasGenerator         = cfg.HasGenerator;
        _genKwMax             = cfg.GeneratorKva * 0.8;
        _userType             = cfg.UserType;
        _tariff               = cfg.Tariff;
        _criticalLoadFraction = cfg.CriticalLoadPct / 100.0;
        _floorSoc             = (1 - _dodPct / 100.0) * 100.0;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static bool IsPeakHour(int hour) => hour >= PeakHoursStart && hour <= PeakHoursEnd;
    private static bool IsOffPeak(int hour)  => OffPeakHours.Contains(hour);

    private bool BatteryEmpty() => _batterySoc <= _floorSoc;

    private double BatteryUsableKwh()
    {
        double usableSoc = Math.Max(0, _batterySoc - _floorSoc);
        return (usableSoc / 100.0) * _batteryKwh;
    }

    // ── Decision tree — 7 rules with 2-cycle hysteresis ───────────────────

    private string Decide(double pvKw, double loadKw, int hour, bool gridAvailable)
    {
        double pvPct = loadKw > 0 ? (pvKw / loadKw) * 100.0 : 0;
        string currently = _currentMode;
        string candidate;

        // Priority 1: EMERGENCY (on-grid only)
        if (!gridAvailable && _gridScenario == "on_grid")
            return ModeEmergency;

        // Priority 2: GENERATOR_BACKUP (off-grid + battery empty + has gen)
        if (_gridScenario == "off_grid" && BatteryEmpty() && _hasGenerator)
            return ModeGeneratorBackup;

        // Candidate selection — match Python's if/elif chain with hysteresis
        if (pvPct >= SolarEnterPct && !BatteryEmpty())
            candidate = ModeSolarOnly;
        else if (currently == ModeSolarOnly && pvPct >= SolarExitPct && !BatteryEmpty())
            candidate = ModeSolarOnly;
        else if (IsPeakHour(hour) && _batterySoc > BatteryEnterSoc)
            candidate = ModeBatteryBackup;
        else if (currently == ModeBatteryBackup && IsPeakHour(hour) && _batterySoc > BatteryExitSoc)
            candidate = ModeBatteryBackup;
        else if (pvPct >= HybridEnterPct)
            candidate = ModeHybrid;
        else if (currently == ModeHybrid && pvPct >= HybridExitPct)
            candidate = ModeHybrid;
        else if (IsOffPeak(hour) && _batterySoc < ChargeEnterSoc && _gridScenario == "on_grid")
            candidate = ModeChargeMode;
        else if (currently == ModeChargeMode && _batterySoc < ChargeExitSoc && _gridScenario == "on_grid")
            candidate = ModeChargeMode;
        else
            candidate = ModeGridOnly;

        // Hysteresis confirmation — emergency modes are immediate
        if (candidate == ModeEmergency || candidate == ModeGeneratorBackup)
            return candidate;

        if (candidate == _currentMode)
        {
            _hysteresisCount = 0;
            return _currentMode;
        }

        _hysteresisCount++;
        if (_hysteresisCount >= HysteresisCycles)
        {
            _hysteresisCount = 0;
            return candidate;
        }
        return _currentMode;
    }

    // ── Energy split per mode ─────────────────────────────────────────────

    private record Split(
        double SolarKw, double BatteryKw, double GridKw,
        double GenKw, double ChargeKw, double ExportKw);

    private Split SplitEnergy(string mode, double pvKw, double loadKw, int hour)
    {
        double solarKw = 0, batteryKw = 0, gridKw = 0, genKw = 0, chargeKw = 0, exportKw = 0;
        double usable = BatteryUsableKwh();
        double maxDis = IntervalH > 0 ? usable / IntervalH : 0;

        switch (mode)
        {
            case ModeSolarOnly:
            {
                solarKw = Math.Min(pvKw, loadKw);
                double surplus = Math.Max(0, pvKw - loadKw);
                if (surplus > 0)
                {
                    if (_batterySoc < 95)
                        chargeKw = Math.Min(surplus, _batteryKwh * 0.3 / IntervalH);
                    else
                        exportKw = surplus;
                }
                break;
            }
            case ModeHybrid:
            {
                solarKw = Math.Min(pvKw, loadKw);
                double deficit = Math.Max(0, loadKw - pvKw);
                gridKw = deficit;
                double surplus = Math.Max(0, pvKw - loadKw);
                if (surplus > 0)
                {
                    if (_batterySoc < 95)
                        chargeKw = Math.Min(surplus, _batteryKwh * 0.2 / IntervalH);
                    else
                        exportKw = surplus;
                }
                break;
            }
            case ModeBatteryBackup:
            {
                solarKw = Math.Min(pvKw, loadKw);
                double deficit = Math.Max(0, loadKw - solarKw);
                batteryKw = Math.Min(deficit, Math.Min(maxDis, _batteryKwh * 0.4 / IntervalH));
                gridKw = Math.Max(0, deficit - batteryKw);
                break;
            }
            case ModeEmergency:
            {
                double critical = loadKw * _criticalLoadFraction;
                solarKw = Math.Min(pvKw, critical);
                double deficit = Math.Max(0, critical - solarKw);
                batteryKw = Math.Min(deficit, maxDis);
                break;
            }
            case ModeChargeMode:
            {
                solarKw = Math.Min(pvKw, loadKw);
                double deficit = Math.Max(0, loadKw - solarKw);
                double chargeAmt = Math.Min(
                    (100 - _batterySoc) / 100.0 * _batteryKwh / IntervalH,
                    _batteryKwh * 0.3 / IntervalH);
                gridKw   = deficit + chargeAmt;
                chargeKw = chargeAmt;
                break;
            }
            case ModeGeneratorBackup:
            {
                solarKw = Math.Min(pvKw, loadKw);
                double deficit = Math.Max(0, loadKw - solarKw);
                genKw = Math.Min(_genKwMax, deficit);
                batteryKw = Math.Max(0, deficit - genKw);
                if (_batterySoc < 30 && genKw < _genKwMax)
                {
                    chargeKw = Math.Min(
                        _genKwMax - genKw,
                        (30 - _batterySoc) / 100.0 * _batteryKwh / IntervalH);
                    genKw += chargeKw;
                }
                break;
            }
            default:  // GRID_ONLY
            {
                solarKw = Math.Min(pvKw, loadKw);
                double deficit = Math.Max(0, loadKw - solarKw);
                gridKw = deficit;
                double surplus = Math.Max(0, pvKw - loadKw);
                if (surplus > 0 && _batterySoc < 90)
                    chargeKw = Math.Min(surplus, _batteryKwh * 0.15 / IntervalH);
                break;
            }
        }

        // Farm-specific: reduce grid draw when PV is plentiful (irrigation shift)
        if (_userType == "farm" && pvKw > loadKw * 0.8)
            gridKw = Math.Max(0, gridKw * 0.5);

        // Update battery SOC
        double energyIn  = chargeKw   * IntervalH;
        double energyOut = batteryKw  * IntervalH;
        if (_batteryKwh > 0)
        {
            double socDelta = ((energyIn - energyOut) / _batteryKwh) * 100.0;
            _batterySoc = Math.Clamp(_batterySoc + socDelta, _floorSoc, 100.0);
        }

        return new Split(
            SolarKw: Math.Round(solarKw, 3),
            BatteryKw: Math.Round(batteryKw, 3),
            GridKw: Math.Round(gridKw, 3),
            GenKw: Math.Round(genKw, 3),
            ChargeKw: Math.Round(chargeKw, 3),
            ExportKw: Math.Round(exportKw, 3));
    }

    // ── KPI calculation ───────────────────────────────────────────────────

    private record Kpis(
        double Co2Saved, double CostSar, double NetMeterSar, double GenFuelSar,
        double SolarUtilPct, double GridDepPct);

    private Kpis ComputeKpis(Split s, double loadKw, double gridPrice, double pvKw)
    {
        double co2Saved    = s.SolarKw * IntervalH * GridCo2KgPerKwh;
        double costSar     = s.GridKw  * IntervalH * gridPrice;
        double netMeter    = s.ExportKw * IntervalH * _tariff.ExportRateSarKwh;
        double genFuelSar  = s.GenKw   * IntervalH * 0.25 * 0.75;
        double solarUtil   = pvKw > 0   ? (s.SolarKw / pvKw) * 100.0  : 0;
        double gridDep     = loadKw > 0 ? (s.GridKw / loadKw) * 100.0 : 0;

        _totalCo2SavedKg   += co2Saved;
        _totalCostSar      += costSar;
        _totalSolarKwh     += s.SolarKw  * IntervalH;
        _totalGridKwh      += s.GridKw   * IntervalH;
        _totalExportKwh    += s.ExportKw * IntervalH;
        _totalNetMeterSar  += netMeter;
        _totalGenFuelSar   += genFuelSar;

        return new Kpis(
            Co2Saved: Math.Round(co2Saved, 4),
            CostSar: Math.Round(costSar, 4),
            NetMeterSar: Math.Round(netMeter, 4),
            GenFuelSar: Math.Round(genFuelSar, 4),
            SolarUtilPct: Math.Round(solarUtil, 1),
            GridDepPct: Math.Round(gridDep, 1));
    }

    // ── Decision log ──────────────────────────────────────────────────────

    private void LogModeChange(string newMode, string reason, string timestamp)
    {
        _decisionLog.Add(new DecisionLogEntryDto(
            Timestamp: timestamp,
            FromMode:  _previousMode,
            ToMode:    newMode,
            Reason:    reason,
            BatterySoc: Math.Round(_batterySoc, 1)));

        if (_decisionLog.Count > 200)
            _decisionLog.RemoveRange(0, _decisionLog.Count - 200);
    }

    private string BuildReason(string newMode, double pvKw, double loadKw, int hour)
    {
        double pvPct = loadKw > 0 ? Math.Round((pvKw / loadKw) * 100) : 0;
        double soc = Math.Round(_batterySoc);
        return newMode switch
        {
            ModeEmergency       => $"Grid outage detected — protecting critical loads ({_criticalLoadFraction * 100:0}% of load)",
            ModeGeneratorBackup => $"Battery at floor SOC {soc}% — generator activated",
            ModeSolarOnly       => $"Solar at {pvPct}% of load — battery SOC {soc}%",
            ModeBatteryBackup   => $"Peak hours (hour {hour}) — battery SOC {soc}%, avoiding peak grid price",
            ModeHybrid          => $"Solar at {pvPct}% of load — grid covering deficit",
            ModeChargeMode      => $"Off-peak hours — battery SOC low at {soc}%, charging cheaply",
            _                    => $"Solar at {pvPct}% of load — insufficient for HYBRID threshold",
        };
    }

    // ══════════════════════════════════════════════════════════════════════
    // Public API — IBrain implementation
    // ══════════════════════════════════════════════════════════════════════

    public SimulationStateDto Step(DatasetRow row, int intervalCount, double progressPct, int currentIndex)
    {
        string timestamp = row.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        int    hour      = row.HourOfDay;
        double pvKw      = row.PvOutputKw;
        double loadKw    = row.FacilityLoadKw + _loadSpikeKw;    // scenario override
        double gridPrice = row.GridPriceSarKwh;
        bool   gridAvail = _forceGridDown ? false : row.GridAvailable;
        string season    = _forceSeason ?? row.Season;

        // Decide mode
        string newMode = Decide(pvKw, loadKw, hour, gridAvail);

        if (newMode != _currentMode)
        {
            string reason = BuildReason(newMode, pvKw, loadKw, hour);
            LogModeChange(newMode, reason, timestamp);
        }
        _previousMode = _currentMode;
        _currentMode  = newMode;

        // Split + KPIs
        var split = SplitEnergy(newMode, pvKw, loadKw, hour);
        var kpis  = ComputeKpis(split, loadKw, gridPrice, pvKw);

        IntervalCount = intervalCount;

        // Last 10 decisions
        var recent = _decisionLog.Count <= 10
            ? _decisionLog.ToList()
            : _decisionLog.GetRange(_decisionLog.Count - 10, 10);

        return new SimulationStateDto(
            Timestamp:      timestamp,
            Hour:           hour,
            Season:         season,
            Interval:       intervalCount,
            Mode:           newMode,
            ModeColor:      ModeColors[newMode],
            ModeDescription:ModeDescriptions[newMode],
            PvOutputKw:     Math.Round(pvKw, 2),
            LoadKw:         Math.Round(loadKw, 2),
            BatterySocPct:  Math.Round(_batterySoc, 1),
            GridAvailable:  gridAvail,
            SolarKw:             split.SolarKw,
            BatteryDischargeKw:  split.BatteryKw,
            GridKw:              split.GridKw,
            GeneratorKw:         split.GenKw,
            BatteryChargeKw:     split.ChargeKw,
            GridExportKw:        split.ExportKw,
            Co2SavedKg:             kpis.Co2Saved,
            CostSar:                kpis.CostSar,
            NetMeteringRevenueSar:  kpis.NetMeterSar,
            GeneratorFuelCostSar:   kpis.GenFuelSar,
            SolarUtilizationPct:    kpis.SolarUtilPct,
            GridDependencyPct:      kpis.GridDepPct,
            TotalCo2SavedKg:     Math.Round(_totalCo2SavedKg, 2),
            TotalCostSar:        Math.Round(_totalCostSar, 2),
            TotalSolarKwh:       Math.Round(_totalSolarKwh, 2),
            TotalGridKwh:        Math.Round(_totalGridKwh, 2),
            TotalNetMeterSar:    Math.Round(_totalNetMeterSar, 2),
            RecentDecisions:     recent,
            ProgressPct:         progressPct,
            CurrentIndex:        currentIndex);
    }

    public void InjectScenario(string scenario, double? value = null)
    {
        switch (scenario)
        {
            case "grid_outage":          _forceGridDown = true;  break;
            case "grid_restore":         _forceGridDown = false; break;
            case "season_summer":        _forceSeason = "summer"; break;
            case "season_moderate":      _forceSeason = "moderate"; break;
            case "season_winter":        _forceSeason = "winter"; break;
            case "season_reset":         _forceSeason = null; break;
            case "load_spike":           _loadSpikeKw = value ?? _peakLoadKw * 0.3; break;
            case "load_restore":         _loadSpikeKw = 0.0; break;
            case "low_battery":          _batterySoc = _floorSoc + 2; break;
            case "low_battery_restore":  _batterySoc = 80.0; break;
        }
    }

    public void Reset()
    {
        _batterySoc       = 80.0;
        _currentMode      = ModeGridOnly;
        _previousMode     = ModeGridOnly;
        _hysteresisCount  = 0;
        _forceGridDown    = false;
        _forceSeason      = null;
        _loadSpikeKw      = 0.0;
        _totalCo2SavedKg  = 0.0;
        _totalCostSar     = 0.0;
        _totalSolarKwh    = 0.0;
        _totalGridKwh     = 0.0;
        _totalExportKwh   = 0.0;
        _totalNetMeterSar = 0.0;
        _totalGenFuelSar  = 0.0;
        IntervalCount     = 0;
        _decisionLog.Clear();
    }
}
