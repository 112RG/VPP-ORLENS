# Building a Virtual Power Plant with Microsoft Orleans and .NET Aspire

**How the virtual actor model maps perfectly to managing thousands of distributed energy resources**

---

Imagine 50,000 homes, each with rooftop solar panels and a battery. Now imagine coordinating them as if they were a single, 535-megawatt power plant — dispatching energy to the grid during peak demand, pulling back during oversupply, and compensating every homeowner for their contribution. That's a Virtual Power Plant (VPP), and Tesla has already built one in South Australia. By 2025, VPP capacity in North America reached **37.5 gigawatts**, and Europe's largest VPP is targeting **1 GW by 2026**.

Building the software to manage a VPP is a distributed systems problem of extraordinary scale: thousands of independent, stateful energy assets, each reporting telemetry every few seconds, each responding to dispatch commands, and the entire fleet needing to survive hardware failures without losing state.

This is exactly the kind of problem Microsoft Orleans was designed to solve.

## The mapping: DERs as grains

Orleans is Microsoft's **Virtual Actor** framework. A virtual actor — or "grain" — is an always-addressable, single-threaded object that manages its own state and has an independent lifecycle. The mapping to VPP concepts is remarkably natural:

| VPP Concept | Orleans Concept | Example |
|---|---|---|
| Each distributed energy resource (DER) | **Grain** | `solarPanelA42 = GetGrain<ISolarPanelGrain>("SP-77341")` |
| DER identity (serial number, meter ID) | **Grain key** | `IGrainWithStringKey` — use asset serial numbers |
| Asset state (power output, battery SOC) | **`IPersistentState<T>`** | State auto-loaded from PostgreSQL on activation |
| Telemetry collection (every 30 seconds) | **Grain timer** | `RegisterGrainTimer(Tick, 30s, 30s)` |
| Scheduled operations (maintenance windows) | **Reminders** | `RegisterOrUpdateReminder("health-check", dueTime, 1 day)` |
| Server hosting grains | **Silo** | One silo per region (US-West, US-East, EU) |
| External system querying DERs | **Cluster client** | REST API → `IClusterClient.GetGrain<T>()` |

The key insight: you never have to think about *which server* hosts a particular solar panel's grain. You call `GetGrain<ISolarPanelGrain>("SP-77341")` and Orleans routes to the correct silo transparently. If that silo fails, the grain reactivates on a healthy silo and reloads its state from PostgreSQL. No data loss. No manual reassignment.

## What's a Virtual Power Plant, anyway?

Before diving into the code, let's establish what we're building toward.

A VPP is a network of small, distributed energy resources — rooftop solar, home batteries, EV chargers with vehicle-to-grid capability, smart thermostats — that are coordinated through software to behave like a single power plant. The Australian government's Clean Energy Regulator defines it as a "network of small, distributed energy resources that are linked and controlled using smart software."

AGL Energy, one of Australia's largest energy retailers, operates a consumer VPP where homeowners with compatible batteries earn **$1/kWh in bill credits** for energy their battery exports during VPP events. The system tracks every event, every kilowatt-hour, and every credit across thousands of individual batteries — a real-time, stateful, distributed system.

VPPs serve multiple roles in the energy grid:

- **Peak shaving**: Discharging batteries during high demand avoids firing up expensive, carbon-intensive peaker plants (40-60% cost savings)
- **Frequency regulation**: Responding to grid frequency changes in seconds to maintain stability
- **Load following**: Dynamically adjusting output as renewable generation fluctuates
- **Emergency resilience**: Islanding neighborhoods during grid outages

## The project architecture

Our reference implementation uses a clean, layered architecture that's directly extendable to a full VPP management platform:

```
                    Aspire Dashboard (OpenTelemetry)
                              |
    +-------------------------+-------------------------+
    |                         |                         |
PostgreSQL              Silo (Orleans Server)    ApiService (REST)
(Clustering + State)    - SolarPanelGrains       - GET /solar/panels
                        - BatteryGrains          - POST /batteries/discharge
                        - VppAggregatorGrain     - GET /fleet/status
                              |                         |
                              +----------+--------------+
                                         |
                                    Blazor Web
                                    (Real-time Dashboard)
```

