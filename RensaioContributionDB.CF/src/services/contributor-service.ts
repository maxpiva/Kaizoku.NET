import type { Env } from '../types';
import type { Contributor } from '../db/schema';

/**
 * Look up a contributor by UUID.
 * Returns the row, or null if it does not exist.
 */
export async function getContributor(db: D1Database, id: string): Promise<Contributor | null> {
  const result = await db
    .prepare('SELECT id, admin, active, ban_reason, last_change FROM contributors WHERE id = ?')
    .bind(id)
    .first<Contributor>();

  return result ?? null;
}

/**
 * Validate that a contributor exists and is active.
 *
 * Returns:
 *  - { ok: true } when the contributor is active
 *  - { ok: false, reason: 'not_found' } when the UUID does not exist
 *  - { ok: false, reason: 'banned', ban_reason } when the contributor is inactive
 */
export async function validateActiveContributor(
  db: D1Database,
  id: string
): Promise<{ ok: true; contributor: Contributor } | { ok: false; reason: 'not_found' | 'banned'; ban_reason?: string | null }> {
  const contributor = await getContributor(db, id);

  if (!contributor) {
    return { ok: false, reason: 'not_found' };
  }

  if (contributor.active !== 1) {
    return { ok: false, reason: 'banned', ban_reason: contributor.ban_reason };
  }

  return { ok: true, contributor };
}

/**
 * Validate that a contributor is an active admin.
 */
export async function validateActiveAdmin(
  db: D1Database,
  id: string
): Promise<{ ok: true; contributor: Contributor } | { ok: false; reason: 'not_found' | 'not_admin' | 'banned' }> {
  const contributor = await getContributor(db, id);

  if (!contributor) {
    return { ok: false, reason: 'not_found' };
  }

  if (contributor.active !== 1) {
    return { ok: false, reason: 'banned' };
  }

  if (contributor.admin !== 1) {
    return { ok: false, reason: 'not_admin' };
  }

  return { ok: true, contributor };
}

/**
 * Create a new contributor.
 *
 * Bootstrap rule: when the contributors table is empty, the first created
 * contributor is always an admin (admin UUID may be omitted). Otherwise the
 * caller must supply a valid active admin UUID.
 *
 * @param db         D1 database
 * @param adminId    UUID of the admin performing the creation (optional when empty table)
 * @param isAdmin    whether the new contributor gets admin privileges
 * @returns the new contributor UUID, or an error result.
 */
export async function createContributor(
  db: D1Database,
  adminId: string | undefined,
  isAdmin: boolean
): Promise<
  | { ok: true; contributor_id: string }
  | { ok: false; status: 400 | 403 | 404; error: string }
> {
  // Count existing contributors
  const count = await db.prepare('SELECT COUNT(*) AS count FROM contributors').first<{ count: number }>();
  const total = count?.count ?? 0;

  if (total === 0) {
    // Bootstrap: first contributor is always an admin.
    const id = crypto.randomUUID();
    const now = new Date().toISOString();
    await db
      .prepare('INSERT INTO contributors (id, admin, active, ban_reason, last_change) VALUES (?, 1, 1, NULL, ?)')
      .bind(id, now)
      .run();
    return { ok: true, contributor_id: id };
  }

  // Non-empty table: require a valid active admin
  if (!adminId) {
    return { ok: false, status: 403, error: 'Admin UUID required (contributor table is not empty)' };
  }

  const admin = await getContributor(db, adminId);
  if (!admin) {
    return { ok: false, status: 404, error: 'Admin contributor not found' };
  }
  if (admin.active !== 1) {
    return { ok: false, status: 403, error: 'Forbidden: admin contributor is inactive' };
  }
  if (admin.admin !== 1) {
    return { ok: false, status: 403, error: 'Forbidden: admin privileges required' };
  }

  const id = crypto.randomUUID();
  const now = new Date().toISOString();
  await db
    .prepare('INSERT INTO contributors (id, admin, active, ban_reason, last_change) VALUES (?, ?, 1, NULL, ?)')
    .bind(id, isAdmin ? 1 : 0, now)
    .run();

  return { ok: true, contributor_id: id };
}

/**
 * Convenience wrapper for callers holding the full Env binding.
 */
export async function getContributorEnv(env: Env, id: string): Promise<Contributor | null> {
  return getContributor(env.DB, id);
}

/**
 * Convenience wrapper for callers holding the full Env binding.
 */
export async function validateActiveContributorEnv(
  env: Env,
  id: string
): Promise<{ ok: true; contributor: Contributor } | { ok: false; reason: 'not_found' | 'banned'; ban_reason?: string | null }> {
  return validateActiveContributor(env.DB, id);
}

/**
 * Convenience wrapper for callers holding the full Env binding.
 */
export async function validateActiveAdminEnv(
  env: Env,
  id: string
): Promise<{ ok: true; contributor: Contributor } | { ok: false; reason: 'not_found' | 'not_admin' | 'banned' }> {
  return validateActiveAdmin(env.DB, id);
}
