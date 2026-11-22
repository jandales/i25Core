using FluentValidation;
using i25Core.Core.Auth;

namespace i25Core.Web.Auth
{
    // Shape checks only. Password strength, uniqueness and lockout are decided by the services.
    // The generous upper limits stop someone from sending huge strings to be hashed.

    public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
    {
        public RegisterRequestValidator()
        {
            RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
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
        public ForgotPasswordRequestValidator() => RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
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
            RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
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
