import { Hono } from 'hono';
import type { Env } from './types';
import contributorRoutes from './routes/contributor';
import uploadRoutes from './routes/upload';
import adminRoutes from './routes/admin';
import keyRoutes from './routes/key';
import { runDailyExport } from './services/export-service';

/**
 * Cloudflare Worker entry point.
 *
 * HTTP endpoints:
 *   GET  /contributor?contributor={UUID}  → validate a contributor
 *   POST /contributor?admin={adminUUID}   → create a contributor (first is auto-admin)
 *   POST /upload?contributor={UUID}       → submit a contribution batch
 *   POST /admin/ban?admin={adminUUID}     → ban a contributor (admin only)
 *   POST /admin/clean?admin={adminUUID}   → wipe sources/metadata/titles (admin only)
 *   POST /admin/export?admin={adminUUID}  → run the GitHub export now (admin only)
 *   GET  /key                            → return the AES key+IV (obfuscation secret, no auth)
 *
 * Scheduled (cron 06:00 UTC daily — handled via the `scheduled` event,
 * NOT an HTTP route; there is intentionally no public /__scheduled endpoint):
 *   1. Scrub archived records older than the retention window (hard-delete)
 *   2. Export latest state to GitHub (sources.json, metadata.json, titles.json)
 */

const app = new Hono<{ Bindings: Env }>();

// ── Health check ──
app.get('/health', (c) => {
  return c.json({ status: 'ok', service: 'rensaio-contribution-db' });
});

// ── Routes ──
app.route('/contributor', contributorRoutes);
app.route('/upload', uploadRoutes);
app.route('/admin', adminRoutes);
app.route('/key', keyRoutes);

// ── Catch-all 404 ──
app.notFound((c) => {
  return c.json({ error: 'Not found' }, 404);
});

// ── Global error handler ──
app.onError((err, c) => {
  console.error('Unhandled error:', err);
  return c.json({ error: 'Internal server error' }, 500);
});

/**
 * Cron handler — runs the daily export when Cloudflare fires the
 * `scheduled` event (per the [triggers] crons in wrangler.toml).
 * This is not reachable over HTTP.
 */
async function scheduled(_controller: ScheduledController, env: Env, ctx: ExecutionContext): Promise<void> {
  ctx.waitUntil(
    (async () => {
      try {
        const result = await runDailyExport(env);
        console.log(
          `Cron export: scrubbed ${JSON.stringify(result.scrubbed)}, ` +
            `orphan titles archived ${result.orphanTitlesArchived}, ` +
            `pushed ${result.files.join(', ')}`
        );
      } catch (err) {
        console.error('Cron export failed:', err);
      }
    })()
  );
}

export default {
  fetch: app.fetch,
  scheduled,
};
