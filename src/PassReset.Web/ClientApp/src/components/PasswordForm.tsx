import { useState, useMemo } from 'react';
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
import { useRecaptcha } from '../hooks/useRecaptcha';
import type { ClientSettings, ApiErrorItem } from '../types/settings';
import { ApiErrorCode } from '../types/settings';
import { levenshtein } from '../utils/levenshtein';
import { generatePassword } from '../utils/passwordGenerator';
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
    case ApiErrorCode.RateLimitExceeded:   return a.errorRateLimitExceeded    ?? 'Too many attempts. Please wait and try again.';
    default:                               return 'An unexpected error occurred. Please contact IT Support.';
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

  const [username, setUsername]                 = useState('');
  const [currentPassword, setCurrentPassword]   = useState('');
  const [newPassword, setNewPassword]           = useState('');
  const [newPasswordVerify, setNewPasswordVerify] = useState('');

  const [showCurrent, setShowCurrent]           = useState(false);
  const [showNew, setShowNew]                   = useState(false);
  const [showVerify, setShowVerify]             = useState(false);

  const [formErrors, setFormErrors]             = useState<FormErrors>({});
  const [submitting, setSubmitting]             = useState(false);

  const { executeRecaptcha } = useRecaptcha(
    settings.recaptcha?.enabled ? settings.recaptcha.siteKey : undefined
  );

  function validate(): FormErrors {
    const errs: FormErrors = {};
    const required = errors_.fieldRequired ?? 'This field is required.';

    if (!username.trim())       { errs.username        = required; }
    if (!currentPassword)       { errs.currentPassword = required; }
    if (!newPassword)           { errs.newPassword     = required; }
    if (!newPasswordVerify)     { errs.newPasswordVerify = required; }

    if (username && settings.useEmail && emailRx) {
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
        const msg = errorMessage(err.errorCode, settings.alerts);
        if (err.fieldName === 'Username')           newErrs.username        = msg;
        else if (err.fieldName === 'CurrentPassword') newErrs.currentPassword = msg;
        else if (err.fieldName === 'NewPassword')   newErrs.newPassword     = msg;
        else if (err.fieldName === 'NewPasswordVerify') newErrs.newPasswordVerify = msg;
        else                                          newErrs.general         = msg;
      });
      setFormErrors(newErrs);
    } catch {
      setFormErrors({ general: 'An unexpected error occurred. Please try again.' });
    } finally {
      setSubmitting(false);
    }
  }

  function handleGenerate() {
    const pwd = generatePassword(settings.passwordEntropy || 16);
    setNewPassword(pwd);
    setNewPasswordVerify(pwd);
    setShowNew(true);
    setShowVerify(true);
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
        helperText={formErrors.username ?? (settings.useEmail
          ? (form.usernameHelpblock ?? 'Your organisation email address')
          : (form.usernameDefaultDomainHelperBlock ?? form.usernameHelpblock ?? ''))}
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

      {/* General error */}
      {formErrors.general && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {formErrors.general}
        </Alert>
      )}

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
