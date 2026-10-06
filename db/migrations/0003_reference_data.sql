-- System GL accounts (codes mirror SystemLedgerCodes in the Ledger module).
INSERT INTO ledger.ledger_account (code, name, type) VALUES
    ('1000', 'Cash and Settlement',  1),
    ('2100', 'Savings Deposits',     2),
    ('2200', 'Current Deposits',     2),
    ('2300', 'Interest Payable',     2),
    ('3000', 'Retained Earnings',    3),
    ('4100', 'Fee Income',           4),
    ('5100', 'Interest Expense',     5),
    ('5900', 'Rounding Differences', 5)
ON CONFLICT (code) DO NOTHING;

-- Indian fiscal years (1 Apr - 31 Mar), 12 monthly periods each.
-- FY2026-27 is open for posting; FY2027-28 exists but is Future (not postable) until opened by the period-close workflow.
DO $$
DECLARE
    fy_id int;
    y     int;
    i     int;
    s     date;
    st    smallint;
BEGIN
    FOR y IN 2026..2027 LOOP
        st := CASE WHEN y = 2026 THEN 1 ELSE 0 END;
        INSERT INTO ledger.fiscal_year (name, start_date, end_date, status)
        VALUES (format('FY%s-%s', y, right((y + 1)::text, 2)), make_date(y, 4, 1), make_date(y + 1, 3, 31), st)
        RETURNING id INTO fy_id;

        FOR i IN 0..11 LOOP
            s := (make_date(y, 4, 1) + make_interval(months => i))::date;
            INSERT INTO ledger.accounting_period (fiscal_year_id, period_no, start_date, end_date, status)
            VALUES (fy_id, i + 1, s, (s + interval '1 month' - interval '1 day')::date, st);
        END LOOP;
    END LOOP;
END
$$;
