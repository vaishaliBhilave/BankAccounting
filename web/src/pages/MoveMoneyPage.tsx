import { useEffect, useState, type FormEvent } from 'react';
import { useSearchParams } from 'react-router-dom';
import { api, BASE_CURRENCY, type AccountSummary, type Receipt } from '../api';
import { useAuth } from '../auth';
import { AccountPicker } from '../components/AccountPicker';
import { ErrorNote } from '../components/Bits';
import { Voucher } from '../components/Voucher';
import { newRequestId, parseAmount } from '../format';
import { kindLabel, parseKind, voucherLines, type Kind } from '../voucher';

interface SentRequest {
  kind: Kind;
  requestId: string;
  amount: number;
  description?: string;
  from: AccountSummary | null;
  to: AccountSummary | null;
}

function send(req: SentRequest): Promise<Receipt> {
  const common = { amount: req.amount, currency: BASE_CURRENCY, description: req.description };
  switch (req.kind) {
    case 'deposit': return api.deposit(req.requestId, { ...common, accountId: req.to!.id });
    case 'withdraw': return api.withdraw(req.requestId, { ...common, accountId: req.from!.id });
    case 'transfer': return api.transfer(req.requestId, { ...common, fromAccountId: req.from!.id, toAccountId: req.to!.id });
  }
}

export function MoveMoneyPage() {
  const { canPost } = useAuth();
  const [params] = useSearchParams();
  const [kind, setKind] = useState<Kind>(parseKind(params.get('kind')));
  const [from, setFrom] = useState<AccountSummary | null>(null);
  const [to, setTo] = useState<AccountSummary | null>(null);
  const [amountText, setAmountText] = useState('');
  const [description, setDescription] = useState('');
  // One request ID per attempt: retrying after a timeout reuses it, so the server can never post twice.
  const [requestId, setRequestId] = useState(newRequestId);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [fieldError, setFieldError] = useState<string | null>(null);
  const [done, setDone] = useState<{ request: SentRequest; receipt: Receipt } | null>(null);

  // Arriving from an account page: pre-fill that account on the right side of the movement.
  const presetId = params.get('account');
  useEffect(() => {
    if (!presetId) return;
    let live = true;
    (async () => {
      const account = await api.getAccount(presetId);
      const customer = await api.getCustomer(account.customerId);
      const summary: AccountSummary = {
        id: account.id, accountNumber: account.accountNumber, customerId: customer.id, customerName: customer.fullName,
        currency: account.currency, status: account.status, ledgerBalance: account.ledgerBalance, availableBalance: account.availableBalance
      };
      if (!live) return;
      if (parseKind(params.get('kind')) === 'deposit') setTo(summary); else setFrom(summary);
    })().catch(() => { /* the picker still works if the preset cannot load */ });
    return () => { live = false; };
  }, [presetId]); // eslint-disable-line react-hooks/exhaustive-deps

  const needsFrom = kind !== 'deposit';
  const needsTo = kind !== 'withdraw';

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setError(null); setFieldError(null);
    if (needsFrom && !from) return setFieldError('Choose the account the money comes from.');
    if (needsTo && !to) return setFieldError('Choose the account that receives the money.');
    const amount = parseAmount(amountText);
    if (!amount.ok) return setFieldError(amount.reason);

    const request: SentRequest = {
      kind, requestId, amount: amount.value, description: description.trim() || undefined,
      from: needsFrom ? from : null, to: needsTo ? to : null
    };
    setBusy(true);
    try { setDone({ request, receipt: await send(request) }); }
    catch (err) { setError(err); }
    finally { setBusy(false); }
  };

  /** Sends the identical request (same ID, same details) again to show the server's idempotent replay. */
  const sendAgain = async () => {
    if (!done) return;
    setBusy(true); setError(null);
    try { setDone({ request: done.request, receipt: await send(done.request) }); }
    catch (err) { setError(err); }
    finally { setBusy(false); }
  };

  const reset = () => {
    setDone(null); setError(null); setFieldError(null);
    setAmountText(''); setDescription(''); setFrom(null); setTo(null);
    setRequestId(newRequestId());
  };

  if (done) {
    const { request, receipt } = done;
    const lines = voucherLines(request.kind, request.amount,
      request.from ? { number: request.from.accountNumber, name: request.from.customerName } : undefined,
      request.to ? { number: request.to.accountNumber, name: request.to.customerName } : undefined);
    return (
      <>
        <header className="page-head"><div><h1>{kindLabel[request.kind]} complete</h1>
          <p className="muted">The ledger now holds this entry. Balances update on each account page.</p></div></header>
        <Voucher receipt={receipt} lines={lines} requestId={request.requestId} description={request.description} />
        {error != null && <ErrorNote error={error} />}
        <div className="actions">
          <button className="btn btn-primary" onClick={reset}>New request</button>
          <button className="btn" onClick={sendAgain} disabled={busy}>Send the same request again</button>
        </div>
        <p className="muted small narrow">Sending the same request again shows idempotency: the server recognises the request ID and returns the original result without moving money.</p>
      </>
    );
  }

  return (
    <>
      <header className="page-head"><div><h1>Move money</h1>
        <p className="muted">Every posting is a balanced journal entry. Overdrafts are never allowed.</p></div></header>

      {!canPost && <p className="note note-warn">Your role is read-only, so posting is turned off.</p>}

      <form className="panel stack" onSubmit={submit} noValidate>
        <div className="field">
          <span className="label" id="kind-label">What are you doing?</span>
          <div className="segmented" role="radiogroup" aria-labelledby="kind-label">
            {(['deposit', 'withdraw', 'transfer'] as Kind[]).map(k => (
              <button key={k} type="button" role="radio" aria-checked={kind === k} className={kind === k ? 'seg is-on' : 'seg'}
                onClick={() => { setKind(k); setFieldError(null); setError(null); }} disabled={!canPost}>
                {kindLabel[k]}
              </button>
            ))}
          </div>
        </div>

        {needsFrom && <AccountPicker id="from" label="From account" value={from} onChange={setFrom} excludeId={to?.id} showAvailable disabled={!canPost} />}
        {needsTo && <AccountPicker id="to" label="To account" value={to} onChange={setTo} excludeId={from?.id} disabled={!canPost} />}

        <div className="form-row">
          <div className="field">
            <label className="label" htmlFor="amount">Amount ({BASE_CURRENCY})</label>
            <input id="amount" inputMode="decimal" value={amountText} onChange={e => setAmountText(e.target.value)} placeholder="0.00" disabled={!canPost} />
          </div>
          <div className="field grow">
            <label className="label" htmlFor="narration">Narration (optional)</label>
            <input id="narration" value={description} onChange={e => setDescription(e.target.value)} maxLength={500} disabled={!canPost} />
          </div>
        </div>

        {fieldError && <p className="note note-error" role="alert">{fieldError}</p>}
        {error != null && <ErrorNote error={error} />}

        <div className="actions">
          <button className="btn btn-primary" disabled={busy || !canPost}>{busy ? 'Posting…' : `Post ${kindLabel[kind].toLowerCase()}`}</button>
          <span className="muted small">Request ID <span className="num">{requestId.slice(0, 8)}</span></span>
        </div>
      </form>
    </>
  );
}
