# Deployment preparation (not executed)

The default Compose setup is a localhost Development demo. Before hosting, build the
multi-stage image, keep PostgreSQL private, and terminate HTTPS through a trusted proxy.
Use `ASPNETCORE_ENVIRONMENT=Production`, remove `BootstrapPassword`, disable development
automatic migration, configure the public hostname in AllowedHosts, and provision the
first manager through a separately authorized administrative procedure. Production
startup refuses a development bootstrap password. There is no public role-grant API.

Run migrations using an administrative database role before starting the application.
Provision `btree_gist` as part of that controlled migration. Use a separate runtime role
with only needed table/sequence rights; do not grant database-owner/DDL privileges. Keep
database backups and test restore/upgrade procedures. Keep timestamps in UTC and install
IANA timezone support on the host. Do not change the organization's timezone silently.

Persist and encrypt ASP.NET data-protection keys with host-appropriate key management.
Configure forwarded headers only for your known proxy and enforce HTTPS/HSTS at the
edge. Set a strict hostname allowlist and retain antiforgery, secure cookies, CSP and
safe exception responses. Add operational log retention, alerting and access controls
without logging credential-bearing request bodies. Review account recovery and email
verification before using this beyond a controlled demo.

CI verifies the image and local Compose setup, not a cloud deployment. No cloud account,
paid resource, external identity or real-money operation was created or authorized.
