using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using PassReset.Common;
using PassReset.PasswordProvider;
using PassReset.Web.Models;
using PassReset.Web.Services;

namespace PassReset.Web.Controllers;

/// <summary>
/// Handles password change requests and exposes client configuration.
/// Rate-limited to 5 requests per 5 minutes per IP on the POST endpoint.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class PasswordController : ControllerBase
{
    private readonly IPasswordChangeProvider _provider;
    private readonly IEmailService _emailService;
    private readonly ISiemService _siemService;
    private readonly IOptions<ClientSettings> _clientSettings;
    private readonly IOptions<EmailNotificationSettings> _emailNotifSettings;
    private readonly PasswordPolicyCache _policyCache;
    private readonly IPwnedPasswordChecker _pwnedChecker;
    private readonly PasswordChangeOptions _passwordOptions;
    private readonly ILogger<PasswordController> _logger;

    // Pre-compiled 5-char hex regex for pwned-check prefix validation.
    private static readonly Regex Sha1PrefixRegex =
        new("^[a-fA-F0-9]{5}$", RegexOptions.Compiled);

    // Static HttpClient for reCAPTCHA v3 verification — avoids socket exhaustion.
    // PooledConnectionLifetime ensures DNS changes are respected without restarting the process.
    private static readonly HttpClient _recaptchaHttp = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
    })
    {
        BaseAddress = new Uri("https://www.google.com/"),
        Timeout     = TimeSpan.FromSeconds(10),
    };

    public PasswordController(
        IPasswordChangeProvider provider,
        IEmailService emailService,
        ISiemService siemService,
        IOptions<ClientSettings> clientSettings,
        IOptions<EmailNotificationSettings> emailNotifSettings,
        PasswordPolicyCache policyCache,
        IPwnedPasswordChecker pwnedChecker,
        IOptions<PasswordChangeOptions> passwordOptions,
        ILogger<PasswordController> logger)
    {
        _provider           = provider;
        _emailService       = emailService;
        _siemService        = siemService;
        _clientSettings     = clientSettings;
        _emailNotifSettings = emailNotifSettings;
        _policyCache        = policyCache;
        _pwnedChecker       = pwnedChecker;
        _passwordOptions    = passwordOptions.Value;
        _logger             = logger;
    }

    /// <summary>
    /// Returns the client-facing configuration (UI strings, feature flags, reCAPTCHA site key).
    /// GET /api/password
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Get() => Ok(_clientSettings.Value);

    /// <summary>
    /// Returns the effective default-domain password policy from RootDSE (FEAT-002).
    /// Returns 404 when ShowAdPasswordPolicy is disabled or the AD query fails — the UI
    /// fails closed and renders nothing.
    /// </summary>
    [HttpGet("policy")]
    [EnableRateLimiting("password-fixed-window")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPolicyAsync()
    {
        if (!_clientSettings.Value.ShowAdPasswordPolicy) return NotFound();
        var policy = await _policyCache.GetOrFetchAsync();
        return policy is null ? NotFound() : Ok(policy);
    }

    /// <summary>
    /// FEAT-004: HIBP k-anonymity pre-check.
    /// Accepts a 5-char SHA-1 hex prefix, proxies to the HIBP range API, and returns
    /// the raw suffix list. The client performs the suffix match locally so the server
    /// never learns which suffix matched. Plaintext never leaves the browser.
    /// Rate-limited via the <c>pwned-check-window</c> policy (20/5min/IP).
    /// Honors <see cref="PasswordChangeOptions.FailOpenOnPwnedCheckUnavailable"/>.
    /// POST /api/password/pwned-check
    /// </summary>
    [HttpPost("pwned-check")]
    [EnableRateLimiting("pwned-check-window")]
    [RequestSizeLimit(64)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> PwnedCheckAsync([FromBody] PwnedCheckRequest req, CancellationToken ct)
    {
        if (req is null || req.Prefix is null || req.Prefix.Length != 5 || !Sha1PrefixRegex.IsMatch(req.Prefix))
            return BadRequest();

        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        var (body, unavailable) = await _pwnedChecker.FetchRangeAsync(req.Prefix, ct);
        if (unavailable)
        {
            _siemService.LogEvent(
                SiemEventType.Generic,
                "pwned-check",
                clientIp,
                $"HIBP range fetch unavailable; FailOpen={_passwordOptions.FailOpenOnPwnedCheckUnavailable}");

            if (_passwordOptions.FailOpenOnPwnedCheckUnavailable)
                return Ok(new { suffixes = string.Empty, unavailable = true });

            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { suffixes = string.Empty, unavailable = true });
        }

        return Ok(new { suffixes = body, unavailable = false });
    }

    /// <summary>
    /// Changes the password for the specified user account.
    /// POST /api/password
    /// </summary>
    [HttpPost]
    [EnableRateLimiting("password-fixed-window")]
    [RequestSizeLimit(8192)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> PostAsync([FromBody] ChangePasswordModel model)
    {
        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        if (!ModelState.IsValid)
        {
            Audit("ValidationFailed", model.Username, clientIp, SiemEventType.ValidationFailed);
            return BadRequest(ApiResult.FromModelStateErrors(ModelState));
        }

        // Validate minimum Levenshtein distance between old and new password
        var settings = _clientSettings.Value;

        if (settings.MinimumDistance > 0 &&
            _provider.MeasureNewPasswordDistance(model.CurrentPassword, model.NewPassword) < settings.MinimumDistance)
        {
            Audit("DistanceTooLow", model.Username, clientIp);
            var result = new ApiResult();
            result.Errors.Add(new ApiErrorItem(ApiErrorCode.MinimumDistance));
            return BadRequest(result);
        }

        // reCAPTCHA v3 validation (skipped unless Enabled = true and PrivateKey is set)
        var recaptchaConfig = settings.Recaptcha;
        if (recaptchaConfig?.Enabled == true && !string.IsNullOrWhiteSpace(recaptchaConfig.PrivateKey))
        {
            if (!await ValidateRecaptchaAsync(model.Recaptcha, recaptchaConfig, clientIp))
            {
                Audit("RecaptchaFailed", model.Username, clientIp, SiemEventType.RecaptchaFailed);
                return BadRequest(ApiResult.InvalidCaptcha());
            }
        }

        // Perform the password change
        var error = await _provider.PerformPasswordChangeAsync(model.Username, model.CurrentPassword, model.NewPassword);

        if (error is not null)
        {
            var siemType = MapErrorCodeToSiemEvent(error.ErrorCode);
            Audit($"Failed:{error.ErrorCode}", model.Username, clientIp, siemType, error.Message);
            var result = new ApiResult();
            result.Errors.Add(error);
            return BadRequest(result);
        }

        Audit("Success", model.Username, clientIp, SiemEventType.PasswordChanged);

        // Fire-and-forget email notification — capture HttpContext values before Task.Run
        if (_emailNotifSettings.Value.Enabled)
        {
            var username  = model.Username;
            var timestamp = DateTime.UtcNow.ToString("u");
            var ip        = clientIp;
            var emailSvc  = _emailService;
            var notifCfg  = _emailNotifSettings.Value;

            _ = Task.Run(async () =>
            {
                var emailAddress = _provider.GetUserEmail(username);
                if (string.IsNullOrWhiteSpace(emailAddress)) return;

                var body = notifCfg.BodyTemplate
                    .Replace("{Username}",  username,  StringComparison.Ordinal)
                    .Replace("{Timestamp}", timestamp, StringComparison.Ordinal)
                    .Replace("{IpAddress}", ip,        StringComparison.Ordinal);

                await emailSvc.SendAsync(emailAddress, username, notifCfg.Subject, body);
            });
        }

        return Ok(new ApiResult("Password changed successfully."));
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    private void Audit(string outcome, string username, string clientIp,
        SiemEventType? siemEvent = null, string? detail = null)
    {
        _logger.LogInformation(
            "PasswordChange outcome={Outcome} user={User} ip={Ip}",
            outcome, username, clientIp);

        if (siemEvent.HasValue)
            _siemService.LogEvent(siemEvent.Value, username, clientIp, detail);
    }

    private static SiemEventType MapErrorCodeToSiemEvent(ApiErrorCode code) => code switch
    {
        ApiErrorCode.InvalidCredentials  => SiemEventType.InvalidCredentials,
        ApiErrorCode.UserNotFound        => SiemEventType.UserNotFound,
        ApiErrorCode.PortalLockout       => SiemEventType.PortalLockout,
        ApiErrorCode.ApproachingLockout  => SiemEventType.ApproachingLockout,
        ApiErrorCode.ChangeNotPermitted  => SiemEventType.ChangeNotPermitted,
        _                                => SiemEventType.Generic,
    };

    private async Task<bool> ValidateRecaptchaAsync(
        string token, Recaptcha config, string clientIp)
    {
        try
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["secret"]   = config.PrivateKey!,
                ["response"] = token,
                ["remoteip"] = clientIp,
            });

            var response = await _recaptchaHttp.PostAsync("recaptcha/api/siteverify", content);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("reCAPTCHA API returned {StatusCode} for IP {ClientIp}",
                    response.StatusCode, clientIp);
                if (config.FailOpenOnUnavailable)
                {
                    _logger.LogWarning("reCAPTCHA fail-open enabled — allowing request through for IP {ClientIp}", clientIp);
                    return true;
                }
                return false;
            }

            var json = await response.Content.ReadFromJsonAsync<RecaptchaResponse>();
            return json?.Success == true
                && json.Score >= config.ScoreThreshold
                && string.Equals(json.Action, "change_password", StringComparison.OrdinalIgnoreCase);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "reCAPTCHA service unreachable for IP {ClientIp}", clientIp);
            if (config.FailOpenOnUnavailable)
            {
                _logger.LogWarning("reCAPTCHA fail-open enabled — allowing request through for IP {ClientIp}", clientIp);
                return true;
            }
            return false;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "reCAPTCHA request timed out for IP {ClientIp}", clientIp);
            if (config.FailOpenOnUnavailable)
            {
                _logger.LogWarning("reCAPTCHA fail-open enabled — allowing request through for IP {ClientIp}", clientIp);
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            // Unexpected errors (JSON parse, etc.) — never fail-open
            _logger.LogWarning(ex, "reCAPTCHA unexpected error for IP {ClientIp}", clientIp);
            return false;
        }
    }

    // Minimal DTO for reCAPTCHA v3 API response deserialization
    private sealed class RecaptchaResponse
    {
        public bool  Success { get; set; }
        public float Score   { get; set; }
        public string? Action { get; set; }
    }
}
