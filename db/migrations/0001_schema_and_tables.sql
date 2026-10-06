-- Phase 1 schema. Applied once, in filename order, by DbUp at startup (see Host/Persistence/DatabaseMigrator.cs).
-- Money is numeric(19,4); timestamps are timestamptz (UTC); enums are smallint (mapped to C# enums).

CREATE SCHEMA IF NOT EXISTS iam;
CREATE SCHEMA IF NOT EXISTS acct;
CREATE SCHEMA IF NOT EXISTS ledger;
CREATE SCHEMA IF NOT EXISTS txn;
CREATE SCHEMA IF NOT EXISTS audit;

-- ---------- iam ----------
CREATE TABLE iam.app_user (
    id            uuid        PRIMARY KEY,
    username      text        NOT NULL,
    password_hash text        NOT NULL,
    role          text        NOT NULL CHECK (role IN ('Admin', 'Teller', 'Auditor')),
    is_active     boolean     NOT NULL DEFAULT true,
    created_at    timestamptz NOT NULL
);
CREATE UNIQUE INDEX uq_app_user_username ON iam.app_user (lower(username));

-- ---------- ledger: chart of accounts ----------
CREATE TABLE ledger.ledger_account (
    id        bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    code      varchar(20) NOT NULL UNIQUE,
    name      text        NOT NULL,
    type      smallint    NOT NULL CHECK (type BETWEEN 1 AND 5),   -- 1 Asset 2 Liability 3 Equity 4 Income 5 Expense
    parent_id bigint      NULL REFERENCES ledger.ledger_account (id),
    is_active boolean     NOT NULL DEFAULT true
);

-- ---------- ledger: fiscal calendar ----------
CREATE TABLE ledger.fiscal_year (
    id         int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name       varchar(20) NOT NULL UNIQUE,
    start_date date        NOT NULL,
    end_date   date        NOT NULL,
    status     smallint    NOT NULL,
    CHECK (end_date > start_date)
);

CREATE TABLE ledger.accounting_period (
    id             int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    fiscal_year_id int         NOT NULL REFERENCES ledger.fiscal_year (id),
    period_no      smallint    NOT NULL,
    start_date     date        NOT NULL,
    end_date       date        NOT NULL,
    status         smallint    NOT NULL CHECK (status BETWEEN 0 AND 4),  -- 0 Future 1 Open 2 SoftClosed 3 Closed 4 Locked
    closed_by      uuid        NULL,
    closed_at      timestamptz NULL,
    UNIQUE (fiscal_year_id, period_no),
    CHECK (end_date >= start_date),
    EXCLUDE USING gist (daterange(start_date, end_date, '[]') WITH &&)   -- periods can never overlap
);

-- ---------- acct ----------
CREATE TABLE acct.customer (
    id         uuid        PRIMARY KEY,
    full_name  text        NOT NULL,
    email      text        NULL,
    created_at timestamptz NOT NULL
);

CREATE SEQUENCE acct.account_number_seq START WITH 1000000001;

CREATE TABLE acct.account (
    id                uuid        PRIMARY KEY,
    account_number    varchar(20) NOT NULL UNIQUE,
    customer_id       uuid        NOT NULL REFERENCES acct.customer (id),
    ledger_account_id bigint      NOT NULL REFERENCES ledger.ledger_account (id),
    currency_code     char(3)     NOT NULL,
    status            smallint    NOT NULL CHECK (status BETWEEN 1 AND 3),   -- 1 Active 2 Frozen 3 Closed
    opened_at         timestamptz NOT NULL
);
CREATE INDEX ix_account_customer ON acct.account (customer_id);

-- Materialized balance, updated in the same DB transaction as every posting (rows are locked FOR UPDATE).
CREATE TABLE acct.account_balance (
    account_id        uuid          PRIMARY KEY REFERENCES acct.account (id),
    currency_code     char(3)       NOT NULL,
    ledger_balance    numeric(19,4) NOT NULL DEFAULT 0,
    hold_total        numeric(19,4) NOT NULL DEFAULT 0,
    available_balance numeric(19,4) GENERATED ALWAYS AS (ledger_balance - hold_total) STORED,
    -- Hard no-negative-balance rule: last line of defence behind the domain check and the row lock.
    CONSTRAINT ck_no_negative CHECK (ledger_balance >= 0 AND hold_total >= 0 AND ledger_balance - hold_total >= 0)
);

-- ---------- txn ----------
CREATE TABLE txn."transaction" (
    id              uuid          PRIMARY KEY,
    idempotency_key varchar(100)  NOT NULL,
    request_hash    char(64)      NOT NULL,
    type            smallint      NOT NULL CHECK (type BETWEEN 1 AND 5),     -- 1 Deposit 2 Withdrawal 3 Transfer 4 Fee 5 Reversal
    status          smallint      NOT NULL CHECK (status BETWEEN 1 AND 4),   -- 1 Pending 2 Posted 3 Reversed 4 Failed
    from_account_id uuid          NULL REFERENCES acct.account (id),
    to_account_id   uuid          NULL REFERENCES acct.account (id),
    amount          numeric(19,4) NOT NULL CHECK (amount > 0),
    currency_code   char(3)       NOT NULL,
    description     text          NULL,
    initiated_by    uuid          NOT NULL,
    posting_date    date          NOT NULL,
    created_at      timestamptz   NOT NULL,
    reversal_of_id  uuid          NULL REFERENCES txn."transaction" (id),
    -- Idempotency keys are scoped per caller.
    CONSTRAINT uq_transaction_idempotency UNIQUE (initiated_by, idempotency_key)
);
CREATE INDEX ix_transaction_from ON txn."transaction" (from_account_id, created_at DESC);
CREATE INDEX ix_transaction_to   ON txn."transaction" (to_account_id, created_at DESC);

CREATE TABLE txn.outbox_message (
    id           uuid        PRIMARY KEY,
    event_type   text        NOT NULL,
    payload      jsonb       NOT NULL,
    created_at   timestamptz NOT NULL,
    processed_at timestamptz NULL,
    attempts     int         NOT NULL DEFAULT 0,
    last_error   text        NULL
);
CREATE INDEX ix_outbox_pending ON txn.outbox_message (created_at) WHERE processed_at IS NULL;

-- ---------- ledger: journal (append-only; see 0002 for immutability) ----------
CREATE TABLE ledger.journal_entry (
    id                bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    transaction_id    uuid        NOT NULL REFERENCES txn."transaction" (id),
    posting_date      date        NOT NULL,
    period_id         int         NOT NULL REFERENCES ledger.accounting_period (id),
    entry_type        smallint    NOT NULL CHECK (entry_type BETWEEN 1 AND 6),  -- 1 Normal 2 Accrual 3 Revaluation 4 Adjustment 5 Closing 6 Reversal
    reverses_entry_id bigint      NULL REFERENCES ledger.journal_entry (id),
    memo              text        NULL,
    created_at        timestamptz NOT NULL
);
CREATE INDEX ix_journal_entry_transaction ON ledger.journal_entry (transaction_id);
CREATE INDEX ix_journal_entry_period      ON ledger.journal_entry (period_id, posting_date);

CREATE TABLE ledger.journal_line (
    id                bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    journal_entry_id  bigint        NOT NULL REFERENCES ledger.journal_entry (id),
    ledger_account_id bigint        NOT NULL REFERENCES ledger.ledger_account (id),
    account_id        uuid          NULL REFERENCES acct.account (id),
    currency_code     char(3)       NOT NULL,
    debit_amount      numeric(19,4) NOT NULL DEFAULT 0,
    credit_amount     numeric(19,4) NOT NULL DEFAULT 0,
    fx_rate           numeric(19,9) NOT NULL DEFAULT 1,
    base_debit        numeric(19,4) NOT NULL DEFAULT 0,
    base_credit       numeric(19,4) NOT NULL DEFAULT 0,
    CONSTRAINT ck_journal_line_one_sided CHECK (
        (debit_amount > 0 AND credit_amount = 0) OR (credit_amount > 0 AND debit_amount = 0))
);
CREATE INDEX ix_journal_line_entry   ON ledger.journal_line (journal_entry_id);
CREATE INDEX ix_journal_line_gl      ON ledger.journal_line (ledger_account_id, journal_entry_id);
CREATE INDEX ix_journal_line_account ON ledger.journal_line (account_id, journal_entry_id) WHERE account_id IS NOT NULL;

-- ---------- audit ----------
CREATE TABLE audit.audit_log (
    id          bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    occurred_at timestamptz NOT NULL,
    actor_id    uuid        NULL,
    action      text        NOT NULL,
    entity_type text        NOT NULL,
    entity_id   uuid        NOT NULL,
    data        jsonb       NULL
);
CREATE INDEX ix_audit_entity ON audit.audit_log (entity_type, entity_id);
