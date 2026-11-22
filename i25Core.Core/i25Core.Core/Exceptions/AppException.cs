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

        /// <summary>
        /// Translation key for the message (for example "errors.vendor_inactive"). When set, the exception
        /// handler shows the translation in the request's language; <see cref="Exception.Message"/> stays
        /// as the English text for logs. Set it with <see cref="WithKey"/>.
        /// </summary>
        public string? MessageKey { get; private set; }

        /// <summary>Values for the placeholders (":name") in the translated message.</summary>
        public IReadOnlyDictionary<string, object?>? MessageArgs { get; private set; }

        protected AppException(
            string message,
            int statusCode,
            IDictionary<string, string[]>? errors = null)
            : base(message)
        {
            StatusCode = statusCode;
            Errors = errors;
        }

        /// <summary>
        /// Attaches a translation key and optional placeholder values:
        /// <code>throw new BusinessRuleException("Vendor is inactive.").WithKey("errors.vendor_inactive", new { name = vendor.Name });</code>
        /// </summary>
        public AppException WithKey(string key, object? args = null)
        {
            MessageKey = key;
            MessageArgs = ToDictionary(args);
            return this;
        }

        private static IReadOnlyDictionary<string, object?>? ToDictionary(object? args)
        {
            if (args == null)
                return null;

            if (args is IReadOnlyDictionary<string, object?> readOnly)
                return readOnly;

            if (args is IDictionary<string, object?> dictionary)
                return new Dictionary<string, object?>(dictionary);

            return args.GetType().GetProperties()
                .Where(p => p.GetIndexParameters().Length == 0)
                .ToDictionary(p => p.Name, p => p.GetValue(args), StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>404 - the requested record does not exist.</summary>
    public class NotFoundException : AppException
    {
        public NotFoundException(string message) : base(message, 404) { }

        public NotFoundException(string entityName, object key)
            : base($"{entityName} with id {key} was not found.", 404)
        {
            WithKey("errors.not_found", new { entity = entityName, id = key });
        }

        /// <summary>A record of this type doesn't exist ("Vendor was not found."), translated through errors.entity_not_found.</summary>
        public static NotFoundException ForEntity(string entityName)
        {
            var exception = new NotFoundException($"{entityName} was not found.");
            exception.WithKey("errors.entity_not_found", new { entity = entityName });
            return exception;
        }
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
            : base(message, 403)
        {
            WithKey("errors.forbidden");
        }
    }
}
