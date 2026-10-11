# Verification evidence

Recorded on 2026-10-11, Pacific/Auckland. Run `node scripts/verify.mjs` (or
`./scripts/verify.ps1` for the optional Windows tool paths and native database).

Local aggregate run passed: format checks, ESLint, TypeScript and production build;
**45 backend tests, 11 frontend tests, five Chromium browser journeys**, plus an actual
API process restart probe. npm/NuGet audits reported no vulnerabilities and the
reachable-history publication scan passed. A fresh clone of commit `4beea1b`, with
`npm ci` and no copied build outputs, also passed the entire aggregate against its own
isolated databases. The SDK/Node runtimes and local PostgreSQL installation were
supplied externally, as documented; application environment files were not copied.

Backend uses an isolated real PostgreSQL 18.6 database, not SQLite or an in-memory
provider. Tests cover migration from empty, upgrade preserving an existing product,
repeatable seed, competing connections, member hold limits, exclusion via direct SQL,
null/unbounded/infinite ranges, expiry while waiting for an asset lock, idempotent
duplicates/changed payloads/lost response, persistent conflicts, atomic reschedule,
cancellation versus confirmation, pickup/return/inspection, early-return retention,
retirement constraints, audit immutability, DST/policy boundaries, cookie sessions,
private ownership, manager authorization, role injection, CSRF and expired tickets.

Browser journeys: member registration/hold/confirm/refresh/back/reschedule/cancel;
two independent sessions competing for one copy; server-committed response loss and
expired hold reuse; manager pickup/damaged return/inspection/product edit; desktop
and 390px mobile layout, keyboard skip link, and axe catalog/product/desk checks.
Frontend unit tests additionally exercise response-body loss, 401/403/429 during
same-key recovery, rapid duplicate calls, time inputs, session error announcements,
timezone display and redirect validation. Screenshots in `docs/screenshots` are
visually inspected; they contain fictional identities only.

The restart probe confirms a booking, stops its isolated API process, starts a new
process against the same database and key directory, then verifies the authenticated
cookie, original booking reference, exact same-key response and single confirmation
audit event. Direct SQL also proves two different bookings cannot open checkouts for
one asset: PostgreSQL rejects the second with the asset partial unique constraint.

Independent reviewer findings fixed: stale retry intent, unsafe next URL, missing
metadata idempotency/version checking, incomplete risk display, wrapped network error
classification, interrupted body/auth-throttle recovery, and shortened inspection
turnaround. Review is evidence of a deliberate separate pass, not a security guarantee.

Docker image build and fresh Compose startup **passed in Linux CI**, including empty
database migrations, health/readiness, the 15-product catalog and same-origin frontend.
The aggregate also passed on the GitHub Linux runner. Local Docker Desktop could not
start; native local execution and the independent CI container checks are distinct.

Gitleaks 8.30.1 passed locally over all reachable history with no findings. CI uses
the same pinned release, verifies its published SHA-256, and scans `--all`, including
the root commit. No rules or findings are suppressed. The final commit's remote result
is the authoritative [GitHub Actions run](https://github.com/ammaar134/Live-Event-Operations/actions/workflows/verify.yml),
linked from the README badge. No cloud deployment was performed.
