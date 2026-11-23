using System.Diagnostics;
using i25Core.Core.DTO;

namespace i25Core.AspNetCore.Versioning
{
    /// <summary>
    /// Asp.Versioning reports a bad, missing or unsupported version as an RFC 7807 "problem details" object.
    /// This writer turns those into the API's normal ApiResponse envelope, so a client sees one error shape everywhere.
    /// </summary>
    public sealed class ApiVersionProblemWriter : IProblemDetailsWriter
    {
        // Every problem Asp.Versioning reports has a "type" like
        // https://docs.api-versioning.org/problems#unsupported (also #invalid, #ambiguous, #unspecified).
        private const string VersioningProblemTypePrefix = "https://docs.api-versioning.org/problems#";

        public bool CanWrite(ProblemDetailsContext context) =>
            context.ProblemDetails.Type?.StartsWith(VersioningProblemTypePrefix, StringComparison.OrdinalIgnoreCase) == true;

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
