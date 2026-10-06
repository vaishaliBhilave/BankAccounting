import { ApiError } from './api';

export const money = (amount: number, currency = 'INR') =>
  new Intl.NumberFormat('en-IN', { style: 'currency', currency }).format(amount);

export const plainAmount = (amount: number) =>
  new Intl.NumberFormat('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(amount);

export const formatDate = (iso: string) =>
  new Intl.DateTimeFormat('en-IN', { day: '2-digit', month: 'short', year: 'numeric' }).format(new Date(iso));

export const formatDateTime = (iso: string) =>
  new Intl.DateTimeFormat('en-IN', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' }).format(new Date(iso));

/** Accepts "1250", "1,250.5", "1250.50". Rejects zero, negatives and more than 2 decimals (the API rejects them too). */
export function parseAmount(input: string): { ok: true; value: number } | { ok: false; reason: string } {
  const cleaned = input.trim().replace(/,/g, '');
  if (!cleaned) return { ok: false, reason: 'Enter an amount.' };
  if (!/^\d{1,13}(\.\d{1,2})?$/.test(cleaned)) return { ok: false, reason: 'Use a positive amount with at most 2 decimal places.' };
  const value = Number(cleaned);
  if (value <= 0) return { ok: false, reason: 'The amount must be greater than zero.' };
  return { ok: true, value };
}

const FRIENDLY: Record<string, string> = {
  INSUFFICIENT_FUNDS: 'The available balance is lower than this amount. Nothing was posted.',
  ACCOUNT_FROZEN: 'The source account is frozen, so money cannot leave it. It can still receive funds.',
  ACCOUNT_CLOSED: 'One of the accounts is closed.',
  ACCOUNT_NOT_FOUND: 'One of the accounts could not be found.',
  PERIOD_CLOSED: 'No accounting period is open for today. Ask an administrator to open one.',
  IDEMPOTENCY_KEY_REUSED: 'This request ID was already used with different details. Start a new request.',
  INVALID_CREDENTIALS: 'The username or password is not correct.',
  CURRENCY_NOT_SUPPORTED: 'Only INR is supported right now.'
};

export function errorMessage(error: unknown): string {
  if (error instanceof ApiError) return FRIENDLY[error.code] ?? error.message;
  return error instanceof Error ? error.message : 'Something went wrong. Try again.';
}

export const newRequestId = () => crypto.randomUUID();
