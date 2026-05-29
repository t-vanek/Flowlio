# Flowlio

Modern personal finance app focused on family budgeting, recurring payments, subscriptions and expense tracking.

Transactions come in three ways: **automatically over Open Banking** (PSD2) using each user's own
[Enable Banking](https://enablebanking.com) application (the free "restricted" mode — see the
[Open Banking setup guide](docs/enable-banking.md)), by **importing PDF account statements**, or
**entered by hand** as manual movements. Flowlio itself is not a certified AISP: every user brings
their own Enable Banking credentials, so a connection reaches only that user's own pre-linked
accounts. (Legacy CSV/XLSX import still parses but is deprecated and hidden from the UI.)

## Key features

- **Family-scoped finances:** every account, transaction, card and budget belongs to a family
  (tenant); members join with their own login or are managed by a guardian.
- **Transactions:** PDF statement import with de-duplication and rule-based auto-categorization,
  plus hand-entered movements and movement batches; filter by account, category, type and date range,
  with server-side pagination.
- **Open Banking (PSD2):** optional automatic transaction sync via the **Enable Banking** aggregator,
  with per-user "bring your own application" credentials (private key encrypted at rest), a consent
  setup wizard and a background re-sync. Synced movements flow through the same dedup/categorization
  pipeline as imports. See the [Open Banking setup guide](docs/enable-banking.md).
- **Accounts & cards:** bank accounts with owners, authorized users ("disponents") and per-account
  access levels, payment cards, and child accounts under a guardian.
- **Role-based access control:** per-family roles (Owner / Adult / Viewer / Child) with editable
  permissions, plus cross-family **system roles** for administering login accounts.
- **Administration:** user management (create, lock/block, password reset, force logout, soft-delete /
  restore / purge), an **audit log** of security-relevant actions, and a dashboard summary.
