using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using SolarBrain.Api.Models;
using SolarBrain.Api.Models.Dtos;

namespace SolarBrain.Api.Services;

/// <summary>
/// C# port of Python's SimulationRunner. Holds the brain + the full-year
/// 15-min interval dataset in memory and exposes UI-facing operations.
/// Registered as a singleton — state persists for the lifetime of the
/// process so the frontend can poll /simulate/next without re-loading.
/// </summary>
public class SimulationRunner : ISimulationRunner
{
    private readonly ILogger<SimulationRunner> _log;
    private readonly object _gate = new();  // protects state from concurrent requests

    private IBrain?              _brain;
    private SimulationConfigDto? _config;
    private List<DatasetRow>     _rows = new();
    private int                  _currentIndex = 0;
    private int                  _speed         = 1;
    private double               _cloudFactor   = 1.0;

    private readonly List<SimulationStateDto> _stateHistory = new();

    public bool IsLoaded => _brain != null && _rows.Count > 0;

    public SimulationRunner(ILogger<SimulationRunner> log) => _log = log;

    // ── Load ─────────────────────────────────────────────────────────────

    public void Load(SimulationConfigDto config, string datasetPath)
    {
        lock (_gate)
        {
            _config = config;
            _brain  = new Brain(config);
            _rows   = LoadCsv(datasetPath);
            _cloudFactor  = 1.0;
            _speed        = 1;
            _stateHistory.Clear();
            _currentIndex = ResolveStartIndex();
            _log.LogInformation("Simulation loaded — {Rows} rows, starting at index {Idx}",
                                 _rows.Count, _currentIndex);
        }
    }

