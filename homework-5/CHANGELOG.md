# Changelog

All notable changes to this project are documented here following
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Changed
- Contract removal of legacy `Passes` is deferred to a separately approved
  operation after every client has switched to the new table.

## [1.9.0] - 2026-10-31

### Added
- New PostgreSQL `AccessPasses` table and three-phase migration scripts.
- `jira.issue=CAMPUS-101` and `campus.pass_id` tracing tags on API requests.
- xUnit production sanity scenario with test-only pass cleanup.
- CR, ADR 0009, branch-protection proposal and SQL verification CI job.

### Changed
- Transitional C# write path supports atomic dual-write and configurable
  `dual`/`new` schema modes.

### Fixed
- Legacy writes, updates and deletes are mirrored to `AccessPasses` during
  mixed-version deployments.

### Breaking Changes
- **None in v1.9.0.** Legacy `Passes` remains accessible. Dropping it is a
  **future breaking schema change**, allowed only after explicit Contract approval.
