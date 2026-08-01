# Hosting Guide: Rensaio Contribution DB on Cloudflare

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

```toml
routes = [
  { pattern = "contrib.rensaio.net/*", zone_id = "your-zone-id" }
]
```

**No custom domain?** Skip this step. The worker will be available at `rensaio-contribution-db.your-subdomain.workers.dev`.

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

**Titles are resolved server-side.** Sources and metadata carry a `title` string — not `title_id`. For each item, the worker reuses an existing active title with the same name, or creates a new title record automatically. No `title` upload items are needed.

**No `id` in payloads.** Records are identified by their **identity key**, never by UUID:
- **source** — `title` + `mihon_source_id` + `language`
- **metadata** — `title` + `metadata_provider` + `metadata_provider_key`

| Code | Meaning |
|------|---------|
| 200 | Batch processed — body contains `processed`, `skipped` counts and per-item `errors` |
| 400 | Missing `contributor`, invalid JSON, or body lacks `items` array |
| 403 | Contributor is banned |
| 404 | Contributor UUID not found |

**Actions:** `add`, `update`, `remove`
- `add` — INSERT (id generated server-side). **Deduplicated:** if an identical active record already exists (identity key match), the item is **skipped** and the existing record is kept. Archived records are never considered duplicates.
- `update` — UPDATE **any** active record matching the identity key, regardless of which contributor created it. The calling contributor becomes the new owner of the record. (`last_chapter`/`data` for sources, `link_type` for metadata)
- `remove` — soft-delete: sets `archived_at` on **any** active record matching the identity key, regardless of which contributor created it

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

**Export format:** `contributor_id` and internal UUID `id` fields are excluded. `sources.json` and `metadata.json` keep `title_id` as a compact reference to `titles.json` — `titles.json` is the single source of truth for title names (used by the server to dedup titles). Upload payloads still use `title` strings; the server resolves them to `title_id` on ingest.

**Key names per file:**
- `titles.json` → `{ id, title }`
- `sources.json` → `{ title_id, source_id, language, last, data }`
- `metadata.json` → `{ title_id, provider, provider_key, type }`

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
