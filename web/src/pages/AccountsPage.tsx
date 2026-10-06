import { useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api';
import { Empty, ErrorNote, Loading, StatusBadge } from '../components/Bits';
import { money } from '../format';
import { useAsync } from '../useAsync';

export function AccountsPage() {
  const [q, setQ] = useState('');
  const { data, error, loading } = useAsync(() => api.listAccounts({ q: q.trim() || undefined }), [q]);

  return (
    <>
      <header className="page-head">
        <div>
          <h1>Accounts</h1>
          <p className="muted">The 50 most recently opened accounts. Search to narrow the list.</p>
        </div>
      </header>

      <div className="field narrow">
        <label className="label" htmlFor="account-search">Search by account number or holder</label>
        <input id="account-search" type="search" value={q} onChange={e => setQ(e.target.value)} placeholder="BA1000000001" />
      </div>

      {error != null && <ErrorNote error={error} />}
      {loading && !data && <Loading />}
      {data && data.length === 0 && <Empty title="No accounts match.">Open an account from a customer's page.</Empty>}
      {data && data.length > 0 && (
        <div className="table-wrap">
          <table className="ledger">
            <thead><tr><th scope="col">Account number</th><th scope="col">Holder</th><th scope="col">Status</th><th scope="col" className="amt">Available</th></tr></thead>
            <tbody>
              {data.map(a => (
                <tr key={a.id}>
                  <td><Link className="num" to={`/accounts/${a.id}`}>{a.accountNumber}</Link></td>
                  <td><Link to={`/customers/${a.customerId}`}>{a.customerName}</Link></td>
                  <td><StatusBadge status={a.status} /></td>
                  <td className="amt num">{money(a.availableBalance, a.currency)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </>
  );
}
