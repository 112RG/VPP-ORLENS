# Asset Status Polling Design

Date: 2026-08-07
Status: Approved
Scope: Backend sharded scheduler + UI periodic refresh

## Goal

Take the current per-asset telemetry timers and turn them into a design that scales to a 100k+ asset fleet, while giving the web UI live (10-30s) asset-status updates without whole-fleet scanning.

## Background / problem

Today each asset grain (`AssetGrainBase<TState>`) registers its own `RegisterGrainTimer` that calls `PollHardwareAsync()` every `TelemetryIntervalSeconds` (default 30s). The web page (`Site.razor`) loads asset status exactly once on initialization and never refreshes.

The per-grain-timer approach does not scale to 100k+ assets because:

1. **Polling stops when grains deactivate.** Orleans deactivates idle grains, so a large fleet is mostly deactivated and most assets stop polling. Effective status is driven by activation/lifecycle, not intent.
2. **Thundering herd.** Every active grain polls on an independent clock with no staggering or concurrency ceiling, spiking load on the Silo and postgres (`WriteStateAsync` each tick).
3. **No fleet roster.** Nothing knows the full set of assets except indirectly through site registries, so status refreshes cannot be driven fleet-wide predictably.

## Design overview

Split polling into two decoupled concerns:

- **Hardware advancement** stays on the per-asset grain (existing `RegisterGrainTimer` in `AssetGrainBase`), advancing simulated hardware telemetry.
- **Status refresh** is driven by new, long-lived, sharded **`AssetPollerGrain`** instances that own a roster of assets and fan out `GetStatus()` on a deterministic cadence.

The web UI refreshes its currently-visible page on a periodic timer instead of loading once.

## Architecture

### New types (GrainInterfaces)

```csharp
public interface IAssetPollerGrain : IGrainWithIntegerKey
{
    Task Register(string assetId);
    Task<string[]> GetAssetIds();       // diagnostics / tests
}

public class AssetPollerOptions            // config section: "AssetPoller"
{
    public int ShardCount { get; set; } = 32;
    public int PollingIntervalSeconds { get; set; } = 30;
    public int MaxConcurrent { get; set; } = 100;   // per-cycle fan-out ceiling
}
```

### New grain (Grains)

`AssetPollerGrain : Grain, IAssetPollerGrain` with persistent state `("poller", "AdoNet")` holding a `List<string>` roster:

- `Register(assetId)` — append if missing, `WriteStateAsync`.
- `OnActivateAsync` registers a `RegisterGrainTimer` on `PollingIntervalSeconds`.
- `PollOnceAsync()` — the cycle body, also public for deterministic testing:
  - iterate roster in `MaxConcurrent`-bounded `Task.WhenAll` fan-out
  - call `IAssetGrain.GetStatus()` per asset
  - swallow per-asset failures (log, continue) so one bad asset does not abort the cycle
  - report metrics (polled count, error count)

### Sharding

Reuse existing `SiteRegistryPartitioning.ComputeShard(assetId, ShardCount)` (FNV-1a hash, already implemented and tested). The provision path computes the shard for an asset id and routes `Register` to the matching `AssetPollerGrain`.

### Provision wiring (ApiService)

In `POST /site/{site}/assets`, after `asset.Initialize(site)`:

```csharp
int shard = SiteRegistryPartitioning.ComputeShard(req.AssetId, pollerOptions.ShardCount);
await cluster.GetGrain<IAssetPollerGrain>(shard).Register(req.AssetId);
```

### Existing asset timers unchanged

The per-asset `RegisterGrainTimer` remains for hardware advancement. The poller only refreshes status; the two stay decoupled, so the poller keeps working even while the majority of assets are deactivated.

## UI: periodic visible-page refresh

`Site.razor`:

- Read `Ui:RefreshIntervalSeconds` (default 15s) from configuration.
- Start a `PeriodicTimer` in `OnInitializedAsync`.
- Each tick calls `LoadPageAsync()`, which re-fetches the **current page** only:
  - `GetSitesAsync(page, pageSize)` (paginated, PageSize=25)
  - current page's `GetAssetsAsync(site)` + per-battery `GetBatteryAsync(id)`
- Per-tick errors set `loadError` and do not stop the timer (reuse existing pattern).
- Implement `IAsyncDisposable` to stop/dispose the timer on navigation.

This bounds API pressure to the visible page; there is no whole-fleet scan.

## Error handling and resilience

- **Poller fan-out:** per-asset `GetStatus()` failures are caught and logged; cycle continues and does not abort.
- **UI timer:** transient API errors surface via `loadError`; the timer keeps running.

## Testing

- `SiteRegistryPartitioning.ComputeShard` — already covered by `SiteRegistryPartitioningTests`.
- **`AssetPollerGrain` test** (using existing `ClusterFixture`/`TestCluster`, memory storage):
  - register N assets across shards; assert roster persisted
  - invoke `PollOnceAsync()` directly and assert `GetStatus()` is invoked per asset (deterministic, matches repo convention of calling grain methods directly rather than waiting on timers)
- **UI refresh:** light component-level test that the timer triggers `LoadPageAsync` and re-renders; primary verification may be manual since the timer is mostly plumbing.

## Non-goals (v1)

- Push/event-based hardware adapters (future seam; simulated adapter stays pull-based).
- Poller-driven hardware advancement (`RefreshHardware`) — deferred; poller is status-only.
- Whole-fleet UI refresh.
- Rate/priority differentiation per asset (all assets poll at the shared cadence).
