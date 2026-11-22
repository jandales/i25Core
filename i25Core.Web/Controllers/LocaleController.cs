using i25Core.Core.DTO;
using i25Core.Core.Localization;
using i25Core.Infrastructure.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace i25Core.Web.Controllers
{
    /// <summary>
    /// Languages the API speaks, and their texts, for front-end apps that want to show the same strings.
    /// Language files are public through this endpoint: never put secrets in them.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Route("api/v{version:apiVersion}/[controller]")]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]   // the list of languages is public: the sign-in screen needs it
    public class LocaleController : ControllerBase
    {
        private readonly ITranslator _translator;
        private readonly LocalizationOptions _options;

        public LocaleController(ITranslator translator, IOptions<LocalizationOptions> options)
        {
            _translator = translator;
            _options = options.Value;
        }

        /// <summary>GET /api/Locale : the language chosen for this request, the default, and what is available.</summary>
        [HttpGet]
        public IActionResult Get() =>
            Ok(ApiResponse<object>.SuccessResult(new
            {
                current = _translator.Locale,
                @default = _options.DefaultLocale,
                fallback = _options.FallbackLocale,
                available = _translator.AvailableLocales
            }));

        /// <summary>
        /// GET /api/Locale/messages?locale=es&amp;group=messages : the translations as { "messages.created": "..." }.
        /// With no locale it uses the language of this request. Missing texts come from the fallback language.
        /// </summary>
        [HttpGet("messages")]
        public IActionResult Messages([FromQuery] string? locale, [FromQuery] string? group)
        {
            if (!string.IsNullOrWhiteSpace(locale) &&
                !_translator.AvailableLocales.Contains(locale, StringComparer.OrdinalIgnoreCase))
            {
                return NotFound(ApiResponse<object?>.Failure(
                    _translator.Get("errors.locale_not_found", new { locale })));
            }

            return Ok(ApiResponse<IReadOnlyDictionary<string, string>>.SuccessResult(_translator.All(locale, group)));
        }
    }
}
