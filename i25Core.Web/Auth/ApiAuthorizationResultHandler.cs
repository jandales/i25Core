using i25Core.Core.DTO;
using i25Core.Core.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using System.Diagnostics;

namespace i25Core.Web.Auth
{
    /// <summary>
    /// When a request is refused by authorization, answer with the API's normal ApiResponse envelope
    /// (translated into the request's language) instead of an empty 401 or 403.
    /// </summary>
    public sealed class ApiAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
    {
        /// <summary>Key under which the JWT bearer handler leaves the reason a token was rejected.</summary>
        public const string TokenFailureItem = "i25.auth.token-failure";

        public async Task HandleAsync(
            RequestDelegate next,
            HttpContext context,
            AuthorizationPolicy policy,
            PolicyAuthorizationResult authorizeResult)
        {
            if (authorizeResult.Challenged)
            {
                await WriteAsync(context, StatusCodes.Status401Unauthorized);
                return;
            }

            if (authorizeResult.Forbidden)
            {
                await WriteAsync(context, StatusCodes.Status403Forbidden);
                return;
            }

            await next(context);
        }

        private static async Task WriteAsync(HttpContext context, int statusCode)
        {
            var translator = context.RequestServices.GetRequiredService<ITranslator>();
            string key;

            if (statusCode == StatusCodes.Status403Forbidden)
            {
                key = "errors.forbidden";
            }
            else
            {
                var sentToken = context.Request.Headers.ContainsKey("Authorization");
                var expired = context.Items.TryGetValue(TokenFailureItem, out var reason) && reason as string == "expired";

                key = !sentToken ? "auth.unauthenticated" : expired ? "auth.token_expired" : "auth.token_invalid";

                // Tells clients (and standard libraries) what to do: send a token, or get a new one.
                context.Response.Headers.WWWAuthenticate = !sentToken
                    ? "Bearer"
                    : expired
                        ? "Bearer error=\"invalid_token\", error_description=\"The token has expired\""
                        : "Bearer error=\"invalid_token\"";
            }

            var response = ApiResponse<object?>.Failure(translator.Get(key));
            response.TraceId = Activity.Current?.Id ?? context.TraceIdentifier;

            context.Response.StatusCode = statusCode;
            await context.Response.WriteAsJsonAsync(response, context.RequestAborted);
        }
    }
}