    private static List<DatasetRow> LoadCsv(string path)
    {
        var cfg = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            HeaderValidated   = null,
        };
        using var reader = new StreamReader(path);
        using var csv    = new CsvReader(reader, cfg);
        return csv.GetRecords<DatasetRow>().ToList();
    }

    /// <summary>
    /// Mirror Python's logic: find the first row matching the current
    /// real-world hour-of-day and season so demos start at "now".
    /// </summary>
    private int ResolveStartIndex()
    {
        if (_rows.Count == 0) return 0;

        var now = DateTime.Now;
        string targetSeason = now.Month switch
        {
            1 or 2 or 12              => "winter",
            3 or 4 or 5 or 10 or 11   => "moderate",
            _                          => "summer",
        };

        for (int i = 0; i < _rows.Count; i++)
        {
            if (_rows[i].HourOfDay == now.Hour && _rows[i].Season == targetSeason)
                return i;
        }
        return 0;
    }

    // ── Step + history ───────────────────────────────────────────────────

    public SimulationStateDto? NextStep()
    {
        lock (_gate)
        {
            if (_brain is null || _rows.Count == 0 || _currentIndex >= _rows.Count)
                return null;

            SimulationStateDto? last = null;
            for (int step = 0; step < _speed; step++)
            {
                if (_currentIndex >= _rows.Count) break;

                // Clone the row and apply cloud factor (scenario override).
                var raw = _rows[_currentIndex];
                var row = new DatasetRow
                {
                    Timestamp = raw.Timestamp, Season = raw.Season, Month = raw.Month,
                    HourOfDay = raw.HourOfDay, IsWeekend = raw.IsWeekend,
                    IsPeakHour = raw.IsPeakHour,
                    SolarIrradianceWm2 = raw.SolarIrradianceWm2,
                    PanelTempCelsius = raw.PanelTempCelsius,
                    PvOutputKw = raw.PvOutputKw * _cloudFactor,
                    FacilityLoadKw = raw.FacilityLoadKw,
                    CriticalLoadKw = raw.CriticalLoadKw,
                    ShiftableLoadKw = raw.ShiftableLoadKw,
                    BatterySocPct = raw.BatterySocPct,
                    BatteryChargeKw = raw.BatteryChargeKw,
                    BatteryDischargeKw = raw.BatteryDischargeKw,
                    GridDrawKw = raw.GridDrawKw, GridExportKw = raw.GridExportKw,
                    GridPriceSarKwh = raw.GridPriceSarKwh,
                    GridAvailable = raw.GridAvailable,
                    GeneratorOutputKw = raw.GeneratorOutputKw,
                    GeneratorFuelCostSar = raw.GeneratorFuelCostSar,
                    EnergySourceMode = raw.EnergySourceMode,
                    Co2SavedKg = raw.Co2SavedKg, CostSar = raw.CostSar,
                    NetMeteringRevenueSar = raw.NetMeteringRevenueSar,
                    SolarUtilizationPct = raw.SolarUtilizationPct,
                    GridDependencyPct = raw.GridDependencyPct,
                };

                int intervalCount = _brain.IntervalCount + 1;
                double progress   = Math.Round((double)_currentIndex / _rows.Count * 100.0, 1);
                last = _brain.Step(row, intervalCount, progress, _currentIndex);

                _stateHistory.Add(last);
                if (_stateHistory.Count > 500)
                    _stateHistory.RemoveRange(0, _stateHistory.Count - 500);

                _currentIndex++;
            }
            return last;
        }
    }

    public IReadOnlyList<SimulationStateDto> GetHistory(int lastN)
    {
        lock (_gate)
        {
            if (_stateHistory.Count == 0) return Array.Empty<SimulationStateDto>();
            int take = Math.Min(lastN, _stateHistory.Count);
            return _stateHistory.GetRange(_stateHistory.Count - take, take);
        }
    }

    public SimulationSummaryDto GetSummary()
    {
        lock (_gate)
        {
            if (_brain is null)
            {
                return new SimulationSummaryDto(0, 0, 0, 0, 0, 0, 0, "GRID_ONLY", 80.0, 0);
            }
            double hours = _brain.IntervalCount * 0.25;
            double solarAndGrid = _brain.TotalSolarKwh + _brain.TotalGridKwh;
            double solarFraction = solarAndGrid > 0
                ? Math.Round(_brain.TotalSolarKwh / solarAndGrid * 100.0, 1)
                : 0;

            return new SimulationSummaryDto(
                IntervalsRun:       _brain.IntervalCount,
                HoursSimulated:     Math.Round(hours, 1),
                TotalCo2SavedKg:    Math.Round(_brain.TotalCo2SavedKg, 2),
                TotalCostSar:       Math.Round(_brain.TotalCostSar, 2),
                TotalSolarKwh:      Math.Round(_brain.TotalSolarKwh, 2),
                TotalGridKwh:       Math.Round(_brain.TotalGridKwh, 2),
                TotalNetMeterSar:   Math.Round(_brain.TotalNetMeterSar, 2),
                CurrentMode:        _brain.CurrentMode,
                BatterySocPct:      _brain.BatterySocPct,
                SolarFractionPct:   solarFraction);
        }
    }

    // ── Controls ─────────────────────────────────────────────────────────

    public void InjectScenario(string scenario, double? value = null)
    {
        lock (_gate)
        {
            // Cloud cover is handled at the runner level (scales PV output from the CSV)
            // — everything else goes to the brain.
            switch (scenario)
            {
                case "cloud_cover":    _cloudFactor = 0.4; break;
                case "cloud_restore":  _cloudFactor = 1.0; break;
                default:               _brain?.InjectScenario(scenario, value); break;
            }
        }
    }

    public void SetSpeed(int speed)
    {
        lock (_gate) { _speed = Math.Clamp(speed, 1, 20); }
    }

    public void JumpToSeason(string season)
    {
        lock (_gate)
        {
            if (_rows.Count == 0) return;
            int idx = _rows.FindIndex(r => r.Season == season);
            if (idx >= 0) _currentIndex = idx;
            _brain?.InjectScenario($"season_{season}");
        }
    }

    public void JumpToHour(int hour)
    {
        lock (_gate)
        {
            if (_rows.Count == 0) return;
            for (int i = _currentIndex; i < _rows.Count; i++)
            {
                if (_rows[i].HourOfDay == hour)
                {
                    _currentIndex = i;
                    return;
                }
            }
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _brain?.Reset();
            _currentIndex = 0;
            _cloudFactor  = 1.0;
            _speed        = 1;
            _stateHistory.Clear();
        }
    }

    /// <summary>
    /// Debug-only: override the auto-chosen starting index.
    /// Used by the BrainTestController to match the Python test's hardcoded start.
    /// </summary>
    public void OverrideStartIndex(int idx)
    {
        lock (_gate)
        {
            _currentIndex = Math.Clamp(idx, 0, Math.Max(0, _rows.Count - 1));
        }
    }
}
