import { Hono } from 'hono';
import type { Env } from './types';
import contributorRoutes from './routes/contributor';
import uploadRoutes from './routes/upload';
import adminRoutes from './routes/admin';
import { runDailyExport } from './services/export-service';

/**
 * Cloudflare Worker entry point.
 *
 * Endpoints:
 *   GET  /contributor?contributor={UUID}  → validate a contributor
 *   POST /contributor?admin={adminUUID}   → create a contributor (first is auto-admin)
 *   POST /upload?contributor={UUID}       → submit a contribution batch
 *   POST /admin/ban?admin={adminUUID}     → ban a contributor (admin only)
 *
 * Cron (06:00 UTC daily):
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

// ── Scheduled cron handler (daily export + scrub) ──
app.get('/__scheduled', async (c) => {
  try {
    const result = await runDailyExport(c.env);
    console.log(
      `Cron export: scrubbed ${JSON.stringify(result.scrubbed)}, pushed ${result.files.join(', ')}`
    );
    return c.json(result);
  } catch (err) {
    console.error('Cron export failed:', err);
    return c.json({ error: 'Daily export failed' }, 500);
  }
});

// ── Catch-all 404 ──
app.notFound((c) => {
  return c.json({ error: 'Not found' }, 404);
});

// ── Global error handler ──
app.onError((err, c) => {
  console.error('Unhandled error:', err);
  return c.json({ error: 'Internal server error' }, 500);
});

export default app;
