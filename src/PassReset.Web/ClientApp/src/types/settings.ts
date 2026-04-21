export interface ValidationRegex {
  emailRegex?: string;
  usernameRegex?: string;
}

export interface RecaptchaSettings {
  enabled?: boolean;
  siteKey?: string;
  languageCode?: string;
}

export interface ChangePasswordFormStrings {
  helpText?: string;
  usernameLabel?: string;
  usernameHelpblock?: string;
  usernameDefaultDomainHelperBlock?: string;
  currentPasswordLabel?: string;
  currentPasswordHelpblock?: string;
  newPasswordLabel?: string;
  newPasswordHelpblock?: string;
  newPasswordVerifyLabel?: string;
  newPasswordVerifyHelpblock?: string;
  changePasswordButtonLabel?: string;
}

export interface ErrorsPasswordForm {
  fieldRequired?: string;
  passwordMatch?: string;
  usernameEmailPattern?: string;
  usernamePattern?: string;
}

export interface Alerts {
  successAlertTitle?: string;
  successAlertBody?: string;
  errorPasswordChangeNotAllowed?: string;
  errorInvalidCredentials?: string;
  errorInvalidDomain?: string;
  errorInvalidUser?: string;
  errorCaptcha?: string;
  errorFieldRequired?: string;
  errorFieldMismatch?: string;
  errorComplexPassword?: string;
  errorConnectionLdap?: string;
  errorScorePassword?: string;
  errorDistancePassword?: string;
  errorPwnedPassword?: string;
  errorPasswordTooYoung?: string;
  errorRateLimitExceeded?: string;
  errorPwnedPasswordCheckFailed?: string;
  errorPortalLockout?: string;
  errorApproachingLockout?: string;
  errorPasswordTooRecentlyChanged?: string;
}

export interface BrandingSettings {
  companyName?: string;
  portalName?: string;
  helpdeskUrl?: string;
  helpdeskEmail?: string;
  usageText?: string;
  logoFileName?: string;
  faviconFileName?: string;
  assetRoot?: string;
}

export interface ClientSettings {
  applicationTitle?: string;
  changePasswordTitle?: string;
  usePasswordGeneration: boolean;
  minimumDistance: number;
  passwordEntropy: number;
  showPasswordMeter: boolean;
  minimumScore: number;
  useEmail: boolean;
  allowedUsernameAttributes?: string[];
  recaptcha?: RecaptchaSettings;
  changePasswordForm?: ChangePasswordFormStrings;
  errorsPasswordForm?: ErrorsPasswordForm;
  validationRegex?: ValidationRegex;
  alerts?: Alerts;
  branding?: BrandingSettings;
  showAdPasswordPolicy?: boolean;
  clipboardClearSeconds?: number;
  // FEAT-004: when true, the blur-triggered HIBP indicator stays silent if HIBP
  // is unreachable (matches server-side PasswordChangeOptions.FailOpenOnPwnedCheckUnavailable).
  failOpenOnPwnedCheckUnavailable?: boolean;
}

// FEAT-004: response shape from POST /api/password/pwned-check.
// The server returns the raw HIBP range body (suffix:count lines) so the client
// can perform the suffix match locally — server never learns which suffix matched.
export interface PwnedCheckResponse {
  suffixes: string;
  unavailable: boolean;
}

export interface PolicyResponse {
  minLength: number;
  requiresComplexity: boolean;
  historyLength: number;
  minAgeDays: number;
  maxAgeDays: number;
}

export interface ChangePasswordRequest {
  username: string;
  currentPassword: string;
  newPassword: string;
  newPasswordVerify: string;
  recaptcha: string;
}

export interface ApiErrorItem {
  errorCode: number;
  fieldName?: string;
  message?: string;
}

export interface ApiResult {
  errors: ApiErrorItem[];
  payload?: unknown;
}

export const ApiErrorCode = {
  Generic: 0,
  FieldRequired: 1,
  FieldMismatch: 2,
  UserNotFound: 3,
  InvalidCredentials: 4,
  InvalidCaptcha: 5,
  ChangeNotPermitted: 6,
  InvalidDomain: 7,
  LdapProblem: 8,
  ComplexPassword: 9,
  MinimumScore: 10,
  MinimumDistance: 11,
  PwnedPassword: 12,
  PasswordTooYoung: 13,
  AccountDisabled: 14,
  RateLimitExceeded: 15,
  PwnedPasswordCheckFailed: 16,
  PortalLockout: 17,
  ApproachingLockout: 18,
  PasswordTooRecentlyChanged: 19,
  BannedWord: 20,
  LocallyKnownPwned: 21,
} as const;
