import { Hono } from 'hono';
import type { Env } from '../types';
import { validateActiveContributor } from '../services/contributor-service';
import { processUpload } from '../services/upload-service';
import type { UploadRequest } from '../models/requests';
import type { ErrorResponse } from '../models/responses';

/**
 * Upload routes.
 * Base path: /upload (set in index.ts)
 */
const uploadRoutes = new Hono<{ Bindings: Env }>();

// POST /upload?contributor={UUID}
uploadRoutes.post('/', async (c) => {
  const contributorId = c.req.query('contributor');

  if (!contributorId) {
    return c.json<ErrorResponse>({ error: 'Missing "contributor" query parameter' }, 400);
  }

  // Validate the contributor is active
  const validation = await validateActiveContributor(c.env.DB, contributorId);
  if (!validation.ok) {
    if (validation.reason === 'not_found') {
      return c.json<ErrorResponse>({ error: 'Contributor not found' }, 404);
    }
    return c.json<ErrorResponse>(
      { error: `Contributor is banned: ${validation.ban_reason ?? 'no reason provided'}` },
      403
    );
  }

  // Parse and validate the request body
  let body: UploadRequest;
  try {
    body = await c.req.json<UploadRequest>();
  } catch {
    return c.json<ErrorResponse>({ error: 'Invalid JSON body' }, 400);
  }

  if (!body || !Array.isArray(body.items)) {
    return c.json<ErrorResponse>({ error: 'Request body must contain an "items" array' }, 400);
  }

  const result = await processUpload(c.env.DB, contributorId, body.items);
  return c.json(result);
});

export default uploadRoutes;
