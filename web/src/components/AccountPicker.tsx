import { useEffect, useState } from 'react';
import { api, type AccountSummary } from '../api';
import { money } from '../format';
import { StatusBadge } from './Bits';

interface Props {
  id: string;
  label: string;
  value: AccountSummary | null;
  onChange: (account: AccountSummary | null) => void;
  excludeId?: string;
  showAvailable?: boolean;
  disabled?: boolean;
}

/** Typeahead over GET /accounts?q= (matches account number or holder name). */
export function AccountPicker({ id, label, value, onChange, excludeId, showAvailable, disabled }: Props) {
  const [text, setText] = useState('');
  const [open, setOpen] = useState(false);
  const [options, setOptions] = useState<AccountSummary[]>([]);
  const [active, setActive] = useState(0);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    if (!open) return;
    let live = true;
    setLoading(true);
    const timer = setTimeout(() => {
      api.listAccounts({ q: text })
        .then(rows => { if (live) { setOptions(rows.filter(a => a.id !== excludeId)); setActive(0); } })
        .catch(() => { if (live) setOptions([]); })
        .finally(() => { if (live) setLoading(false); });
    }, 200);
    return () => { live = false; clearTimeout(timer); };
  }, [text, open, excludeId]);

  const choose = (account: AccountSummary) => { onChange(account); setOpen(false); setText(''); };

  if (value) {
    return (
      <div className="field">
        <span className="label" id={`${id}-label`}>{label}</span>
        <div className="picked" aria-labelledby={`${id}-label`}>
          <div>
            <strong className="num">{value.accountNumber}</strong>
            <span className="muted"> {value.customerName}</span>
            {showAvailable && <div className="muted small">Available {money(value.availableBalance, value.currency)}</div>}
          </div>
          {!disabled && <button type="button" className="btn btn-quiet" onClick={() => onChange(null)}>Change</button>}
        </div>
      </div>
    );
  }

  const listId = `${id}-list`;
  return (
    <div className="field combo">
      <label className="label" htmlFor={id}>{label}</label>
      <input
        id={id} type="text" role="combobox" autoComplete="off" disabled={disabled}
        aria-expanded={open} aria-controls={listId} aria-autocomplete="list"
        aria-activedescendant={open && options[active] ? `${id}-opt-${active}` : undefined}
        placeholder="Account number or holder name"
        value={text}
        onChange={e => { setText(e.target.value); setOpen(true); }}
        onFocus={() => setOpen(true)}
        onBlur={() => setOpen(false)}
        onKeyDown={e => {
          if (e.key === 'ArrowDown') { e.preventDefault(); setOpen(true); setActive(i => Math.min(i + 1, options.length - 1)); }
          else if (e.key === 'ArrowUp') { e.preventDefault(); setActive(i => Math.max(i - 1, 0)); }
          else if (e.key === 'Enter' && open && options[active]) { e.preventDefault(); choose(options[active]); }
          else if (e.key === 'Escape') setOpen(false);
        }}
      />
      {open && (
        <ul className="options" id={listId} role="listbox" aria-label={`${label} suggestions`}>
          {loading && options.length === 0 && <li className="option-note muted">Searching…</li>}
          {!loading && options.length === 0 && <li className="option-note muted">No accounts match.</li>}
          {options.map((a, i) => (
            <li key={a.id} id={`${id}-opt-${i}`} role="option" aria-selected={i === active}
              className={i === active ? 'option is-active' : 'option'}
              onMouseDown={e => { e.preventDefault(); choose(a); }}>
              <span className="num">{a.accountNumber}</span>
              <span className="muted"> {a.customerName}</span>
              <span className="option-side">
                {showAvailable && <span className="num small">{money(a.availableBalance, a.currency)}</span>}
                <StatusBadge status={a.status} />
              </span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
