using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SolarBrain.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Batteries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Brand = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Tier = table.Column<int>(type: "INTEGER", nullable: false),
                    CapacityKwh = table.Column<double>(type: "REAL", nullable: false),
                    Chemistry = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    CycleLife = table.Column<int>(type: "INTEGER", nullable: false),
                    DodPct = table.Column<int>(type: "INTEGER", nullable: false),
                    WarrantyYears = table.Column<int>(type: "INTEGER", nullable: false),
                    PriceSar = table.Column<double>(type: "REAL", nullable: false),
                    VoltageV = table.Column<double>(type: "REAL", nullable: false),
                    MaxChargeKw = table.Column<double>(type: "REAL", nullable: false),
                    MaxDischargeKw = table.Column<double>(type: "REAL", nullable: false),
                    GridScenario = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    AvailableSa = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Batteries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeratingFactors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TempDerating = table.Column<double>(type: "REAL", nullable: false),
                    SoilingFactor = table.Column<double>(type: "REAL", nullable: false),
                    SystemLosses = table.Column<double>(type: "REAL", nullable: false),
                    AnnualDegradation = table.Column<double>(type: "REAL", nullable: false),
                    PerformanceRatio = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeratingFactors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Inverters",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Brand = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Tier = table.Column<int>(type: "INTEGER", nullable: false),
                    CapacityKw = table.Column<double>(type: "REAL", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    EfficiencyPct = table.Column<double>(type: "REAL", nullable: false),
                    WarrantyYears = table.Column<int>(type: "INTEGER", nullable: false),
                    PriceSar = table.Column<double>(type: "REAL", nullable: false),
                    MaxPvInputKw = table.Column<double>(type: "REAL", nullable: false),
                    GridScenario = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    AvailableSa = table.Column<bool>(type: "INTEGER", nullable: false),
                    SecApproved = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Inverters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Panels",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Brand = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Tier = table.Column<int>(type: "INTEGER", nullable: false),
                    PowerWp = table.Column<int>(type: "INTEGER", nullable: false),
                    EfficiencyPct = table.Column<double>(type: "REAL", nullable: false),
                    AreaM2 = table.Column<double>(type: "REAL", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    WarrantyYears = table.Column<int>(type: "INTEGER", nullable: false),
                    PriceSar = table.Column<double>(type: "REAL", nullable: false),
                    TempCoefficientPct = table.Column<double>(type: "REAL", nullable: false),
                    AvailableSa = table.Column<bool>(type: "INTEGER", nullable: false),
                    SecApproved = table.Column<bool>(type: "INTEGER", nullable: false),
                    GridScenario = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Panels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProtectionItems",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    PriceSar = table.Column<double>(type: "REAL", nullable: false),
                    PriceBasis = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProtectionItems", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "Regions",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    CitiesCsv = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    GhiKwhM2Day = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Regions", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "SizingConstants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SafetyFactorOnGrid = table.Column<double>(type: "REAL", nullable: false),
                    SafetyFactorOffGrid = table.Column<double>(type: "REAL", nullable: false),
                    AutonomyHoursOnGrid = table.Column<double>(type: "REAL", nullable: false),
                    AutonomyHoursOffGrid = table.Column<double>(type: "REAL", nullable: false),
                    PeakLoadFactor = table.Column<double>(type: "REAL", nullable: false),
                    InverterOversizeFactor = table.Column<double>(type: "REAL", nullable: false),
                    GeneratorPowerFactor = table.Column<double>(type: "REAL", nullable: false),
                    DieselConsumptionLphPerKva = table.Column<double>(type: "REAL", nullable: false),
                    DieselPriceSarPerLiter = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SizingConstants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tariffs",
                columns: table => new
                {
                    UserType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    RateSarKwh = table.Column<double>(type: "REAL", nullable: false),
                    PeakRateSarKwh = table.Column<double>(type: "REAL", nullable: false),
                    OffpeakRateSarKwh = table.Column<double>(type: "REAL", nullable: false),
                    ExportRateSarKwh = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tariffs", x => x.UserType);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Batteries");

            migrationBuilder.DropTable(
                name: "DeratingFactors");

            migrationBuilder.DropTable(
                name: "Inverters");

            migrationBuilder.DropTable(
                name: "Panels");

            migrationBuilder.DropTable(
                name: "ProtectionItems");

            migrationBuilder.DropTable(
                name: "Regions");

            migrationBuilder.DropTable(
                name: "SizingConstants");

            migrationBuilder.DropTable(
                name: "Tariffs");
        }
    }
}
