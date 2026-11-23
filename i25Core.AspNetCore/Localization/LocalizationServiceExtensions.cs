using System.Globalization;
using i25Core.Core.Localization;
using i25Core.Infrastructure.Localization;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Options;

namespace i25Core.AspNetCore.Localization
{
    public static class LocalizationServiceExtensions
    {
        /// <summary>
        /// Registers ITranslator and request-language detection, using the "Localization" section of
        /// appsettings.json and the language files in the lang/ folder.
        /// </summary>
        public static IServiceCollection AddI25Localization(
            this IServiceCollection services,
            IConfiguration configuration,
            IHostEnvironment environment)
        {
            services.Configure<LocalizationOptions>(configuration.GetSection(LocalizationOptions.SectionName));
            services.PostConfigure<LocalizationOptions>(o => o.RootPath = environment.ContentRootPath);

            services.AddSingleton<ITranslator, JsonTranslator>();

            // Built when the first request arrives, so it knows which languages were really loaded.
            services.AddOptions<RequestLocalizationOptions>()
                .Configure<ITranslator, IOptions<LocalizationOptions>>((request, translator, localization) =>
                {
                    var settings = localization.Value;
                    var defaultLanguage = CultureInfo.GetCultureInfo(settings.DefaultLocale.Replace('_', '-'));

                    // Only the UI language follows the request. Number and date formatting (CurrentCulture) stays as the
                    // server has it, so one client's language can never change how data is parsed or stored.
                    var serverCulture = CultureInfo.CurrentCulture;

                    request.DefaultRequestCulture = new RequestCulture(serverCulture, defaultLanguage);
                    request.SupportedCultures = new List<CultureInfo> { serverCulture };
                    request.SupportedUICultures = translator.AvailableLocales
                        .Select(name => CultureInfo.GetCultureInfo(name))
                        .ToList();
                    request.FallBackToParentUICultures = true;      // "es-MX" is served with "es"
                    request.ApplyCurrentCultureToResponseHeaders = false;

                    // Order matters: the first provider that names a supported language wins.
                    request.RequestCultureProviders.Clear();

                    if (!string.IsNullOrWhiteSpace(settings.QueryParameter))
                    {
                        request.RequestCultureProviders.Add(new QueryStringRequestCultureProvider
                        {
                            QueryStringKey = settings.QueryParameter,
                            UIQueryStringKey = settings.QueryParameter
                        });
                    }

                    if (!string.IsNullOrWhiteSpace(settings.HeaderName))
                        request.RequestCultureProviders.Add(new LocaleHeaderProvider(settings.HeaderName));

                    if (settings.ReadAcceptLanguage)
                        request.RequestCultureProviders.Add(new AcceptLanguageHeaderRequestCultureProvider());
                });

            return services;
        }

        /// <summary>
        /// Picks the language for each request and adds a Content-Language header.
        /// Call it before UseExceptionHandler, so error messages are translated too.
        /// </summary>
        public static WebApplication UseI25Localization(this WebApplication app)
        {
            app.UseRequestLocalization();

            var settings = app.Services.GetRequiredService<IOptions<LocalizationOptions>>().Value;

            if (settings.SetContentLanguageHeader)
            {
                var translator = app.Services.GetRequiredService<ITranslator>();

                app.Use(async (context, next) =>
                {
                    // Read the language now, while the request's culture is in effect. By the time a response with
                    // no body (a plain 404) starts, the request has unwound and the culture is back to the default.
                    var locale = translator.Locale;

                    context.Response.OnStarting(() =>
                    {
                        context.Response.Headers.ContentLanguage = locale;
                        return Task.CompletedTask;
                    });

                    await next();
                });
            }

            return app;
        }
    }

    /// <summary>Reads the language from a request header such as X-Locale: es.</summary>
    public sealed class LocaleHeaderProvider : RequestCultureProvider
    {
        private readonly string _header;

        public LocaleHeaderProvider(string header)
        {
            _header = header;
        }

        public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
        {
            var value = httpContext.Request.Headers[_header].ToString();

            return Task.FromResult(string.IsNullOrWhiteSpace(value)
                ? null
                : new ProviderCultureResult(value.Trim()));
        }
    }
}
