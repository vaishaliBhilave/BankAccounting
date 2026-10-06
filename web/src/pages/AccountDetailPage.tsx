import { useMemo, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { api, type StatementRow } from '../api';
import { useAuth } from '../auth';
import { Empty, ErrorNote, Loading, ConfirmButton, StatusBadge } from '../components/Bits';
import { formatDate, money, plainAmount } from '../format';
import { useAsync } from '../useAsync';

/** Rows come newest-first; the running balance walks back from today's ledger balance. */
export function withRunningBalance(rows: StatementRow[], ledgerBalance: number) {
  let balance = ledgerBalance;
  return rows.map(row => {
    const after = balance;
    balance = row.direction === 'Credit' ? balance - row.amount : balance + row.amount;
    return { row, after };
  });
}

function particulars(row: StatementRow): string {
  const counterparty = row.counterpartyNumber;
  switch (row.type) {
    case 'Deposit': return 'Cash deposit';
    case 'Withdrawal': return 'Cash withdrawal';
    case 'Transfer': return row.direction === 'Debit' ? `Transfer to ${counterparty}` : `Transfer from ${counterparty}`;
    default: return row.type;
  }
}

export function AccountDetailPage() {
  const { id = '' } = useParams();
  const { canPost, isAdmin } = useAuth();
  const account = useAsync(() => api.getAccount(id), [id]);
  const statement = useAsync(() => api.statement(id), [id]);
  const customerId = account.data?.customerId;
  const customer = useAsync(() => (customerId ? api.getCustomer(customerId) : Promise.resolve(null)), [customerId]);
  const [statusError, setStatusError] = useState<unknown>(null);

  const rows = useMemo(
    () => (account.data && statement.data ? withRunningBalance(statement.data, account.data.ledgerBalance) : []),
    [account.data, statement.data]);

  if (account.error != null) return <ErrorNote error={account.error} />;
  if (!account.data) return <Loading />;
  const a = account.data;

  const changeStatus = (action: () => Promise<unknown>) => async () => {
    setStatusError(null);
    try { await action(); account.reload(); } catch (err) { setStatusError(err); }
  };

  return (
    <>
      <p className="crumb"><Link to="/accounts">Accounts</Link></p>
      <header className="page-head">
        <div>
          <h1 className="num">{a.accountNumber}</h1>
          <p className="muted">
            {customer.data ? <Link to={`/customers/${customer.data.id}`}>{customer.data.fullName}</Link> : 'Loading holder'}
            {' '}<StatusBadge status={a.status} />
          </p>
        </div>
      </header>

      <section className="passbook" aria-label="Balances">
        <div className="balance-main">
          <span className="label">Available to spend</span>
          <span className="balance-figure num">{money(a.availableBalance, a.currency)}</span>
        </div>
        <dl className="balance-side">
          <div><dt>Ledger balance</dt><dd className="num">{money(a.ledgerBalance, a.currency)}</dd></div>
          <div><dt>Held</dt><dd className="num">{money(a.holdTotal, a.currency)}</dd></div>
        </dl>
      </section>

      <div className="actions">
        {canPost && (
          <>
            <Link className="btn btn-primary" to={`/move?kind=deposit&account=${a.id}`}>Deposit</Link>
            <Link className="btn" to={`/move?kind=withdraw&account=${a.id}`}>Withdraw</Link>
            <Link className="btn" to={`/move?kind=transfer&account=${a.id}`}>Transfer out</Link>
          </>
        )}
        {isAdmin && a.status === 'Active' && <ConfirmButton danger label="Freeze account" confirmLabel="Freeze now" onConfirm={changeStatus(() => api.freeze(a.id))} />}
        {isAdmin && a.status === 'Frozen' && <ConfirmButton label="Unfreeze account" confirmLabel="Unfreeze now" onConfirm={changeStatus(() => api.unfreeze(a.id))} />}
      </div>
      {a.status === 'Frozen' && <p className="note note-warn">This account is frozen. It can receive money but nothing can leave it.</p>}
      {statusError != null && <ErrorNote error={statusError} />}

      <section aria-labelledby="statement-h" className="statement">
        <h2 id="statement-h">Statement</h2>
        {statement.error != null && <ErrorNote error={statement.error} />}
        {statement.loading && !statement.data && <Loading />}
        {statement.data && statement.data.length === 0 && <Empty title="No transactions yet.">Deposits, withdrawals and transfers will appear here.</Empty>}
        {rows.length > 0 && (
          <div className="table-wrap">
            <table className="ledger">
              <thead>
                <tr>
                  <th scope="col">Date</th><th scope="col">Particulars</th>
                  <th scope="col" className="amt rule-left">Debit</th>
                  <th scope="col" className="amt rule-left">Credit</th>
                  <th scope="col" className="amt rule-left">Balance</th>
                </tr>
              </thead>
              <tbody>
                {rows.map(({ row, after }) => (
                  <tr key={row.transactionId}>
                    <td>{formatDate(row.postingDate)}</td>
                    <td>{particulars(row)}{row.description && <span className="muted small block">{row.description}</span>}</td>
                    <td className="amt num rule-left">{row.direction === 'Debit' ? plainAmount(row.amount) : ''}</td>
                    <td className="amt num rule-left credit">{row.direction === 'Credit' ? plainAmount(row.amount) : ''}</td>
                    <td className="amt num rule-left">{plainAmount(after)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </>
  );
}
