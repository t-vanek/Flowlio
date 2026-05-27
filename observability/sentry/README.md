# Self-hosted Sentry

Flowlio integrates the Sentry .NET SDK (see `Sentry` config in `appsettings.json`). Sentry itself is
**not** part of Flowlio's `docker-compose.yml` because the official self-hosted distribution is a large,
standalone stack (Kafka, ClickHouse, Snuba, Relay, Postgres, Redis, web, workers …). Run it separately,
then point Flowlio at its DSN.

## Requirements
- Docker + Docker Compose v2
- ~16 GB RAM recommended, several GB of disk
- A release tag from <https://github.com/getsentry/self-hosted/releases>

## Install & run
```bash
SENTRY_VERSION=25.1.0 ./observability/sentry/install-self-hosted.sh
cd .sentry-self-hosted
docker compose up -d
```
`install.sh` applies migrations and (unless skipped) creates the admin user. The UI is at
<http://localhost:9000>. `.sentry-self-hosted/` is ignored by git.

## Connect Flowlio
1. In the Sentry UI create a project (platform: **.NET**) and copy its **DSN** —
   for self-hosted it looks like `http://<public_key>@localhost:9000/<project_id>`.
2. Provide it to Flowlio in one of these ways:
   - `appsettings.json` (or `appsettings.Development.json`):
     ```json
     "Sentry": { "Dsn": "http://<public_key>@localhost:9000/<project_id>" }
     ```
   - or the standard environment variable:
     ```bash
     export SENTRY_DSN="http://<public_key>@localhost:9000/<project_id>"
     ```
3. Restart Flowlio. With no DSN, the Sentry integration stays disabled (no-op).

## Notes
- The app sends errors (and lower-level breadcrumbs) to Sentry via the Serilog sink; unhandled
  exceptions are captured by the ASP.NET Core integration.
- Performance tracing to Sentry is off by default; raise `Sentry:TracesSampleRate` (0.0–1.0) to enable.
- Distributed tracing/metrics still flow through OpenTelemetry to Jaeger/Grafana — Sentry is for error
  monitoring and is complementary.
- To stop Sentry: `cd .sentry-self-hosted && docker compose down`.
