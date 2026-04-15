import { useState, useMemo, useRef, useEffect } from 'react';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
import IconButton from '@mui/material/IconButton';
import InputAdornment from '@mui/material/InputAdornment';
import TextField from '@mui/material/TextField';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import AutorenewIcon from '@mui/icons-material/Autorenew';
import Visibility from '@mui/icons-material/Visibility';
import VisibilityOff from '@mui/icons-material/VisibilityOff';
import { changePassword } from '../api/client';
import { usePolicy } from '../hooks/usePolicy';
import { useRecaptcha } from '../hooks/useRecaptcha';
import type { ClientSettings, ApiErrorItem } from '../types/settings';
import { ApiErrorCode } from '../types/settings';
import { levenshtein } from '../utils/levenshtein';
import { generatePassword } from '../utils/passwordGenerator';
import { scheduleClipboardClear, type ClipboardClearHandle } from '../utils/clipboardClear';
import AdPasswordPolicyPanel from './AdPasswordPolicyPanel';
import ClipboardCountdown from './ClipboardCountdown';
import HibpIndicator from './HibpIndicator';
import { useHibpCheck } from '../hooks/useHibpCheck';
import { PasswordStrengthMeter } from './PasswordStrengthMeter';

interface Props {
  settings: ClientSettings;
  onSuccess: () => void;
}

interface FormErrors {
  username?: string;
  currentPassword?: string;
  newPassword?: string;
  newPasswordVerify?: string;
  general?: string;
}

function errorMessage(code: number, alerts: ClientSettings['alerts']): string {
  const a = alerts ?? {};
  switch (code) {
    case ApiErrorCode.FieldRequired:       return a.errorFieldRequired        ?? 'This field is required.';
    case ApiErrorCode.FieldMismatch:       return a.errorFieldMismatch        ?? 'Passwords do not match.';
    case ApiErrorCode.UserNotFound:        return a.errorInvalidUser          ?? 'User account not found.';
    case ApiErrorCode.InvalidCredentials:  return a.errorInvalidCredentials   ?? 'Current password is incorrect.';
    case ApiErrorCode.InvalidCaptcha:      return a.errorCaptcha              ?? 'Could not verify you are not a robot.';
    case ApiErrorCode.ChangeNotPermitted:  return a.errorPasswordChangeNotAllowed ?? 'Password change not allowed.';
    case ApiErrorCode.InvalidDomain:       return a.errorInvalidDomain        ?? 'Invalid domain.';
    case ApiErrorCode.LdapProblem:         return a.errorConnectionLdap       ?? 'Directory connection error.';
    case ApiErrorCode.ComplexPassword:     return a.errorComplexPassword      ?? 'Password does not meet complexity requirements.';
    case ApiErrorCode.MinimumScore:        return a.errorScorePassword        ?? 'Password is not strong enough.';
    case ApiErrorCode.MinimumDistance:     return a.errorDistancePassword     ?? 'New password is too similar to the current password.';
    case ApiErrorCode.PwnedPassword:       return a.errorPwnedPassword        ?? 'This password is publicly known. Please choose another.';
    case ApiErrorCode.PasswordTooYoung:    return a.errorPasswordTooYoung     ?? 'Password was changed too recently.';
    case ApiErrorCode.AccountDisabled:     return 'Your account is disabled. Contact IT Support.';
    case ApiErrorCode.RateLimitExceeded:        return a.errorRateLimitExceeded         ?? 'Too many attempts. Please wait and try again.';
    case ApiErrorCode.PwnedPasswordCheckFailed: return a.errorPwnedPasswordCheckFailed ?? 'Could not verify password safety. Please try again.';
    case ApiErrorCode.PortalLockout:            return a.errorPortalLockout            ?? 'Too many failed attempts. Please wait before trying again.';
    // ApproachingLockout uses the configured warning string as both the general error and warning banner.
    case ApiErrorCode.ApproachingLockout:       return a.errorApproachingLockout       ?? 'Incorrect password. One more failed attempt will temporarily lock your portal access.';
    case ApiErrorCode.PasswordTooRecentlyChanged: return a.errorPasswordTooRecentlyChanged ?? 'Your password was changed too recently. Please wait before trying again.';
    default:                                    return 'An unexpected error occurred. Please contact IT Support.';
  }
}

