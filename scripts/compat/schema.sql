CREATE TABLE IF NOT EXISTS checks (
 base_sha256 TEXT NOT NULL, mod_sha256 TEXT NOT NULL, analyzer_version INTEGER NOT NULL,
 compatibility TEXT NOT NULL, reason TEXT NOT NULL, adds_json TEXT NOT NULL,
 source_build_utc TEXT, mod_name TEXT NOT NULL, provider TEXT NOT NULL, external_id TEXT,
 file_id TEXT NOT NULL, version TEXT, size_bytes INTEGER, checked_utc TEXT NOT NULL,
 PRIMARY KEY(base_sha256, mod_sha256, analyzer_version)
);
CREATE INDEX IF NOT EXISTS checks_source ON checks(base_sha256, provider, file_id, analyzer_version);
CREATE TABLE IF NOT EXISTS source_files (
 base_sha256 TEXT NOT NULL, mod_sha256 TEXT NOT NULL, analyzer_version INTEGER NOT NULL,
 provider TEXT NOT NULL, file_id TEXT NOT NULL, mod_name TEXT NOT NULL, external_id TEXT,
 version TEXT, size_bytes INTEGER,
 PRIMARY KEY(base_sha256, provider, file_id, analyzer_version)
);
