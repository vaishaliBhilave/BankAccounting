import type { Receipt } from '../api';
import { formatDate, plainAmount } from '../format';
import type { VoucherLine } from '../voucher';

interface Props { receipt: Receipt; lines: VoucherLine[]; requestId: string; description?: string }

/**
 * The double-entry slip for a posting. The stamp says whether this call posted new money or was an
 * idempotent replay of an earlier identical request (nothing moved the second time).
 */
export function Voucher({ receipt, lines, requestId, description }: Props) {
  const total = lines.filter(l => l.side === 'Dr').reduce((sum, l) => sum + l.amount, 0);
  return (
    <article className="voucher" aria-label="Posting voucher">
      <div key={String(receipt.replayed)} className={receipt.replayed ? 'stamp stamp-replayed' : 'stamp stamp-posted'} role="status">
        {receipt.replayed ? 'Replayed' : 'Posted'}
      </div>

      <h2 className="voucher-title">{receipt.type} voucher</h2>
      <p className="muted">Posting date {formatDate(receipt.postingDate)}</p>
      {description && <p className="muted">Narration: {description}</p>}

      <table className="ledger voucher-table">
        <thead>
          <tr><th scope="col">Particulars</th><th scope="col" className="amt rule-left">Dr</th><th scope="col" className="amt rule-left">Cr</th></tr>
        </thead>
        <tbody>
          {lines.map((l, i) => (
            <tr key={i}>
              <td className={l.side === 'Cr' ? 'indent' : undefined}>
                <span className="num">{l.title}</span>
                <span className="muted small block">{l.detail}</span>
              </td>
              <td className="amt num rule-left">{l.side === 'Dr' ? plainAmount(l.amount) : ''}</td>
              <td className="amt num rule-left">{l.side === 'Cr' ? plainAmount(l.amount) : ''}</td>
            </tr>
          ))}
        </tbody>
        <tfoot>
          <tr>
            <th scope="row">Total (debits equal credits)</th>
            <td className="amt num rule-left">{plainAmount(total)}</td>
            <td className="amt num rule-left">{plainAmount(total)}</td>
          </tr>
        </tfoot>
      </table>

      <dl className="voucher-meta">
        <div><dt>Request ID</dt><dd className="num">{requestId}</dd></div>
        <div><dt>Transaction</dt><dd className="num">{receipt.transactionId}</dd></div>
      </dl>
      {receipt.replayed && (
        <p className="note note-info">The same request ID arrived again, so the original result was returned and no money moved a second time.</p>
      )}
    </article>
  );
}
