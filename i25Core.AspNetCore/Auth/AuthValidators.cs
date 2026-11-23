using FluentValidation;
using System.Text.RegularExpressions;
using i25Core.Core.Auth;

namespace i25Core.AspNetCore.Auth
{
    /// <summary>
    /// A practical email check. FluentValidation's own EmailAddress() only looks for an "@", so it accepts text such as
    /// "a'; DROP TABLE Users;--@x.com". This accepts the usual dot-atom form: letters, digits and the symbols
    /// RFC 5322 allows in the name part, then a domain with at least one dot. (Unicode addresses are not accepted.)
    /// </summary>
    internal static class EmailRules
    {
        private static readonly Regex Pattern = new(
            // [.] is a literal dot. Name part: dot-separated atoms. Domain: labels of letters, digits and hyphens, at least one dot.
            @"^[A-Za-z0-9!#$%&'*+/=?^_`{|}~-]+([.][A-Za-z0-9!#$%&'*+/=?^_`{|}~-]+)*@[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?([.][A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?)+$",
            RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

        public static IRuleBuilderOptions<T, string> StrictEmail<T>(this IRuleBuilder<T, string> rule) =>
            rule.NotEmpty().MaximumLength(256).EmailAddress().Matches(Pattern);
    }

    // Shape checks only. Password strength, uniqueness and lockout are decided by the services.
    // The generous upper limits stop someone from sending huge strings to be hashed.

    public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
    {
        public RegisterRequestValidator()
        {
            RuleFor(x => x.Email).StrictEmail();
            RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
            RuleFor(x => x.Password).NotEmpty().MaximumLength(1024);
        }
    }

    public class LoginRequestValidator : AbstractValidator<LoginRequest>
    {
        public LoginRequestValidator()
        {
            RuleFor(x => x.Email).NotEmpty().MaximumLength(256);
            RuleFor(x => x.Password).NotEmpty().MaximumLength(1024);
        }
    }

    public class RefreshRequestValidator : AbstractValidator<RefreshRequest>
    {
        public RefreshRequestValidator() => RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(512);
    }

    public class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
    {
        public ChangePasswordRequestValidator()
        {
            RuleFor(x => x.CurrentPassword).NotEmpty().MaximumLength(1024);
            RuleFor(x => x.NewPassword).NotEmpty().MaximumLength(1024);
        }
    }

    public class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
    {
        public ForgotPasswordRequestValidator() => RuleFor(x => x.Email).StrictEmail();
    }

    public class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
    {
        public ResetPasswordRequestValidator()
        {
            RuleFor(x => x.Email).NotEmpty().MaximumLength(256);
            RuleFor(x => x.Token).NotEmpty().MaximumLength(512);
            RuleFor(x => x.NewPassword).NotEmpty().MaximumLength(1024);
        }
    }

    public class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
    {
        public CreateUserRequestValidator()
        {
            RuleFor(x => x.Email).StrictEmail();
            RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
            RuleFor(x => x.Password).NotEmpty().MaximumLength(1024);
        }
    }

    public class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
    {
        public UpdateUserRequestValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
    }

    public class SetRolesRequestValidator : AbstractValidator<SetRolesRequest>
    {
        public SetRolesRequestValidator() => RuleFor(x => x.Roles).NotNull();
    }

    public class CreateRoleRequestValidator : AbstractValidator<CreateRoleRequest>
    {
        public CreateRoleRequestValidator()
        {
            RuleFor(x => x.Name).NotEmpty().MaximumLength(64);
            RuleFor(x => x.Description).MaximumLength(256);
        }
    }
}
