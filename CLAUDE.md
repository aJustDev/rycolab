# rycolab

## Data

Everything rycolab measures or records (guard ticks and events, sweep runs,
samples, limits, bench logs, battery health) is persisted in one SQLite
database, `%LOCALAPPDATA%\rycolab\rycolab.db`, through `Store`
(`src/Rycolab.Core/Store.cs`). Rules:

- A new measurement or event = a column or table in `Store` (an additive
  `ALTER TABLE` in `Migrate`, never a rebuild), a round-trip test in
  `tests/Rycolab.Tests/StoreTests.cs`, and a way to see it (`report`, or at
  least `rycolab db sql`).
- Never a new time-series file (no JSONL, no CSV journal). `--out` / `--json`
  on `dev` commands are exports of what is already in the database, not
  history.
- Whole-file JSON (`Journal.WriteJsonFile`) is for state and configuration
  only: profile, config, state, validation, the small marker files.
- Timestamps are ISO 8601 round-trip strings (`ToString("o")`), local time.

## Working on the code

- Read before editing; prefer `Edit` to rewrites. Simplest thing that
  works, no speculative features, no abstractions for one use.
- Before every commit: `dotnet build -c Release src/Rycolab.Cli` with 0
  warnings and `dotnet test -c Release tests/Rycolab.Tests` green. Code
  that talks to the hardware is verified on the machine and the numbers go
  to `docs/lab-notebook.md` (dated entry) or `docs/field-notes.md` (the
  durable lesson).
- ASCII only in code, docs, commits and JSON. Keep the Unicode that already
  exists in a file.
- Commits: one per purpose, message in English, lowercase, `scope: what
  changed and why it matters` (`guard:`, `sweep:`, `store:`, `report:`,
  `status:`, `docs:`, `notebook:`, `ci:`, `release:`). No trailers of any
  kind. Never push, tag or release unless asked in the current turn.
- Anything that changes the machine (services, drivers, power plans, the
  installed guard, a campaign) is confirmed with the user first, even when
  the task is clear.

## Releases

Versioning rules are in `README.md` (Versioning). A release is: bump
`<Version>` in `Directory.Build.props`, commit `release: vX.Y.Z`, annotated
tag `vX.Y.Z` whose message is the release notes (plain ASCII, what changed
for the user), push main and the tag; `.github/workflows/build.yml` builds
the zip and `SHA256SUMS.txt` and creates the GitHub release. The schema
version in `Store` moves only with a minor or a major.
