using FluentValidation;

namespace i25Core.AspNetCore.Validator.Base
{
    public abstract class BaseValidator
    {
    }

    public abstract class BaseValidator<T> : AbstractValidator<T>
        where T : class
    {
    }
}