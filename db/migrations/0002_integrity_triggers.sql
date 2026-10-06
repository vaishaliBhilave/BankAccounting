-- Integrity safety nets. The domain layer enforces these rules first; the database refuses to
-- store a violation even if a bug (or a direct SQL session) tries.

-- 1. Every journal entry must balance per currency, and in base currency, checked at COMMIT
--    (deferred) so lines can be inserted one by one.
CREATE FUNCTION ledger.check_entry_balanced() RETURNS trigger AS $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM ledger.journal_line
        WHERE journal_entry_id = NEW.journal_entry_id
        GROUP BY currency_code
        HAVING SUM(debit_amount) <> SUM(credit_amount)
    ) OR EXISTS (
        SELECT 1 FROM ledger.journal_line
        WHERE journal_entry_id = NEW.journal_entry_id
        HAVING SUM(base_debit) <> SUM(base_credit)
    ) THEN
        RAISE EXCEPTION 'Journal entry % is not balanced', NEW.journal_entry_id USING ERRCODE = '23514';
    END IF;
    RETURN NULL;
END
$$ LANGUAGE plpgsql;

CREATE CONSTRAINT TRIGGER trg_journal_entry_balanced
    AFTER INSERT ON ledger.journal_line
    DEFERRABLE INITIALLY DEFERRED
    FOR EACH ROW EXECUTE FUNCTION ledger.check_entry_balanced();

-- 2. The ledger is append-only: corrections are reversing entries, never edits.
CREATE FUNCTION ledger.deny_change() RETURNS trigger AS $$
BEGIN
    RAISE EXCEPTION '% on %.% is not allowed: this table is append-only (post a reversal instead)',
        TG_OP, TG_TABLE_SCHEMA, TG_TABLE_NAME USING ERRCODE = '42501';
END
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_journal_line_immutable  BEFORE UPDATE OR DELETE ON ledger.journal_line  FOR EACH ROW EXECUTE FUNCTION ledger.deny_change();
CREATE TRIGGER trg_journal_entry_immutable BEFORE UPDATE OR DELETE ON ledger.journal_entry FOR EACH ROW EXECUTE FUNCTION ledger.deny_change();
CREATE TRIGGER trg_audit_log_immutable     BEFORE UPDATE OR DELETE ON audit.audit_log      FOR EACH ROW EXECUTE FUNCTION ledger.deny_change();
CREATE TRIGGER trg_journal_line_no_truncate  BEFORE TRUNCATE ON ledger.journal_line  FOR EACH STATEMENT EXECUTE FUNCTION ledger.deny_change();
CREATE TRIGGER trg_journal_entry_no_truncate BEFORE TRUNCATE ON ledger.journal_entry FOR EACH STATEMENT EXECUTE FUNCTION ledger.deny_change();
CREATE TRIGGER trg_audit_log_no_truncate     BEFORE TRUNCATE ON audit.audit_log      FOR EACH STATEMENT EXECUTE FUNCTION ledger.deny_change();

-- 3. Postings must fall inside their accounting period, and the period must accept them:
--    Open accepts everything; SoftClosed accepts only Adjustment entries; everything else is refused.
CREATE FUNCTION ledger.guard_posting_period() RETURNS trigger AS $$
DECLARE
    p ledger.accounting_period%ROWTYPE;
BEGIN
    SELECT * INTO p FROM ledger.accounting_period WHERE id = NEW.period_id;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'Unknown accounting period %', NEW.period_id USING ERRCODE = '23514';
    END IF;
    IF NEW.posting_date < p.start_date OR NEW.posting_date > p.end_date THEN
        RAISE EXCEPTION 'Posting date % is outside period %', NEW.posting_date, NEW.period_id USING ERRCODE = '23514';
    END IF;
    IF NOT (p.status = 1 OR (p.status = 2 AND NEW.entry_type = 4)) THEN
        RAISE EXCEPTION 'Period % is not open for posting', NEW.period_id USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_journal_entry_period_guard
    BEFORE INSERT ON ledger.journal_entry
    FOR EACH ROW EXECUTE FUNCTION ledger.guard_posting_period();
