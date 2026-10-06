import { NavLink, Navigate, Outlet, useLocation } from 'react-router-dom';
import { useAuth } from '../auth';

export function RequireAuth() {
  const { session } = useAuth();
  const location = useLocation();
  if (!session) return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />;
  return <Layout />;
}

function Layout() {
  const { session, logout } = useAuth();
  return (
    <div className="shell">
      <aside className="rail">
        <div className="brand">Ledger Desk</div>
        <nav aria-label="Main">
          <NavLink to="/customers">Customers</NavLink>
          <NavLink to="/accounts">Accounts</NavLink>
          <NavLink to="/move">Move money</NavLink>
        </nav>
        <div className="rail-user">
          <div><strong>{session?.username}</strong></div>
          <div className="rail-role">{session?.role}</div>
          <button type="button" className="rail-signout" onClick={logout}>Sign out</button>
        </div>
      </aside>
      <main className="main" id="main"><Outlet /></main>
    </div>
  );
}
