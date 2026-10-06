import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { api } from '../api';
import { useAuth } from '../auth';
import { Empty, ErrorNote, Loading, StatusBadge } from '../components/Bits';
import { formatDate, money } from '../format';
import { useAsync } from '../useAsync';

export function CustomerDetailPage() {
  const { id = '' } = useParams();
  const { canPost } = useAuth();
  const customer = useAsync(() => api.getCustomer(id), [id]);
  const accounts = useAsync(() => api.listAccounts({ customerId: id }), [id]);
  const [product, setProduct] = useState<'savings' | 'current'>('savings');
  const [opening, setOpening] = useState(false);
  const [openError, setOpenError] = useState<unknown>(null);

  const open = async () => {
    setOpening(true); setOpenError(null);
    try { await api.openAccount(id, product); accounts.reload(); }
    catch (err) { setOpenError(err); }
    finally { setOpening(false); }
  };

  if (customer.error != null) return <ErrorNote error={customer.error} />;
  if (!customer.data) return <Loading />;

  return (
    <>
      <p className="crumb"><Link to="/customers">Customers</Link></p>
      <header className="page-head">
        <div>
          <h1>{customer.data.fullName}</h1>
          <p className="muted">{customer.data.email ?? 'No email on file'}. Customer since {formatDate(customer.data.createdAt)}.</p>
        </div>
      </header>

      <section aria-labelledby="accounts-h">
        <div className="section-head">
          <h2 id="accounts-h">Accounts</h2>
          {canPost && (
            <div className="inline-form">
              <label className="label" htmlFor="product">Account type</label>
              <select id="product" value={product} onChange={e => setProduct(e.target.value as 'savings' | 'current')}>
                <option value="savings">Savings</option>
                <option value="current">Current</option>
              </select>
              <button className="btn btn-primary" onClick={open} disabled={opening}>Open account</button>
            </div>
          )}
        </div>
        {openError != null && <ErrorNote error={openError} />}
        {accounts.error != null && <ErrorNote error={accounts.error} />}
        {accounts.data && accounts.data.length === 0 && (
          <Empty title="No accounts yet.">{canPost ? 'Open a savings or current account above.' : 'This customer has not opened an account.'}</Empty>
        )}
        {accounts.data && accounts.data.length > 0 && (
          <div className="table-wrap">
            <table className="ledger">
              <thead><tr><th scope="col">Account number</th><th scope="col">Status</th><th scope="col" className="amt">Ledger balance</th><th scope="col" className="amt">Available</th></tr></thead>
              <tbody>
                {accounts.data.map(a => (
                  <tr key={a.id}>
                    <td><Link className="num" to={`/accounts/${a.id}`}>{a.accountNumber}</Link></td>
                    <td><StatusBadge status={a.status} /></td>
                    <td className="amt num">{money(a.ledgerBalance, a.currency)}</td>
                    <td className="amt num">{money(a.availableBalance, a.currency)}</td>
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
