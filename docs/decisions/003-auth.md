# Same-origin sessions

ASP.NET Core Identity owns hashing, verification, lockout and cookie authentication.
The browser holds an HttpOnly, SameSite=Strict session cookie, with Secure required
outside Development/Testing. Session lifetime is eight hours without sliding renewal.
Session restoration reads `/api/session`; role values originate on the server.
Registration cannot assign a role. Development manager bootstrap requires an explicit
locally generated password and fails startup outside Development.

All browser POST endpoints, including login, registration and logout, validate an
ASP.NET antiforgery token from `/api/session/csrf` in `X-CSRF-TOKEN`. Fetch a new token
after authentication identity changes. APIs return 401/403 rather than HTML redirects.
Private reads constrain ownership; a guessed booking belonging to another member is
404. Manager routes require the Manager role, and service methods independently check
privileged lifecycle transitions. No CORS policy enables cross-origin browser access.

Login and registration are limited per IP, holds per member, and Identity adds account
lockout. The request body is capped at 64 KiB; catalog and desk queries are bounded.
SQL values are parameters. Exceptions return safe messages with trace IDs; no request
bodies or passwords are logged. CSP restricts assets/scripts to the local origin.
Post-login navigation resolves the URL and requires the same origin and an allowed
internal path, including rejecting backslash URL tricks.

Local key material and credentials live in ignored `.local`/`.env` paths; never commit
or share them. Production hosting needs TLS, protected persistent data-protection keys,
a least-privilege application database role, migrations through an administrative role,
trusted reverse-proxy configuration, backups and an explicit manager provisioning path.
No production deployment was performed. Password reset/email verification and external
identity providers are outside this single-organization portfolio application's scope.

Reference: https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery
