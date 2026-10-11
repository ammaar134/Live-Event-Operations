# Fieldroom handoff

Phases 1–4 complete: foundation and booking slice; guarded consistency/lifecycle;
member and lending-desk operations; original artwork, responsive layouts and accessible
recovery states. Phase 5 review fixes, documentation, clean-clone verification and
publication are complete; commit-specific CI results are linked below.

The 12 October visual revision replaces the serif treatment with locally hosted
Schibsted Grotesk, forest/paper colors, an original borrow/return mark, illustrated
category links and a responsive catalog rail. Booking, account and desk screens use
the same hierarchy. The original equipment art remains intact. [Design research](DESIGN.md)
and [font licensing](ASSETS.md) record the sources and decisions. The full local
aggregate passed again with 45 backend, 11 frontend and five browser tests, including
320/390/768px overflow checks and the new browsing controls.

The supplied repository `ammaar134/Live-Event-Operations` was empty and is the explicit
public destination. It contains only this equipment app. MIT was confirmed by Ammaar.
Git identity uses the verified account's GitHub noreply address. Implementation and
release commits were published without rewriting history. No cloud deployment or payments.

Architecture: React/TypeScript, ASP.NET Core Identity, EF Core and PostgreSQL; one
allocation ledger, post-lock database time, durable idempotency and separate custody.
Independent review fixes include uncertain-response recovery, stale retry intents,
safe redirects, manager metadata idempotency/version checks, risk display, wrapped
database failures and retention of accepted inspection turnaround.

Local aggregate and a clean clone of `4beea1b` passed: **45 backend / 11 frontend /
5 browser tests**, real PostgreSQL 18.6, API process restart, formatting/lint/build,
dependency audits and full reachable-history publication scan. Desktop/mobile screenshots
were inspected; axe checks cover catalog, product and lending desk. See
[verification](VERIFICATION.md) for scope and [CI](https://github.com/ammaar134/Live-Event-Operations/actions)
for commit-specific remote results.

Exact aggregate: `npm ci --prefix web`, `npx --prefix web playwright install chromium`,
then `node scripts/verify.mjs`. Windows native helper: `./scripts/verify.ps1`.
The browser runner creates its own database and ports 5095/5185, then removes its
resources. No production test endpoints or external service dependencies.

Native setup was verified on loopback ports 5175 (web), 5085 (API), 55435 (PostgreSQL).
Run `./scripts/dev.ps1` and `./scripts/web.ps1` in separate terminals. The optional
SDK/Node/PostgreSQL binaries are ignored under `.tools`; credentials and local data are
ignored under `.env`/`.local`. Docker Desktop could not start locally; the image build
and fresh Compose startup passed on the Linux CI runner. Deployment preparation is
documentation only. Gitleaks scans all reachable history, including the root commit.

Intentional limits: one organization/asset per loan, no email recovery/verification,
no payments/uploads/3D, no idempotency retention cleanup, 200 most recent loans in the
member history. These are documented boundaries, not placeholder routes.
