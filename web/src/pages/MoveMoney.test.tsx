import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { AccountSummary, Receipt } from '../api';
import { AuthProvider } from '../auth';
import { MoveMoneyPage } from './MoveMoneyPage';

vi.mock('../api', async importOriginal => {
  const actual = await importOriginal<typeof import('../api')>();
  return { ...actual, api: { ...actual.api, listAccounts: vi.fn(), transfer: vi.fn(), deposit: vi.fn() } };
});
import { api } from '../api';

const acct = (n: number, name: string, available: number): AccountSummary => ({
  id: `00000000-0000-0000-0000-00000000000${n}`, accountNumber: `BA100000000${n}`, customerId: 'c', customerName: name,
  currency: 'INR', status: 'Active', ledgerBalance: available, availableBalance: available
});
const asha = acct(1, 'Asha Verma', 5000);
const ravi = acct(2, 'Ravi Shah', 0);

const receipt = (replayed: boolean): Receipt => ({
  transactionId: 'tx-1', type: 'Transfer', status: 'Posted', postingDate: '2026-10-01', amount: 1250.5, currency: 'INR', replayed
});

function renderPage(role: 'Teller' | 'Auditor' = 'Teller', url = '/move?kind=transfer') {
  sessionStorage.setItem('ledger-desk.session', JSON.stringify({
    accessToken: 't', expiresAt: new Date(Date.now() + 3_600_000).toISOString(), role, username: role.toLowerCase()
  }));
  return render(<MemoryRouter initialEntries={[url]}><AuthProvider><MoveMoneyPage /></AuthProvider></MemoryRouter>);
}

describe('Move money', () => {
  beforeEach(() => {
    sessionStorage.clear();
    vi.mocked(api.listAccounts).mockResolvedValue([asha, ravi]);
    vi.mocked(api.transfer).mockReset();
  });

  it('posts a transfer, shows the balanced voucher, and replays with the same request ID', async () => {
    const user = userEvent.setup();
    vi.mocked(api.transfer).mockResolvedValueOnce(receipt(false)).mockResolvedValueOnce(receipt(true));
    renderPage();

    await user.click(screen.getByLabelText('From account'));
    await user.click(await screen.findByRole('option', { name: /BA1000000001/ }));
    await user.click(screen.getByLabelText('To account'));
    await user.click(await screen.findByRole('option', { name: /BA1000000002/ }));
    await user.type(screen.getByLabelText(/Amount/), '1,250.50');
    await user.click(screen.getByRole('button', { name: 'Post transfer' }));

    expect(await screen.findByText('Posted')).toBeInTheDocument();
    expect(screen.getByText('Total (debits equal credits)')).toBeInTheDocument();
    const [firstKey, firstBody] = vi.mocked(api.transfer).mock.calls[0];
    expect(firstBody).toMatchObject({ fromAccountId: asha.id, toAccountId: ravi.id, amount: 1250.5, currency: 'INR' });

    await user.click(screen.getByRole('button', { name: 'Send the same request again' }));
    expect(await screen.findByText('Replayed')).toBeInTheDocument();
    expect(vi.mocked(api.transfer).mock.calls[1][0]).toBe(firstKey);   // same Idempotency-Key
  });

  it('shows a friendly message and keeps the form when the server refuses', async () => {
    const user = userEvent.setup();
    const { ApiError } = await import('../api');
    vi.mocked(api.transfer).mockRejectedValueOnce(new ApiError(422, 'INSUFFICIENT_FUNDS', 'raw'));
    renderPage();

    await user.click(screen.getByLabelText('From account'));
    await user.click(await screen.findByRole('option', { name: /BA1000000001/ }));
    await user.click(screen.getByLabelText('To account'));
    await user.click(await screen.findByRole('option', { name: /BA1000000002/ }));
    await user.type(screen.getByLabelText(/Amount/), '99999');
    await user.click(screen.getByRole('button', { name: 'Post transfer' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/available balance is lower/i);
    expect(screen.getByLabelText(/Amount/)).toHaveValue('99999');
  });

  it('validates before calling the server', async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(screen.getByRole('button', { name: 'Post transfer' }));
    expect(await screen.findByRole('alert')).toHaveTextContent(/choose the account the money comes from/i);
    expect(api.transfer).not.toHaveBeenCalled();
  });

  it('is read-only for auditors', async () => {
    renderPage('Auditor');
    expect(screen.getByText(/read-only/i)).toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Post transfer' })).toBeDisabled());
  });
});
