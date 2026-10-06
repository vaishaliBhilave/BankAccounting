import { useState, type FormEvent } from 'react';
import { Navigate, useLocation, useNavigate } from 'react-router-dom';
import { useAuth } from '../auth';
import { ErrorNote } from '../components/Bits';

export function LoginPage() {
  const { session, login } = useAuth();
  const navigate = useNavigate();
  const from = (useLocation().state as { from?: string } | null)?.from ?? '/customers';
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);

  if (session) return <Navigate to={from} replace />;

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setBusy(true); setError(null);
    try { await login(username, password); navigate(from, { replace: true }); }
    catch (err) { setError(err); }
    finally { setBusy(false); }
  };

  return (
    <div className="login">
      <form className="login-card" onSubmit={submit}>
        <h1 className="brand-large">Ledger Desk</h1>
        <p className="muted">Sign in to look up customers and move money.</p>
        <div className="field">
          <label className="label" htmlFor="username">Username</label>
          <input id="username" autoComplete="username" value={username} onChange={e => setUsername(e.target.value)} required />
        </div>
        <div className="field">
          <label className="label" htmlFor="password">Password</label>
          <input id="password" type="password" autoComplete="current-password" value={password} onChange={e => setPassword(e.target.value)} required />
        </div>
        {error != null && <ErrorNote error={error} />}
        <button className="btn btn-primary btn-block" disabled={busy}>{busy ? 'Signing in…' : 'Sign in'}</button>
        <p className="muted small">Demo accounts: admin (full access), teller (posts transactions), auditor (read only).</p>
      </form>
    </div>
  );
}
