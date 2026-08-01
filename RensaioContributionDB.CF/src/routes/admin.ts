import { Hono } from 'hono';
import type { Env } from '../types';
import { banContributor } from '../services/admin-service';
import type { BanRequest } from '../models/requests';
import type { ErrorResponse } from '../models/responses';

/**
 * Admin routes.
 * Base path: /admin (set in index.ts)
 */
const adminRoutes = new Hono<{ Bindings: Env }>();

// POST /admin/ban?admin={adminUUID}
adminRoutes.post('/ban', async (c) => {
  const adminId = c.req.query('admin');

  if (!adminId) {
    return c.json<ErrorResponse>({ error: 'Missing "admin" query parameter' }, 400);
  }

  let body: BanRequest;
  try {
    body = await c.req.json<BanRequest>();
  } catch {
    return c.json<ErrorResponse>({ error: 'Invalid JSON body' }, 400);
  }

  if (!body || typeof body.contributor_id !== 'string' || body.contributor_id.length === 0) {
    return c.json<ErrorResponse>({ error: 'Request body must contain a "contributor_id"' }, 400);
  }
  if (typeof body.ban_reason !== 'string' || body.ban_reason.length === 0) {
    return c.json<ErrorResponse>({ error: 'Request body must contain a non-empty "ban_reason"' }, 400);
  }

  const result = await banContributor(c.env.DB, adminId, body.contributor_id, body.ban_reason);

  if (!result.ok) {
    return c.json<ErrorResponse>({ error: result.error }, result.status);
  }

  return c.json(result.response);
});

export default adminRoutes;
