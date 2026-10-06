import { useState, type ReactNode } from 'react';
import type { AccountStatus } from '../api';
import { errorMessage } from '../format';

export function StatusBadge({ status }: { status: AccountStatus }) {
  return <span className={`badge badge-${status.toLowerCase()}`}>{status}</span>;
}

export function ErrorNote({ error }: { error: unknown }) {
  return <p className="note note-error" role="alert">{errorMessage(error)}</p>;
}

export function Loading({ label = 'Loading' }: { label?: string }) {
  return <p className="muted" role="status">{label}…</p>;
}

export function Empty({ title, children }: { title: string; children?: ReactNode }) {
  return (
    <div className="empty">
      <p className="empty-title">{title}</p>
      {children && <p className="muted">{children}</p>}
    </div>
  );
}

/** Two-step confirmation for state changes (freeze/unfreeze) without a modal. */
export function ConfirmButton({ label, confirmLabel, onConfirm, danger = false }:
  { label: string; confirmLabel: string; onConfirm: () => Promise<void> | void; danger?: boolean }) {
  const [asking, setAsking] = useState(false);
  const [busy, setBusy] = useState(false);

  if (!asking) return <button type="button" className={danger ? 'btn btn-danger' : 'btn'} onClick={() => setAsking(true)}>{label}</button>;
  return (
    <span className="confirm-row">
      <button type="button" className={danger ? 'btn btn-danger-solid' : 'btn btn-primary'} disabled={busy}
        onClick={async () => { setBusy(true); try { await onConfirm(); } finally { setBusy(false); setAsking(false); } }}>
        {confirmLabel}
      </button>
      <button type="button" className="btn btn-quiet" onClick={() => setAsking(false)}>Cancel</button>
    </span>
  );
}