**The database layer**: PostgreSQL serves double duty — it stores Orleans clustering tables (silo membership) and grain state (every DER's configuration, last reported power output, battery state of charge). We use `WithDataVolume()` to persist data across container restarts, and the silo auto-provisions all required tables on first startup.

**The silo**: Hosts grain activations. Each physical energy asset has its own grain instance that maintains state and responds to commands. Orleans distributes grains across silos automatically. Add a silo, and thousands of grains redistribute — no code changes.

**The API gateway**: ASP.NET Core minimal API endpoints that accept HTTP requests and route them to grains via `IClusterClient`. External operators, grid utilities, and monitoring systems interact through this layer.

**The dashboard**: Blazor Interactive Server with real-time polling. A `PeriodicTimer` refreshes the UI every two seconds, showing live grain state — perfect for a VPP operator monitoring fleet health.

## Grain persistence: state that survives anything

Every VPP grain must survive failures. A battery grain holding the state-of-charge for a customer's $15,000 Tesla Powerwall cannot lose that data if a server crashes.

Here's how a battery grain persists its state using `IPersistentState<T>`:

```csharp
public class BatteryGrain : Grain, IBatteryGrain
{
    private readonly IPersistentState<BatteryState> _state;

    public BatteryGrain(
        [PersistentState("battery", "AdoNet")] IPersistentState<BatteryState> state)
    {
        _state = state;
    }

    public async Task ReportStateOfCharge(double socPercent)
    {
        _state.State.ChargePercent = socPercent;
        _state.State.LastReport = DateTime.UtcNow;
        await _state.WriteStateAsync(); // Persisted to PostgreSQL
    }

    public Task<double> GetAvailableCapacity() =>
        Task.FromResult(_state.State.TotalCapacityKwh * _state.State.ChargePercent / 100);
}
```

What happens under the hood:

1. On first invocation, Orleans activates the grain, reads `BatteryState` from PostgreSQL's `OrleansStorage` table, and injects it via constructor
2. On `WriteStateAsync()`, Orleans serializes the state as binary and upserts it into PostgreSQL
3. Optimistic concurrency via a `version` column prevents conflicting writes
4. If the silo crashes, the grain reactivates elsewhere and state is reloaded from PostgreSQL

The `[PersistentState]` attribute names the state ("battery") and the storage provider ("AdoNet", configured in the silo). A single grain can have multiple named state objects — for example, a VPP aggregator grain might maintain separate state objects for fleet configuration and operational history.

## Real-time telemetry with grain timers

In a real VPP, solar inverters report power output every 15-60 seconds. Batteries report state of charge on similar intervals. The VPP management system needs to process these updates continuously.

Orleans grain timers handle this beautifully. The timer runs *inside* the grain's single-threaded execution context — a dispatch command and a telemetry tick never race:

```csharp
public override Task OnActivateAsync(CancellationToken ct)
{
    _telemetryTimer = this.RegisterGrainTimer(
        static (grain, ct) => ((SolarPanelGrain)grain).ReportTelemetry(ct),
        this,
        new GrainTimerCreationOptions
        {
            DueTime = TimeSpan.FromSeconds(5),
            Period = TimeSpan.FromSeconds(30),  // Match real inverter intervals
            KeepAlive = true                     // Prevent grain collection
        });
    return Task.CompletedTask;
}

private async Task ReportTelemetry(CancellationToken ct)
{
    // In production: call the actual inverter API here
    _state.State.CurrentWatts = await _inverterClient.ReadPowerOutput(ct);
    _state.State.DailyKwh += _state.State.CurrentWatts / 1000.0 * (30 / 3600.0);
    _state.State.LastReport = DateTimeOffset.UtcNow;
    await _state.WriteStateAsync();
}
```

The `KeepAlive = true` option ensures the grain stays activated as long as the timer runs. Without it, Orleans could deactivate an idle grain and stop telemetry collection — bad news for a battery that needs to respond to grid signals.

## Scheduled operations with reminders

Timers are ephemeral — they die when the grain deactivates. For operations that must fire at specific times and survive all failures, Orleans provides **reminders**:

```csharp
public class BatteryGrain : Grain, IBatteryGrain, IRemindable
{
    public override async Task OnActivateAsync(CancellationToken ct)
    {
        // Schedule daily health check at 3 AM
        await this.RegisterOrUpdateReminder(
            "daily-health-check",
            TimeUntilNextDailySlot(3, 0),   // 3:00 AM
            TimeSpan.FromDays(1));
    }

    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        if (reminderName == "daily-health-check")
        {
            var health = await RunDiagnostics();
            if (health.StateOfHealthPercent < 80)
                await GetGrain<IVppAggregatorGrain>("primary")
                    .ReportAssetAlert(this.GetPrimaryKeyString(),
                        $"Battery SOH degraded to {health.StateOfHealthPercent}%");
        }
    }
}
```

Reminders are persisted in PostgreSQL and fire reliably even after complete cluster restarts. They're ideal for daily maintenance windows, firmware updates, tariff changes at midnight, and compliance reporting.

## Fleet orchestration with an aggregator grain

The VPP aggregator grain ties everything together. It tracks registered assets, computes fleet-wide capacity, and dispatches commands:

```csharp
public interface IVppAggregatorGrain : IGrainWithStringKey
{
    Task RegisterAsset(string assetType, string assetId);
    Task<double> GetTotalAvailableCapacityMw();
    Task DispatchDischarge(double targetMw, TimeSpan duration);
}

public class VppAggregatorGrain : Grain, IVppAggregatorGrain
{
    private readonly IPersistentState<FleetState> _state;

    public async Task<double> GetTotalAvailableCapacityMw()
    {
        double total = 0;
        foreach (var assetId in _state.State.RegisteredAssets)
        {
            try
            {
                var battery = GrainFactory.GetGrain<IBatteryGrain>(assetId);
                total += await battery.GetAvailableCapacityKwh() / 1000.0;
            }
            catch { /* Asset temporarily unavailable, skip */ }
        }
        return total;
    }

    public async Task DispatchDischarge(double targetMw, TimeSpan duration)
    {
        var batteries = _state.State.RegisteredAssets
            .Where(id => id.StartsWith("BATT-")).ToList();
        var kwPerBattery = (targetMw * 1000) / batteries.Count;

        foreach (var id in batteries)
        {
            var battery = GrainFactory.GetGrain<IBatteryGrain>(id);
            await battery.SetDischargeRate(kwPerBattery);
        }

        // Schedule end of discharge event
        await this.RegisterOrUpdateReminder(
            "dispatch-end",
            duration,
            TimeSpan.FromMilliseconds(-1)); // One-shot
    }
}
```

The aggregator demonstrates Orleans' key scalability property: it calls thousands of battery grains in parallel, each independently manages its own state, and the aggregator gracefully handles individual failures. No lock contention. No shared mutable state.

## OpenTelemetry: watching the fleet in real time

Every grain call, timer tick, and reminder fires an ActivitySource event. With OpenTelemetry wired into the Aspire dashboard, you get full observability:

```csharp
// In ServiceDefaults/Extensions.cs
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("Microsoft.Orleans")          // Grain activations, message counts
        .AddRuntimeInstrumentation())           // CPU, GC, thread pool
    .WithTracing(tracing => tracing
        .AddSource("Microsoft.Orleans.Runtime")      // Runtime operations
        .AddSource("Microsoft.Orleans.Application")); // Your grain calls
```

Enable distributed tracing with one environment variable: `Orleans__EnableDistributedTracing=true`. The Aspire dashboard then shows full request traces from the Blazor UI through the API gateway, into the grain, and out to the database.

## Scaling from 100 homes to 100,000

The virtual actor model scales naturally:

| Scale | Silo Count | Grains per Silo | What Changes |
|---|---|---|---|
| 100 homes | 1 silo | ~300 grains | Nothing — start with one silo |
| 1,000 homes | 1 silo | ~3,000 grains | Still fine — Orleans handles this easily |
| 10,000 homes | 3 silos | ~10,000 grains each | Add silos — Orleans redistributes grains |
| 100,000 homes | 10 silos | ~30,000 grains each | Add more silos — PostgreSQL scales separately |
| 1,000,000 homes | 100 silos | ~30,000 grains each | Add regions, custom placement strategies |

Orleans applies placement strategies automatically. You can customize them — for example, co-locate all DERs in California on US-West silos for lower latency to those inverters.

## The codebase

Our reference project is fully functional and demonstrates every pattern discussed above:

```
VPP-ORLEANS/
├── VPP-ORLEANS.AppHost/           Aspire orchestration
│   └── AppHost.cs                 PostgreSQL + services + OTel
├── VPP-ORLEANS.GrainInterfaces/   Grain contracts (shared)
│   └── IScheduleGrain.cs          Interface + ScheduleState record
├── VPP-ORLEANS.Grains/            Grain implementations
│   └── ScheduleGrain.cs           IPersistentState + RegisterGrainTimer
├── Silo/                          Orleans server host
│   └── Program.cs                 ADO.NET clustering + DB auto-provisioning
├── VPP-ORLEANS.ApiService/        REST API gateway
│   └── Program.cs                 IClusterClient injection + endpoints
├── VPP-ORLEANS.Web/               Blazor Interactive Server dashboard
│   ├── ScheduleApiClient.cs       HTTP client to ApiService
│   └── Components/Pages/
│       └── Schedule.razor         Real-time UI with PeriodicTimer polling
└── VPP-ORLEANS.ServiceDefaults/   OpenTelemetry + health checks + resilience
    └── Extensions.cs              OTel metrics/tracing configuration
```

Key patterns demonstrated:

- **PostgreSQL ADO.NET clustering** for production-ready silo discovery
- **Grain persistence with `IPersistentState`** — state survives all failures
- **Grain timers with `RegisterGrainTimer`** — periodic telemetry without races
- **Database auto-provisioning** — no manual SQL scripts needed
- **Persistent data volumes** — database survives container restarts
- **API gateway pattern** — Web → ApiService → Grains (clean separation)
- **Blazor real-time UI** — `PeriodicTimer` polling with live state display
- **Aspire orchestration** — `WaitFor()` ordering, `WithDataVolume()`, health checks

## From demo to production

To extend this project into a real VPP platform, you'd add these grain types using the same patterns shown above:

| Grain | Key | State | Timer | Reminder |
|---|---|---|---|---|
| `ISolarPanelGrain` | Serial number | Current watts, daily kWh, temperature | Every 30s: read inverter | Nightly: firmware check |
| `IBatteryGrain` | Serial number | State of charge, cycle count, SOH% | Every 15s: read BMS | Daily 3 AM: diagnostics |
| `IEvChargerGrain` | Charger ID | Vehicle SOC, charging rate, schedule | Every 60s: read status | Weekly: tariff update |
| `ISmartMeterGrain` | Meter ID | Current demand, daily profile | Every 30s: read meter | Monthly: billing summary |
| `IVppAggregatorGrain` | "primary" | Registered assets, fleet capacity | — | Hourly: capacity report |

Each follows the same pattern: `IPersistentState<T>` for durability, `RegisterGrainTimer` for telemetry, and `IRemindable` for scheduled operations.

## Why not microservices?

A natural question: why grains instead of microservices?

For a VPP, each DER is a stateful entity that independently manages its own data. With microservices, you'd need a database per service, careful partitioning, manual retry logic, and a service mesh for routing. With Orleans, you get all of this for free:

- **Activation**: Grains activate on demand, saving memory when idle (just like a DER that disconnects at night)
- **Single-threaded**: No locks inside a grain — the simplest concurrency model possible
- **Location transparency**: Never hardcode which server hosts which grain
- **Automatic failover**: Grain state reloaded from database on silo crash
- **Built-in timers**: No external cron infrastructure
- **Built-in reminders**: No external scheduler service

## Get started

```bash
# Clone and run
git clone https://github.com/your-org/vpp-orleans
cd vpp-orleans
dotnet run --project VPP-ORLEANS.AppHost
```

The Aspire dashboard opens automatically at `https://localhost:17004`. Docker must be running for the PostgreSQL container.

Navigate to `/schedule` to see the real-time schedule grain in action — a grain timer updates the `LastTick` timestamp every 10 seconds, the Blazor UI polls every 2 seconds, and OpenTelemetry traces every grain call in the dashboard's Traces tab.

---

*Orleans version 10.2.2 | .NET 10.0 | Aspire 13.4.6 | Npgsql 10.0.3*

*References: Wood Mackenzie (2025 N.A. VPP capacity), Tesla South Australia VPP, AGL VPP program, Victorian Government Solar Victoria, Australian Clean Energy Regulator, Microsoft Orleans documentation*