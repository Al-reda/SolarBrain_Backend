using Microsoft.EntityFrameworkCore;
using SolarBrain.Api.Data;
using SolarBrain.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Services ──────────────────────────────────────────────────────────────────

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "SolarBrain API", Version = "v1" });
});

// SQLite via EF Core
var dbPath = Path.Combine(builder.Environment.ContentRootPath, "solarbrain.db");
builder.Services.AddDbContext<SolarBrainDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

builder.Services.AddScoped<ComponentSeeder>();
builder.Services.AddScoped<ISizingEngine, SizingEngine>();
builder.Services.AddScoped<IDatasetGenerator, DatasetGenerator>();
builder.Services.AddSingleton<ISimulationRunner, SimulationRunner>();
builder.Services.AddSingleton<IDesignStore, DesignStore>();

// CORS — allow any localhost port during dev (5173 dev, 4173 preview, etc.)
const string CorsPolicy = "FrontendDev";
builder.Services.AddCors(opt =>
{
    opt.AddPolicy(CorsPolicy, p => p
        .SetIsOriginAllowed(origin =>
        {
            var uri = new Uri(origin);
            return uri.Host is "localhost" or "127.0.0.1";
        })
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();

// ── Middleware ────────────────────────────────────────────────────────────────

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors(CorsPolicy);
app.MapControllers();

// Health check — matches the FastAPI contract
app.MapGet("/api/health", (ISimulationRunner r) => Results.Ok(new
{
    status       = "ok",
    version      = "1.0.0",
    runnerReady  = r.IsLoaded,
}));

// ── Seed database on startup ─────────────────────────────────────────────────

using (var scope = app.Services.CreateScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<ComponentSeeder>();
    await seeder.SeedAsync();
}

app.Run();
