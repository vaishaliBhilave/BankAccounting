# BankAccounting - Phase 1 skeleton

Modular monolith: ASP.NET Core (.NET 10) + PostgreSQL + optional Redis. Double-entry ledger, idempotent
postings, hard no-negative balances, append-only journal, transactional outbox.

## Run locally
```bash
cp .env.example .env        # edit the secrets
docker compose up --build   # API on http://localhost:8080, Postgres on 5432
```
Open http://localhost:8080 for the UI (Ledger Desk), sign in as `teller`, add a customer, open two accounts,
then use Move money. Or run `requests.http` top to bottom to exercise the API directly. Demo users: `admin`, `teller`, `auditor` (password = `DEMO_PASSWORD`).
OpenAPI document (Development environment only): `/openapi/v1.json`. Health: `/health`, `/health/ready`.

UI development with hot reload: keep `docker compose up postgres redis api` running, then
`cd web && npm install && npm run dev` (http://localhost:5173, `/api` is proxied to :8080).
Checks: `npm run typecheck`, `npm test`, `npm run build`.

Without Docker: start Postgres yourself and run
`ConnectionStrings__Default=... Jwt__Key=... dotnet run --project src/Host`.
Migrations (`db/migrations/*.sql`) are applied automatically at startup by DbUp.

## Layout
```
src/BuildingBlocks   Money, Result/Error, IClock, IDomainEvent
src/Accounts         Customer, Account, AccountBalance (no-negative rule)
src/Ledger           LedgerAccount, AccountingPeriod, JournalEntry/Line (balance + append-only rules)
src/Transactions     Transaction + PostTransactionHandler (the posting algorithm) + persistence ports
src/Host             API, EF Core persistence, JWT auth, outbox relay, DbUp migrations
db/migrations        SQL schema, integrity triggers, reference data
web                  React + TypeScript + Vite UI (Ledger Desk); built into src/Host/wwwroot by the Dockerfile
tests/Domain.Tests   xunit: domain rules + handler behaviour against in-memory fakes
```
Rules: domain/application projects have no package dependencies; Host owns all infrastructure.

## Design decisions worth knowing
- **SQL migrations, not EF migrations**: constraint triggers, partial indexes and exclusion constraints are first-class.
  EF Core is used for data access only.
- **One DbContext** so a posting (transaction + journal + balances + audit + outbox) commits atomically.
- **Three layers protect balances**: domain check, `SELECT ... FOR UPDATE` in id order, `ck_no_negative` CHECK.
- **Idempotency** is scoped per caller `(initiated_by, key)`; same key + different payload = 409.
- **Redis is optional**: remove `ConnectionStrings__Redis` and the app runs on the in-memory cache.
- Single base currency (INR). Non-base currencies are rejected until the FX module is enabled.

## Not in this increment
Holds/authorizations, reversals, interest accrual, period-close workflow, AI module, reconciliation. Package versions in `Host.csproj` float (`10.*`); pin them after your first restore.
