# Rensaio Contribution DB Worker

A [Cloudflare Worker](https://developers.cloudflare.com/workers/) that powers the Rensaio **contribution database** — a shared, community-maintained dataset of manga titles, sources, and metadata provider links.

Contributors (user machines running Rensaio) submit title/source/metadata data through the API. The worker stores it in a [Cloudflare D1](https://developers.cloudflare.com/d1/) database, validates and deduplicates it, and publishes the latest state to a GitHub repository once per day.

---

## Table of Contents

- [How it works](#how-it-works)
- [Data model](#data-model)
- [API endpoints](#api-endpoints)
  - [Create Contributor](#create-contributor)
  - [Validate Contributor](#validate-contributor)
  - [Upload Contribution Batch](#upload-contribution-batch)
  - [Ban Contributor](#ban-contributor)
- [Actions: add / update / remove](#actions-add--update--remove)
- [Daily export](#daily-export)
- [Reconciliation: banning & re-uploading](#reconciliation-banning--re-uploading)
- [Deployment](#deployment)

---

## How it works

```
                    ┌────────────────────────────────────────────┐
                    │                Cloudflare Worker            │
                    │                                            │
  Contributor ──►  GET /contributor   ──► validate / create      │
  Contributor ──►  POST /upload       ──► store + dedup          │
  Admin       ──►  POST /admin/ban    ──► deactivate + scrub     │
                    │                                            │
                    │                    ┌──────────────────┐    │
                    │                    │   D1 database    │    │
                    │                    │ contributors     │    │
                    │                    │ titles            │    │
                    │                    │ sources           │    │
                    │                    │ metadata          │    │
                    │                    └──────────────────┘    │
                    │                                            │
  Daily cron (06:00 UTC) ──► scrub old archives ──► export      │
                    │         sources.json / metadata.json /     │
                    │         titles.json  ──► GitHub repo       │
                    └────────────────────────────────────────────┘
```

**The async contribution loop:**

1. The daily cron exports the latest state to a GitHub repository (`sources.json`, `metadata.json`, `titles.json`).
2. A contributor machine downloads this snapshot and scans its local sources, auto-matching titles and creating metadata.
3. The contributor uploads its results via `POST /upload`.
4. The next daily export includes the new data, and other contributors pick it up.

Because every row is identified by stable UUIDs and deduplication happens server-side, contributors working from different snapshots never collide.

---

## Data model

Four tables in D1 (SQLite):

| Table | Columns | Notes |
|-------|---------|-------|
| `contributors` | `id` (UUID), `admin`, `active`, `ban_reason`, `last_change` | `active = 0` means banned |
| `titles` | `id` (UUID), `title`, `archived_at` | Single source of truth for title names |
| `sources` | `id` (UUID), `title_id`, `mihon_source_id`, `language`, `last_chapter`, `data` (BLOB), `contributor_id`, `last_change`, `archived_at` | One row per source entry per title |
| `metadata` | `id` (UUID), `title_id`, `metadata_provider`, `metadata_provider_key`, `link_type`, `contributor_id`, `last_change`, `archived_at` | One row per metadata link per title |

All deletes are **soft deletes** — `archived_at` is set instead of removing the row. Archived rows are hard-deleted by the daily cron after 30 days.

---

## API endpoints

All endpoints accept the contributor UUID as a query parameter — **there is no API key**. The UUID *is* the authentication.

### Create Contributor

```
POST /contributor?admin={adminUUID}
```

Creates a new contributor and returns its UUID.

```json
// Request body
{ "is_admin": true }   // optional, defaults to false
```

```json
// Response (201 Created)
{ "contributor_id": "7b3f7c9e-..." }
```

**Bootstrap rule:** when the `contributors` table is empty, the `admin` parameter is ignored and the first created contributor is **always an admin**. On any later call, a valid active admin UUID is required.

| Code | Meaning |
|------|---------|
| 201 | Contributor created |
| 400 | Invalid JSON body |
| 403 | Missing/not-admin/inactive admin UUID (non-empty table) |
| 404 | Admin UUID not found |

### Validate Contributor

```
GET /contributor?contributor={UUID}
```

Verifies whether a contributor UUID exists and is active.

```json
// Response (200 OK)
{ "active": true, "admin": false, "ban_reason": null }
```

| Code | Meaning |
|------|---------|
| 200 | Contributor found — `active`, `admin`, `ban_reason` |
| 400 | Missing `contributor` query parameter |
| 404 | Contributor not found |

### Upload Contribution Batch

```
POST /upload?contributor={UUID}
```

Submits a batch of source and metadata records. **Titles are not uploaded directly** — each item carries a `title` string and the worker resolves it server-side (reusing an existing title by name or creating a new one).

```json
{
  "items": [
    {
      "type": "source",
      "action": "add",
      "data": {
        "title": "My Manga",
        "mihon_source_id": "123456",
        "language": "en",
        "last_chapter": "ch.1000",
        "data": "<base64-encoded-binary-payload>"
      }
    },
    {
      "type": "metadata",
      "action": "add",
      "data": {
        "title": "My Manga",
        "metadata_provider": "anilist",
        "metadata_provider_key": "12345",
        "link_type": 1
      }
    }
  ]
}
```

```json
// Response (200 OK)
{
  "processed": 2,
  "skipped": 0,
  "errors": []
}
```

| Code | Meaning |
|------|---------|
| 200 | Batch processed — `processed`, `skipped`, per-item `errors` |
| 400 | Missing `contributor`, invalid JSON, or no `items` array |
| 403 | Contributor is banned |
| 404 | Contributor not found |

### Ban Contributor (Admin Only)

```
POST /admin/ban?admin={adminUUID}
```

Deactivates a bad-actor contributor and archives all of their contributed data.

```json
{
  "contributor_id": "7b3f7c9e-...",
  "ban_reason": "Submitting malicious metadata links"
}
```

```json
// Response (200 OK)
{
  "banned": true,
  "contributor_id": "7b3f7c9e-...",
  "ban_reason": "Submitting malicious metadata links",
  "scrubbed": { "sources": 12, "metadata": 3 }
}
```

| Code | Meaning |
|------|---------|
| 200 | Contributor banned — scrub summary returned |
| 400 | Missing fields, or target is an admin |
| 403 | Caller is not an active admin |
| 404 | Admin or target not found |
| 409 | Target already banned |

The ban runs atomically: it deactivates the contributor, then archives all their non-archived sources and metadata. Titles are left untouched (they are a shared registry).

---

## Actions: add / update / remove

Every item in an upload batch carries an action. Records are identified by their **identity key** — never by UUID (payloads and exports don't expose internal IDs for sources/metadata):

| Entity | Identity key |
|--------|-------------|
| source | `title` + `mihon_source_id` + `language` |
| metadata | `title` + `metadata_provider` + `metadata_provider_key` |

| Action | Behavior |
|--------|----------|
| `add` | Insert a new record (UUID generated server-side). **Deduplicated:** if an identical active record already exists, the item is **skipped** and the existing record is kept. |
| `update` | Update **any** active record matching the identity key — regardless of who created it. The calling contributor becomes the new owner. Updates `last_chapter`/`data` (sources) or `link_type` (metadata). |
| `remove` | Soft-delete **any** active record matching the identity key — regardless of who created it. Sets `archived_at`. |

---

## Daily export

The worker runs once per day at **06:00 UTC** via a cron trigger. Two steps:

### 1. Scrub

Hard-deletes records that have been soft-deleted (archived) for more than 30 days. Sources and metadata are deleted before titles, respecting foreign-key ordering.

### 2. Export to GitHub

Pushes the latest non-archived state to the configured GitHub repository (via the Contents API) as three files:

**`titles.json`** — single source of truth for title names:
```json
[
  { "id": "uuid-1", "title": "My Manga" }
]
```

**`sources.json`** — compact rows referencing `titles.json`:
```json
[
  { "title_id": "uuid-1", "source_id": "123456", "language": "en", "last": "ch.1000", "data": "<base64>" }
]
```

**`metadata.json`**:
```json
[
  { "title_id": "uuid-1", "provider": "anilist", "provider_key": "12345", "type": 1 }
]
```

Key mapping to the internal schema:

| Export key | DB column |
|-----------|-----------|
| `title_id` (sources/metadata) | `title_id` (FK → `titles.id`) |
| `source_id` | `mihon_source_id` |
| `last` | `last_chapter` |
| `provider` | `metadata_provider` |
| `provider_key` | `metadata_provider_key` |
| `type` | `link_type` |

`contributor_id` and internal `id` (of sources/metadata) are **never exported**. `titles.json` keeps its `id` because it is the join key for `title_id`.

**Why the asymmetry?** Upload payloads use full field names and `title` strings so the server can dedup titles (one source of truth). Exports use short keys and `title_id` to keep the files small.

---

## Reconciliation: banning & re-uploading

If a contributor is banned, all their sources/metadata are archived. Other contributors who download the next export will see those rows missing and can **re-upload** the data — the new records get fresh UUIDs under the new contributor's ownership. Archived records are never treated as duplicates, so this flow always works.

Records that a banned contributor created but that were later **updated by someone else** belong to the new owner and survive the ban.

---

## Deployment

See [`DEPLOY.md`](DEPLOY.md) for the full step-by-step guide: creating the D1 database, applying migrations, setting the `GITHUB_TOKEN` secret, bootstrapping the first admin, and deploying with Wrangler.

Quick reference:

```powershell
cd RensaioContributionDB.CF
npm install
npx wrangler login
npx wrangler d1 create rensaio-contribution-db   # paste database_id into wrangler.toml
npx wrangler d1 migrations apply rensaio-contribution-db
npx wrangler secret put GITHUB_TOKEN
npx wrangler deploy
```

Then bootstrap the first admin:

```powershell
curl -X POST https://<worker-url>/contributor -H "Content-Type: application/json" -d '{}'
# → 201 {"contributor_id":"<first-admin-uuid>"}
```
