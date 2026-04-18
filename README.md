# SolarBrain Backend ☀️🔋

**ASP.NET Core 9 Web API** that powers the SolarBrain hybrid energy management
system — intelligent PV + battery + (optional) generator sizing and a live
7-mode decision engine for Saudi Arabian conditions.

> The companion frontend lives at
> [Al-reda/SolarBrain_FrontEnd](https://github.com/Al-reda/SolarBrain_FrontEnd).
> For the full story (motivation, architecture diagram, demo instructions)
> see that repo's README.

---

## Quick start

```bash
cd SolarBrain.Api
dotnet run
```

API comes up on **http://localhost:5099** — Swagger UI at
`/swagger`, health check at `/api/health`.

The SQLite catalogue (17 panels, 19 inverters, 19 batteries, 3 regions,
3 tariffs, 10 protection items) is seeded on first start from
`SolarBrain.Api/Data/components.json` — fully self-contained.

---

## What's in this repo

```
backend/
├── SolarBrain.sln
└── SolarBrain.Api/
    ├── Program.cs                        CORS, DI, Swagger, seed on startup
    ├── Controllers/                      Design, Simulation, Components
    ├── Services/                         SizingEngine, Brain, DatasetGenerator,
    │                                     SimulationRunner, DesignStore, Seeder
    ├── Models/
    │   ├── Entities/                     8 EF Core entities
    │   ├── Dtos/                         15 DTO records
    │   └── DatasetRow.cs
    ├── Data/
    │   ├── SolarBrainDbContext.cs
    │   └── components.json               Self-contained seed source
    └── Migrations/                       EF Core InitialCreate
```

---

## API endpoints

All 18 endpoints auto-documented at **http://localhost:5099/swagger** once running.

### Layer 1 — Design

- `POST /api/Design` — run the sizing engine, generate the dataset,
  and prime the simulation runner (one call does everything)
- `GET  /api/Design/current` — retrieve the cached design without resizing

### Layer 2 — Simulation

- `GET  /api/Simulation/next` — advance one interval
- `GET  /api/Simulation/history?lastN=96` — last N states
- `GET  /api/Simulation/summary` — cumulative KPI totals
- `POST /api/Simulation/scenario` — inject a scenario (12 valid strings)
- `POST /api/Simulation/speed` — 1–20×
- `POST /api/Simulation/reset`
- `POST /api/Simulation/jump/season/{season}`
- `POST /api/Simulation/jump/hour/{hour}`

### Catalogue

- `GET  /api/Components`           — full catalogue
- `GET  /api/Components/summary`   — record counts
- `GET  /api/Components/panels`    — just panels
- `GET  /api/Components/inverters`
- `GET  /api/Components/batteries`
- `GET  /api/Components/regions`
- `GET  /api/Components/tariffs`

### Health

- `GET  /api/health` — `{"status":"ok","version":"1.0.0","runnerReady":true}`

---

## Tech stack

| Layer         | Tech                                | Version |
|---------------|-------------------------------------|---------|
| Runtime       | .NET                                | 9.0     |
| Framework     | ASP.NET Core Web API                | 9.0     |
| Data          | Entity Framework Core               | 9.0     |
| Storage       | SQLite (file-based, WAL mode)       | —       |
| CSV           | CsvHelper                           | 33.0.1  |
| Docs          | Swashbuckle                         | 7.2     |

---

## The 7 decision modes

| Mode               | When it fires                                          |
|--------------------|--------------------------------------------------------|
| `SOLAR_ONLY`       | PV ≥ 92% of load AND battery SOC above floor           |
| `HYBRID`           | PV ≥ 52% of load, grid covers deficit                  |
| `BATTERY_BACKUP`   | Peak price hours (12–17) with battery SOC > 42%         |
| `EMERGENCY`        | Grid outage — on-grid systems serve only critical load  |
| `CHARGE_MODE`      | Off-peak (≤ 06 or ≥ 22) AND battery SOC < 23%          |
| `GRID_ONLY`        | Default fallback when no other condition matches        |
| `GENERATOR_BACKUP` | Off-grid + battery at floor + has generator            |

All transitions go through **2-cycle hysteresis** except EMERGENCY and
GENERATOR_BACKUP which fire immediately.

---

## License

Internal hackathon / demo project.
