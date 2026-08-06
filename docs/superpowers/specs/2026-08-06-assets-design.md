# VPP Assets Design

Date: 2026-08-06
Status: Approved

## Goal
Add Battery and Solar distributed-energy assets as Orleans grains, linked to their parent site, with simulated hardware telemetry, per-asset dispatch (battery), and API/UI exposure. Builds on the existing Site grain.

## Architecture
- `GrainInterfaces` — grain contracts (`IAssetGrain`, `IBatteryGrain`, `ISolarGrain`), persisted state records, telemetry records, `AssetKind`, `AssetOptions`.
- `Grains` — shared abstract `AssetGrainBase<TState>` + `BatteryGrain`, `SolarGrain`; hardware seam `IProtocolAdapter` and `SimulatedProtocolAdapter`.
- `SiteGrain`/`SiteState` — extended to hold lists of battery and solar asset ids it owns; `RegisterAsset(kind, id)`.
- `Contracts` — now references `GrainInterfaces` (single `AssetKind` source); adds asset DTOs.
- `ApiService` — provisioning, listing, detail, dispatch endpoints.
- `Web` — asset status/dispatch UI on the Sites page.
- `Tests` — grain, adapter, and registration tests.

## Grain model
```
IAssetGrain : IGrainWithStringKey
    Task Initialize(string siteId);
    Task<AssetStatus> GetStatus();

IBatteryGrain : IAssetGrain
    Task ReportTelemetry(BatteryTelemetry t);
    Task SetDesiredPowerKw(double kw);
    Task<double> GetAvailableCapacityKwh();
    Task<BatteryState> GetState();

ISolarGrain : IAssetGrain
    Task ReportTelemetry(SolarTelemetry t);
    Task<SolarState> GetState();
```
- `AssetState`: `AssetId` from grain key; `SiteId` set by `Initialize`.
- Shared query returns `AssetStatus` snapshot including a staleness-derived `IsOnline` flag.

## Grains / protocol
- `IProtocolAdapter`: `ReadBatteryAsync`, `ReadSolarAsync`, `SendBatteryCommandAsync`.
- `SimulatedProtocolAdapter` (DI singleton, deterministic): owns "physical truth" — battery SOC drifts with the desired setpoint; solar generation follows a deterministic oscillating curve.
- `AssetGrainBase<TState>`: persistent state, telemetry `RegisterGrainTimer`, reconciliation (re-send setpoint on drift), staleness -> `IsOnline`.
- `AssetOptions`: `TelemetryIntervalSeconds` (default 30), `OnlineStalenessSeconds` (default 60). Section: `Asset`.

## API
- `POST /site/{site}/assets` — provision an asset, `Initialize(siteId)`, register on the site.
- `GET /site/{site}/assets` — list the site's assets with status (bounded parallel fan-out).
- `GET /assets/battery/{id}` / `GET /assets/solar/{id}` — detailed state.
- `POST /assets/battery/{id}/dispatch` — per-asset dispatch primitive.

Fleet orchestration (aggregator grain) is intentionally a later pass.

## Tests
Battery dispatch + available-capacity math; solar generation reporting; field/asset-state mapping; site↔asset registration; simulated adapter determinism. Timer path not required (call `ReportTelemetry` directly).