- **Data integrity:** layered validation, optimistic concurrency, soft delete / archive and cascading
  deletes (see [Data integrity & safety](#data-integrity--safety)).

## Screenshots

The UI is in Czech. Click any image to enlarge.

### Dashboard

![Dashboard](docs/screenshots/dashboard.png)

### Transactions

![Transactions](docs/screenshots/transactions.png)

<details>
<summary>More screens</summary>

| | |
|---|---|
| **Manual movements** <br> ![Manual movements](docs/screenshots/movements.png) | **Statement import** <br> ![Statement import](docs/screenshots/import.png) |
| **Accounts** <br> ![Accounts](docs/screenshots/accounts.png) | **Family** <br> ![Family](docs/screenshots/family.png) |
| **Family management – roles** <br> ![Family roles](docs/screenshots/family-roles.png) | **Family management – settings** <br> ![Family settings](docs/screenshots/family-settings.png) |
| **Administration – users** <br> ![Administration users](docs/screenshots/admin-users.png) | **Administration – account roles** <br> ![Administration account roles](docs/screenshots/admin-roles.png) |
| **Administration – audit log** <br> ![Audit log](docs/screenshots/admin-audit.png) | **Not found** <br> ![Not found](docs/screenshots/not-found.png) |

</details>

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
- **MailKit** for e-mail over SMTP; the mailer **authorizes with an OAuth2 token (XOAUTH2)** minted
  by OpenIddict via the **client-credentials** grant, with **smtp4dev** as the local test inbox
- **Riok.Mapperly** for entity ↔ DTO mapping
- **PdfPig** for PDF statement text extraction (Tesseract OCR planned for scanned PDFs)
- **Enable Banking** (PSD2 / Open Banking aggregator) for optional live transaction sync, authenticated
  with a self-signed RS256 JWT; per-user credentials encrypted via ASP.NET Data Protection
- **OpenTelemetry** (traces + metrics) exported via OTLP, with **Jaeger**, **Prometheus** and
  **Grafana** for the local observability stack; optional **Sentry** error reporting

## Solution layout

```
src/
  Flowlio.Domain          Entities, enums (pure domain)
  Flowlio.Application     Wolverine handlers, statement parsing contracts, Mapperly mappers
  Flowlio.Infrastructure  EF Core context, migrations, Identity, OpenIddict store, CSV/PDF parsers
  Flowlio.Shared          DTOs (with DataAnnotations) shared between client and server
  Flowlio.Server          ASP.NET Core host: minimal-API endpoints, auth, SignalR, serves the WASM client
  Flowlio.Client          Blazor WebAssembly + FluentUI
tests/
  Flowlio.Tests           Unit tests (statement parser, dedup)
observability/            otel-collector, Prometheus and Grafana provisioning
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

## Transactions

- **Statement import:** PDF is the primary supported format. PDF statements are read by a
  **coordinate-based table parser** (`Statements/Pdf`): PdfPig yields positioned words, columns are
  reconstructed from their X-positions, each transaction is anchored on the row carrying its amount, and
  continuation rows (counterparty account, value date, multi-line details) are folded in — so the amount
  is never confused with the running balance. Dedicated layouts cover **ČSOB** (year-less dates inferred
  from the statement period) and **Air Bank** (inline VS/KS/SS, merchant from card-payment details);
  unknown layouts fall back to a heuristic parser. Imported rows are de-duplicated (per account
  fingerprint) and auto-categorized via user rules. The profile-driven **CSV/XLSX** parsers
  (`Flowlio.Infrastructure/Statements`, with layouts for ČSOB, Komerční banka, Česká spořitelna, Fio,
  Air Bank and Revolut) are deprecated and hidden from the UI, but still parse uploads for backward
  compatibility.
- **Manual movements:** transactions can also be entered by hand and grouped into a labelled
  **movement batch**. Every batch (`ImportBatch`) records its `BatchOrigin` — `FileImport` for a parsed
  statement or `Manual` for hand-entered movements — so the source of any transaction stays traceable.
  Creating, editing and deleting transactions/batches requires the `ManageTransactions` permission.
- **Browsing:** the transaction list supports free-text search plus filters by account, category, type
  (income / expense) and booking-date range, with server-side pagination and a page-size selector.

## Open Banking (Enable Banking)

Beyond files and manual entry, Flowlio can pull transactions directly from a bank's **PSD2 / Open
Banking** API through the [Enable Banking](https://enablebanking.com) aggregator.

- **Bring your own application (per user):** each user registers their own Enable Banking application
  (free *restricted* mode) and links their own accounts in the Enable Banking portal. The Application
  ID and RSA private key are stored **per user, encrypted at rest** (ASP.NET Data Protection, key ring
  in Redis); Flowlio never acts as a certified AISP and only ever reaches that user's pre-linked
  accounts. Requests are authenticated with a self-signed **RS256 JWT**.
- **Consent flow:** a setup wizard (`/bank-connect`) starts the authorization, the user completes
  strong customer authentication at their bank, and the bank's redirect to `/bank-connections/callback`
  finalizes the consent (correlated by an unguessable state token) and binds it to a Flowlio account.
- **Sync:** transactions are fetched (paged) and run through the **same dedup + auto-categorization
  pipeline** as statement import, emitting the same completion event (dashboard cache invalidation +
  live notification). A background service re-syncs active connections periodically; the PSD2 consent
  expires after ~90 days and prompts re-authorization.

See the [step-by-step setup guide](docs/enable-banking.md).

## Families, roles & access control

- **Families are the tenant.** A user's family is provisioned on first sign-in with a default category
  set and role permissions. Members either have their own login (via invitation) or are guardian-managed
  (e.g. young children).
- **Family roles** — `Owner`, `Adult`, `Viewer`, `Child` — map to editable sets of `Permission`s
  (view finances, manage accounts / cards / members / roles / family, import statements, manage
  transactions, grant per-account access). The API authorizes by permission, not by role name; the
  Owner always holds every permission.
- **Per-account access:** non-owner members can be granted **disponent** or read-only access to an
  account; payment cards can be assigned to members, including spending limits for children.
- **System roles** are cross-family and govern administration of login accounts (view, create, manage
  roles, lockout, passwords, force logout, delete, manage system roles, view the audit log). The
  built-in `Administrator` role always holds every system permission and is immutable.

## Data integrity & safety

- **Layered validation:** request DTOs in `Flowlio.Shared` carry DataAnnotations (one source of truth);
  the Blazor forms validate them with `EditForm` for inline feedback, a server-side endpoint filter
  re-validates every request (the client is bypassable), and PostgreSQL `CHECK` constraints back the key
  invariants (currency length, card expiry / limit, non-empty names, non-negative amounts, …).
- **Optimistic concurrency:** family, member, card and account-access edits use the Postgres `xmin`
  system column as a row-version token. A stale update fails with **HTTP 409** instead of silently
  overwriting a concurrent change, and the UI prompts to reload. (Login accounts keep Identity's
  `ConcurrencyStamp`.)
- **Soft delete & archive:** login accounts, bank accounts, family members and cards are soft-deleted
  (hidden by global query filters) rather than removed, so history and references survive; accounts can
  be archived and restored, and deleted users can be restored or permanently purged.
- **Cascading deletes:** deleting a family cascades to all of its data; deleting an account cascades to
  its transactions, cards and access grants. The append-only audit log is deliberately preserved.
- **Audit log:** security- and administration-relevant actions (account/member/card/role changes,
  access grants, family changes, user administration) are recorded to an append-only log, viewable by
  holders of the `ViewAuditLog` system permission.

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

## E-mail (SMTP)

Transactional e-mail (currently family invitations) goes through the `IEmailSender` abstraction
(`Flowlio.Application.Abstractions`), implemented over SMTP with **MailKit** in
`Flowlio.Infrastructure/Email`. Sending never blocks inviting: SMTP failures are logged and the
invite link is still returned to the inviter.

Authorization to the SMTP server uses **OAuth2 / XOAUTH2** rather than a static password:

1. `OpenIddictSmtpTokenProvider` requests an access token from OpenIddict via the **client-credentials**
   grant (confidential client `flowlio-mailer`, scope `flowlio.smtp`), caching it until just before expiry.
2. `SmtpEmailSender` presents that token to the SMTP server with MailKit's `SaslMechanismOAuth2` (XOAUTH2).

The `flowlio.smtp` scope (resource/audience `flowlio-smtp`) and the `flowlio-mailer` client are seeded on
startup; SMTP settings live under `Smtp` in `appsettings.json`.

For local testing, `docker compose` starts **smtp4dev** as a catch-all inbox that requires XOAUTH2:

- **Web inbox:** http://localhost:5080
- **SMTP:** `localhost:2525`

By default smtp4dev accepts the presented token (catcher-friendly). To validate the token's signature,
issuer and audience against the running app, set `SmtpAllowAnyCredentials=false` and the `OAuth2*` options
in `docker-compose.yml` (the app must be reachable from the container, e.g. `https://host.docker.internal:5443/`).

## Observability

The app instruments traces and metrics with **OpenTelemetry** and exports them over OTLP. Point it at a
collector via `OpenTelemetry:OtlpEndpoint` (or `OTEL_EXPORTER_OTLP_ENDPOINT`), e.g.
`http://localhost:4317`. `docker compose` runs the full local stack — an OpenTelemetry collector that
fans traces out to **Jaeger** and exposes metrics for **Prometheus**, with **Grafana** on top:

- **Jaeger (traces):** http://localhost:16686
- **Grafana:** http://localhost:3000
- **Prometheus:** http://localhost:9090

Errors can optionally be reported to **Sentry** by setting `SENTRY_DSN` (see the `Sentry` config).

## Running locally

1. Trust the ASP.NET HTTPS development certificate (once):
   ```bash
   dotnet dev-certs https --trust
   ```
2. Start PostgreSQL, Redis, RabbitMQ, the smtp4dev test inbox and the observability stack:
   ```bash
   docker compose up -d
   ```
   The smtp4dev web inbox is then at **http://localhost:5080** (Jaeger / Grafana / Prometheus as above).
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
- Bank PDF layouts are a starting point and may need tuning against specific statement variants. The
  legacy CSV/XLSX profiles are deprecated (hidden from the UI) and kept only for backward compatibility.
</content>
</invoke>
