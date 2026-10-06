export type Role = 'Admin' | 'Teller' | 'Auditor';
export type AccountStatus = 'Active' | 'Frozen' | 'Closed';
export const BASE_CURRENCY = 'INR';

export interface Session { accessToken: string; expiresAt: string; role: Role; username: string }
export interface Customer { id: string; fullName: string; email: string | null; createdAt: string }
export interface Account {
  id: string; accountNumber: string; customerId: string; currency: string; status: AccountStatus;
  ledgerBalance: number; holdTotal: number; availableBalance: number;
}
export interface AccountSummary {
  id: string; accountNumber: string; customerId: string; customerName: string; currency: string;
  status: AccountStatus; ledgerBalance: number; availableBalance: number;
}
export interface StatementRow {
  transactionId: string; type: string; description: string | null; postingDate: string; createdAt: string;
  direction: 'Debit' | 'Credit'; amount: number; currency: string; counterpartyNumber: string | null;
}
export interface Receipt {
  transactionId: string; type: string; status: string; postingDate: string; amount: number; currency: string; replayed: boolean;
}

export class ApiError extends Error {
  constructor(public status: number, public code: string, message: string) { super(message); }
}

let token: string | null = null;
let onUnauthorized: (() => void) | null = null;
export function setApiToken(value: string | null) { token = value; }
export function setUnauthorizedHandler(handler: (() => void) | null) { onUnauthorized = handler; }

async function request<T>(method: string, path: string, body?: unknown, headers?: Record<string, string>): Promise<T> {
  let res: Response;
  try {
    res = await fetch(`/api/v1${path}`, {
      method,
      headers: {
        ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}),
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
        ...headers
      },
      body: body !== undefined ? JSON.stringify(body) : undefined
    });
  } catch {
    throw new ApiError(0, 'NETWORK', 'Could not reach the server. Check your connection and try again.');
  }

  if (res.status === 401 && token) onUnauthorized?.();
  if (res.status === 429) throw new ApiError(429, 'RATE_LIMITED', 'Too many requests. Wait a minute and try again.');
  if (!res.ok) {
    let code = 'REQUEST_FAILED';
    let message = `The request failed (${res.status}).`;
    try {
      const problem = await res.json();
      code = problem.code ?? problem.title ?? code;
      message = problem.detail ?? message;
    } catch { /* body was not JSON */ }
    throw new ApiError(res.status, code, message);
  }
  return (res.status === 204 ? undefined : await res.json()) as T;
}

const qs = (params: Record<string, string | undefined>) => {
  const search = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) if (v) search.set(k, v);
  const s = search.toString();
  return s ? `?${s}` : '';
};

export interface MoneyBody { amount: number; currency: string; description?: string }

export const api = {
  login: (username: string, password: string) => request<Session & { role: Role }>('POST', '/auth/login', { username, password }),

  listCustomers: (q?: string) => request<Customer[]>('GET', `/customers${qs({ q })}`),
  getCustomer: (id: string) => request<Customer>('GET', `/customers/${id}`),
  createCustomer: (fullName: string, email: string) => request<Customer>('POST', '/customers', { fullName, email: email || null }),

  listAccounts: (opts: { q?: string; customerId?: string } = {}) => request<AccountSummary[]>('GET', `/accounts${qs(opts)}`),
  getAccount: (id: string) => request<Account>('GET', `/accounts/${id}`),
  openAccount: (customerId: string, product: 'savings' | 'current') => request<Account>('POST', '/accounts', { customerId, product }),
  freeze: (id: string) => request<Account>('POST', `/accounts/${id}/freeze`),
  unfreeze: (id: string) => request<Account>('POST', `/accounts/${id}/unfreeze`),
  statement: (id: string) => request<StatementRow[]>('GET', `/accounts/${id}/transactions?limit=50`),

  // Every posting carries an Idempotency-Key so a retry can never post twice.
  deposit: (key: string, body: MoneyBody & { accountId: string }) =>
    request<Receipt>('POST', '/transactions/deposits', body, { 'Idempotency-Key': key }),
  withdraw: (key: string, body: MoneyBody & { accountId: string }) =>
    request<Receipt>('POST', '/transactions/withdrawals', body, { 'Idempotency-Key': key }),
  transfer: (key: string, body: MoneyBody & { fromAccountId: string; toAccountId: string }) =>
    request<Receipt>('POST', '/transactions/transfers', body, { 'Idempotency-Key': key })
};
