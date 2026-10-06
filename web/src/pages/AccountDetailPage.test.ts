import { describe, expect, it } from 'vitest';
import type { StatementRow } from '../api';
import { withRunningBalance } from './AccountDetailPage';

const row = (direction: 'Debit' | 'Credit', amount: number, id: string): StatementRow => ({
  transactionId: id, type: 'Transfer', description: null, postingDate: '2026-10-01', createdAt: '2026-10-01T09:00:00Z',
  direction, amount, currency: 'INR', counterpartyNumber: 'BA1000000002'
});

describe('withRunningBalance', () => {
  it('walks back from the current ledger balance, newest row first', () => {
    // History: +1000 (credit), -250 (debit), +50 (credit) => balance 800. Newest first:
    const result = withRunningBalance([row('Credit', 50, 'c'), row('Debit', 250, 'b'), row('Credit', 1000, 'a')], 800);
    expect(result.map(r => r.after)).toEqual([800, 750, 1000]);
  });
});
