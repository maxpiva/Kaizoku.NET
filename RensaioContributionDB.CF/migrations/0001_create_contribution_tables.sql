-- 0001_create_contribution_tables.sql
-- Cloudflare Contribution Database — initial schema

CREATE TABLE contributors (
  id          TEXT PRIMARY KEY NOT NULL,
  admin       INTEGER NOT NULL DEFAULT 0,
  active      INTEGER NOT NULL DEFAULT 1,
  ban_reason  TEXT,
  last_change TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE titles (
  id          TEXT PRIMARY KEY NOT NULL,
  title       TEXT NOT NULL,
  archived_at TEXT
);

CREATE TABLE sources (
  id              TEXT PRIMARY KEY NOT NULL,
  title_id        TEXT NOT NULL,
  mihon_source_id TEXT NOT NULL,
  language        TEXT NOT NULL,
  last_chapter    TEXT,
  data            BLOB,
  contributor_id  TEXT NOT NULL,
  last_change     TEXT NOT NULL,
  archived_at     TEXT,
  FOREIGN KEY (title_id) REFERENCES titles(id),
  FOREIGN KEY (contributor_id) REFERENCES contributors(id)
);

CREATE TABLE metadata (
  id                    TEXT PRIMARY KEY NOT NULL,
  title_id              TEXT NOT NULL,
  metadata_provider     TEXT NOT NULL,
  metadata_provider_key TEXT NOT NULL,
  link_type             INTEGER NOT NULL,
  contributor_id        TEXT NOT NULL,
  last_change           TEXT NOT NULL,
  archived_at           TEXT,
  FOREIGN KEY (title_id) REFERENCES titles(id),
  FOREIGN KEY (contributor_id) REFERENCES contributors(id)
);

-- Indexes for performance
CREATE INDEX idx_sources_title_id ON sources(title_id);
CREATE INDEX idx_sources_contributor_id ON sources(contributor_id);
CREATE INDEX idx_sources_archived ON sources(archived_at);
CREATE INDEX idx_metadata_title_id ON metadata(title_id);
CREATE INDEX idx_metadata_contributor_id ON metadata(contributor_id);
CREATE INDEX idx_metadata_archived ON metadata(archived_at);
CREATE INDEX idx_titles_archived ON titles(archived_at);
CREATE INDEX idx_contributors_active ON contributors(active);
CREATE INDEX idx_contributors_admin ON contributors(admin);
