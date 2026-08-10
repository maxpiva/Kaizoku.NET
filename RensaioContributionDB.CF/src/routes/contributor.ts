import { Hono } from 'hono';
import type { Env } from '../types';
import { getContributor, createContributor } from '../services/contributor-service';
import type { ContributorResponse, CreateContributorResponse, ErrorResponse } from '../models/responses';
import type { CreateContributorRequest } from '../models/requests';

/**
 * Contributor routes.
 * Base path: /contributor (set in index.ts)
 */
const contributorRoutes = new Hono<{ Bindings: Env }>();

// GET /contributor?contributor={UUID}
contributorRoutes.get('/', async (c) => {
  const contributorId = c.req.query('contributor');

  if (!contributorId) {
    return c.json<ErrorResponse>({ error: 'Missing "contributor" query parameter' }, 400);
  }

  const contributor = await getContributor(c.env.DB, contributorId);

  if (!contributor) {
    return c.json<ErrorResponse>({ error: 'Contributor not found' }, 404);
  }

  const response: ContributorResponse = {
    active: contributor.active === 1,
    admin: contributor.admin === 1,
    ban_reason: contributor.ban_reason,
  };

  return c.json(response);
});

// POST /contributor?admin={adminUUID}
// Create a new contributor. When the contributors table is empty, the admin
// UUID may be omitted and the first contributor becomes an admin (bootstrap).
contributorRoutes.post('/', async (c) => {
  const adminId = c.req.query('admin') ?? undefined;

  let body: CreateContributorRequest;
  try {
    body = await c.req.json<CreateContributorRequest>();
  } catch {
    return c.json<ErrorResponse>({ error: 'Invalid JSON body' }, 400);
  }

  if (body === null || typeof body !== 'object') {
    return c.json<ErrorResponse>({ error: 'Invalid JSON body' }, 400);
  }

  const isAdmin = body.is_admin === true;

  const result = await createContributor(c.env.DB, adminId, isAdmin);

  if (!result.ok) {
    return c.json<ErrorResponse>({ error: result.error }, result.status);
  }

  const response: CreateContributorResponse = { contributor_id: result.contributor_id };
  return c.json(response, 201);
});

export default contributorRoutes;
