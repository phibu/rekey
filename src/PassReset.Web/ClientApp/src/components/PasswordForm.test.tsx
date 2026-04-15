import { describe, it, expect, afterEach, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { PasswordForm } from './PasswordForm';
import { ApiErrorCode } from '../types/settings';
import type { ClientSettings } from '../types/settings';
import { mockFetchOnce } from '../test-utils/fetchMock';

function baseSettings(overrides: Partial<ClientSettings> = {}): ClientSettings {
  return {
    usePasswordGeneration: false,
    minimumDistance: 0,
    passwordEntropy: 12,
    showPasswordMeter: false,
    minimumScore: 0,
    useEmail: false,
    ...overrides,
  };
}

function renderForm(overrides: Partial<ClientSettings> = {}) {
  const onSuccess = vi.fn();
  const settings = baseSettings(overrides);
  const utils = render(<PasswordForm settings={settings} onSuccess={onSuccess} />);
  return { ...utils, onSuccess, settings };
}

async function fillForm(
  user: ReturnType<typeof userEvent.setup>,
  values: { username?: string; current?: string; next?: string; verify?: string },
) {
  if (values.username !== undefined) {
    await user.type(screen.getByLabelText(/username/i), values.username);
  }
  if (values.current !== undefined) {
    await user.type(screen.getByLabelText(/current password/i), values.current);
  }
  if (values.next !== undefined) {
    await user.type(screen.getByLabelText(/^new password/i), values.next);
  }
  if (values.verify !== undefined) {
    await user.type(screen.getByLabelText(/re-enter new password/i), values.verify);
  }
}

describe('PasswordForm', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('renders all four fields and submit button', () => {
    renderForm();
    expect(screen.getByLabelText(/username/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/current password/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/^new password/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/re-enter new password/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /change password/i })).toBeInTheDocument();
  });

  it('shows field-required errors on empty submit and does not call fetch', async () => {
    const fetchFn = vi.fn();
    vi.stubGlobal('fetch', fetchFn);
    const user = userEvent.setup();
    renderForm();

    await user.click(screen.getByRole('button', { name: /change password/i }));

    expect(await screen.findAllByText(/this field is required/i)).toHaveLength(4);
    expect(fetchFn).not.toHaveBeenCalled();
  });

  it('shows mismatch error when new password and confirm differ', async () => {
    const fetchFn = vi.fn();
    vi.stubGlobal('fetch', fetchFn);
    const user = userEvent.setup();
    renderForm();

    await fillForm(user, {
      username: 'jdoe',
      current: 'OldPass1!',
      next: 'NewPass1!',
      verify: 'Different2@',
    });
    await user.click(screen.getByRole('button', { name: /change password/i }));

    expect(await screen.findByText(/passwords do not match/i)).toBeInTheDocument();
    expect(fetchFn).not.toHaveBeenCalled();
  });

  it('calls onSuccess when server returns empty errors array', async () => {
    mockFetchOnce({ errors: [] });
    const user = userEvent.setup();
    const { onSuccess } = renderForm();

    await fillForm(user, {
      username: 'jdoe',
      current: 'OldPass1!',
      next: 'NewPass1!',
      verify: 'NewPass1!',
    });
    await user.click(screen.getByRole('button', { name: /change password/i }));

    await waitFor(() => expect(onSuccess).toHaveBeenCalledTimes(1));
  });

  it('surfaces InvalidCredentials error on CurrentPassword field', async () => {
    mockFetchOnce({
      errors: [{ errorCode: ApiErrorCode.InvalidCredentials, fieldName: 'CurrentPassword' }],
    });
    const user = userEvent.setup();
    const { onSuccess } = renderForm();

    await fillForm(user, {
      username: 'jdoe',
      current: 'wrong',
      next: 'NewPass1!',
      verify: 'NewPass1!',
    });
    await user.click(screen.getByRole('button', { name: /change password/i }));

    expect(await screen.findByText(/current password is incorrect/i)).toBeInTheDocument();
    expect(onSuccess).not.toHaveBeenCalled();
  });

  it('surfaces a general error alert when server returns unfielded error', async () => {
    mockFetchOnce({
      errors: [{ errorCode: ApiErrorCode.LdapProblem }],
    });
    const user = userEvent.setup();
    const { onSuccess } = renderForm();

    await fillForm(user, {
      username: 'jdoe',
      current: 'OldPass1!',
      next: 'NewPass1!',
      verify: 'NewPass1!',
    });
    await user.click(screen.getByRole('button', { name: /change password/i }));

    expect(await screen.findByText(/directory connection error/i)).toBeInTheDocument();
    expect(onSuccess).not.toHaveBeenCalled();
  });

  it('shows approaching-lockout warning banner when server signals ApproachingLockout', async () => {
    mockFetchOnce({
      errors: [
        { errorCode: ApiErrorCode.ApproachingLockout, fieldName: 'CurrentPassword' },
      ],
    });
    const user = userEvent.setup();
    renderForm();

    await fillForm(user, {
      username: 'jdoe',
      current: 'wrong',
      next: 'NewPass1!',
      verify: 'NewPass1!',
    });
    await user.click(screen.getByRole('button', { name: /change password/i }));

    expect(await screen.findAllByText(/one more failed attempt/i)).not.toHaveLength(0);
  });

  it('shows PasswordTooRecentlyChanged message on BUG-002 error code', async () => {
    mockFetchOnce({
      errors: [{ errorCode: ApiErrorCode.PasswordTooRecentlyChanged }],
    });
    const user = userEvent.setup();
    renderForm();

    await fillForm(user, {
      username: 'jdoe',
      current: 'OldPass1!',
      next: 'NewPass1!',
      verify: 'NewPass1!',
    });
    await user.click(screen.getByRole('button', { name: /change password/i }));

    expect(await screen.findByText(/changed too recently/i)).toBeInTheDocument();
  });

  it('fills both new-password fields when generate button clicked', async () => {
    const user = userEvent.setup();
    renderForm({ usePasswordGeneration: true, passwordEntropy: 16 });

    await user.click(screen.getByRole('button', { name: /generate password/i }));

    const newPassword = screen.getByLabelText(/^new password/i) as HTMLInputElement;
    const verify = screen.getByLabelText(/re-enter new password/i) as HTMLInputElement;
    expect(newPassword.value.length).toBeGreaterThanOrEqual(12);
    expect(newPassword.value).toBe(verify.value);
  });

  it('enforces minimumDistance when configured', async () => {
    const fetchFn = vi.fn();
    vi.stubGlobal('fetch', fetchFn);
    const user = userEvent.setup();
    renderForm({ minimumDistance: 5 });

    await fillForm(user, {
      username: 'jdoe',
      current: 'Passw0rd!',
      next: 'Passw0rd@', // only 1 char different
      verify: 'Passw0rd@',
    });
    await user.click(screen.getByRole('button', { name: /change password/i }));

    expect(await screen.findByText(/too similar/i)).toBeInTheDocument();
    expect(fetchFn).not.toHaveBeenCalled();
  });
});
