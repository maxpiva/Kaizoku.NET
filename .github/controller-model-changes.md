# Controller & Model Changes (Mihon Migration)

| Date | Area | Details |
| --- | --- | --- |
| 2026-01-31 | General | Removed Suwayomi runtime assets and background host; no API endpoints or models changed in this iteration. |
| 2026-02-01 | Providers | Added Mihon identifiers (`extensionPackageId`, `extensionRepositoryId`, `extensionSourceId`, version fields) to `SeriesProvider` and `LatestSerie` models; consumers must persist and emit these fields alongside legacy identifiers until migration completes. |
| 2026-02-01 | ProviderController | `GET /api/provider/list` now returns `extensionPackageId`, `extensionRepositoryId`, and `sources[]` (with Mihon source ids) on each extension payload to support bridge-driven workflows. |
| 2026-02-01 | SearchController | `POST /api/search/augment` enriches each `FullSeries.meta` bag with Mihon identifiers (`mihon.sourceId`, `mihon.repository`) so downstream imports can persist bridge metadata. |
| 2026-02-02 | SeriesController | `GET /api/serie` and `PATCH /api/serie` now include Mihon identifiers (`extensionPackageId`, `extensionRepositoryId`, `extensionSourceId`, version fields) inside each `ProviderExtendedInfo` within the `SeriesExtendedInfo` payload. |
| 2026-02-02 | Providers / LatestSeries | Introduced deterministic `bridgeSeriesId` across `SeriesProvider`, `LatestSerie`, `LatestSeriesInfo`, and `ProviderExtendedInfo`. API responses now expose both `bridgeSeriesId` (primary identifier) and `legacy` Suwayomi ids for backward compatibility. |
| 2026-02-02 | SeriesController / Latest | `GET /api/serie/latest` now surfaces `bridgeSeriesId`, `legacySuwayomiId`, and Mihon extension metadata (`extensionPackageId`, `extensionRepositoryId`, `extensionSourceId`, version fields) on each `LatestSeriesInfo` entry. |
| 2026-02-02 | SearchController | `LinkedSeries` responses (search + augment payloads) now include `packageId`, `repositoryId`, `extensionSourceId`, and `sourceUrl` so the client can echo bridge metadata when requesting augmentations. |
| 2026-02-03 | SeriesController | `GET /api/serie/thumb/{id}` now resolves thumbnails through the Mihon bridge when possible (no schema change, improved behavior on bridge-only series). |
| 2026-02-04 | SearchController / SeriesController | Removed legacy Suwayomi fallbacks: `POST /api/search/augment` and series download jobs now require Mihon bridge metadata, and `GET /api/serie/thumb/{id}` returns the placeholder image if bridge assets are unavailable. |
| 2026-02-05 | Data Integrity | Startup now backfills missing bridge metadata on `SeriesProvider` and `LatestSerie` rows so Mihon-only workflows never skip providers; the obsolete `SuwayomiClient` service was removed. |
