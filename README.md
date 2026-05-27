# Flowlio

Modern personal finance app focused on family budgeting, recurring payments, subscriptions and expense tracking.

Because Flowlio is **not** certified for direct bank-API access, transactions are brought in by
**importing account statements** (CSV / PDF) rather than live banking connections.

## Tech stack

- **.NET 10**, C#
- **Blazor WebAssembly** + **Microsoft FluentUI** (client)
- **ASP.NET Core** host serving the WASM client and the API
- **PostgreSQL** + **Entity Framework Core 10** (Npgsql)
- **Wolverine** as the command/message bus, with a **RabbitMQ** transport and a **durable
  transactional outbox** (PostgreSQL-backed) for guaranteed event delivery
- **Redis** for distributed caching, the SignalR backplane and the Data Protection key ring
- **SignalR** for live updates (e.g. import-completed notifications)
- **ASP.NET Core Identity** + **OpenIddict** for authentication — OAuth2 **authorization-code flow
  with PKCE** over HTTPS (no password grant)
- **Riok.Mapperly** for entity ↔ DTO mapping
- **PdfPig** for PDF statement text extraction (Tesseract OCR planned for scanned PDFs)

## Solution layout

```
src/
  Flowlio.Domain          Entities, enums (pure domain)
  Flowlio.Application     Wolverine handlers, statement parsing contracts, Mapperly mappers
  Flowlio.Infrastructure  EF Core context, Identity, OpenIddict store, CSV/PDF parsers
  Flowlio.Shared          DTOs shared between client and server
  Flowlio.Server          ASP.NET Core host: API, auth, SignalR, serves the WASM client
  Flowlio.Client          Blazor WebAssembly + FluentUI
tests/
  Flowlio.Tests           Unit tests (statement parser, dedup)
```

## Messaging & caching

- **RabbitMQ (via Wolverine):** when an import completes, the API publishes a `StatementImported`
  event to the `flowlio.statement-imported` queue. A handler consumes it asynchronously to invalidate
  the cached dashboard and push a live SignalR notification to the family.
- **Durable transactional outbox:** Wolverine persists message envelopes in PostgreSQL (`wolverine`
  schema) and enrolls outgoing messages in the same EF Core transaction as the imported data
  (`[Transactional]` + `UseEntityFrameworkCoreTransactions`). Events are stored on commit and
  delivered with retries by a background agent, so they survive a broker outage or a process crash —
  no event is lost and none is sent for a transaction that rolled back. A durable inbox de-duplicates
  on the consuming side.
- **Redis:** the dashboard summary is cached read-through (per family, 5-minute TTL) and evicted on
  import; SignalR uses a Redis backplane so broadcasts reach clients on any instance; Data Protection
  keys are persisted to Redis so auth/antiforgery cookies survive restarts and scale-out.

## Statement import

`Flowlio.Infrastructure/Statements` contains a profile-driven CSV parser with best-effort layouts for
**ČSOB, Komerční banka, Česká spořitelna, Fio, Air Bank and Revolut**, plus a heuristic PDF parser.
Imported rows are de-duplicated (per account fingerprint) and auto-categorized via user rules.

## Authentication

The Blazor SPA authenticates via the **authorization-code + PKCE** flow against the OpenIddict server
that also hosts it:

1. The SPA redirects unauthenticated users to OpenIddict's `/connect/authorize`.
2. That endpoint requires an interactive cookie login, served by the Razor page `/Account/Login`
   (registration at `/Account/Register`).
3. After login the SPA receives an authorization code and exchanges it (with the PKCE verifier) at
   `/connect/token` for access + refresh tokens.
4. API calls send the access token as a bearer; the API is validated by OpenIddict locally.

The OIDC client (`flowlio-spa`, public, PKCE-required) and the `flowlio.api` scope are seeded on
startup. Redirect URIs are configured under `Spa` in `appsettings.json`.

## Running locally

1. Trust the ASP.NET HTTPS development certificate (once):
   ```bash
   dotnet dev-certs https --trust
   ```
2. Start PostgreSQL, Redis and RabbitMQ:
   ```bash
   docker compose up -d
   ```
3. Run the server (applies EF migrations and seeds the OIDC client + a demo user on first start):
   ```bash
   dotnet run --project src/Flowlio.Server --launch-profile https
   ```
   The app is served at **https://localhost:5443**.
4. Sign in with the seeded demo account:
   - **E-mail:** `rodina@flowlio.local`
   - **Heslo:** `Flowlio123!`

The connection string, redirect URIs and demo credentials live in `src/Flowlio.Server/appsettings.json`.

## Tests

```bash
dotnet test
```

## Notes / next steps

- Tesseract OCR for scanned PDF statements is stubbed (`ImportFormat.PdfOcr`) pending the native library.
- Bank CSV/PDF profiles are a starting point and may need tuning against specific export variants.
