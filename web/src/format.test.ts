import { describe, expect, it } from 'vitest';
import { ApiError } from './api';
import { errorMessage, money, parseAmount } from './format';

describe('parseAmount', () => {
  it('accepts plain and grouped amounts', () => {
    expect(parseAmount('1250')).toEqual({ ok: true, value: 1250 });
    expect(parseAmount('1,250.5')).toEqual({ ok: true, value: 1250.5 });
    expect(parseAmount(' 0.01 ')).toEqual({ ok: true, value: 0.01 });
  });
  it('rejects empty, zero, negative and over-precise input', () => {
    for (const bad of ['', '0', '0.00', '-5', '12.345', 'abc', '1e3']) {
      expect(parseAmount(bad).ok).toBe(false);
    }
  });
});

describe('money', () => {
  it('formats rupees with Indian digit grouping', () => {
    expect(money(1234567.5)).toContain('12,34,567.50');
  });
});

describe('errorMessage', () => {
  it('translates known business error codes and falls back to the server message', () => {
    expect(errorMessage(new ApiError(422, 'INSUFFICIENT_FUNDS', 'raw'))).toMatch(/available balance/i);
    expect(errorMessage(new ApiError(400, 'SOMETHING_NEW', 'Server says no.'))).toBe('Server says no.');
  });
});
