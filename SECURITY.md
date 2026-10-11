# Security

Fieldroom is a local portfolio application. Do not submit real member information,
production passwords or payment credentials to this demo. No payment integration exists.

To report a vulnerability, use GitHub's private vulnerability reporting if enabled;
otherwise contact the repository owner through their GitHub profile without posting
exploit details or secrets in a public issue. Do not include a database dump or `.env`.

Security controls and deployment requirements are documented in
[the authentication decision](docs/decisions/003-auth.md). Production bootstrap fails
closed. The Compose configuration is intentionally Development and loopback-only.
Changing its binding is not a production deployment procedure.

Run the aggregate verifier for NuGet/npm vulnerability checks, authorization/CSRF
tests, and history secret checks. CI additionally runs Gitleaks. No known exceptions
are suppressed. A passing scanner is evidence within its scope, not proof of security.
