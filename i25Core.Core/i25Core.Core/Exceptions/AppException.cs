namespace i25Core.Core.Exceptions
{
    /// <summary>
    /// Base type for expected application errors. The global exception handler
    /// turns these into an ApiResponse with <see cref="StatusCode"/>.
    /// </summary>
    public abstract class AppException : Exception
    {
        public int StatusCode { get; }
        public IDictionary<string, string[]>? Errors { get; }

        protected AppException(
            string message,
            int statusCode,
            IDictionary<string, string[]>? errors = null)
            : base(message)
        {
            StatusCode = statusCode;
            Errors = errors;
        }
    }

    /// <summary>404 - the requested record does not exist.</summary>
    public class NotFoundException : AppException
    {
        public NotFoundException(string message) : base(message, 404) { }

        public NotFoundException(string entityName, object key)
            : base($"{entityName} with id {key} was not found.", 404) { }
    }

    /// <summary>422 - the request is valid but breaks a business rule.</summary>
    public class BusinessRuleException : AppException
    {
        public BusinessRuleException(
            string message,
            IDictionary<string, string[]>? errors = null)
            : base(message, 422, errors) { }
    }

    /// <summary>409 - the request conflicts with the current state of the data.</summary>
    public class ConflictException : AppException
    {
        public ConflictException(string message) : base(message, 409) { }
    }

    /// <summary>403 - the caller is not allowed to perform this action.</summary>
    public class ForbiddenException : AppException
    {
        public ForbiddenException(string message = "You are not allowed to perform this action.")
            : base(message, 403) { }
    }
}
