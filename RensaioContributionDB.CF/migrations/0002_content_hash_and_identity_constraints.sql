-- 0002_content_hash_and_identity_constraints.sql
-- Rensaio Contribution Database — schema hardening for concurrent contributors.

-- 1. Deduplicate pre-existing active titles (keep the OLDEST id so existing
--    foreign-key references stay valid; archive the rest).
UPDATE titles
SET archived_at = COALESCE(archived_at, datetime('now'))
WHERE archived_at IS NULL
  AND rowid NOT IN (
    SELECT MIN(rowid) FROM titles GROUP BY title
  );

-- 2. Deduplicate pre-existing active metadata rows by identity key
--    (keep the NEWEST; archive the rest — last upload wins).
UPDATE metadata
SET archived_at = COALESCE(archived_at, datetime('now'))
WHERE archived_at IS NULL
  AND rowid NOT IN (
    SELECT MAX(rowid) FROM metadata
    GROUP BY title_id, metadata_provider, metadata_provider_key
  );

-- 3. Prevent duplicate ACTIVE titles. Concurrent contributors racing to
--    create the same title used to produce a primary-key violation that
--    failed the whole batch. The partial index only constrains active rows,
--    so archived titles can be re-created after reconciliation.
CREATE UNIQUE INDEX idx_titles_title_active ON titles(title) WHERE archived_at IS NULL;

-- 4. Prevent duplicate ACTIVE metadata identity rows
--    (title_id + metadata_provider + metadata_provider_key).
CREATE UNIQUE INDEX idx_metadata_identity_active
  ON metadata(title_id, metadata_provider, metadata_provider_key)
  WHERE archived_at IS NULL;

-- 5. content_hash columns for the skip-if-match write optimization: on
--    `add`, if the incoming content hashes to the stored value, the row is
--    skipped entirely (no UPDATE, no ownership transfer).
ALTER TABLE sources  ADD COLUMN content_hash TEXT;
ALTER TABLE metadata ADD COLUMN content_hash TEXT;

-- 6. Index for the server-side title lookup (title resolution on upload)
--    and the daily orphan-title archive.
CREATE INDEX idx_titles_title ON titles(title);
