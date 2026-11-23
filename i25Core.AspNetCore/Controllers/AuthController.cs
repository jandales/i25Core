using FluentValidation;
using i25Core.Core.Auth;
using i25Core.Core.DTO;
using i25Core.Core.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace i25Core.AspNetCore.Controllers
{
    /// <summary>Create an account, sign in, keep the session alive and manage your own password.</summary>
    [ApiController]
    [Route("api/[controller]")]
    [Route("api/v{version:apiVersion}/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _auth;
        private readonly ICurrentUser _me;
        private readonly ITranslator _t;

        public AuthController(IAuthService auth, ICurrentUser me, ITranslator translator)
        {
            _auth = auth;
            _me = me;
            _t = translator;
        }

        private string? Ip => HttpContext.Connection.RemoteIpAddress?.ToString();

        private async Task Validate<T>(T model)
        {
            var validator = HttpContext.RequestServices.GetService<IValidator<T>>();

            if (validator != null)
                await validator.ValidateAndThrowAsync(model);   // becomes a 400 with the field errors
        }

        /// <summary>POST /api/Auth/register : create an account (role User). Can be switched off with Auth:AllowRegistration.</summary>
        [AllowAnonymous]
        [EnableRateLimiting("auth")]   // strict per-address limit: these check or change credentials
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
        {
            await Validate(request);
            var profile = await _auth.RegisterAsync(request, cancellationToken);
            return Ok(ApiResponse<UserProfile>.SuccessResult(profile, _t.Get("auth.registered")));
        }

        /// <summary>POST /api/Auth/login : returns an access token (send it as "Authorization: Bearer ...") and a refresh token.</summary>
        [AllowAnonymous]
        [EnableRateLimiting("auth")]   // strict per-address limit: these check or change credentials
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
        {
            await Validate(request);
            var tokens = await _auth.LoginAsync(request, Ip, cancellationToken);
            return Ok(ApiResponse<AuthResponse>.SuccessResult(tokens, _t.Get("auth.logged_in")));
        }

        /// <summary>POST /api/Auth/refresh : trade a refresh token for a new access token AND a new refresh token. Each refresh token works once.</summary>
        [AllowAnonymous]
        [EnableRateLimiting("auth")]   // strict per-address limit: these check or change credentials
        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh([FromBody] RefreshRequest request, CancellationToken cancellationToken)
        {
            await Validate(request);
            var tokens = await _auth.RefreshAsync(request.RefreshToken, Ip, cancellationToken);
            return Ok(ApiResponse<AuthResponse>.SuccessResult(tokens));
        }

        /// <summary>POST /api/Auth/logout : ends this session by revoking its refresh token. Always answers OK.</summary>
        [AllowAnonymous]
        [HttpPost("logout")]
        public async Task<IActionResult> Logout([FromBody] RefreshRequest request, CancellationToken cancellationToken)
        {
            await _auth.LogoutAsync(request.RefreshToken, cancellationToken);
            return Ok(ApiResponse<object?>.SuccessResult(null, _t.Get("auth.logged_out")));
        }

        /// <summary>POST /api/Auth/logout-all : ends every session of the signed-in user, on every device.</summary>
        [Authorize]
        [HttpPost("logout-all")]
        public async Task<IActionResult> LogoutAll(CancellationToken cancellationToken)
        {
            await _auth.LogoutAllAsync(_me.Id!.Value, cancellationToken);
            return Ok(ApiResponse<object?>.SuccessResult(null, _t.Get("auth.logged_out_all")));
        }

        /// <summary>GET /api/Auth/me : the signed-in user's profile.</summary>
        [Authorize]
        [HttpGet("me")]
        public async Task<IActionResult> Me(CancellationToken cancellationToken)
        {
            var profile = await _auth.GetProfileAsync(_me.Id!.Value, cancellationToken);
            return Ok(ApiResponse<UserProfile>.SuccessResult(profile));
        }

        /// <summary>POST /api/Auth/change-password : needs the current password. Signs out every other device and returns a fresh session.</summary>
        [Authorize]
        [EnableRateLimiting("auth")]   // strict per-address limit: these check or change credentials
        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken cancellationToken)
        {
            await Validate(request);
            var tokens = await _auth.ChangePasswordAsync(_me.Id!.Value, request, Ip, cancellationToken);
            return Ok(ApiResponse<AuthResponse>.SuccessResult(tokens, _t.Get("auth.password_changed")));
        }

        /// <summary>POST /api/Auth/forgot-password : emails a reset link. The answer is the same whether or not the address has an account.</summary>
        [AllowAnonymous]
        [EnableRateLimiting("auth")]   // strict per-address limit: these check or change credentials
        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken cancellationToken)
        {
            await Validate(request);
            await _auth.ForgotPasswordAsync(request.Email, cancellationToken);
            return Ok(ApiResponse<object?>.SuccessResult(null, _t.Get("auth.reset_sent")));
        }

        /// <summary>POST /api/Auth/reset-password : sets a new password using the token from the email. Signs out every device.</summary>
        [AllowAnonymous]
        [EnableRateLimiting("auth")]   // strict per-address limit: these check or change credentials
        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken cancellationToken)
        {
            await Validate(request);
            await _auth.ResetPasswordAsync(request, cancellationToken);
            return Ok(ApiResponse<object?>.SuccessResult(null, _t.Get("auth.password_reset")));
        }
    }
}
