using CsvHelper.Configuration.Attributes;

namespace SolarBrain.Api.Models;

/// <summary>
/// One 15-minute interval read from simulation.csv. Field names match the
/// CSV headers produced by the Python dataset generator (snake_case), so
/// CsvHelper maps them directly without custom config.
/// </summary>
public class DatasetRow
{
    [Name("timestamp")]              public DateTime Timestamp       { get; set; }
    [Name("season")]                 public string   Season          { get; set; } = "moderate";
    [Name("month")]                  public int      Month           { get; set; }
    [Name("hour_of_day")]            public int      HourOfDay       { get; set; }
    [Name("is_weekend")]             public bool     IsWeekend       { get; set; }
    [Name("is_peak_hour")]           public bool     IsPeakHour      { get; set; }

    [Name("solar_irradiance_wm2")]   public double SolarIrradianceWm2 { get; set; }
    [Name("panel_temp_celsius")]     public double PanelTempCelsius   { get; set; }
    [Name("pv_output_kw")]           public double PvOutputKw         { get; set; }

    [Name("facility_load_kw")]       public double FacilityLoadKw  { get; set; }
    [Name("critical_load_kw")]       public double CriticalLoadKw  { get; set; }
    [Name("shiftable_load_kw")]      public double ShiftableLoadKw { get; set; }

    [Name("battery_soc_pct")]        public double BatterySocPct       { get; set; }
    [Name("battery_charge_kw")]      public double BatteryChargeKw     { get; set; }
    [Name("battery_discharge_kw")]   public double BatteryDischargeKw  { get; set; }

    [Name("grid_draw_kw")]           public double GridDrawKw       { get; set; }
    [Name("grid_export_kw")]         public double GridExportKw     { get; set; }
    [Name("grid_price_sar_kwh")]     public double GridPriceSarKwh  { get; set; }

    // CSV writes True/False — CsvHelper's default bool parser handles this.
    [Name("grid_available")]         public bool   GridAvailable    { get; set; }

    [Name("generator_output_kw")]        public double GeneratorOutputKw     { get; set; }
    [Name("generator_fuel_cost_sar")]    public double GeneratorFuelCostSar  { get; set; }
    [Name("energy_source_mode")]         public string EnergySourceMode      { get; set; } = "GRID_ONLY";

    [Name("co2_saved_kg")]              public double Co2SavedKg             { get; set; }
    [Name("cost_sar")]                   public double CostSar                { get; set; }
    [Name("net_metering_revenue_sar")]   public double NetMeteringRevenueSar  { get; set; }
    [Name("solar_utilization_pct")]      public double SolarUtilizationPct    { get; set; }
    [Name("grid_dependency_pct")]        public double GridDependencyPct     { get; set; }
}
