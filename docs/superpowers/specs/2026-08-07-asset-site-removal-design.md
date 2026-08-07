# Asset/Site Removal Design

Date: 2026-08-07
Status: Approved
Scope: Add UI + API + grain support to remove assets and delete sites (full cascade), each behind a confirmation dialog.

## Goal

Let a user delete an individual asset or an entire site from the web UI, with full cascade cleanup of persisted grain state, and a confirmation dialog before any destructive action.

## Domain model

Single-owner: **an asset belongs to exactly one site.** This keeps the cascade simple — each site's grain owns the authoritative list of its asset ids, so deletion is driven from the owning site.

## Backend — grain contracts

New command methods (no existing delete support anywhere):

- `IAssetGrain.Delete()` — clears the asset's persisted state (`State.ClearStateAsync()`) and deactivates the grain.
- `ISiteGrain.RemoveAsset(AssetKind kind, string assetId)` — unlink the id from the site's `BatteryIds`/`SolarIds` and persist.
- `ISiteGrain.Delete()` — cascade root: for each current asset id, delete the asset grain, remove it from the asset poller roster, then clear the site's own persisted state and deactivate.
- `IAssetPollerGrain.Remove(string assetId)` — drop the id from the poller roster and persist.

## Backend — API

- `DELETE /site/{title}` → `ISiteGrain.Delete()` (full cascade).
- `DELETE /site/{site}/assets/{kind}/{assetId}` → `ISiteGrain.RemoveAsset(kind, assetId)`, then `IAssetGrain.Delete()`, then `IAssetPollerGrain.Remove(assetId)` (shard from `SiteRegistryPartitioning.ComputeShard`).

The API takes `kind` from the route rather than deriving it, because the UI already knows each asset's kind.

## Client

`SiteApiClient`:
- `DeleteSiteAsync(string title)` → `DELETE /site/{title}`.
- `RemoveAssetAsync(string site, AssetKind kind, string assetId)` → `DELETE /site/{site}/assets/{kind}/{assetId}`.

## UI

`Site.razor`:
- **Remove asset:** a danger (`Appearance.Danger`) trash button on each asset card; opens a confirmation `FluentDialog`, then calls `RemoveAssetAsync`; on success removes the asset from `assetsBySite[site]` and, if battery, from `batteryInfo`.
- **Delete site:** a danger button on each site row; opens a confirmation `FluentDialog`, then calls `DeleteSiteAsync`; on success removes the site from `sites`.
- Confirmation uses `IDialogService` / `FluentDialog` (the established Fluent pattern, already provided).

## Error handling

- API failures surface through the existing `loadError` path; the timer-triggered refresh is unaffected.
- On success, local collections are updated in place so the page reflects removal without waiting for the next refresh tick.

## Testing

- `AssetPollerGrainTests`: `Remove` drops the id from the roster.
- `SiteGrain`/integration (using existing `ClusterFixture`): 
  - `RemoveAsset` unlinks the id and no longer returns it in `GetAssetIds`.
  - `Delete` cascades — site state cleared, child asset grains deleted, and their poller roster entries removed.
- `SiteRegistryPartitioning.ComputeShard` already covered (reused for poller sharding).

## Non-goals (v1)

- Confirm-dialog UX variants (no batch delete, no undo).
- Cross-site asset sharing (single-owner per asset per domain model).
- Soft-delete / archival — removal is destructive and clears persisted state.
