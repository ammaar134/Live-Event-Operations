# Contributing

Use the pinned SDK and Node version. Follow README setup, then run
`node scripts/verify.mjs` before proposing a change. Use fictional data only.

Keep occupancy mutations in the documented transaction/lock protocol. Never replace
the PostgreSQL race tests with SQLite or an in-memory provider. Add a deterministic
regression test for any booking, custody or retry bug. Preserve accepted policy
snapshots and append-only audit records. UI actions must have real persistent behavior.

Format C# with `dotnet format` and the web project with `npm run format --prefix web`.
Keep lockfiles current; review dependency changes and image/font licenses. Do not
commit `.env`, keys, database files, runtime downloads, reports or credential-bearing
screenshots. Use logically scoped commits and a draft PR for review.
