import type { BanResponse, BanScrubSummary } from '../models/responses';
import type { Contributor } from '../db/schema';

/**
 * Ban a contributor and archive all their non-archived data.
 *
 * All operations run in a single D1 batch (atomic):
 *  1. Deactivate the target contributor (active = 0) and set ban_reason.
 *  2. Archive all non-archived sources owned by the contributor.
 *  3. Archive all non-archived metadata owned by the contributor.
 *
 * Titles are NOT auto-archived: they are a shared registry and may be
 * referenced by other contributors' sources/metadata.
 *
 * @returns the ban response with a scrub summary, or an error result.
 */
export async function banContributor(
  db: D1Database,
  adminId: string,
  targetId: string,
  banReason: string
): Promise<
  | { ok: true; response: BanResponse }
  | { ok: false; status: 400 | 403 | 404 | 409; error: string }
> {
  // 1. Validate the admin
  const admin = await db
    .prepare('SELECT id, admin, active, ban_reason, last_change FROM contributors WHERE id = ?')
    .bind(adminId)
    .first<Contributor>();

  if (!admin) {
    return { ok: false, status: 404, error: 'Admin contributor not found' };
  }
  if (admin.active !== 1) {
    return { ok: false, status: 403, error: 'Forbidden: admin contributor is inactive' };
  }
  if (admin.admin !== 1) {
    return { ok: false, status: 403, error: 'Forbidden: admin privileges required' };
  }

  // 2. Validate the target
  const target = await db
    .prepare('SELECT id, admin, active, ban_reason, last_change FROM contributors WHERE id = ?')
    .bind(targetId)
    .first<Contributor>();

  if (!target) {
    return { ok: false, status: 404, error: 'Target contributor not found' };
  }
  if (target.active === 0) {
    return { ok: false, status: 409, error: 'Contributor already banned' };
  }
  if (target.admin === 1) {
    return { ok: false, status: 400, error: 'Cannot ban an admin contributor' };
  }

  // 3. Count rows that will be archived (before mutation)
  const sourceCount = await db
    .prepare(
      'SELECT COUNT(*) AS count FROM sources WHERE contributor_id = ? AND archived_at IS NULL'
    )
    .bind(targetId)
    .first<{ count: number }>();
  const metadataCount = await db
    .prepare(
      'SELECT COUNT(*) AS count FROM metadata WHERE contributor_id = ? AND archived_at IS NULL'
    )
    .bind(targetId)
    .first<{ count: number }>();

  const now = new Date().toISOString();

  // 4. Execute the ban atomically
  await db.batch([
    db
      .prepare('UPDATE contributors SET active = 0, ban_reason = ?, last_change = ? WHERE id = ?')
      .bind(banReason, now, targetId),
    db
      .prepare(
        'UPDATE sources SET archived_at = ? WHERE contributor_id = ? AND archived_at IS NULL'
      )
      .bind(now, targetId),
    db
      .prepare(
        'UPDATE metadata SET archived_at = ? WHERE contributor_id = ? AND archived_at IS NULL'
      )
      .bind(now, targetId),
  ]);

  const scrubbed: BanScrubSummary = {
    sources: sourceCount?.count ?? 0,
    metadata: metadataCount?.count ?? 0,
  };

  return {
    ok: true,
    response: {
      banned: true,
      contributor_id: targetId,
      ban_reason: banReason,
      scrubbed,
    },
  };
}