export function PasswordForm({ settings, onSuccess }: Props) {
  const form    = settings.changePasswordForm ?? {};
  const errors_ = settings.errorsPasswordForm ?? {};
  const regex   = settings.validationRegex    ?? {};

  // Build validation regexes once at mount. Try/catch guards against invalid patterns
  // in config — a bad pattern silently disables that check rather than breaking the form.
  const emailRx    = useMemo(() => { try { return regex.emailRegex    ? new RegExp(regex.emailRegex)    : null; } catch { return null; } }, [regex.emailRegex]);
  const usernameRx = useMemo(() => { try { return regex.usernameRegex ? new RegExp(regex.usernameRegex) : null; } catch { return null; } }, [regex.usernameRegex]);

  const attrs = settings.allowedUsernameAttributes;
  const usernameHint = useMemo(() => {
    if (!attrs || attrs.length === 0) return null;
    const parts = attrs.map(attr => {
      if (attr === 'samaccountname')    return 'username (e.g. jdoe)';
      if (attr === 'userprincipalname') return 'user principal name (e.g. jdoe@corp.com)';
      if (attr === 'mail')              return 'email address';
      return attr;
    });
    return 'Enter your ' + parts.join(' or ');
  }, [attrs]);

  const [username, setUsername]                 = useState('');
  const [currentPassword, setCurrentPassword]   = useState('');
  const [newPassword, setNewPassword]           = useState('');
  const [newPasswordVerify, setNewPasswordVerify] = useState('');

  // FEAT-002: fetch effective AD password policy when the operator opts in.
  const { policy: adPolicy, loading: adPolicyLoading } = usePolicy(
    settings.showAdPasswordPolicy === true
  );

  const [showCurrent, setShowCurrent]           = useState(false);
  const [showNew, setShowNew]                   = useState(false);
  const [showVerify, setShowVerify]             = useState(false);

  const [formErrors, setFormErrors]             = useState<FormErrors>({});
  const [submitting, setSubmitting]             = useState(false);
  const [approachingLockout, setApproachingLockout] = useState(false);

  // FEAT-003: clipboard auto-clear lifecycle.
  const [clipboardRemaining, setClipboardRemaining] = useState<number>(0);
  const [clipboardState, setClipboardState]
    = useState<'idle' | 'counting' | 'cleared' | 'cancelled'>('idle');
  const clipboardHandleRef = useRef<ClipboardClearHandle | null>(null);
  const clearedResetTimerRef = useRef<number | null>(null);

  // Cancel any pending clipboard timer when the form unmounts.
  useEffect(() => {
    return () => {
      clipboardHandleRef.current?.cancel();
      if (clearedResetTimerRef.current !== null) {
        window.clearTimeout(clearedResetTimerRef.current);
      }
    };
  }, []);

  const { executeRecaptcha } = useRecaptcha(
    settings.recaptcha?.enabled ? settings.recaptcha.siteKey : undefined
  );

  // FEAT-004: HIBP blur-triggered breach indicator. Debounced at 400ms,
  // AbortController-cancelled on subsequent blurs. Plaintext never leaves the
  // browser — only the 5-char SHA-1 prefix is POSTed to the server.
  const { state: hibpState, count: hibpCount, check: hibpCheck } = useHibpCheck(400);
  // Fail-open flag mirrors the server-side PasswordChangeOptions. Default TRUE
  // (hide indicator on HIBP outage) unless operator explicitly sets it to false,
  // in which case the warning Alert is rendered.
  const hibpFailOpen = settings.failOpenOnPwnedCheckUnavailable !== false;

  function validate(): FormErrors {
    const errs: FormErrors = {};
    const required = errors_.fieldRequired ?? 'This field is required.';

    if (!username.trim())       { errs.username        = required; }
    if (!currentPassword)       { errs.currentPassword = required; }
    if (!newPassword)           { errs.newPassword     = required; }
    if (!newPasswordVerify)     { errs.newPasswordVerify = required; }

    if (username && attrs && attrs.length > 0) {
      // Apply email regex only when every configured attribute requires an email-format input.
      const allRequireEmail = attrs.every(a => a === 'userprincipalname' || a === 'mail');
      if (allRequireEmail && emailRx) {
        if (!emailRx.test(username))
          errs.username = errors_.usernameEmailPattern ?? 'Please enter a valid email address.';
      }
      // samaccountname (or any combo including it): no regex — bare name and email-format both accepted
    } else if (username && settings.useEmail && emailRx) {
      if (!emailRx.test(username))
        errs.username = errors_.usernameEmailPattern ?? 'Please enter a valid email address.';
    } else if (username && usernameRx) {
      if (!usernameRx.test(username))
        errs.username = errors_.usernamePattern ?? 'Please enter a valid username.';
    }

    if (newPassword && newPasswordVerify && newPassword !== newPasswordVerify)
      errs.newPasswordVerify = errors_.passwordMatch ?? 'Passwords do not match.';

    // minimumScore client-side check is intentionally skipped here;
    // the strength meter gives visual feedback and the backend enforces nothing
    // (score is UI-only). Server-side we only enforce minimumDistance.

    if (newPassword && currentPassword && settings.minimumDistance > 0) {
      if (levenshtein(currentPassword, newPassword) < settings.minimumDistance)
        errs.newPassword = settings.alerts?.errorDistancePassword ?? 'New password is too similar to your current password.';
    }

    return errs;
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    const errs = validate();
    setFormErrors(errs);
    if (Object.keys(errs).length > 0) return;

    setSubmitting(true);
    setApproachingLockout(false);
    // Cancel any pending clipboard-clear timer — form submission supersedes it.
    clipboardHandleRef.current?.cancel();
    clipboardHandleRef.current = null;
    setClipboardState('idle');
    try {
      const recaptchaToken = settings.recaptcha?.enabled && settings.recaptcha?.siteKey
        ? await executeRecaptcha()
        : '';

      const result = await changePassword({
        username,
        currentPassword,
        newPassword,
        newPasswordVerify,
        recaptcha: recaptchaToken,
      });

      if (!result.errors || result.errors.length === 0) {
        onSuccess();
        return;
      }

      const newErrs: FormErrors = {};
      result.errors.forEach((err: ApiErrorItem) => {
        if (err.errorCode === ApiErrorCode.ApproachingLockout) {
          setApproachingLockout(true);
        }
        const msg = errorMessage(err.errorCode, settings.alerts);
        if (err.fieldName === 'Username')               newErrs.username        = msg;
        else if (err.fieldName === 'CurrentPassword')   newErrs.currentPassword = msg;
        else if (err.fieldName === 'NewPassword')       newErrs.newPassword     = msg;
        else if (err.fieldName === 'NewPasswordVerify') newErrs.newPasswordVerify = msg;
        else                                            newErrs.general         = msg;
      });
      setFormErrors(newErrs);
    } catch {
      setFormErrors({ general: 'An unexpected error occurred. Please try again.' });
    } finally {
      setSubmitting(false);
    }
  }

  async function handleGenerate() {
    const pwd = generatePassword(settings.passwordEntropy || 16);
    setNewPassword(pwd);
    setNewPasswordVerify(pwd);
    setShowNew(true);
    setShowVerify(true);

    // FEAT-003: copy to clipboard and schedule auto-clear.
    // Cancel any prior timer first (regenerate case) so the old countdown
    // does not race the new password's clear timer.
    clipboardHandleRef.current?.cancel();
    clipboardHandleRef.current = null;
    if (clearedResetTimerRef.current !== null) {
      window.clearTimeout(clearedResetTimerRef.current);
      clearedResetTimerRef.current = null;
    }

    try {
      if (typeof navigator !== 'undefined' && navigator.clipboard?.writeText) {
        await navigator.clipboard.writeText(pwd);
      } else {
        // Clipboard API unavailable — skip scheduling entirely.
        setClipboardState('idle');
        return;
      }
    } catch {
      // Write failed (permission denied, insecure context) — do not schedule.
      setClipboardState('idle');
      return;
    }

    const secs = settings.clipboardClearSeconds ?? 30;
    if (secs > 0) {
      setClipboardState('counting');
      setClipboardRemaining(secs);
      clipboardHandleRef.current = scheduleClipboardClear(
        pwd,
        secs,
        (r) => setClipboardRemaining(r),
        () => {
          setClipboardState('cleared');
          if (clearedResetTimerRef.current !== null) {
            window.clearTimeout(clearedResetTimerRef.current);
          }
          clearedResetTimerRef.current = window.setTimeout(() => {
            setClipboardState('idle');
            clearedResetTimerRef.current = null;
          }, 2000);
        },
        () => setClipboardState('cancelled'),
      );
    } else {
      setClipboardState('idle');
    }
  }

  const visibilityAdornment = (show: boolean, toggle: () => void) => (
    <InputAdornment position="end">
      <IconButton onClick={toggle} edge="end" aria-label={show ? 'Hide password' : 'Show password'}>
        {show ? <VisibilityOff /> : <Visibility />}
      </IconButton>
    </InputAdornment>
  );

  return (
    <Box component="form" onSubmit={handleSubmit} noValidate>
      {form.helpText && (
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          {form.helpText}
        </Typography>
      )}

      {/* Username */}
      <TextField
        fullWidth
        required
        label={form.usernameLabel ?? 'Username'}
        helperText={formErrors.username ?? (usernameHint !== null
          ? usernameHint
          : (settings.useEmail
            ? (form.usernameHelpblock ?? 'Your organisation email address')
            : (form.usernameDefaultDomainHelperBlock ?? form.usernameHelpblock ?? '')))}
        error={!!formErrors.username}
        value={username}
        onChange={e => setUsername(e.target.value)}
        autoComplete="username"
        inputProps={{ maxLength: 256 }}
        sx={{ mb: 2 }}
      />

      {/* Current password */}
      <TextField
        fullWidth
        required
        label={form.currentPasswordLabel ?? 'Current Password'}
        helperText={formErrors.currentPassword ?? (form.currentPasswordHelpblock ?? '')}
        error={!!formErrors.currentPassword}
        type={showCurrent ? 'text' : 'password'}
        value={currentPassword}
        onChange={e => setCurrentPassword(e.target.value)}
        autoComplete="current-password"
        inputProps={{ maxLength: 256 }}
        InputProps={{ endAdornment: visibilityAdornment(showCurrent, () => setShowCurrent(v => !v)) }}
        sx={{ mb: 2 }}
      />

      {/* AD password policy panel (FEAT-002) — fails closed when policy=null */}
      {settings.showAdPasswordPolicy && (
        <AdPasswordPolicyPanel policy={adPolicy} loading={adPolicyLoading} />
      )}

      {/* New password */}
      <TextField
        fullWidth
        required
        label={form.newPasswordLabel ?? 'New Password'}
        helperText={formErrors.newPassword ?? (form.newPasswordHelpblock ?? '')}
        error={!!formErrors.newPassword}
        type={showNew ? 'text' : 'password'}
        value={newPassword}
        onChange={e => setNewPassword(e.target.value)}
        onBlur={e => hibpCheck(e.target.value)}
        autoComplete="new-password"
        inputProps={{ maxLength: 256 }}
        InputProps={{
          endAdornment: (
            <InputAdornment position="end">
              {settings.usePasswordGeneration && (
                <Tooltip title="Generate password">
                  <IconButton onClick={handleGenerate} edge="end" aria-label="Generate password">
                    <AutorenewIcon />
                  </IconButton>
                </Tooltip>
              )}
              <IconButton onClick={() => setShowNew(v => !v)} edge="end" aria-label={showNew ? 'Hide password' : 'Show password'}>
                {showNew ? <VisibilityOff /> : <Visibility />}
              </IconButton>
            </InputAdornment>
          ),
        }}
        sx={{ mb: settings.showPasswordMeter ? 0.5 : 2 }}
      />

      {/* FEAT-004: HIBP breach indicator (blur-triggered, debounced, k-anonymity) */}
      <HibpIndicator state={hibpState} count={hibpCount} failOpen={hibpFailOpen} />

      {/* FEAT-003: clipboard auto-clear countdown / cleared chip */}
      <ClipboardCountdown remaining={clipboardRemaining} state={clipboardState} />

      {settings.showPasswordMeter && (
        <Box sx={{ mb: 2 }}>
          <PasswordStrengthMeter password={newPassword} />
        </Box>
      )}

      {/* Confirm new password */}
      <TextField
        fullWidth
        required
        label={form.newPasswordVerifyLabel ?? 'Re-enter New Password'}
        helperText={formErrors.newPasswordVerify ?? (form.newPasswordVerifyHelpblock ?? '')}
        error={!!formErrors.newPasswordVerify}
        type={showVerify ? 'text' : 'password'}
        value={newPasswordVerify}
        onChange={e => setNewPasswordVerify(e.target.value)}
        autoComplete="new-password"
        inputProps={{ maxLength: 256 }}
        InputProps={{ endAdornment: visibilityAdornment(showVerify, () => setShowVerify(v => !v)) }}
        sx={{ mb: 3 }}
      />

      {/* Live region for screen reader announcements of dynamic errors */}
      <Box aria-live="assertive" aria-atomic="true">
        {/* Approaching-lockout warning — shown when the next failure will trigger portal lockout */}
        {approachingLockout && (
          <Alert severity="warning" sx={{ mb: 2 }}>
            {settings.alerts?.errorApproachingLockout
              ?? 'Warning: one more failed attempt will temporarily lock your access to this portal.'}
          </Alert>
        )}

        {/* General error */}
        {formErrors.general && (
          <Alert severity="error" sx={{ mb: 2 }}>
            {formErrors.general}
          </Alert>
        )}
      </Box>

      <Button
        type="submit"
        variant="contained"
        fullWidth
        size="large"
        disabled={submitting}
        startIcon={submitting ? <CircularProgress size={18} color="inherit" /> : undefined}
      >
        {submitting ? 'Changing…' : (form.changePasswordButtonLabel ?? 'Change Password')}
      </Button>
    </Box>
  );
}
