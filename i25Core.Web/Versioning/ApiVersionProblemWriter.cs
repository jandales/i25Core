using System.Diagnostics;
using i25Core.Core.DTO;

namespace i25Core.Web.Versioning
{
    /// <summary>
    /// Asp.Versioning reports a bad, missing or unsupported version as an RFC 7807 "problem details" object.
    /// This writer turns those into the API's normal ApiResponse envelope, so a client sees one error shape everywhere.
    /// </summary>
    public sealed class ApiVersionProblemWriter : IProblemDetailsWriter
    {
        // The "code" values Asp.Versioning puts on its problem details.
        private static readonly HashSet<string> VersionErrorCodes = new(StringComparer.Ordinal)
        {
            "UnsupportedApiVersion",
            "ApiVersionUnspecified",
            "InvalidApiVersion",
            "AmbiguousApiVersion"
        };

        public bool CanWrite(ProblemDetailsContext context) =>
            context.ProblemDetails.Extensions.TryGetValue("code", out var code) &&
            code is string text &&
            VersionErrorCodes.Contains(text);

        public async ValueTask WriteAsync(ProblemDetailsContext context)
        {
            var problem = context.ProblemDetails;
            var http = context.HttpContext;

            var message = !string.IsNullOrWhiteSpace(problem.Detail) ? problem.Detail
                        : !string.IsNullOrWhiteSpace(problem.Title) ? problem.Title
                        : "The requested API version is not valid.";

            var response = ApiResponse<object?>.Failure(message);
            response.TraceId = Activity.Current?.Id ?? http.TraceIdentifier;

            http.Response.StatusCode = problem.Status ?? StatusCodes.Status400BadRequest;
            await http.Response.WriteAsJsonAsync(response, http.RequestAborted);
        }
    }
}
