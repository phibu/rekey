using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using PassReset.Common;
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
    private readonly ILogger<PasswordController> _logger;

    // Static HttpClient for reCAPTCHA v3 verification — avoids socket exhaustion.
    private static readonly HttpClient _recaptchaHttp = new()
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
        ILogger<PasswordController> logger)
    {
        _provider           = provider;
        _emailService       = emailService;
        _siemService        = siemService;
        _clientSettings     = clientSettings;
        _emailNotifSettings = emailNotifSettings;
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
    /// Changes the password for the specified user account.
    /// POST /api/password
    /// </summary>
    [HttpPost]
    [EnableRateLimiting("password-fixed-window")]
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
            if (!await ValidateRecaptchaAsync(model.Recaptcha, recaptchaConfig.PrivateKey, clientIp))
            {
                Audit("RecaptchaFailed", model.Username, clientIp, SiemEventType.RecaptchaFailed);
                return BadRequest(ApiResult.InvalidCaptcha());
            }
        }

        // Perform the password change
        var error = _provider.PerformPasswordChange(model.Username, model.CurrentPassword, model.NewPassword);

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

    private static async Task<bool> ValidateRecaptchaAsync(
        string token, string privateKey, string clientIp)
    {
        try
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["secret"]   = privateKey,
                ["response"] = token,
                ["remoteip"] = clientIp,
            });

            var response = await _recaptchaHttp.PostAsync("recaptcha/api/siteverify", content);

            if (!response.IsSuccessStatusCode) return false;

            var json = await response.Content.ReadFromJsonAsync<RecaptchaResponse>();
            // v3 returns a score 0.0–1.0; treat >= 0.5 as human.
            // Action must match the client-side action to prevent token replay from other pages.
            return json?.Success == true
                && json.Score >= 0.5f
                && string.Equals(json.Action, "change_password", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
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
