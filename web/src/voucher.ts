export type Kind = 'deposit' | 'withdraw' | 'transfer';
export interface Party { number: string; name: string }
export interface VoucherLine { side: 'Dr' | 'Cr'; title: string; detail: string; amount: number }

const CASH = { title: 'Cash and settlement', detail: 'General ledger 1000' };
const party = (p: Party) => ({ title: p.number, detail: p.name });

/**
 * The journal entry the API posts for each kind of movement (customer accounts are liabilities of the bank,
 * so paying money out debits them and receiving money credits them).
 */
export function voucherLines(kind: Kind, amount: number, from?: Party, to?: Party): VoucherLine[] {
  switch (kind) {
    case 'deposit':
      return [{ side: 'Dr', ...CASH, amount }, { side: 'Cr', ...party(to!), amount }];
    case 'withdraw':
      return [{ side: 'Dr', ...party(from!), amount }, { side: 'Cr', ...CASH, amount }];
    case 'transfer':
      return [{ side: 'Dr', ...party(from!), amount }, { side: 'Cr', ...party(to!), amount }];
  }
}

export const kindLabel: Record<Kind, string> = { deposit: 'Deposit', withdraw: 'Withdrawal', transfer: 'Transfer' };
export const parseKind = (value: string | null): Kind =>
  value === 'withdraw' || value === 'transfer' ? value : 'deposit';
