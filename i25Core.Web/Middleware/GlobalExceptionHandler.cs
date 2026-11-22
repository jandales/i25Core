using System.Diagnostics;
using FluentValidation;
using i25Core.Core.DTO;
using i25Core.Core.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace i25Core.Web.Middleware
{
    /// <summary>
    /// Converts every unhandled exception into the standard ApiResponse envelope.
    /// </summary>
    public class GlobalExceptionHandler : IExceptionHandler
    {
        // SQL Server error numbers
        private const int ForeignKeyViolation = 547;
        private const int UniqueIndexViolation = 2601;
        private const int UniqueConstraintViolation = 2627;
        private const int FirstUserDefinedError = 50000; // RAISERROR / THROW in stored procedures

        private readonly ILogger<GlobalExceptionHandler> _logger;
        private readonly IHostEnvironment _environment;

        public GlobalExceptionHandler(
            ILogger<GlobalExceptionHandler> logger,
            IHostEnvironment environment)
        {
            _logger = logger;
            _environment = environment;
        }

        public async ValueTask<bool> TryHandleAsync(
            HttpContext httpContext,
            Exception exception,
            CancellationToken cancellationToken)
        {
            var (statusCode, message, errors) = Map(exception);
            var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;

            if (statusCode >= StatusCodes.Status500InternalServerError)
            {
                _logger.LogError(exception,
                    "Unhandled exception on {Method} {Path} (traceId {TraceId})",
                    httpContext.Request.Method, httpContext.Request.Path, traceId);
            }
            else
            {
                _logger.LogWarning(
                    "{ExceptionType} on {Method} {Path} returned {StatusCode}: {Message} (traceId {TraceId})",
                    exception.GetType().Name, httpContext.Request.Method,
                    httpContext.Request.Path, statusCode, exception.Message, traceId);
            }

            // Only expose internal details while developing.
            if (statusCode >= StatusCodes.Status500InternalServerError && _environment.IsDevelopment())
            {
                message = $"{message} {exception.GetBaseException().Message}";
            }

            var response = ApiResponse<object?>.Failure(message, errors);
            response.TraceId = traceId;

            httpContext.Response.StatusCode = statusCode;
            await httpContext.Response.WriteAsJsonAsync(response, cancellationToken);

            return true;
        }

        private static (int StatusCode, string Message, IDictionary<string, string[]>? Errors) Map(Exception exception)
        {
            switch (exception)
            {
                case AppException app:
                    return (app.StatusCode, app.Message, app.Errors);

                case ValidationException validation:
                    return (StatusCodes.Status400BadRequest, "Validation failed.",
                        validation.Errors
                            .GroupBy(e => e.PropertyName)
                            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));

                case KeyNotFoundException:
                    return (StatusCodes.Status404NotFound, exception.Message, null);

                // Thrown by QueryableExtensions for unknown filter/sort fields or operators.
                case ArgumentException:
                case NotSupportedException:
                    return (StatusCodes.Status400BadRequest, exception.Message, null);

                // Malformed Filters/Sorts JSON in the query string (GetRequestModelBinder).
                case System.Text.Json.JsonException:
                    return (StatusCodes.Status400BadRequest, "The request contains malformed JSON.", null);

                case DbUpdateConcurrencyException:
                    return (StatusCodes.Status409Conflict,
                        "The record was modified by another user. Reload it and try again.", null);

                case DbUpdateException { InnerException: SqlException sql }:
                    return MapSql(sql);

                case SqlException sql:
                    return MapSql(sql);

                case OperationCanceledException:
                    return (499, "The request was cancelled.", null);

                default:
                    return (StatusCodes.Status500InternalServerError, "An unexpected error occurred.", null);
            }
        }

        private static (int, string, IDictionary<string, string[]>?) MapSql(SqlException sql)
        {
            return sql.Number switch
            {
                ForeignKeyViolation => (StatusCodes.Status409Conflict,
                    "The record is referenced by other data or points to a record that does not exist.", null),

                UniqueIndexViolation or UniqueConstraintViolation => (StatusCodes.Status409Conflict,
                    "A record with the same unique value already exists.", null),

                // Messages raised deliberately by stored procedures are business errors meant for the user.
                >= FirstUserDefinedError => (StatusCodes.Status422UnprocessableEntity, sql.Message, null),

                _ => (StatusCodes.Status500InternalServerError, "A database error occurred.", null)
            };
        }
    }
}
