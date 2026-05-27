# Flowlio

Modern personal finance app focused on family budgeting, recurring payments, subscriptions and expense tracking.

Because Flowlio is **not** certified for direct bank-API access, transactions are brought in by
**importing account statements** (CSV / PDF) rather than live banking connections.

## Tech stack

- **.NET 10**, C#
- **Blazor WebAssembly** + **Microsoft FluentUI** (client)
- **ASP.NET Core** host serving the WASM client and the API
- **PostgreSQL** + **Entity Framework Core 10** (Npgsql)
- **Wolverine** as the in-process command/message bus
- **SignalR** for live updates
- **ASP.NET Core Identity** + **OpenIddict** for authentication (OAuth2 / OIDC)
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

## Statement import

`Flowlio.Infrastructure/Statements` contains a profile-driven CSV parser with best-effort layouts for
**ČSOB, Komerční banka, Česká spořitelna, Fio, Air Bank and Revolut**, plus a heuristic PDF parser.
Imported rows are de-duplicated (per account fingerprint) and auto-categorized via user rules.

## Running locally

1. Start PostgreSQL:
   ```bash
   docker compose up -d
   ```
2. Run the server (applies EF migrations and seeds a demo user on first start):
   ```bash
   dotnet run --project src/Flowlio.Server
   ```
3. Open the app and sign in with the seeded demo account:
   - **E-mail:** `rodina@flowlio.local`
   - **Heslo:** `Flowlio123!`

The connection string and demo credentials live in `src/Flowlio.Server/appsettings.json`.

## Tests

```bash
dotnet test
```

## Notes / next steps

- OAuth uses the password grant for the first-party SPA; hardening to authorization-code + PKCE is a
  recommended follow-up. HTTPS transport security is only relaxed in the Development environment.
- Tesseract OCR for scanned PDF statements is stubbed (`ImportFormat.PdfOcr`) pending the native library.
- Bank CSV/PDF profiles are a starting point and may need tuning against specific export variants.
