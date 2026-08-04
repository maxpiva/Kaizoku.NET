id # Hosting Guide: Rensaio Contribution DB on Cloudflare

This guide walks through deploying the `RensaioContributionDB.CF` Cloudflare Worker from scratch.

---

## Prerequisites

1. **Cloudflare account** — [sign up free](https://dash.cloudflare.com/signup)
2. **Node.js 18+** — verify with `node --version`
3. **GitHub Personal Access Token** with `repo` scope (for the daily export)

---

## Step 1: Authenticate Wrangler

```powershell
# Navigate to the project
cd RensaioContributionDB.CF

# Login to Cloudflare (opens browser)
npx wrangler login
```

---

## Step 2: Create the D1 Database

```powershell
npx wrangler d1 create rensaio-contribution-db
```

**Output:**
```
✅ Successfully created DB 'rensaio-contribution-db' in region WEUR

[[d1_databases]]
binding = "DB"
database_name = "rensaio-contribution-db"
database_id = "your-database-id-here"
```

Copy the `database_id` value and paste it into [`wrangler.toml`](RensaioContributionDB.CF/wrangler.toml).

---

## Step 3: Apply the Database Migration

```powershell
npx wrangler d1 migrations apply rensaio-contribution-db
```

This runs [`migrations/0001_create_contribution_tables.sql`](RensaioContributionDB.CF/migrations/0001_create_contribution_tables.sql) which creates the four tables: `contributors`, `titles`, `sources`, and `metadata`.

---

## Step 4: Configure the Export Target

Edit [`wrangler.toml`](RensaioContributionDB.CF/wrangler.toml):

```toml
[vars]
EXPORT_GITHUB_REPO = "your-org/your-repo"
EXPORT_PATH = "data/"
```

### Set the GitHub Token (sensitive — via wrangler secret)

```powershell
npx wrangler secret put GITHUB_TOKEN
# Paste your GitHub PAT, press Enter
```

The token needs `repo` scope (or `Contents: Read and write` for fine-grained tokens) to update files in the target repository.

### Set the AES Obfuscation Secret (sensitive — via wrangler secret)

```powershell
npx wrangler secret put AESKEY256IV
# Paste base64(32-byte AES-256 key + 16-byte IV), press Enter
```

`AESKEY256IV` is a base64 string of the AES-256 key (32 bytes) concatenated with the IV (16 bytes) — 48 bytes total, 64 base64 characters. It is used to **obfuscate** (not secure) source data in `sources.json` exports. The key is public by design: consumers fetch it from `GET /key`.

Example generation (PowerShell):
```powershell
$key = New-Object byte[] 32; $iv = New-Object byte[] 16
[System.Security.Cryptography.RandomNumberGenerator]::Fill($key)
[System.Security.Cryptography.RandomNumberGenerator]::Fill($iv)
[Convert]::ToBase64String($key + $iv)
```

---

## Step 5: Bootstrap the First Admin

The first contributor is created via the API when the `contributors` table is empty. Call `POST /contributor` with **no admin UUID** — the first created contributor automatically becomes an admin:

```powershell
curl -X POST https://contrib.rensaio.net/contributor
  -H "Content-Type: application/json"
  -d '{}'
# → 201 {"contributor_id":"<first-admin-uuid>"}
```

Save the returned UUID — it is your first administrator. From now on, creating additional contributors requires this admin UUID (see `POST /contributor` in the API Reference below).

If you prefer, you can still seed contributors directly in D1 (Cloudflare Dashboard → D1 → rensaio-contribution-db → Console):

```sql
INSERT INTO contributors (id, admin, active, ban_reason, last_change)
VALUES ('<uuid>', 1, 1, NULL, datetime('now'));
```

---

## Step 6: Configure the Route (Optional Custom Domain)

The route is already declared in [`wrangler.toml`](wrangler.toml):

```toml
routes = [
  { pattern = "contrib.rensaio.net/*", zone_id = "618dd986bbb822c7977287c05b4f5e16" }
]
```

If you deploy to a different zone or subdomain, replace the `pattern`/`zone_id` in `wrangler.toml`.

**No custom domain?** Remove the `routes` block. The worker will be available at `rensaio-contribution-db.your-subdomain.workers.dev`.

---

## Step 7: Deploy

```powershell
npx wrangler deploy
```

---

## Step 8: Verify the Deployment

```powershell
# Health check
curl https://contrib.rensaio.net/health
# → {"status":"ok","service":"rensaio-contribution-db"}

# Validate a contributor
curl "https://contrib.rensaio.net/contributor?contributor=<contributor-uuid>"
# → {"active":true,"admin":false,"ban_reason":null}
```

---

## API Reference

### Validate Contributor

```
GET /contributor?contributor={UUID}
```

| Code | Meaning |
|------|---------|
| 200 | Contributor found — body contains `active`, `admin`, `ban_reason` |
| 400 | Missing `contributor` query parameter |
| 404 | Contributor UUID not found |

### Create Contributor (Admin Only, except first)

```
POST /contributor?admin={adminUUID}
```

```json
{
  "is_admin": true
}
```

`is_admin` is optional (defaults to `false`).

**Bootstrap rule:** when the `contributors` table is empty, the `admin` query parameter is ignored/optional and the first contributor is always created as an admin. On subsequent calls, a valid active admin UUID is required.

| Code | Meaning |
|------|---------|
| 201 | Contributor created — body contains the new `contributor_id` |
| 400 | Invalid JSON body |
| 403 | Admin UUID missing/not an admin/inactive (non-empty table) |
| 404 | Admin UUID not found |

### Upload Contribution Batch

```
POST /upload?contributor={UUID}
```

```json
{
  "items": [
    {
      "type": "source",
      "action": "add",
      "data": {
        "id": "123456",
        "title": "My Manga",
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

**Titles are resolved server-side.** Sources and metadata carry a `title` string — not `title_id`. For each item, the worker reuses an existing active title with the same name, or creates a new title record automatically. No `title` upload items are needed.

**Source identity key is `id`** — the contributor-provided identifier (e.g. the Mihon source ID). It is also the DB primary key. Metadata keeps its identity key: `title` + `metadata_provider` + `metadata_provider_key`.

| Code | Meaning |
|------|---------|
| 200 | Batch processed — body contains `processed`, `skipped` counts and per-item `errors` |
| 400 | Missing `contributor`, invalid JSON, or body lacks `items` array |
| 403 | Contributor is banned |
| 404 | Contributor UUID not found |

**Actions:** `add`, `remove`
- `add` — **unconditional upsert** by identity key:
  - record missing → **insert**
  - record exists → **update** (values + ownership transfer to the caller)
  - no content comparison — the latest upload always wins
  - (source identity key: `id`; metadata identity key: `title` + `provider` + `provider_key`)
- `remove` — soft-deletes (`archived_at`) **any** active record by `id` (source) or identity key (metadata), regardless of which contributor created it.

### Admin Maintenance Endpoints

Both require an active admin UUID via the `admin` query parameter:

```
POST /admin/clean?admin={adminUUID}
```
Hard-deletes **all** rows from `sources`, `metadata` and `titles` (atomic batch). The `contributors` table is preserved so admin auth keeps working.

```json
// Response (200 OK)
{ "cleaned": true, "deleted": { "sources": 12, "metadata": 3, "titles": 4 } }
```

| Code | Meaning |
|------|---------|
| 200 | Tables cleaned |
| 400 | Missing `admin` |
| 403 | Inactive or non-admin UUID |
| 404 | Admin UUID not found |

```
POST /admin/export?admin={adminUUID}
```
Runs the daily export pipeline on demand (scrub + archive orphan titles + push the three JSON files to GitHub).

```json
// Response (200 OK)
{
  "exported": true,
  "files": ["sources.json", "metadata.json", "titles.json"],
  "scrubbed": { "sources": 0, "metadata": 0, "titles": 0 },
  "orphanTitlesArchived": 0
}
```

| Code | Meaning |
|------|---------|
| 200 | Export pushed to GitHub |
| 400 | Missing `admin` |
| 403 | Inactive or non-admin UUID |
| 404 | Admin UUID not found |
| 500 | Export failed — error message returned |

### Ban Contributor (Admin Only)

```
POST /admin/ban?admin={adminUUID}
```

```json
{
  "contributor_id": "<target-uuid>",
  "ban_reason": "Submitting malicious metadata links"
}
```

| Code | Meaning |
|------|---------|
| 200 | Contributor banned — body contains scrub summary (`sources`, `metadata` archived) |
| 400 | Missing fields, or target is an admin |
| 403 | Caller is not an active admin |
| 404 | Admin or target not found |
| 409 | Target already banned |

The ban atomically deactivates the contributor and archives all their non-archived sources/metadata. Titles are left untouched (shared registry). After 30 days the archived records are hard-deleted by the daily scrub.

---

## Daily Export

The worker runs once per day at **06:00 UTC** (`[triggers] crons = ["0 6 * * *"]`):

1. **Scrub** — hard-deletes records soft-deleted more than 30 days ago
2. **Export** — pushes the latest non-archived state to the GitHub repo:
   - `data/sources.json`
   - `data/metadata.json`
   - `data/titles.json`

**Export format:** `contributor_id` is excluded. `sources.json` and `metadata.json` keep `title_id` as a compact reference to `titles.json` — `titles.json` is the single source of truth for title names (used by the server to dedup titles). Upload payloads still use `title` strings; the server resolves them to `title_id` on ingest.

**Key names per file:**
- `titles.json` → `{ id, title }`
- `sources.json` → `{ title_id, id, data }`
- `metadata.json` → `{ title_id, provider, provider_key, type }`

**Source data obfuscation:** each source's `data` in `sources.json` is the result of:
```
BLOB (binary) → AES-256-CBC encrypt → base64
```
Consumers fetch the key/IV from `GET /key` to reverse it: `base64 → AES-256-CBC decrypt → binary`.

### Get the Obfuscation Key

```
GET /key
```

Returns the base64 concatenation of the AES-256 key + IV (the `AESKEY256IV` secret) as plain text. **No authorization required** — the key is for obfuscation only, not security.

| Code | Meaning |
|------|---------|
| 200 | Body is the base64 `AESKEY256IV` value |
| 500 | `AESKEY256IV` secret not configured |

---

## Free Tier Limits

| Resource | Free Tier Limit | Our Usage |
|----------|----------------|-----------|
| D1 reads | 10M rows/month | Small (validation + export) |
| D1 writes | 100k writes/day | 1 batch per upload |
| Worker requests | 100k/day | 1 request per API call |
| Cron triggers | 1 per day minimum | 1 per day (free) |

---

## Troubleshooting

- **`Cannot find type definition file for '@cloudflare/workers-types'`** — run `npm install` in `RensaioContributionDB.CF/`
- **Export fails with GitHub 401/403** — the `GITHUB_TOKEN` lacks `repo`/`Contents` scope, or the repo does not exist
- **Export fails with GitHub 409** — a concurrent update changed the file `sha`; the next run will succeed
