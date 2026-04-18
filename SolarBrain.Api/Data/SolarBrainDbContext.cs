using Microsoft.EntityFrameworkCore;
using SolarBrain.Api.Models.Entities;

namespace SolarBrain.Api.Data;

/// <summary>
/// EF Core DbContext — one SQLite database file holds every component and
/// reference value the sizing engine needs.
/// </summary>
public class SolarBrainDbContext : DbContext
{
    public SolarBrainDbContext(DbContextOptions<SolarBrainDbContext> options)
        : base(options)
    {
    }

    // Component catalogue
    public DbSet<Panel>    Panels    => Set<Panel>();
    public DbSet<Inverter> Inverters => Set<Inverter>();
    public DbSet<Battery>  Batteries => Set<Battery>();

    // Reference data
    public DbSet<Region>          Regions          => Set<Region>();
    public DbSet<Tariff>          Tariffs          => Set<Tariff>();
    public DbSet<ProtectionItem>  ProtectionItems  => Set<ProtectionItem>();

    // Singletons
    public DbSet<DeratingFactors> DeratingFactors  => Set<DeratingFactors>();
    public DbSet<SizingConstants> SizingConstants  => Set<SizingConstants>();
}
