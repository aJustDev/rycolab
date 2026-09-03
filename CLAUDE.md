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
