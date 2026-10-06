import { useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { api } from '../api';
import { useAuth } from '../auth';
import { Empty, ErrorNote, Loading } from '../components/Bits';
import { formatDate } from '../format';
import { useAsync } from '../useAsync';

export function CustomersPage() {
  const { canPost } = useAuth();
  const navigate = useNavigate();
  const [q, setQ] = useState('');
  const [adding, setAdding] = useState(false);
  const { data, error, loading } = useAsync(() => api.listCustomers(q.trim() || undefined), [q]);

  return (
    <>
      <header className="page-head">
        <div>
          <h1>Customers</h1>
          <p className="muted">Find a customer to open accounts or review balances.</p>
        </div>
        {canPost && !adding && <button className="btn btn-primary" onClick={() => setAdding(true)}>New customer</button>}
      </header>

      {adding && <NewCustomerForm onDone={id => navigate(`/customers/${id}`)} onCancel={() => setAdding(false)} />}

      <div className="field narrow">
        <label className="label" htmlFor="customer-search">Search by name</label>
        <input id="customer-search" type="search" value={q} onChange={e => setQ(e.target.value)} placeholder="For example, Asha" />
      </div>

      {error != null && <ErrorNote error={error} />}
      {loading && !data && <Loading />}
      {data && data.length === 0 && (
        <Empty title={q ? 'No customers match that name.' : 'No customers yet.'}>
          {canPost ? 'Add the first customer, then open an account for them.' : 'A teller or admin needs to add customers.'}
        </Empty>
      )}
      {data && data.length > 0 && (
        <div className="table-wrap">
          <table className="ledger">
            <thead><tr><th scope="col">Name</th><th scope="col">Email</th><th scope="col">Customer since</th></tr></thead>
            <tbody>
              {data.map(c => (
                <tr key={c.id}>
                  <td><Link to={`/customers/${c.id}`}>{c.fullName}</Link></td>
                  <td>{c.email ?? <span className="muted">Not provided</span>}</td>
                  <td>{formatDate(c.createdAt)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </>
  );
}

function NewCustomerForm({ onDone, onCancel }: { onDone: (id: string) => void; onCancel: () => void }) {
  const [fullName, setFullName] = useState('');
  const [email, setEmail] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setBusy(true); setError(null);
    try { onDone((await api.createCustomer(fullName, email)).id); }
    catch (err) { setError(err); setBusy(false); }
  };

  return (
    <form className="panel form-row" onSubmit={submit}>
      <div className="field grow">
        <label className="label" htmlFor="full-name">Full name</label>
        <input id="full-name" value={fullName} onChange={e => setFullName(e.target.value)} required maxLength={200} autoFocus />
      </div>
      <div className="field grow">
        <label className="label" htmlFor="email">Email (optional)</label>
        <input id="email" type="email" value={email} onChange={e => setEmail(e.target.value)} />
      </div>
      <div className="actions">
        <button className="btn btn-primary" disabled={busy}>Add customer</button>
        <button type="button" className="btn btn-quiet" onClick={onCancel}>Cancel</button>
      </div>
      {error != null && <ErrorNote error={error} />}
    </form>
  );
}
