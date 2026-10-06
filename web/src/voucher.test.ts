import { describe, expect, it } from 'vitest';
import { parseKind, voucherLines } from './voucher';

const a = { number: 'BA1000000001', name: 'Asha Verma' };
const b = { number: 'BA1000000002', name: 'Ravi Shah' };

describe('voucherLines', () => {
  it('deposit debits cash and credits the customer', () => {
    const [dr, cr] = voucherLines('deposit', 500, undefined, a);
    expect(dr).toMatchObject({ side: 'Dr', title: 'Cash and settlement', amount: 500 });
    expect(cr).toMatchObject({ side: 'Cr', title: a.number });
  });
  it('withdrawal debits the customer and credits cash', () => {
    const [dr, cr] = voucherLines('withdraw', 75, a);
    expect(dr).toMatchObject({ side: 'Dr', title: a.number });
    expect(cr).toMatchObject({ side: 'Cr', title: 'Cash and settlement' });
  });
  it('transfer debits the source and credits the destination, and always balances', () => {
    const lines = voucherLines('transfer', 120.5, a, b);
    expect(lines.map(l => l.side)).toEqual(['Dr', 'Cr']);
    expect(lines[0].amount).toBe(lines[1].amount);
  });
});

describe('parseKind', () => {
  it('defaults to deposit for unknown values', () => {
    expect(parseKind('transfer')).toBe('transfer');
    expect(parseKind('withdraw')).toBe('withdraw');
    expect(parseKind('bogus')).toBe('deposit');
    expect(parseKind(null)).toBe('deposit');
  });
});
